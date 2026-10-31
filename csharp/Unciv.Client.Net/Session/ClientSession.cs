using System.Text.Json;
using System.Threading.Channels;
using Unciv.Client.Net.Mods;
using Unciv.Commands;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Mods;
using Unciv.Net.Remote;
using Unciv.Net.Standby;
using Unciv.Queries;
using CommandResult = Unciv.Net.CommandResult;

namespace Unciv.Client.Net.Session;

/// <summary>Everything a <see cref="ClientSession"/> needs to know about who is joining where. Plain data and callbacks: no Godot, no storage.</summary>
public sealed class ClientSessionOptions
{
    public required ClientIdentity Identity { get; init; }
    public required string DisplayName { get; init; }
    public required string RulesetHash { get; init; }
    public string ClientVersion { get; init; } = "unciv-client";
    public List<string> ModList { get; init; } = [];

    public string? Password { get; init; }
    public string? InviteToken { get; init; }

    /// <summary>Fingerprint carried by an invite code: verified before anything is sent, no first-use window.</summary>
    public string? ExpectedFingerprint { get; init; }

    /// <summary>The key into <see cref="KnownHosts"/> (<c>address:port</c> as the user typed it).</summary>
    public string HostAddress { get; init; } = "";

    public KnownHosts? KnownHosts { get; init; }

    /// <summary>Called after a new pin was added so the UI layer can write <c>known-hosts.json</c>.</summary>
    public Action<KnownHosts>? PersistKnownHosts { get; init; }

    /// <summary>The TOFU prompt: (host address, fingerprint) to "trust this host?". Without it an unknown host is refused.</summary>
    public Func<string, string, Task<bool>>? TrustPrompt { get; init; }

    /// <summary>Accept a host with neither an invite fingerprint nor known hosts (the in-process loopback host, tests). Off by default.</summary>
    public bool AllowUnpinnedHost { get; init; }

    /// <summary>The game id kept from the last session; with it a reconnect sends <c>Resume</c>, without it a <c>ResyncRequest</c> (the host answers both with a snapshot or the missed deltas).</summary>
    public string? GameId { get; init; }

    /// <summary>T269: the highest host epoch seen for <see cref="GameId"/>, loaded by the UI layer from its per-game store (null or 0: none). A host that announces a lower epoch is refused with <c>stale-epoch</c>.</summary>
    public int? KnownEpoch { get; init; }

    /// <summary>T269: called when a pinned host announced a higher epoch than the stored one, so the UI layer persists it next to the game's <c>lastSeq</c>.</summary>
    public Action<int>? PersistKnownEpoch { get; init; }

    /// <summary>T269: tell the host this client understands host migration (a host that cannot hand off sends the plain <c>HostClosing</c> to a client that did not).</summary>
    public bool AdvertiseHostMigration { get; init; } = true;

    /// <summary>
    /// T272: the data directory under which this player keeps the standby copy (<c>standby/&lt;gameId&gt;/</c>) when it is the backup host. Null (the default) never stores replicated state. The UI sets it
    /// only for a player who can be a backup host; the copy is created only after this process called <c>AcceptStandbyAsync</c> (or already exists from an earlier accept), never because a host asked.
    /// It holds full game state and must never feed the client's rendering.
    /// </summary>
    public string? StandbyRoot { get; init; }

    /// <summary>
    /// T273: opens a connection to one announced endpoint (<c>host:port</c>) of the successor after a <see cref="HostHandoff"/>. The fingerprint is checked by the session itself against the successor's
    /// announcement (<see cref="HostSuccessor"/>). Without it (or <see cref="OnHandoff"/>) a hand-off is treated like a plain <see cref="HostClosing"/>.
    /// </summary>
    public Func<string, CancellationToken, Task<ITransport>>? ConnectEndpoint { get; init; }

    /// <summary>
    /// T273: called once per hand-off, after the old connection ended and before the session connects to the successor. The designated successor's process promotes its standby copy here (it must be
    /// complete: a hand-off is only sent once it is) and returns the connector of its own new host (an in-memory loopback); every other client returns null and uses <see cref="ConnectEndpoint"/>.
    /// A throw ends the redirect (the session then runs its normal reconnect and gives up).
    /// </summary>
    public Func<HostHandoff, CancellationToken, Task<Func<CancellationToken, Task<ITransport>>?>>? OnHandoff { get; init; }

    /// <summary>T273: the promotion proof for <see cref="KnownEpoch"/> loaded from the per-game store (null: none). Sent in <c>Hello</c> so a superseded host can verify the claim.</summary>
    public PromotionProof? EpochProof { get; init; }

    /// <summary>T273: called when the session joined a promoted host, with the proof that host presented, so the UI layer persists it next to <see cref="KnownEpoch"/>.</summary>
    public Action<PromotionProof>? PersistEpochProof { get; init; }

    public int MaxReconnectAttempts { get; init; } = 5;

    /// <summary>T292: waits before resubscribe attempt n (1-based) after the host answered a subscribe or stopped its stream with <c>standby-protocol</c>. The default backs off 1, 2, 4, ... 30 seconds; tests pass an immediate one.</summary>
    public Func<int, CancellationToken, Task> StandbyResubscribeDelay { get; init; } =
        (attempt, token) => Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt - 1, 5))), token);

    /// <summary>T292: resubscribe attempts per connection without progress in between (a stored frame resets the count).</summary>
    public int MaxStandbyResubscribes { get; init; } = 5;

    /// <summary>How long connecting plus the join handshake may take (the TOFU prompt does not count) before the attempt is abandoned.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Waits before reconnect attempt n (1-based). The default backs off 1, 2, 4, ... 30 seconds; tests pass an immediate one.</summary>
    public Func<int, CancellationToken, Task> ReconnectDelay { get; init; } =
        (attempt, token) => Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt - 1, 5))), token);

    /// <summary>How often a settled connection pings the host so the host's idle timeout never drops a quiet but live player. <see cref="Timeout.InfiniteTimeSpan"/> disables it.</summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>A settled connection that receives nothing for this long (not even a Pong) is a dead or half-open host: it is dropped and the reconnect logic takes over. <see cref="Timeout.InfiniteTimeSpan"/> disables it.</summary>
    public TimeSpan HostSilenceTimeout { get; init; } = TimeSpan.FromSeconds(75);

    public QueryRegistry? Queries { get; init; }
    public ISecureRandom? Random { get; init; }

    /// <summary>T247: takes the host's mod files when the rules differ (the host answers a mismatched join with a manifest). Without it a mismatch is refused as before.</summary>
    public IModSyncHandler? ModSync { get; init; }

    /// <summary>T247: the checksums of the mods installed here, sent with the Hello so the host can spot a same-named but different mod.</summary>
    public IReadOnlyDictionary<string, string>? ModChecksums { get; init; }

    /// <summary>T247: how long the host may take to answer one mod file request.</summary>
    public TimeSpan ModFileTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>The result of <see cref="ClientSession.JoinAsync"/>: joined, or the refusal code.</summary>
public sealed record JoinResult(bool Joined, string? Code);

/// <summary>
/// The client side of the host-a-lobby protocol (SERVER.md 2.3, 6, 7): join (invite fingerprint or TOFU, password, key proof), lobby, mirror of the
/// viewer's state moved by snapshots and deltas, commands with retry by <c>ClientCmdId</c>, queries, reconnect with the same key and resume.
/// No Godot types and no sockets: it talks to an <see cref="ITransport"/> from a factory, so tests drive it over <see cref="InMemoryTransport"/>.
/// All incoming messages are handled in order by one background loop; <see cref="Events"/> is the ordered stream the UI consumes.
/// </summary>
public sealed class ClientSession : IAsyncDisposable
{
    private const int MaxPendingCommands = 64;

    /// <summary>A UI that stops pumping <see cref="Events"/> loses the oldest events, never memory: the mirror (<see cref="Game"/>) stays authoritative and the final <see cref="SessionEnded"/> is always the newest.</summary>
    private const int EventCapacity = 4096;

    private readonly Func<CancellationToken, Task<ITransport>> connect;
    private readonly ClientSessionOptions options;
    private readonly Channel<ClientEvent> events = Channel.CreateBounded<ClientEvent>(
        new BoundedChannelOptions(EventCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly Lock gate = new();
    private readonly Dictionary<string, PendingCommand> pending = [];
    private readonly RemoteQueryClient queries;
    private readonly string idPrefix;
    private readonly TaskCompletionSource<JoinResult> joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CommandRegistry<GameInfo>? commandRegistry;

    private volatile ITransport? transport;
    private volatile ClientSessionState state = ClientSessionState.Disconnected;
    private long nextCommand = 1;
    private Task? loop;
    private bool stopped;

    // Receive-loop state (touched by the loop only, except where noted).
    private bool everJoined;
    private bool connectionSettled; // the connection delivered a lobby view or a snapshot, so the host really accepted us
    private bool desyncAnnounced;
    private volatile bool awaitingSnapshot;
    private volatile bool gameStarted;
    private CancellationTokenSource? handshakeTimer;
    private bool handshakeTimedOut;
    private string? gameId;
    private int knownEpoch;
    private int pendingEpoch;
    private string connectedHostFingerprint = "";
    private int connectionEpoch;
    private TaskCompletionSource? resyncWaiter;

    // T273: a hand-off in progress. The address and fingerprint the next handshake is checked against start as the options say and move to the successor while a redirect runs.
    private HostHandoff? redirect;
    private Func<CancellationToken, Task<ITransport>>? redirectConnector;
    private bool redirectPrepared;
    private bool redirectAnnounced;
    private string hostAddress = "";
    private string? expectedFingerprint;
    private PromotionProof? epochProof;
    private PromotionProof? pendingProof;

    // T272: standby replication (receive-loop state; one connection at a time).
    // T292: the frames are applied (and fsynced) by a worker, not the receive loop, so a slow disk never delays heartbeats, pongs or the game stream. Everything below up to standbyHave is guarded by standbyGate
    // (the worker applies under it; closing the store waits for the frame in progress).
    private readonly Lock standbyGate = new();
    private StandbyStore? standbyStore;
    private string? standbyGameId;
    private const string AnyGame = "*";
    private string? standbyConsentedGame;
    private bool standbyBroken;
    private bool standbyRestarting;
    private int standbyRestarts;
    private int standbyAppliedSinceReject;
    private int standbyResubscribes;
    private bool standbyResubscribePending;
    private volatile bool standbyResubscribeDue;
    private long standbyLastAck = long.MinValue;
    private Channel<Message>? standbyFrames;
    private volatile ChannelWriter<Message>? standbyWriter; // read by the receive loop without the gate, so a frame being fsynced never delays it
    private long standbyHave = -1;

    /// <summary>T272: the last log seq of this player's standby copy (-1 when it holds none or is not a backup host). Counters only: the copy itself is never exposed.</summary>
    public long StandbyHaveSeq => Interlocked.Read(ref standbyHave);

    private sealed class PendingCommand(Command message, long ordinal)
    {
        public Command Message { get; } = message;
        public long Ordinal { get; } = ordinal;
        public TaskCompletionSource<CommandOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public ClientSession(Func<CancellationToken, Task<ITransport>> connect, ClientSessionOptions options)
    {
        this.connect = connect;
        this.options = options;
        gameId = options.GameId;
        knownEpoch = Math.Max(0, options.KnownEpoch ?? 0);
        hostAddress = options.HostAddress;
        expectedFingerprint = options.ExpectedFingerprint;
        epochProof = options.EpochProof;
        var random = options.Random ?? SystemSecureRandom.Instance;
        Span<byte> bytes = stackalloc byte[6];
        random.Fill(bytes);
        idPrefix = Convert.ToHexString(bytes).ToLowerInvariant();

        var registry = options.Queries;
        if (registry == null)
        {
            registry = new QueryRegistry(UncivJson.DefaultOptions);
            registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        }
        queries = new RemoteQueryClient(registry, UncivJson.DefaultOptions, SendAsync);
    }

    public ClientSessionState State => state;

    /// <summary>The mirror of the viewer's state; null until the first snapshot.</summary>
    public ClientGameState Game { get; } = new();

    /// <summary>Latest lobby view (null before the first <c>LobbyState</c>).</summary>
    public LobbyState? Lobby { get; private set; }

    /// <summary>Latest turn status message, if any.</summary>
    public TurnStatus? TurnStatus { get; private set; }

    /// <summary>Commands submitted and not yet answered (they are resent after a reconnect).</summary>
    public int PendingCommandCount
    {
        get { lock (gate) return pending.Count; }
    }

    /// <summary>The ordered stream of what happened. Read it with <c>ReadAllAsync</c> or <c>TryRead</c> from the UI pump.</summary>
    public ChannelReader<ClientEvent> Events => events.Reader;

    /// <summary>
    /// Connects, runs the join handshake and completes when the host admitted this key (or refused, or the connection failed).
    /// The session then keeps running in the background (and reconnects when the connection drops) until <see cref="LeaveAsync"/>.
    /// </summary>
    public Task<JoinResult> JoinAsync()
    {
        lock (gate)
        {
            if (loop != null) throw new InvalidOperationException("Session already started");
            loop = RunAsync();
        }
        return joined.Task;
    }

    // ------------------------------------------------------------ commands and queries

    /// <summary>
    /// Submits a command and completes with the host's answer. The command keeps its <c>ClientCmdId</c> through reconnects (the host answers a repeated id
    /// with the first result instead of applying it twice), so a command submitted while the connection is down is sent after the resume.
    /// </summary>
    public async Task<CommandOutcome> SubmitAsync(string commandType, JsonElement payload, CancellationToken cancellationToken = default)
    {
        PendingCommand entry;
        lock (gate)
        {
            if (stopped) return new CommandOutcome("", false, ClientCodes.SessionClosed, null);
            if (!CanQueueCommands()) return new CommandOutcome("", false, ClientCodes.NotInGame, null);
            if (pending.Count >= MaxPendingCommands) return new CommandOutcome("", false, ClientCodes.TooManyPending, null);
            var ordinal = nextCommand++;
            var id = $"{idPrefix}-{ordinal}";
            entry = new PendingCommand(new Command { ClientCmdId = id, CommandType = commandType, Payload = payload }, ordinal);
            pending[id] = entry;
        }

        if (state is ClientSessionState.InGame or ClientSessionState.Syncing)
        {
            try { await SendAsync(entry.Message, cancellationToken).ConfigureAwait(false); }
            catch (Exception e) when (IsConnectionGone(e)) { /* connection just dropped: resent after the resume */ }
            catch (OperationCanceledException)
            {
                // Cancelled before or during the send: the entry must not leak into the pending cap or be resent after a reconnect.
                lock (gate) pending.Remove(entry.Message.ClientCmdId);
                throw;
            }
        }

        using var registration = cancellationToken.Register(() =>
        {
            lock (gate) pending.Remove(entry.Message.ClientCmdId);
            entry.Completion.TrySetCanceled(cancellationToken);
        });
        return await entry.Completion.Task.ConfigureAwait(false);
    }

    /// <summary>Commands only make sense once a game started: in the lobby nothing would ever send them, so they are refused instead of waiting forever.</summary>
    private bool CanQueueCommands() => state switch
    {
        ClientSessionState.InGame or ClientSessionState.Syncing => true,
        ClientSessionState.Reconnecting => gameStarted || Game.Snapshot != null,
        _ => false
    };

    /// <summary>Submits an engine command (serialized with the command registry under its registered type).</summary>
    public Task<CommandOutcome> SubmitAsync(GameCommand command, CancellationToken cancellationToken = default)
    {
        commandRegistry ??= GameCommandRegistry.Create();
        CommandEnvelope envelope;
        try { envelope = commandRegistry.ToEnvelope(0, "", command); }
        catch (InvalidOperationException) { return Task.FromResult(new CommandOutcome("", false, ClientCodes.MalformedCommand, null)); }
        return SubmitAsync(envelope.Type, envelope.Payload, cancellationToken);
    }

    /// <summary>A read-only question to the host as this session's civ. Fails with <see cref="ClientCodes.QueryTimeout"/> after <paramref name="timeout"/> (default 10 s).</summary>
    public async Task<RemoteQueryResult<TResult>> QueryAsync<TResult>(IQuery<TResult> query, TimeSpan? timeout = null, CancellationToken cancellationToken = default) where TResult : notnull
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        try
        {
            return await queries.QueryAsync(query, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RemoteQueryResult<TResult>.Fail(ClientCodes.QueryTimeout);
        }
    }

    /// <summary>Asks the host for a full snapshot and completes when it replaced the mirror. Faults with <see cref="InvalidOperationException"/> when the connection is lost, the snapshot is rejected, or the session is over.</summary>
    public async Task ResyncAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource waiter;
        lock (gate)
        {
            if (stopped) throw new InvalidOperationException(ClientCodes.SessionClosed);
            waiter = resyncWaiter ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        await RequestResyncAsync("requested").ConfigureAwait(false);
        await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ T281: backup host

    /// <summary>The latest backup-host announcement from the host this session is connected to (null until one arrives).</summary>
    public HostSuccessor? Successor { get; private set; }

    /// <summary>Consents to the backup-host role offered by <see cref="StandbyOffered"/>: <paramref name="endpoints"/> are the addresses this player can be reached on, <paramref name="port"/> and <paramref name="fingerprint"/> those of its own listener. The host probes one endpoint before the standby counts as ready.</summary>
    public ValueTask AcceptStandbyAsync(IReadOnlyList<string> endpoints, int port, string fingerprint)
    {
        // From here the host's replication stream of this game may be stored under StandbyRoot: in memory for this process and as a per-game record for the next one.
        standbyConsentedGame = gameId ?? AnyGame; // a session joins one game: with its id not known yet the accept covers the game the host's next status names
        if (gameId != null && options.StandbyRoot != null) StandbyStore.RecordConsent(options.StandbyRoot, gameId);
        return SendAsync(new StandbyAccept { Endpoints = [.. endpoints], Port = port, Fingerprint = fingerprint });
    }

    /// <summary>Declines the offer, or withdraws an accepted standby.</summary>
    public ValueTask DeclineStandbyAsync()
    {
        var kept = standbyGameId ?? gameId;
        EndStandbyReplication();
        if (kept != null && options.StandbyRoot != null && StandbyStore.DirectoryFor(options.StandbyRoot, kept) is { } path)
        {
            // Withdrawing consent removes the copy: a later host status must not find the directory and treat it as an earlier accept.
            try { Directory.Delete(path, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best effort */ }
        }
        return SendAsync(new StandbyDecline());
    }

    /// <summary>
    /// Takes the host's announcement. It came over the authenticated host channel; one from a lower epoch than this client already knows is ignored. A reachable successor (the host names one only while its probe passes, whatever the replication state) is pinned in <see cref="KnownHosts"/>
    /// under each announced endpoint, vouched for by the host this session trusts, so a later reconnect there has no first-use prompt (design/HOST-MIGRATION.md section 3).
    /// </summary>
    private void HandleSuccessor(HostSuccessor successor)
    {
        if (successor.Epoch != connectionEpoch) return; // the connected host speaks for its own epoch only
        Successor = successor;
        if (successor.Successor is { } info && options.KnownHosts is { } known && connectedHostFingerprint.Length > 0)
        {
            var added = false;
            foreach (var endpoint in info.Endpoints.Take(8).Where(it => it.Length is > 2 and <= 260 && it[0] != '$' && it.Contains(':')))
            {
                var had = known.Pins.ContainsKey(endpoint);
                if (known.PinVouched(endpoint, info.Fingerprint, connectedHostFingerprint, successor.Epoch) && !had) added = true;
            }
            if (added) options.PersistKnownHosts?.Invoke(known);
        }
        Emit(new SuccessorChanged(successor));
    }

    // ------------------------------------------------------------ T272: standby replication

    private void EndStandbyReplication()
    {
        var game = standbyGameId ?? gameId;
        standbyConsentedGame = null;
        if (game != null && options.StandbyRoot != null) StandbyStore.ClearConsent(options.StandbyRoot, game); // the designation ended: the copy stays, but nothing is stored into it until a new accept
        CloseStandbyStore();
    }

    private void CloseStandbyStore()
    {
        lock (standbyGate)
        {
            standbyWriter = null;
            standbyFrames?.Writer.TryComplete();
            standbyFrames = null;
            standbyStore?.Dispose();
            standbyStore = null;
            standbyGameId = null;
            standbyBroken = false;
            standbyRestarting = false;
            standbyRestarts = 0;
            standbyAppliedSinceReject = 0;
            standbyResubscribes = 0;
            standbyResubscribePending = false;
            standbyResubscribeDue = false;
            standbyLastAck = long.MinValue;
        }
    }

    /// <summary>
    /// Subscribes to the host's replication stream, once per connection, resuming from what the standby copy holds. Only for a player that consented to this game (in this process or by the per-game record
    /// an earlier one left), and only for a game id that is a safe directory name (it comes from the host).
    /// </summary>
    private async Task SubscribeStandbyAsync(string? statusGameId, CancellationToken token)
    {
        if (standbyStore != null || options.StandbyRoot == null) return;
        if (statusGameId != null && gameId != null && !string.Equals(statusGameId, gameId, StringComparison.Ordinal)) return; // only the game this session joined
        var id = statusGameId ?? gameId;
        if (id == null) return;
        if (StandbyStore.DirectoryFor(options.StandbyRoot, id) == null) return;
        if (string.Equals(standbyConsentedGame, AnyGame, StringComparison.Ordinal))
        {
            standbyConsentedGame = id;
            StandbyStore.RecordConsent(options.StandbyRoot, id);
        }
        if (!string.Equals(standbyConsentedGame, id, StringComparison.Ordinal) && !StandbyStore.HasConsent(options.StandbyRoot, id)) return;
        StandbyStore opened;
        try
        {
            opened = StandbyStore.Open(options.StandbyRoot, id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Emit(new StandbyRejected("store-unavailable"));
            return;
        }
        long have;
        string? haveSha;
        lock (standbyGate)
        {
            standbyStore = opened;
            standbyGameId = id;
            standbyFrames = Channel.CreateBounded<Message>(new BoundedChannelOptions(128) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
            standbyWriter = standbyFrames.Writer;
            _ = Task.Run(() => RunStandbyWorkerAsync(opened, standbyFrames.Reader, token), CancellationToken.None);
            have = opened.HaveSeq;
            haveSha = opened.HaveLineSha256;
        }
        Interlocked.Exchange(ref standbyHave, have);
        await SendAsync(new StandbySubscribe { GameId = id, HaveSeq = have, HaveLineSha256 = haveSha }, token).ConfigureAwait(false);
    }

    // Receive loop: hands the frame to the worker (bounded: a disk that cannot keep up pauses reading, and the host's pacing then stops sending).
    private async Task HandleStandbyFrameAsync(Message frame, CancellationToken token)
    {
        var writer = standbyWriter; // null: never subscribed on this connection (a host cannot push a copy onto a player who did not ask); a broken copy is checked by the worker
        if (writer == null) return;
        var epoch = frame switch { StandbyMeta m => m.Epoch, StandbySnapshot p => p.Epoch, StandbyEntry e => e.Epoch, _ => -1 };
        if (epoch != connectionEpoch) return;
        if (await writer.WaitToWriteAsync(token).ConfigureAwait(false)) writer.TryWrite(frame); // a copy closed while the frame was in flight just drops it
    }

    private async Task RunStandbyWorkerAsync(StandbyStore store, ChannelReader<Message> reader, CancellationToken token)
    {
        try
        {
            await foreach (var frame in reader.ReadAllAsync(token).ConfigureAwait(false))
                await ApplyStandbyFrameAsync(store, frame, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (!IsConnectionGone(e)) { MarkStandbyBroken(); }
        catch (Exception) { /* the connection is gone: the next one subscribes anew */ }
    }

    private void MarkStandbyBroken()
    {
        lock (standbyGate)
        {
            standbyBroken = true; // the disk cannot take the copy: nothing further is acked, the host's stream turns lagging
            standbyWriter = null; // and nothing more is queued: no worker may be left behind a full channel
            standbyFrames?.Writer.TryComplete();
        }
        Emit(new StandbyRejected("store-io"));
    }

    private async Task ApplyStandbyFrameAsync(StandbyStore store, Message frame, CancellationToken token)
    {
        string? emit = null;
        var restartRequest = false;
        long? ack = null;
        string? id;
        lock (standbyGate)
        {
            if (!ReferenceEquals(standbyStore, store) || standbyBroken) return;
            id = standbyGameId;
            // After a restart request the frames already on the wire belong to the old stream: they are skipped up to the new stream's first frame (the meta with the log header).
            if (standbyRestarting)
            {
                if (frame is not StandbyMeta { Header: not null }) return;
                standbyRestarting = false;
            }

            StandbyApply result;
            try
            {
                result = frame switch
                {
                    StandbyMeta m => store.Apply(m),
                    StandbySnapshot p => store.Apply(p),
                    _ => store.Apply((StandbyEntry)frame), // HandleStandbyFrameAsync only queues the three frame types
                };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                MarkStandbyBroken(); // (the lock is reentrant)
                return;
            }

            if (result == StandbyApply.Rejected)
            {
                emit = store.LastRejection ?? "rejected";
                standbyAppliedSinceReject = 0;
                if (++standbyRestarts > 3)
                {
                    standbyBroken = true;
                }
                else
                {
                    standbyRestarting = true;
                    standbyLastAck = long.MinValue;
                    restartRequest = true;
                }
            }
            else
            {
                Interlocked.Exchange(ref standbyHave, store.HaveSeq);
                // Acked: every stored log line, and a snapshot that restarted the store (the host ignores acks until the one for that snapshot's seq).
                var resetDone = frame is StandbySnapshot { Reset: true } && result == StandbyApply.Applied;
                if (resetDone)
                {
                    standbyRestarts = 0; // a restart that worked
                    standbyAppliedSinceReject = 0;
                    standbyResubscribes = 0;
                }
                if (frame is StandbyEntry && result == StandbyApply.Applied)
                {
                    standbyResubscribes = 0;
                    if (++standbyAppliedSinceReject >= 100) standbyRestarts = 0; // the counter decays: isolated rejections over a long healthy stream must not add up to a permanent stop
                }
                if (resetDone || (frame is StandbyEntry && result == StandbyApply.Applied && store.HaveSeq != standbyLastAck))
                {
                    standbyLastAck = store.HaveSeq;
                    ack = store.HaveSeq;
                }
            }
        }

        if (emit != null) Emit(new StandbyRejected(emit));
        if (restartRequest) await SendAsync(new StandbySubscribe { GameId = id!, HaveSeq = -1 }, token).ConfigureAwait(false);
        else if (ack != null) await SendAsync(new StandbyAck { Seq = ack.Value }, token).ConfigureAwait(false);
    }

    // T292: the host refused a subscribe (rate) or ended the stream (a violation, its store failing). The subscribe is repeated after a growing delay, a bounded number of times per connection.
    // The delay runs off the receive loop; when it ends a Ping makes the host's Pong wake the loop, which then does the resubscribe (so the store is only ever touched from the loop and the worker).
    private void ScheduleStandbyResubscribe(CancellationToken token)
    {
        int attempt;
        lock (standbyGate)
        {
            if (standbyStore == null || standbyBroken || standbyResubscribePending || standbyResubscribes >= options.MaxStandbyResubscribes) return;
            standbyResubscribePending = true;
            attempt = ++standbyResubscribes;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await options.StandbyResubscribeDelay(attempt, token).ConfigureAwait(false);
                standbyResubscribeDue = true;
                await SendAsync(new Ping(), token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException || IsConnectionGone(e)) { /* the connection ended during the delay */ }
        }, CancellationToken.None);
    }

    private async Task ResubscribeStandbyAsync(CancellationToken token)
    {
        StandbySubscribe? subscribe = null;
        lock (standbyGate)
        {
            standbyResubscribeDue = false;
            standbyResubscribePending = false;
            if (standbyStore is { } store && !standbyBroken && standbyGameId != null)
            {
                subscribe = standbyRestarting
                    ? new StandbySubscribe { GameId = standbyGameId, HaveSeq = -1 }
                    : new StandbySubscribe { GameId = standbyGameId, HaveSeq = store.HaveSeq, HaveLineSha256 = store.HaveLineSha256 };
            }
        }
        if (subscribe != null) await SendAsync(subscribe, token).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ lobby actions

    public ValueTask SetLobbySlotAsync(int slot, string? nation, string? color, bool ready) =>
        SendAsync(new LobbySet { Slot = slot, Nation = nation, Color = color, Ready = ready });

    public ValueTask SendChatAsync(string text) => SendAsync(new LobbyChat { Text = text });

    /// <summary>C090: pings a tile for the allies (the host relays it, also back to this player, and drops it when it is not allowed).</summary>
    public ValueTask SendMapPingAsync(int x, int y, string kind) => SendAsync(new MapPing { X = x, Y = y, Kind = kind });

    public ValueTask PingAsync() => SendAsync(new Ping());

    /// <summary>Tells the host this player leaves and ends the session (no reconnect).</summary>
    public async Task LeaveAsync()
    {
        try { await SendAsync(new Leave()).ConfigureAwait(false); }
        catch (Exception e) when (IsConnectionGone(e)) { }
        await StopAsync("left").ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync("disposed").ConfigureAwait(false);
        lifetime.Dispose();
    }

    // ------------------------------------------------------------ connection loop

    private async Task RunAsync()
    {
        try
        {
            await RunLoopAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Nothing may fault this task: JoinAsync would never complete and DisposeAsync would rethrow.
            Emit(new ProtocolError(ClientCodes.InternalError));
            await EndAsync(ClientCodes.InternalError, refused: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await EndAsync("disposed", refused: false).ConfigureAwait(false);
        }
    }

    private async Task RunLoopAsync()
    {
        var token = lifetime.Token;
        var attempt = 0;
        while (!stopped)
        {
            ITransport? current = null;
            string? connectCode = null; // T338: why a relay refused, so the player is told "host gone" and not just "could not connect"
            connectionSettled = false;
            handshakeTimedOut = false;
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(token);
            handshakeTimer = timer;
            if (redirect != null && !redirectPrepared && !await PrepareRedirectAsync(token).ConfigureAwait(false)) redirect = null; // promotion failed: the normal reconnect runs (and gives up)
            timer.CancelAfter(options.HandshakeTimeout);
            try { current = await ConnectAsync(attempt, timer.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { handshakeTimedOut = true; }
            catch (Unciv.Server.Transport.RelayException e) { connectCode = e.Code; }
            catch (Exception) { /* any failure to connect (socket, TLS, factory bug) is a failed attempt: fall through to the retry logic */ }

            if (current != null) await RunConnectionAsync(current, timer, token).ConfigureAwait(false);
            if (connectionSettled) attempt = 0;
            if (stopped) break;

            if (!everJoined)
            {
                var code = handshakeTimedOut ? ClientCodes.HandshakeTimeout : current == null ? connectCode ?? ClientCodes.ConnectFailed : ClientCodes.ConnectionLost;
                await EndAsync(code, refused: false).ConfigureAwait(false);
                return;
            }
            // The mirror may have missed deltas while we were away: IGameSession.Desynchronized fires once per outage, before the resume (T180 note, T244).
            if (Game.Snapshot != null && !desyncAnnounced)
            {
                desyncAnnounced = true;
                Emit(new Desynchronized("reconnect"));
            }
            if (++attempt > options.MaxReconnectAttempts)
            {
                await EndAsync("reconnect-failed", refused: false).ConfigureAwait(false);
                return;
            }
            SetState(ClientSessionState.Reconnecting);
            FailWaiters();
            try { await options.ReconnectDelay(attempt, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    // The connection factory of the session, or, while a hand-off redirect runs, the successor: its own connector (the promoted process) or one of the announced endpoints (rotated per attempt).
    private Task<ITransport> ConnectAsync(int attempt, CancellationToken token)
    {
        if (redirect == null) return connect(token);
        if (redirectConnector != null) return redirectConnector(token);
        var endpoints = redirect.Successor.Endpoints;
        var endpoint = endpoints[attempt % endpoints.Count];
        hostAddress = endpoint;
        expectedFingerprint = redirect.Successor.Fingerprint;
        return options.ConnectEndpoint!(endpoint, token);
    }

    private async Task<bool> PrepareRedirectAsync(CancellationToken token)
    {
        redirectPrepared = true;
        var handoff = redirect!;
        hostAddress = handoff.Successor.Endpoints[0];
        expectedFingerprint = handoff.Successor.Fingerprint;
        if (options.OnHandoff == null) return options.ConnectEndpoint != null;
        try
        {
            redirectConnector = await options.OnHandoff(handoff, token).ConfigureAwait(false);
            return redirectConnector != null || options.ConnectEndpoint != null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Emit(new ProtocolError(ClientCodes.BadPromotion)); // the successor's process could not promote; nobody else will host this game from here
            return false;
        }
    }

    private async Task RunConnectionAsync(ITransport current, CancellationTokenSource timer, CancellationToken lifetimeToken)
    {
        transport = current;
        connectionSettled = false;
        awaitingSnapshot = false;
        CloseStandbyStore(); // each connection subscribes anew, resuming from what is on disk
        var token = timer.Token;
        using var keepAlive = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task keepAliveTask = Task.CompletedTask;
        try
        {
            if (!everJoined) SetState(ClientSessionState.Joining);
            await SendAsync(new Hello
            {
                Protocol = ProtocolVersion.Current,
                ClientVersion = options.ClientVersion,
                RulesetHash = options.RulesetHash,
                ModList = options.ModList,
                ModSync = options.ModSync != null ? true : null,
                ModChecksums = options.ModSync != null && options.ModChecksums != null ? new Dictionary<string, string>(options.ModChecksums) : null,
                GameId = gameId != null && knownEpoch > 0 ? gameId : null,
                KnownEpoch = gameId != null && knownEpoch > 0 ? knownEpoch : null,
                HostMigration = options.AdvertiseHostMigration ? true : null,
                EpochProof = gameId != null && knownEpoch > 0 && epochProof is { } held && held.Epoch == knownEpoch && string.Equals(held.GameId, gameId, StringComparison.Ordinal) ? held : null
            }, token).ConfigureAwait(false);

            keepAliveTask = RunKeepAliveAsync(keepAlive.Token);
            while (!stopped)
            {
                string? frame;
                using (var silence = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    if (connectionSettled) silence.CancelAfter(options.HostSilenceTimeout);
                    try { frame = await current.ReceiveAsync(silence.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (silence.IsCancellationRequested && !token.IsCancellationRequested) { break; }
                }
                if (frame == null) break;
                if (!await HandleAsync(ProtocolCodec.Decode(frame), token).ConfigureAwait(false)) break;
            }
        }
        catch (OperationCanceledException) when (!lifetimeToken.IsCancellationRequested) { handshakeTimedOut = true; }
        catch (OperationCanceledException) { }
        catch (Exception e) when (IsConnectionGone(e)) { /* a dead transport ends this connection; the loop decides about reconnecting */ }
        catch (Exception)
        {
            // A hostile or buggy host (undecodable frame, handler fault) ends this connection like a dead one, but the UI gets told.
            Emit(new ProtocolError(ClientCodes.InternalError));
        }
        finally
        {
            transport = null;
            CloseStandbyStore();
            await keepAlive.CancelAsync().ConfigureAwait(false);
            try { await keepAliveTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            try { await current.CloseAsync().ConfigureAwait(false); } catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
            queries.FailAll();
        }
    }

    private async Task RunKeepAliveAsync(CancellationToken token)
    {
        if (options.KeepAliveInterval == Timeout.InfiniteTimeSpan) return;
        using var ticks = new PeriodicTimer(options.KeepAliveInterval);
        try { while (await ticks.WaitForNextTickAsync(token).ConfigureAwait(false)) if (connectionSettled) await SendAsync(new Ping(), token).ConfigureAwait(false); }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException) { /* the connection ended; the receive loop reports it */ }
    }

    /// <summary>Handles one message; false ends this connection.</summary>
    private async Task<bool> HandleAsync(Message message, CancellationToken token)
    {
        switch (message)
        {
            case Welcome welcome:
                return await HandleWelcomeAsync(welcome, token).ConfigureAwait(false);
            case JoinOk ok:
                if (ok.Successor != null) HandleSuccessor(ok.Successor); // before the join completes, so a caller that awaited the join sees it
                await HandleJoinOkAsync(token).ConfigureAwait(false);
                return true;
            case HostSuccessor successor:
                HandleSuccessor(successor);
                return true;
            case StandbyOffer offer:
                // Only an offer addressed to this player's own key counts, and nothing is accepted without the UI's answer.
                if (string.Equals(offer.KeyId, options.Identity.PlayerKey, StringComparison.Ordinal)) Emit(new StandbyOffered(offer.KeyId));
                return true;
            case StandbyStatus standby:
                Emit(new StandbyStatusChanged(standby));
                if (string.Equals(standby.State, StandbyStates.None, StringComparison.Ordinal) || string.Equals(standby.State, StandbyStates.Offered, StringComparison.Ordinal)) EndStandbyReplication();
                else await SubscribeStandbyAsync(standby.GameId, token).ConfigureAwait(false);
                return true;
            case StandbyRevoke:
                EndStandbyReplication();
                Emit(new StandbyRevoked());
                return true;
            case StandbyMeta or StandbySnapshot or StandbyEntry:
                await HandleStandbyFrameAsync(message, token).ConfigureAwait(false);
                return true;
            case JoinRefused refused:
                await EndAsync(refused.Code, refused: true).ConfigureAwait(false);
                return false;
            case ModManifest manifest:
                return await HandleModManifestAsync(manifest, token).ConfigureAwait(false);
            case LobbyState lobby:
                Lobby = lobby;
                connectionSettled = true;
                Emit(new LobbyUpdated(lobby));
                return true;
            case LobbyChat chat:
                Emit(new LobbyChatReceived(chat.Sender, chat.Text));
                return true;
            case MapPingNotice ping:
                Emit(new MapPingReceived(ping));
                return true;
            case GameStarting:
                gameStarted = true;
                awaitingSnapshot = true;
                SetState(ClientSessionState.Syncing);
                Emit(new GameStarted());
                return true;
            case Snapshot snapshot:
                await HandleSnapshotAsync(snapshot).ConfigureAwait(false);
                return !stopped;
            case ViewDelta delta:
                await HandleDeltaAsync(delta).ConfigureAwait(false);
                return true;
            case TurnStatus status:
                TurnStatus = status;
                Emit(new TurnStatusChanged(status));
                return true;
            case CommandResult result:
                HandleCommandResult(result);
                return true;
            case QueryResult result:
                queries.Complete(result);
                return true;
            case HostClosing:
                Emit(new HostClosed());
                return true;
            case HostHandoff handoff:
                HandleHandoff(handoff);
                return true;
            case Error error:
                Emit(new ProtocolError(error.Code));
                if (string.Equals(error.Code, ErrorCodes.StandbyViolation, StringComparison.Ordinal)) ScheduleStandbyResubscribe(token);
                return true;
            case Pong:
                if (standbyResubscribeDue) await ResubscribeStandbyAsync(token).ConfigureAwait(false);
                return true;
            default:
                Emit(new ProtocolError(ErrorCodes.InvalidMessage));
                return true;
        }
    }

    /// <summary>
    /// T273: the host quit cleanly and named its successor. It came over the authenticated host channel, but is still checked: the next epoch of the game this session is in, and exactly the successor this host
    /// announced earlier (same key and fingerprint, endpoints among the announced ones). Anything else is not followed (the session behaves as for a plain <see cref="HostClosing"/>).
    /// </summary>
    private void HandleHandoff(HostHandoff handoff)
    {
        var announced = Successor?.Successor;
        var acceptable = (options.ConnectEndpoint != null || options.OnHandoff != null)
            && announced != null && Successor!.Epoch == connectionEpoch && handoff.Epoch == connectionEpoch + 1
            && (gameId == null || string.Equals(gameId, handoff.GameId, StringComparison.Ordinal))
            && handoff.GameId.Length is > 0 and <= 200
            && string.Equals(announced.KeyId, handoff.Successor.KeyId, StringComparison.Ordinal) && HostKey.Matches(announced.Fingerprint, handoff.Successor.Fingerprint)
            && handoff.Successor.Endpoints.Count is > 0 and <= 8 && handoff.Successor.Endpoints.All(it => announced.Endpoints.Contains(it, StringComparer.Ordinal));
        if (!acceptable)
        {
            Emit(new ProtocolError(ErrorCodes.InvalidMessage));
            Emit(new HostClosed());
            return;
        }
        gameId ??= handoff.GameId;
        redirect = handoff;
        redirectPrepared = false;
        redirectAnnounced = false;
        redirectConnector = null;
        Emit(new HostHandedOff(handoff));
    }

    /// <summary>What the last mod sync installed (set before the session ends with <see cref="ModSyncCodes.Synced"/>); the caller hot-loads it and joins again.</summary>
    public ModSyncOutcome? ModSyncResult { get; private set; }

    private async Task<bool> HandleModManifestAsync(ModManifest manifest, CancellationToken token)
    {
        var handler = options.ModSync;
        if (handler == null || connectionSettled || everJoined)
        {
            await EndAsync(JoinRefusedCodes.RulesetMismatch, refused: true).ConfigureAwait(false);
            return false;
        }
        // The player may think about the download as long as they like; each file has its own limit.
        handshakeTimer?.CancelAfter(Timeout.InfiniteTimeSpan);
        ModSyncOutcome outcome;
        try
        {
            outcome = await handler.SyncAsync(manifest, (mod, path, cancellation) => FetchModFileAsync(mod, path, cancellation), token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            outcome = new ModSyncOutcome(e is ModSyncException sync ? sync.Code : ModSyncCodes.Failed, null, []);
        }

        if (!outcome.Success)
        {
            await EndAsync(outcome.Code ?? ModSyncCodes.Failed, refused: true).ConfigureAwait(false);
        }
        else if (outcome.Installed.Count == 0)
        {
            // Every mod already is the host's and the rules still differ (a built-in ruleset): nothing a download can fix.
            await EndAsync(JoinRefusedCodes.RulesetMismatch, refused: true).ConfigureAwait(false);
        }
        else
        {
            ModSyncResult = outcome;
            await EndAsync(ModSyncCodes.Synced, refused: false).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>Asks the host for one file and waits for its answer on the connection's own reader (the receive loop is busy inside the sync, so nothing else reads).</summary>
    private async Task<byte[]> FetchModFileAsync(string mod, string path, CancellationToken cancellation)
    {
        var current = transport ?? throw new ModSyncException(ModSyncCodes.Failed, "not connected");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(options.ModFileTimeout);
        try
        {
            await SendAsync(new ModFileRequest { Mod = mod, Path = path }, limit.Token).ConfigureAwait(false);
            while (true)
            {
                var frame = await current.ReceiveAsync(limit.Token).ConfigureAwait(false) ?? throw new ModSyncException(ModSyncCodes.Failed, "connection closed");
                switch (ProtocolCodec.Decode(frame))
                {
                    case ModFileData data when data.Mod == mod && data.Path == path:
                        if (data.Data.Length > ModSyncRules.MaxFileBytes / 3 * 4 + 8) throw new ModSyncException(ModSyncCodes.BadFile, "file too large");
                        try { return Convert.FromBase64String(data.Data); }
                        catch (FormatException) { throw new ModSyncException(ModSyncCodes.BadFile, "bad encoding"); }
                    case Pong:
                        continue;
                    case Error:
                        throw new ModSyncException(ModSyncCodes.Failed, "host refused the file");
                    default:
                        throw new ModSyncException(ModSyncCodes.BadFile, "unexpected message");
                }
            }
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !cancellation.IsCancellationRequested)
        {
            throw new ModSyncException(ModSyncCodes.Failed, "host did not answer");
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            throw new ModSyncException(ModSyncCodes.Failed, "connection lost");
        }
    }

    private async Task<bool> HandleWelcomeAsync(Welcome welcome, CancellationToken token)
    {
        if (!ProtocolVersion.IsCompatible(welcome.Protocol))
        {
            await EndAsync(ClientCodes.ProtocolMismatch, refused: true).ConfigureAwait(false);
            return false;
        }
        if (welcome.Standby == true)
        {
            await EndAsync(ClientCodes.NotHostYet, refused: true).ConfigureAwait(false);
            return false;
        }
        var refusal = await CheckHostKeyAsync(welcome.HostKeyFingerprint).ConfigureAwait(false);
        if (refusal != null)
        {
            await EndAsync(refusal, refused: true).ConfigureAwait(false);
            return false;
        }

        // T269: the host key is trusted by now. A host below the epoch this client has seen is a superseded one: a hard stop, like a changed host key.
        var epoch = welcome.Epoch ?? 0;
        if (epoch < knownEpoch)
        {
            await EndAsync(ClientCodes.StaleEpoch, refused: true).ConfigureAwait(false);
            return false;
        }
        pendingProof = null;
        if (redirect != null && !redirectAnnounced)
        {
            // Redirected by a hand-off: the host here must be the successor the old host named, promoted to exactly the announced epoch, and say so with the successor's own signature.
            var proof = welcome.Promotion;
            if (epoch != redirect.Epoch || proof == null || !PlayerKeys.VerifyPromotion(proof) || proof.Epoch != redirect.Epoch
                || !string.Equals(proof.GameId, redirect.GameId, StringComparison.Ordinal) || !string.Equals(proof.KeyId, redirect.Successor.KeyId, StringComparison.Ordinal)
                || !HostKey.Matches(proof.Fingerprint, welcome.HostKeyFingerprint))
            {
                await EndAsync(ClientCodes.BadPromotion, refused: true).ConfigureAwait(false);
                return false;
            }
            pendingProof = proof;
        }
        connectedHostFingerprint = welcome.HostKeyFingerprint;
        connectionEpoch = epoch;
        pendingEpoch = epoch; // remembered only once the host accepted our seat (JoinOk), so a refused join or a wrong game cannot move the stored epoch

        await SendAsync(new Join
        {
            Password = options.Password,
            PlayerKey = options.Identity.PlayerKey,
            DisplayName = options.DisplayName,
            InviteToken = options.InviteToken
        }, token).ConfigureAwait(false);
        await SendAsync(new Auth { Proof = options.Identity.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }, token).ConfigureAwait(false);
        return true;
    }

    /// <summary>The refusal code when the host's key must not be trusted, else null. A changed key is a hard stop, never a prompt.</summary>
    private async Task<string?> CheckHostKeyAsync(string presented)
    {
        var known = options.KnownHosts;
        var address = hostAddress;
        var pinnedTrust = known?.Check(address, presented) ?? HostTrust.Unknown;
        if (pinnedTrust == HostTrust.Mismatch) return ClientCodes.HostKeyChanged;

        if (!string.IsNullOrEmpty(expectedFingerprint))
        {
            if (!HostKey.Matches(expectedFingerprint, presented)) return ClientCodes.HostKeyMismatch;
        }
        else if (pinnedTrust == HostTrust.Unknown && known != null && known.IsFingerprintKnown(presented))
        {
            // T338: this key was pinned under another address (the host moved to another relay, room or port): the key is what was trusted, so the new address gets the same pin.
            if (known.Pin(address, presented)) options.PersistKnownHosts?.Invoke(known);
            return null;
        }
        else if (pinnedTrust != HostTrust.Trusted)
        {
            if (known != null)
            {
                if (options.TrustPrompt == null) return ClientCodes.HostUntrusted;
                // The user may take as long as they like: the handshake clock stops while the prompt is open.
                handshakeTimer?.CancelAfter(Timeout.InfiniteTimeSpan);
                bool trusted;
                try { trusted = await options.TrustPrompt(address, presented).ConfigureAwait(false); }
                catch (Exception e) when (e is not OperationCanceledException) { trusted = false; } // a failing prompt is a "no", never a crash
                handshakeTimer?.CancelAfter(options.HandshakeTimeout);
                if (!trusted) return ClientCodes.HostUntrusted;
            }
            else if (!options.AllowUnpinnedHost)
            {
                return ClientCodes.HostUntrusted;
            }
        }

        if (known != null && pinnedTrust == HostTrust.Unknown && known.Pin(address, presented)) options.PersistKnownHosts?.Invoke(known);
        return null;
    }

    private async Task HandleJoinOkAsync(CancellationToken token)
    {
        handshakeTimer?.CancelAfter(Timeout.InfiniteTimeSpan);
        var firstJoin = !everJoined;
        everJoined = true;
        if (pendingEpoch > knownEpoch)
        {
            knownEpoch = pendingEpoch;
            try { options.PersistKnownEpoch?.Invoke(pendingEpoch); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a store that cannot write must not abort the join; the epoch is kept in memory */ }
        }
        if (pendingProof != null)
        {
            epochProof = pendingProof;
            try { options.PersistEpochProof?.Invoke(pendingProof); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* kept in memory */ }
            pendingProof = null;
        }
        if (redirect != null && !redirectAnnounced)
        {
            // The redirect is done. The session stays on the successor (a later drop reconnects there, not to the old host).
            redirectAnnounced = true;
            Emit(new HostChanged(redirect.Epoch));
        }

        // The state moves before JoinAsync completes, so a caller that awaits the join never observes the stale Joining state.
        var snapshot = Game.Snapshot;
        if (snapshot == null && !gameStarted)
        {
            SetState(ClientSessionState.Lobby);
            joined.TrySetResult(new JoinResult(true, null));
            return;
        }

        SetState(ClientSessionState.Syncing);
        joined.TrySetResult(new JoinResult(true, null));
        if (snapshot == null)
        {
            awaitingSnapshot = true;
            if (!firstJoin) await SendAsync(new ResyncRequest(), token).ConfigureAwait(false);
        }
        else if (gameId != null)
        {
            await SendAsync(new Resume { GameId = gameId, LastSeq = snapshot.Seq, LastViewHash = snapshot.ViewHash ?? "" }, token).ConfigureAwait(false);
        }
        else
        {
            awaitingSnapshot = true;
            await SendAsync(new ResyncRequest(), token).ConfigureAwait(false);
        }
        await ResendPendingAsync(token).ConfigureAwait(false);
    }

    private async Task ResendPendingAsync(CancellationToken token)
    {
        List<PendingCommand> all;
        // Submission order, not ClientCmdId text order ("10" sorts before "2"): the host applies a player's commands in the order issued.
        lock (gate) all = [.. pending.Values.OrderBy(it => it.Ordinal)];
        foreach (var entry in all) await SendAsync(entry.Message, token).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ mirror

    private async Task HandleSnapshotAsync(Snapshot message)
    {
        switch (Game.Replace(message))
        {
            case SnapshotOutcome.Accepted:
                awaitingSnapshot = false;
                connectionSettled = true;
                desyncAnnounced = false;
                queries.FailAll(); // an answer computed against the old state must not be applied to the new one
                SetState(ClientSessionState.InGame);
                Emit(new SnapshotReplaced(Game.Snapshot!));
                if (message.RolledBack == true) Emit(new HostRolledBack(Math.Max(0, message.LostCommands ?? 0)));
                lock (gate)
                {
                    resyncWaiter?.TrySetResult();
                    resyncWaiter = null;
                }
                break;
            case SnapshotOutcome.Malformed:
                await RejectSnapshotAsync("malformed-snapshot").ConfigureAwait(false);
                break;
            default:
                await RejectSnapshotAsync("snapshot-hash-mismatch").ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// A snapshot the host sent was unusable. Nobody may keep waiting for it: the resync waiter faults, and when there is no mirror at all
    /// (first sync) the session ends, since a host that cannot produce a valid first snapshot never will. Otherwise the old mirror stays and the caller may retry.
    /// </summary>
    private async Task RejectSnapshotAsync(string reason)
    {
        awaitingSnapshot = false;
        Emit(new Desynchronized(reason));
        lock (gate)
        {
            resyncWaiter?.TrySetException(new InvalidOperationException(ClientCodes.SnapshotRejected));
            resyncWaiter = null;
        }
        if (Game.Snapshot == null) await EndAsync(ClientCodes.SnapshotRejected, refused: false).ConfigureAwait(false);
        else SetState(ClientSessionState.InGame);
    }

    private async Task HandleDeltaAsync(ViewDelta delta)
    {
        if (awaitingSnapshot) return; // a snapshot is on its way and will supersede this
        var outcome = Game.ApplyDelta(delta);
        switch (outcome)
        {
            case DeltaOutcome.Applied:
                SettleResume();
                Emit(new DeltaApplied(delta.Seq, delta.Events, Game.Snapshot!));
                break;
            case DeltaOutcome.Duplicate:
                SettleResume();
                break;
            default:
                await RequestResyncAsync(DescribeOutcome(outcome)).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>The host answered a Resume with deltas (no snapshot): the mirror is current again, so the connection counts as settled and the outage is over.</summary>
    private void SettleResume()
    {
        connectionSettled = true;
        desyncAnnounced = false;
        if (state == ClientSessionState.Syncing) SetState(ClientSessionState.InGame);
    }

    private static string DescribeOutcome(DeltaOutcome outcome) => outcome switch
    {
        DeltaOutcome.Gap => "sequence-gap",
        DeltaOutcome.NoSnapshot => "delta-before-snapshot",
        DeltaOutcome.NoDiff => "delta-without-diff",
        DeltaOutcome.BadDiff => "malformed-diff",
        _ => "view-hash-mismatch"
    };

    private async Task RequestResyncAsync(string reason)
    {
        if (awaitingSnapshot) return;
        awaitingSnapshot = true;
        queries.FailAll();
        Emit(new Desynchronized(reason));
        try { await SendAsync(new ResyncRequest()).ConfigureAwait(false); }
        catch (Exception e) when (IsConnectionGone(e)) { /* connection lost: the reconnect resyncs */ }
    }

    private void HandleCommandResult(CommandResult result)
    {
        PendingCommand? entry;
        lock (gate)
        {
            if (!pending.Remove(result.ClientCmdId, out entry)) return; // duplicate answer (retry, resume) or cancelled command
        }
        var outcome = new CommandOutcome(result.ClientCmdId, result.Accepted, result.Code, result.Seq);
        entry.Completion.TrySetResult(outcome);
        Emit(new CommandCompleted(outcome));
    }

    // ------------------------------------------------------------ plumbing

    /// <summary>A send that failed because the connection died (closed transport, broken pipe, disposed stream): not a command failure, the reconnect resends.</summary>
    private static bool IsConnectionGone(Exception e) => e is InvalidOperationException or IOException or ObjectDisposedException;

    private async ValueTask SendAsync(Message message, CancellationToken cancellationToken = default)
    {
        var target = transport ?? throw new InvalidOperationException("Not connected");
        var frame = ProtocolCodec.Encode(message);
        await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await target.SendAsync(frame, cancellationToken).ConfigureAwait(false); }
        finally { sendLock.Release(); }
    }

    private void SetState(ClientSessionState next)
    {
        if (state == next) return;
        state = next;
        Emit(new StateChanged(next));
    }

    private void Emit(ClientEvent clientEvent) => events.Writer.TryWrite(clientEvent);

    private void FailWaiters()
    {
        queries.FailAll();
        lock (gate)
        {
            resyncWaiter?.TrySetException(new InvalidOperationException(ClientCodes.ConnectionLost));
            resyncWaiter = null;
        }
    }

    /// <summary>The session is over for good: answers everything still waiting and reports why.</summary>
    private async Task EndAsync(string reason, bool refused)
    {
        List<PendingCommand> all;
        lock (gate)
        {
            if (stopped) return;
            stopped = true;
            all = [.. pending.Values];
            pending.Clear();
        }
        joined.TrySetResult(new JoinResult(false, reason));
        foreach (var entry in all) entry.Completion.TrySetResult(new CommandOutcome(entry.Message.ClientCmdId, false, ClientCodes.SessionClosed, null));
        FailWaiters();
        if (refused) Emit(new JoinRefusedEvent(reason));
        SetState(ClientSessionState.Disconnected);
        Emit(new SessionEnded(reason));
        events.Writer.TryComplete();
        await lifetime.CancelAsync().ConfigureAwait(false);
    }

    private async Task StopAsync(string reason)
    {
        await EndAsync(reason, refused: false).ConfigureAwait(false);
        var current = transport;
        if (current != null) await current.CloseAsync().ConfigureAwait(false);
        var running = loop;
        if (running != null) await running.ConfigureAwait(false);
    }
}
