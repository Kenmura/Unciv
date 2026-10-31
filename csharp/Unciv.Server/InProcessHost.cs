using System.Security.Cryptography.X509Certificates;
using Unciv.Commands;
using Unciv.Commands.Log;
using Unciv.Logic;
using Unciv.Models.Metadata;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Persistence;
using Unciv.Net.Relay;
using Unciv.Net.Turns;
using Unciv.Server.Transport;

namespace Unciv.Server;

/// <summary>Knobs of <see cref="InProcessHost"/>; every member has a sensible default.</summary>
public sealed record InProcessHostOptions
{
    public IClock? Clock { get; init; }

    public ISecureRandom? Random { get; init; }

    public StoreOptions? Store { get; init; }

    /// <summary>C045: send every session the roster (who holds which seat, who is connected) whenever it changes, so the turn bar and the player list have it. Off by default: the frames are extra.</summary>
    public bool AnnounceRoster { get; init; }

    /// <summary>How often the host ticks the turn timer. Null or non-positive: no background ticking (tests drive <see cref="GameHost.Tick"/>).</summary>
    public TimeSpan? TickInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The turn timer policy (T252: <c>LobbySettings.ToTurnPolicy()</c>); null means <see cref="TurnPolicy.Default"/>. <see cref="InProcessHost.Start"/> stores it in game.json (T254); on resume an explicit value overrides the stored one.</summary>
    public TurnPolicy? TurnPolicy { get; init; }

    /// <summary>Time a remote client gets to finish Hello, Join and Auth.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>T273: set by <see cref="InProcessHost.ResumeFromDirectory"/> callers that are promoting a standby copy (see <see cref="HostPromotion"/>); null for an ordinary resume.</summary>
    public HostPromotion? Promotion { get; init; }

    /// <summary>T302: the host's protection and performance limits (including <see cref="HostLimits.ParallelViewBuilding"/>); null means <see cref="HostLimits.Default"/>.</summary>
    public HostLimits? Limits { get; init; }
}

/// <summary>
/// T273: how a standby copy becomes the host (design/HOST-MIGRATION.md section 2). <see cref="InProcessHost.ResumeFromDirectory"/> first checks what it opened against the old host's <c>HostHandoff</c>
/// (<see cref="FinalSeq"/>, <see cref="FinalHash"/>: the promoted game must reproduce the state hash of the old host at the hand-off point, else nothing is promoted), then raises the epoch to
/// <see cref="Epoch"/> and stores the successor's signed <see cref="Proof"/> in <c>game.json</c> and the log header, and finally treats the old host's seat (<see cref="DepartedKeyId"/>) as a clean leave
/// (the T267 rule: its civ stays human, its turn is force-ended, the AI takes over at the next boundary unless that player reconnects).
/// </summary>
public sealed record HostPromotion
{
    public required int Epoch { get; init; }
    public required PromotionProof Proof { get; init; }
    public required long FinalSeq { get; init; }
    public required string FinalHash { get; init; }
    public string? DepartedKeyId { get; init; }
}

/// <summary>
/// The Host button (SERVER.md 5.3, 6.5, 7): the same <see cref="GameHost"/> as the dedicated server, running inside the client process on a game directory.
/// The host player connects through the in-memory loopback transport (<see cref="ConnectHostPlayerAsync"/>); friends reach it through an optional TLS/TCP listener
/// (<see cref="Listen"/>) and prove their seat with the key bound in <c>game.json</c>. <see cref="StopAsync"/> finishes the current command, snapshots (including each civ's
/// ended flag) and writes the clean marker; <see cref="ResumeFromDirectory"/> reopens the directory with the same bindings.
/// </summary>
public sealed class InProcessHost : IAsyncDisposable
{
    private readonly GameHost host;
    private readonly FileGameStore store;
    private readonly InProcessHostOptions options;
    private readonly ISecureRandom random;
    private readonly string hostCivId;
    private readonly CancellationTokenSource cts = new();
    private readonly List<Task> background = [];
    private TcpTlsListener? listener;
    private RelayListener? relay;
    private string fingerprint = "";
    private bool stopped;

    private InProcessHost(GameHost host, FileGameStore store, string hostCivId, InProcessHostOptions options, RecoveryReport? report)
    {
        this.host = host;
        this.store = store;
        this.hostCivId = hostCivId;
        this.options = options;
        random = options.Random ?? SystemSecureRandom.Instance;
        Report = report;
        if (options.TickInterval is { } interval && interval > TimeSpan.Zero)
        {
            background.Add(host.RunTickLoopAsync(interval, cts.Token));
        }
    }

    public GameHost Host => host;

    public string GameId => store.GameId;

    public string Directory => store.DirectoryPath;

    /// <summary>What recovery did when the host was resumed (null for a new game).</summary>
    public RecoveryReport? Report { get; }

    /// <summary>The TCP port being listened on, or null when <see cref="Listen"/> was not called.</summary>
    public int? Port => listener?.Port;

    /// <summary>T338: the relay registration of this game (design/RELAY.md), or null when <see cref="Listen"/> was called without a relay. Its <see cref="RelayListener.Room"/> is the room code for invites.</summary>
    public RelayListener? Relay => relay;

    /// <summary>The host key fingerprint carried in invites (empty until <see cref="Listen"/>).</summary>
    public string Fingerprint => fingerprint;

    /// <summary>
    /// Starts a freshly created game: writes the game directory (log header, bindings of the human civs, first snapshot) and starts the host actor.
    /// <paramref name="humanKeys"/> maps each human civ id to the player key bound to it (and its display name); the host player's civ is <paramref name="hostCivId"/>.
    /// </summary>
    public static InProcessHost Start(
        GameInfo game,
        GameSetupInfo setup,
        long seed,
        long startClockMillis,
        string directory,
        string hostCivId,
        IReadOnlyDictionary<string, SlotBinding> humanKeys,
        InProcessHostOptions? options = null)
    {
        options ??= new InProcessHostOptions();
        var clock = options.Clock ?? SystemClock.Instance;
        if (!humanKeys.ContainsKey(hostCivId)) throw new ArgumentException("The host civ needs a binding", nameof(hostCivId));
        var header = CommandLogHeader.Create(setup, seed, startClockMillis);
        if (game.TurnState != null) header = header with { TurnMode = CommandLogHeader.SimultaneousTurns };
        var meta = new GameMeta { Slots = new SortedDictionary<string, SlotBinding>(humanKeys.ToDictionary(x => x.Key, x => x.Value), StringComparer.Ordinal), TimerPolicy = options.TurnPolicy };
        var store = FileGameStore.Create(directory, game.GameId, header, meta, game, options.Store);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };
        return new InProcessHost(new GameHost(session, store, clock: clock, announceRoster: options.AnnounceRoster, turnPolicy: options.TurnPolicy, limits: options.Limits), store, hostCivId, options, null);
    }

    /// <summary>
    /// Reopens a game directory after a clean stop or a crash (recovery per SERVER.md 5.1). A half-resolved round continues through <c>NextResolveStep</c>.
    /// The host player is the civ bound to <paramref name="hostPlayerKey"/>.
    /// </summary>
    public static InProcessHost ResumeFromDirectory(string directory, string hostPlayerKey, InProcessHostOptions? options = null)
    {
        options ??= new InProcessHostOptions();
        var clock = options.Clock ?? SystemClock.Instance;
        var opened = FileGameStore.Open(directory, options.Store);
        try
        {
            var promotion = options.Promotion;
            if (promotion != null) VerifyPromotable(opened, promotion);
            var meta = opened.Store.ReadMeta();
            var hostCiv = meta.Slots.FirstOrDefault(x => string.Equals(x.Value.PlayerKey, hostPlayerKey, StringComparison.Ordinal)).Key
                ?? throw new GameStoreException("The host player key is not bound to a civ in this game");
            var session = new GameSession(opened.Session.Game, lastSeq: opened.Session.LastSeq) { Clock = () => clock.NowMillis };
            var host = new GameHost(session, opened.Store, clock: clock, announceRoster: options.AnnounceRoster, turnPolicy: options.TurnPolicy ?? meta.TimerPolicy, limits: options.Limits);
            var result = new InProcessHost(host, opened.Store, hostCiv, options, opened.Report);
            if (promotion != null)
            {
                opened.Store.PromoteTo(promotion.Epoch, promotion.Proof); // idempotent for a retry of the same promotion
                var departed = promotion.DepartedKeyId == null ? null : meta.Slots.FirstOrDefault(x => string.Equals(x.Value.PlayerKey, promotion.DepartedKeyId, StringComparison.Ordinal)).Key;
                if (departed != null && !string.Equals(departed, hostCiv, StringComparison.Ordinal))
                {
                    host.EnqueueAsync(() =>
                    {
                        host.TurnTimer.Depart(departed); // the old process is gone: its player left cleanly (T267)
                        return Task.CompletedTask;
                    }).GetAwaiter().GetResult();
                }
            }
            // Crash mid-resolve: the log holds the steps already applied, the rest is derived from the state.
            if (session.Game.TurnState is { Phase: SimultaneousPhase.Resolving })
            {
                host.EnqueueAsync(() =>
                {
                    host.PumpResolving();
                    return Task.CompletedTask;
                }).GetAwaiter().GetResult();
            }
            return result;
        }
        catch
        {
            opened.Dispose();
            throw;
        }
    }

    // The promoted game must be exactly the old host's game at the hand-off: a clean recovery of the whole replicated log, at the announced seq, with the announced state hash.
    private static void VerifyPromotable(OpenedGame opened, HostPromotion promotion)
    {
        if (opened.Report.Outcome != RecoveryOutcome.Recovered) throw new GameStoreException("standby-invalid: the standby copy did not recover cleanly");
        if (opened.Store.LastSeq != promotion.FinalSeq) throw new GameStoreException($"standby-incomplete: the standby copy ends at {opened.Store.LastSeq}, the hand-off at {promotion.FinalSeq}");
        if (!PlayerKeys.VerifyPromotion(promotion.Proof)) throw new GameStoreException("standby-invalid: the promotion proof does not verify");
        var hash = ReplayVerifier.FullHash(opened.Session.Game).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(hash, promotion.FinalHash, StringComparison.OrdinalIgnoreCase)) throw new GameStoreException("standby-diverged: the standby copy's state hash differs from the old host's");
        var held = opened.Store.ReadMeta();
        if (held.HostEpoch == promotion.Epoch && held.Promotion is { } stored && string.Equals(stored.KeyId, promotion.Proof.KeyId, StringComparison.Ordinal)
            && string.Equals(stored.Fingerprint, promotion.Proof.Fingerprint, StringComparison.Ordinal)) return;
        if (held.HostEpoch >= promotion.Epoch) throw new GameStoreException("standby-epoch: the standby copy is already at or above the new epoch");
    }

    /// <summary>Connects the host player through the in-memory loopback transport and returns the client end (the first frame is the Snapshot).</summary>
    public async Task<ITransport> ConnectHostPlayerAsync()
    {
        var (client, hostEnd) = InMemoryTransport.CreatePair();
        var binding = store.ReadMeta().Slots[hostCivId];
        await host.AttachSessionAsync("local-" + hostCivId, hostCivId, hostEnd, binding.PlayerKey).ConfigureAwait(false);
        return client;
    }

    /// <summary>
    /// T273: an in-memory connection that goes through the same admission as a TCP friend (Hello, Welcome, Join, Auth with the player's key), for the player of this process when it is a promoted successor
    /// (its <c>ClientSession</c> redirects to its own host with the same key, seat and pinned fingerprint as everybody else). Returns the client end.
    /// </summary>
    public ITransport ConnectLoopback()
    {
        var (client, hostEnd) = InMemoryTransport.CreatePair();
        _ = AdmitAsync(hostEnd, cts.Token);
        return client;
    }

    /// <summary>
    /// C045, host only: disconnects the player of <paramref name="civId"/>, clears the seat's key binding and hands the civ to the AI (<see cref="GameHost.KickCivAsync"/>).
    /// The host's own seat cannot be kicked. Null on success, else a refusal code (<c>own-seat</c> or the host's).
    /// </summary>
    public Task<string?> KickAsync(string civId) =>
        civId == hostCivId ? Task.FromResult<string?>("own-seat") : host.KickCivAsync(civId);

    /// <summary>
    /// C045, host only: moves the seat of <paramref name="civId"/> to <paramref name="newPlayerKey"/>, for a friend who lost their key or plays from another device (SERVER.md 6.2).
    /// The new key's next connection takes the civ back from the AI. The host's own seat cannot be rebound. Null on success, else a refusal code (<see cref="GameHost.RebindCivAsync"/>).
    /// </summary>
    public Task<string?> RebindAsync(string civId, string newPlayerKey) =>
        civId == hostCivId ? Task.FromResult<string?>("own-seat") : host.RebindCivAsync(civId, newPlayerKey);

    /// <summary>
    /// Starts accepting friends over TLS/TCP (port 0 picks a free one). Remote players must present a key bound in <c>game.json</c>.
    /// T338: with <paramref name="relayEndpoint"/> the host also dials out to that relay and registers a room, so friends reach it with no open port; the local port stays as it is.
    /// T354: <paramref name="adoptRelay"/> takes over a registration that already exists (the promoted successor's standby room, see <see cref="StandbyListener.DetachRelay"/>); it must have been created with this game's id and certificate.
    /// </summary>
    public void Listen(X509Certificate2 certificate, int port = 47821, RelayEndpoint? relayEndpoint = null, RelayListenerOptions? relayOptions = null, Action<string>? relayLog = null, RelayListener? adoptRelay = null)
    {
        if (listener != null) throw new InvalidOperationException("Already listening");
        if (adoptRelay != null && relayEndpoint != null) throw new ArgumentException("Give a relay to adopt or a relay to dial, not both");
        fingerprint = HostCertificate.GetFingerprint(certificate);
        store.WriteMeta(store.ReadMeta() with { HostFingerprint = fingerprint });
        listener = new TcpTlsListener(certificate, port, options.HandshakeTimeout);
        listener.Start();
        background.Add(RunAcceptLoopAsync(listener, cts.Token));
        if (adoptRelay != null)
        {
            // T354: the promoted successor keeps the unlisted room it held as a standby (the address the clients were told), now answering the game.
            relay = adoptRelay;
            background.Add(RunRelayAcceptLoopAsync(relay, cts.Token));
            relay.Start();
        }
        else if (relayEndpoint != null)
        {
            // T354: this host's own relay is one it agreed to by configuring it, so a standby probe may go through it; any other relay stays refused until the UI layer installs a prober that knows the player's agreements.
            if (host.Standby.Prober is TlsEndpointProber)
                host.Standby.Prober = new TlsEndpointProber(named => string.Equals(named.Key, relayEndpoint.Key, StringComparison.OrdinalIgnoreCase) ? new RelayDialPlan(relayEndpoint, relayOptions?.Dial ?? new RelayDialOptions()) : null);
            relay = new RelayListener(relayEndpoint, certificate, store.GameId, relayOptions, relayLog);
            background.Add(RunRelayAcceptLoopAsync(relay, cts.Token));
            relay.Start();
        }
    }

    /// <summary>
    /// T273: quits by handing the game to the designated successor when its standby copy is current (see <see cref="GameHost.HandOffAsync"/>): the host stops, clients are sent to the successor.
    /// <paramref name="hostPlayerKey"/> is the key of this process's own player (its seat is treated as a clean leave by the new host). When there is no ready successor, or it does not catch up within
    /// <paramref name="flushTimeout"/> (default 5 s), this is a plain <see cref="StopAsync"/> and the refusal code is returned; null means the hand-off was made.
    /// </summary>
    public async Task<string?> HandOffAndStopAsync(string? hostPlayerKey = null, TimeSpan? flushTimeout = null)
    {
        if (stopped) return GameHost.HandoffRefusals.Closed;
        listener?.Stop(); // no new connection can slip past the hand-off; the game's own sessions are told by it
        if (relay != null) await relay.DisposeAsync().ConfigureAwait(false);
        string? refusal;
        try
        {
            refusal = await host.HandOffAsync(flushTimeout ?? TimeSpan.FromSeconds(5), hostPlayerKey ?? store.ReadMeta().Slots.GetValueOrDefault(hostCivId)?.PlayerKey).ConfigureAwait(false);
        }
        finally
        {
            await StopAsync().ConfigureAwait(false); // whatever happened, the host ends cleanly (a plain HostClosing for anyone not handed off)
        }
        return refusal;
    }

    /// <summary>Clean stop: no new connections, the current command finishes, a half-resolved round is completed, then snapshot, clean marker, and HostClosing to every client.</summary>
    public async Task StopAsync()
    {
        if (stopped) return;
        stopped = true;
        listener?.Stop();
        if (relay != null) await relay.DisposeAsync().ConfigureAwait(false);
        cts.Cancel();
        foreach (var task in background)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        await host.DisposeAsync().ConfigureAwait(false);
        cts.Dispose();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task RunAcceptLoopAsync(TcpTlsListener tcp, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            ITransport transport;
            try
            {
                transport = await tcp.AcceptTransportAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (System.Net.Sockets.SocketException) when (token.IsCancellationRequested) { return; }
            catch (Exception) { continue; } // a failed handshake costs only that connection
            _ = AdmitAsync(transport, token);
        }
    }

    private async Task RunRelayAcceptLoopAsync(RelayListener source, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            ITransport transport;
            try
            {
                transport = await source.AcceptTransportAsync(token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException) { return; }
            _ = AdmitAsync(transport, token);
        }
    }

    // Hello -> Welcome(challenge) -> Join(key) -> Auth(proof) -> JoinOk -> attach to the bound civ. A key that owns no seat is refused: the lobby is closed once the game runs.
    private async Task AdmitAsync(ITransport transport, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(options.HandshakeTimeout);
        try
        {
            if (await ReceiveAsync(transport, timeout.Token) is not Hello hello || hello.Protocol != ProtocolVersion.Current)
            {
                await RefuseAsync(transport, JoinRefusedCodes.ProtocolMismatch);
                return;
            }

            // T269: a superseded host (another host took over) answers nobody. A Hello is unauthenticated, so what a client claims to know is only acted on after its key proof.
            var epochs = store.ReadMeta();
            if (epochs.SeenEpoch > epochs.HostEpoch)
            {
                await RefuseAsync(transport, JoinRefusedCodes.StaleEpoch);
                return;
            }
            var claimedEpoch = string.Equals(hello.GameId, store.GameId, StringComparison.Ordinal) ? hello.KnownEpoch ?? 0 : 0;

            var challenge = new HostChallenge(fingerprint, random);
            await transport.SendAsync(ProtocolCodec.Encode(new Welcome
            {
                Protocol = ProtocolVersion.Current,
                SessionId = challenge.SessionId,
                Challenge = challenge.Challenge,
                HostKeyFingerprint = fingerprint,
                RequiresPassword = false,
                Epoch = epochs.HostEpoch,
                Promotion = epochs.HostEpoch > 0 ? epochs.Promotion : null,
            }), timeout.Token);

            if (await ReceiveAsync(transport, timeout.Token) is not Join join || !PlayerKeys.IsValid(join.PlayerKey))
            {
                await RefuseAsync(transport, JoinRefusedCodes.BadKey);
                return;
            }
            if (await ReceiveAsync(transport, timeout.Token) is not Auth auth || !challenge.Verify(join.PlayerKey, auth.Proof))
            {
                await RefuseAsync(transport, JoinRefusedCodes.AuthFailed);
                return;
            }

            var civ = store.ReadMeta().Slots.FirstOrDefault(x => string.Equals(x.Value.PlayerKey, join.PlayerKey, StringComparison.Ordinal)).Key;
            if (civ == null)
            {
                await RefuseAsync(transport, JoinRefusedCodes.GameAlreadyStarted);
                return;
            }

            if (claimedEpoch > epochs.HostEpoch)
            {
                // A seat holder claims a newer host. Only a promotion proof signed by the successor this host designated makes it supersede this host for good (it is remembered across a restart);
                // a bare claim, which any seat holder could forge, refuses just this connection (T273, the T269 residual risk).
                if (ProvesPromotion(hello.EpochProof, claimedEpoch)) store.RaiseEpochs(epochs.HostEpoch, claimedEpoch);
                await RefuseAsync(transport, JoinRefusedCodes.StaleEpoch);
                return;
            }

            await transport.SendAsync(ProtocolCodec.Encode(new JoinOk { Successor = await host.SuccessorForJoinAsync().ConfigureAwait(false) }), timeout.Token);
            await host.AttachSessionAsync(challenge.SessionId, civ, transport, join.PlayerKey, hello.HostMigration == true).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Timeout, malformed frame or a client that went away: drop the connection, nothing else is affected.
            await transport.CloseAsync();
        }
    }

    // The proof must be for this game and the claimed epoch, signed by the key this host designated as its successor (and only that key: any seat key can sign something), with a valid signature.
    private bool ProvesPromotion(PromotionProof? proof, int claimedEpoch) =>
        proof != null && proof.Epoch == claimedEpoch && string.Equals(proof.GameId, store.GameId, StringComparison.Ordinal)
        && host.Standby.IsAcceptedCandidate(proof.KeyId) && PlayerKeys.VerifyPromotion(proof);

    private static async Task<Message?> ReceiveAsync(ITransport transport, CancellationToken token)
    {
        var frame = await transport.ReceiveAsync(token).ConfigureAwait(false);
        return frame == null ? null : ProtocolCodec.Decode(frame);
    }

    private static async Task RefuseAsync(ITransport transport, string code)
    {
        try { await transport.SendAsync(ProtocolCodec.Encode(new JoinRefused { Code = code })); }
        catch (InvalidOperationException) { }
        await transport.CloseAsync();
    }
}
