using System.Text.Json;
using System.Threading.Channels;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Models.Metadata;
using Unciv.Net.Identity;
using Unciv.Net.Mods;
using Unciv.Net.Turns;
using Unciv.Queries.Setup;

namespace Unciv.Net.Lobby;

public enum LobbyStatus
{
    InLobby,
    Starting,
    Started,
    Closed
}

public sealed record LobbyStartResult(
    GameInfo Game,
    GameSetupInfo Setup,
    IReadOnlyList<Slot> Slots
);

/// <summary>
/// Authoritative lobby state machine (SERVER.md 6.5).
/// Manages player slots, nation picking, ready-up states, chat, admission,
/// ruleset verification, and game start transition.
/// </summary>
public sealed class LobbyHost : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, LobbySession> sessions = new(StringComparer.Ordinal);
    private readonly ISecureRandom random;
    private readonly IClock clock;
    private readonly List<Slot> slots = [];
    private bool disposed;

    public LobbyHost(
        LobbySettings? settings = null,
        SlotBindings? bindings = null,
        LobbyAdmission? admission = null,
        string? hostFingerprint = null,
        ISecureRandom? random = null,
        IClock? clock = null,
        string hostName = "Host")
    {
        Settings = settings ?? new LobbySettings();
        Bindings = bindings ?? new SlotBindings();
        this.random = random ?? SystemSecureRandom.Instance;
        this.clock = clock ?? SystemClock.Instance;
        HostFingerprint = hostFingerprint ?? "ABCDE-FGHIJ-KLMNO-PQRST-UVWXY-Z";
        HostName = hostName;

        Admission = admission ?? CreateDefaultAdmission(Settings, this.random);

        InitializeSlots();
    }

    public LobbySettings Settings { get; private set; }
    public SlotBindings Bindings { get; }
    public LobbyAdmission Admission { get; }
    public string HostFingerprint { get; }
    public string HostName { get; }
    public LobbyStatus Status { get; private set; } = LobbyStatus.InLobby;
    public IReadOnlyList<Slot> Slots => slots;

    private static LobbyAdmission CreateDefaultAdmission(LobbySettings settings, ISecureRandom random)
    {
        var password = settings.Password != null ? JoinPassword.Create(settings.Password, random) : null;
        var tokens = new InviteTokens(TimeProvider.System, random);
        var limiter = new AttemptLimiter(TimeProvider.System);
        return new LobbyAdmission(password, inviteOnly: false, tokens, limiter);
    }

    private void InitializeSlots()
    {
        slots.Clear();
        var count = Math.Max(2, Settings.MaxMajorCivs);
        for (var i = 0; i < count; i++)
        {
            slots.Add(new Slot
            {
                Index = i,
                Kind = SlotKind.Open,
                Nation = Constants.Random
            });
        }
    }

    private sealed class LobbySession
    {
        public required string SessionId { get; init; }
        public required ITransport Transport { get; init; }
        public HostChallenge? Challenge { get; set; }
        public string? PendingPlayerKey { get; set; }
        public string? PendingDisplayName { get; set; }
        public string? PlayerKey { get; set; }
        public string? DisplayName { get; set; }
        public bool IsAuthenticated { get; set; }
        public int? SlotIndex { get; set; }

        /// <summary>T247: the client announced it can take mod files and its rules differ, so this connection ends in a <see cref="ModManifest"/> instead of a seat.</summary>
        public bool ModSync { get; set; }

        /// <summary>T247: the manifest this connection was sent (null before); only its files are served, each once.</summary>
        public ModManifest? ModManifestSent { get; set; }

        public HashSet<string> ModFilesServed { get; } = new(StringComparer.Ordinal);

        /// <summary>Set once this seat's transport belongs to the running game: the lobby reader (the only reader of the socket) then forwards every frame here.</summary>
        public Channel<string>? Handoff { get; set; }
        public ITransport? GameTransport { get; set; }

        /// <summary>True once the game host reads this seat's hand-over stream (it answers pings itself from then on).</summary>
        public volatile bool GameReading;
    }

    /// <summary>Replaces <see cref="NewGame.Start"/> as the creator of the game (tests make the creation fail or stall); null uses the engine.</summary>
    public Func<GameSetupInfo, long, long, GameInfo>? GameFactory { get; set; }

    private TaskCompletionSource startSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The game host now reads the seat's hand-over stream; until then the lobby reader answers its pings (the game actor can be busy seating others).</summary>
    public void MarkGameReading(string sessionId)
    {
        lock (gate)
        {
            if (sessions.TryGetValue(sessionId, out var s)) s.GameReading = true;
        }
    }

    /// <summary>
    /// After the start the host replaced the player of <paramref name="civId"/> (kick, or rebind to <paramref name="newPlayerKey"/>): the lobby's binding follows, so the old key is refused
    /// from now on and the new key is seated on its next connection. Connections still held by the old player are closed. Returns false when no human slot has that nation.
    /// </summary>
    public async Task<bool> ReleaseSeatAsync(string civId, string? newPlayerKey)
    {
        List<ITransport> dropped = [];
        lock (gate)
        {
            var slot = slots.FirstOrDefault(s => s.Kind == SlotKind.Human && string.Equals(s.Nation, civId, StringComparison.Ordinal));
            if (slot == null) return false;
            Bindings.Clear(slot.Index);
            slot.PlayerKeyId = newPlayerKey != null && Bindings.TryBind(slot.Index, newPlayerKey) ? newPlayerKey : null;
            slot.Connected = false;
            slot.SessionId = null;
            slot.Transport = null;
            foreach (var session in sessions.Values.Where(x => x.SlotIndex == slot.Index).ToList())
            {
                sessions.Remove(session.SessionId);
                dropped.Add(session.Transport);
            }
        }
        foreach (var transport in dropped) await CloseQuietlyAsync(transport).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// What the game host reads once a seat is handed over: the frames the lobby reader keeps pulling off the real transport.
    /// A single reader per socket means no inbound frame can be taken by the wrong side.
    /// </summary>
    private sealed class HandoffTransport(ITransport inner, Channel<string> frames) : ITransport
    {
        public bool IsOpen => inner.IsOpen;
        public ValueTask SendAsync(string frame, CancellationToken cancellationToken = default) => inner.SendAsync(frame, cancellationToken);
        public ValueTask CloseAsync()
        {
            frames.Writer.TryComplete(); // the game side let go: a lobby reader blocked on a full channel must not wait forever
            return inner.CloseAsync();
        }

        public ValueTask DisposeAsync()
        {
            frames.Writer.TryComplete();
            return inner.DisposeAsync();
        }

        public async ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await frames.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Raised (after <see cref="JoinOk"/>) when a player whose key already holds a seat authenticates after the game started.
    /// The handler seats the connection in the running game; the transport is the hand-over one the game host must read.
    /// Brand-new keys are refused after the start (<see cref="JoinRefusedCodes.GameAlreadyStarted"/>): join-in-progress for new
    /// players is not supported even when <see cref="LobbySettings.AllowJoinInProgress"/> is set (T250).
    /// </summary>
    public Func<LateSeat, Task>? LateSeated { get; set; }

    /// <summary>A late seat captured under the lobby lock: the connection's own session id and hand-over transport travel together, so a newer connection of the same key can never be mixed with an older one.</summary>
    public sealed record LateSeat(string SessionId, string Nation, string? PlayerKeyId, ITransport Transport);

    /// <summary>The transport the game host must read for a seat that was connected when the game started (null if it was not handed over).</summary>
    public ITransport? GameTransportFor(string? sessionId)
    {
        if (sessionId == null) return null;
        lock (gate)
        {
            return sessions.TryGetValue(sessionId, out var s) ? s.GameTransport : null;
        }
    }

    private static async Task CloseQuietlyAsync(ITransport transport)
    {
        try { await transport.CloseAsync().ConfigureAwait(false); }
        catch (Exception) { /* already gone */ }
    }

    /// <summary>Bounded so a client flooding frames while the game actor is busy is held back by the socket (TCP backpressure), not buffered without limit.</summary>
    private const int HandoffCapacity = 256;

    private static ITransport HandOff(LobbySession session)
    {
        session.Handoff = Channel.CreateBounded<string>(new BoundedChannelOptions(HandoffCapacity) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var gameTransport = new HandoffTransport(session.Transport, session.Handoff);
        session.GameTransport = gameTransport;
        return gameTransport;
    }

    /// <summary>
    /// Attaches an incoming transport connection to the lobby, reading and dispatching frames until closed.
    /// </summary>
    public async Task AttachConnectionAsync(ITransport transport, CancellationToken cancellationToken = default)
    {
        var sessionId = Base64Url.Encode(random.Bytes(16));
        var session = new LobbySession
        {
            SessionId = sessionId,
            Transport = transport
        };

        lock (gate)
        {
            sessions[sessionId] = session;
        }

        try
        {
            while (transport.IsOpen && !cancellationToken.IsCancellationRequested && Status != LobbyStatus.Closed)
            {
                var frame = await transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (frame == null) break;

                ChannelWriter<string>? handoff;
                lock (gate)
                {
                    handoff = session.Handoff?.Writer;
                }
                if (handoff != null)
                {
                    if (!session.GameReading) await AnswerEarlyPingAsync(session, frame).ConfigureAwait(false);
                    await handoff.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var message = ProtocolCodec.Decode(frame);
                await HandleMessageAsync(session, message).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Ignore transport errors on disconnect
        }
        finally
        {
            Channel<string>? handoff;
            lock (gate)
            {
                handoff = session.Handoff;
            }
            if (handoff != null)
            {
                // The game host owns this seat now; it notices the end of stream itself.
                handoff.Writer.TryComplete();
                lock (gate)
                {
                    sessions.Remove(session.SessionId);
                }
            }
            else
            {
                await HandleDisconnectAsync(session).ConfigureAwait(false);
            }
        }
    }

    /// <summary>A seated player's ping while the game actor is still busy seating others: answered here (the game answers the forwarded copy too, a second Pong is harmless).</summary>
    private static async Task AnswerEarlyPingAsync(LobbySession session, string frame)
    {
        if (frame.Length > 256) return;
        try
        {
            if (ProtocolCodec.Decode(frame) is Ping) await SendAsync(session.Transport, new Pong()).ConfigureAwait(false);
        }
        catch (Exception) { /* not a ping, or a dead socket: the game side judges the frame */ }
    }

    /// <summary>Processes a decoded protocol message from a session.</summary>
    public async Task HandleMessageAsync(string sessionId, Message message)
    {
        LobbySession? session;
        lock (gate)
        {
            sessions.TryGetValue(sessionId, out session);
        }
        if (session != null)
        {
            await HandleMessageAsync(session, message).ConfigureAwait(false);
        }
    }

    private async Task HandleMessageAsync(LobbySession session, Message message)
    {
        switch (message)
        {
            case Hello hello:
                await HandleHelloAsync(session, hello).ConfigureAwait(false);
                break;
            case Join join:
                await HandleJoinAsync(session, join).ConfigureAwait(false);
                break;
            case Auth auth:
                await HandleAuthAsync(session, auth).ConfigureAwait(false);
                break;
            case LobbySet set:
                await HandleLobbySetAsync(session, set).ConfigureAwait(false);
                break;
            case LobbyChat chat:
                await HandleLobbyChatAsync(session, chat).ConfigureAwait(false);
                break;
            case ModFileRequest request:
                await HandleModFileRequestAsync(session, request).ConfigureAwait(false);
                break;
            case Leave:
                await HandleDisconnectAsync(session).ConfigureAwait(false);
                break;
            case Ping:
                await SendAsync(session.Transport, new Pong()).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleHelloAsync(LobbySession session, Hello hello)
    {
        if (hello.Protocol != ProtocolVersion.Current)
        {
            await SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.ProtocolMismatch }).ConfigureAwait(false);
            return;
        }

        var expectedRulesetHash = Settings.ComputeRulesetHash();
        if (!string.Equals(hello.RulesetHash, expectedRulesetHash, StringComparison.OrdinalIgnoreCase))
        {
            if (hello.ModSync != true)
            {
                await SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.RulesetMismatch }).ConfigureAwait(false);
                return;
            }
            // T247: nothing about the mods is revealed yet. The handshake goes on (host key check, password, key proof) and only then does the client get the manifest.
            lock (gate)
            {
                session.ModSync = true;
            }
        }
        else if (hello.ModSync == true && hello.ModChecksums != null && ModsDifferFrom(hello.ModChecksums))
        {
            // The rule names agree but a mod's content does not (the hash covers names only): same path as a mismatch.
            lock (gate)
            {
                session.ModSync = true;
            }
        }

        var challenge = new HostChallenge(HostFingerprint, random);
        lock (gate)
        {
            session.Challenge = challenge;
        }

        await SendAsync(session.Transport, new Welcome
        {
            Protocol = ProtocolVersion.Current,
            SessionId = challenge.SessionId,
            Challenge = challenge.Challenge,
            HostKeyFingerprint = HostFingerprint,
            RequiresPassword = Admission.RequiresPassword
        }).ConfigureAwait(false);
    }

    /// <summary>The folders the host serves mods from (the loaded mods' own folders); tests replace it.</summary>
    public IModFolders ModFolders { get; set; } = RulesetCacheModFolders.Instance;

    /// <summary>True when a downloadable mod of the host is missing from, or differs from, the client's reported mods.</summary>
    private bool ModsDifferFrom(Dictionary<string, string> clientChecksums) =>
        CurrentModManifest() is { } manifest
        && manifest.Entries.Any(it => !clientChecksums.TryGetValue(it.Name, out var theirs) || !string.Equals(theirs, it.Checksum, StringComparison.Ordinal));

    private (string Key, ModManifest? Manifest)? modManifestCache;
    private (string Key, long UntilMillis)? noModsToOffer;

    /// <summary>How long "nothing to offer" is remembered, so a flood of Hellos cannot make the host re-read its mods every time.</summary>
    private const long NegativeCacheMillis = 5_000;

    /// <summary>The manifest of this lobby's downloadable mods, or null when there is nothing the host can offer. Built once per set of rules; the files are re-verified when served.</summary>
    private ModManifest? CurrentModManifest()
    {
        string ruleset;
        List<string> mods;
        lock (gate)
        {
            ruleset = Settings.Ruleset;
            mods = [.. Settings.Mods];
        }
        var key = ruleset + "\0" + string.Join("\0", mods);
        lock (gate)
        {
            if (modManifestCache is { } cached && cached.Key == key) return cached.Manifest;
            if (noModsToOffer is { } none && none.Key == key && clock.NowMillis < none.UntilMillis) return null;
        }

        ModManifest? manifest = null;
        var entries = new List<ModEntry>();
        var names = new List<string>();
        if (!ModSyncRules.IsBuiltinRuleset(ruleset)) names.Add(ruleset);
        names.AddRange(mods.Where(it => !names.Contains(it, StringComparer.Ordinal)));
        var usable = true;
        long total = 0;
        foreach (var name in names)
        {
            var folder = ModFolders.FolderOf(name);
            if (folder == null) continue;
            var entry = ModManifestBuilder.Build(name, folder);
            if (entry == null) { usable = false; break; }
            total += entry.Files.Sum(it => it.Size);
            entries.Add(entry);
        }
        if (usable && entries.Count is > 0 and <= ModSyncRules.MaxMods && total <= ModSyncRules.MaxTotalBytes)
        {
            manifest = new ModManifest { BaseRuleset = ruleset, Mods = mods, RulesetHash = Settings.ComputeRulesetHash(), Entries = entries };
        }
        lock (gate)
        {
            // Only a usable manifest is kept: "nothing to offer" is re-checked next time (the host may have fixed a mod meanwhile).
            modManifestCache = manifest == null ? null : (key, manifest);
            noModsToOffer = manifest == null ? (key, clock.NowMillis + NegativeCacheMillis) : null;
        }
        return manifest;
    }

    private async Task SendModManifestAsync(LobbySession session)
    {
        // One manifest per connection: the key proof is single-use (a repeated Auth fails with auth-failed before reaching here), which the tests pin.
        var manifest = CurrentModManifest();
        if (manifest == null)
        {
            await SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.ModSyncUnavailable }).ConfigureAwait(false);
            return;
        }
        lock (gate)
        {
            session.ModManifestSent = manifest;
        }
        await SendAsync(session.Transport, manifest).ConfigureAwait(false);
    }

    private async Task HandleModFileRequestAsync(LobbySession session, ModFileRequest request)
    {
        var manifest = session.ModManifestSent;
        var entry = manifest?.Entries.FirstOrDefault(it => string.Equals(it.Name, request.Mod, StringComparison.Ordinal));
        var listed = entry?.Files.FirstOrDefault(it => string.Equals(it.Path, request.Path, StringComparison.Ordinal));
        var folder = entry == null ? null : ModFolders.FolderOf(entry.Name);
        bool first;
        lock (gate)
        {
            first = listed != null && session.ModFilesServed.Add(request.Mod + "/" + request.Path);
        }

        // Only a listed file, once per connection, whose bytes still are the ones the manifest promised.
        var bytes = first && folder != null ? ModManifestBuilder.ReadFile(folder, request.Path) : null;
        if (listed == null || bytes == null || bytes.Length != listed.Size || !string.Equals(ModManifestBuilder.HashOf(bytes), listed.Sha256, StringComparison.Ordinal))
        {
            if (first && listed != null)
            {
                // The files changed after the manifest was built: drop it so the next joiner gets a fresh one.
                lock (gate)
                {
                    modManifestCache = null;
                }
            }
            await SendAsync(session.Transport, new Error { Code = ErrorCodes.ModFileRefused }).ConfigureAwait(false);
            return;
        }
        await SendAsync(session.Transport, new ModFileData { Mod = request.Mod, Path = request.Path, Data = Convert.ToBase64String(bytes) }).ConfigureAwait(false);
    }

    private async Task HandleJoinAsync(LobbySession session, Join join)
    {
        if (session.Challenge == null)
        {
            await SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.AuthFailed }).ConfigureAwait(false);
            return;
        }

        if (!PlayerKeys.IsValid(join.PlayerKey))
        {
            await SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.BadKey }).ConfigureAwait(false);
            return;
        }

        var admissionResult = session.ModSync
            ? Admission.AdmitForModSync(join.PlayerKey, join.Password, join.InviteToken)
            : Admission.Admit(join.PlayerKey, join.Password, join.InviteToken);
        if (admissionResult == AdmissionResult.RateLimited)
        {
            await SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.RateLimited }).ConfigureAwait(false);
            return;
        }
        if (admissionResult == AdmissionResult.BadCredentials)
        {
            await SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.BadCredentials }).ConfigureAwait(false);
            return;
        }

        lock (gate)
        {
            session.PendingPlayerKey = join.PlayerKey;
            session.PendingDisplayName = join.DisplayName;
        }
    }

    private async Task HandleAuthAsync(LobbySession session, Auth auth)
    {
        if (session.Challenge == null || session.PendingPlayerKey == null)
        {
            await SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.AuthFailed }).ConfigureAwait(false);
            return;
        }

        if (!session.Challenge.Verify(session.PendingPlayerKey, auth.Proof))
        {
            await SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.AuthFailed }).ConfigureAwait(false);
            return;
        }

        if (session.ModSync)
        {
            await SendModManifestAsync(session).ConfigureAwait(false);
            return;
        }

        // A known key arriving while the game is being created waits for the start, then is seated like any late reconnect.
        while (true)
        {
            Task? pendingStart = null;
            lock (gate)
            {
                if (Status == LobbyStatus.Starting && Bindings.SlotOf(session.PendingPlayerKey) is int pendingSeat && pendingSeat < slots.Count)
                    pendingStart = startSettled.Task;
            }
            if (pendingStart == null) break;
            await pendingStart.ConfigureAwait(false);
            await Task.Yield();
        }

        bool stateChanged = false;
        LateSeat? lateSeat = null;
        ITransport? replacedTransport = null;
        lock (gate)
        {
            // Decided in the same lock that seats the player: a start flipping the status in between can no longer let a new key take a free slot.
            if (Status is LobbyStatus.Starting or LobbyStatus.Started
                && (Status != LobbyStatus.Started || Bindings.SlotOf(session.PendingPlayerKey) is not int knownSeat || knownSeat >= slots.Count))
            {
                _ = SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.GameAlreadyStarted });
                return;
            }

            session.IsAuthenticated = true;
            session.PlayerKey = session.PendingPlayerKey;
            session.DisplayName = session.PendingDisplayName;

            var existingSlotIndex = Bindings.SlotOf(session.PlayerKey);
            if (existingSlotIndex.HasValue && existingSlotIndex.Value < slots.Count)
            {
                var slot = slots[existingSlotIndex.Value];
                if (Status == LobbyStatus.Started && slot.SessionId != null && slot.SessionId != session.SessionId
                    && sessions.TryGetValue(slot.SessionId, out var replaced))
                {
                    // The same key on a new connection replaces the old one: it must not keep a hand-over channel nobody reads.
                    sessions.Remove(replaced.SessionId);
                    replacedTransport = replaced.Transport;
                }
                slot.Connected = true;
                slot.Transport = session.Transport;
                slot.SessionId = session.SessionId;
                slot.DisplayName = session.DisplayName;
                session.SlotIndex = existingSlotIndex.Value;
                stateChanged = true;
                if (Status == LobbyStatus.Started)
                {
                    var gameTransport = HandOff(session);
                    lateSeat = new LateSeat(session.SessionId, slot.Nation, slot.PlayerKeyId, gameTransport);
                }
            }
            else
            {
                var freeSlot = slots.FirstOrDefault(s => s.Kind == SlotKind.Open);
                if (freeSlot == null)
                {
                    _ = SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.LobbyFull });
                    return;
                }

                if (!Bindings.TryBind(freeSlot.Index, session.PlayerKey))
                {
                    _ = SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.SlotTaken });
                    return;
                }

                freeSlot.Kind = SlotKind.Human;
                freeSlot.PlayerKeyId = session.PlayerKey;
                freeSlot.DisplayName = session.DisplayName;
                freeSlot.Connected = true;
                freeSlot.Transport = session.Transport;
                freeSlot.SessionId = session.SessionId;
                freeSlot.Ready = false;
                session.SlotIndex = freeSlot.Index;
                stateChanged = true;
            }
        }

        await SendAsync(session.Transport, new JoinOk()).ConfigureAwait(false);

        if (lateSeat != null)
        {
            if (replacedTransport != null) await CloseQuietlyAsync(replacedTransport).ConfigureAwait(false);
            // Seating waits for the game actor, which can be busy: it runs beside the reader, so the reader keeps forwarding frames and answering pings meanwhile.
            _ = SeatLateInBackgroundAsync(lateSeat, session.Transport);
            return;
        }

        if (stateChanged)
        {
            await BroadcastLobbyStateAsync().ConfigureAwait(false);
        }
    }

    private async Task SeatLateInBackgroundAsync(LateSeat lateSeat, ITransport socket)
    {
        try
        {
            var seated = LateSeated;
            if (seated != null) await seated(lateSeat).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await CloseQuietlyAsync(socket).ConfigureAwait(false); // never seated: the client must see the drop, not wait for a snapshot
        }
    }

    private async Task HandleLobbySetAsync(LobbySession session, LobbySet set)
    {
        if (!session.IsAuthenticated || !session.SlotIndex.HasValue || session.SlotIndex.Value != set.Slot)
        {
            return;
        }

        if (session.PlayerKey == null || !Bindings.IsOwner(set.Slot, session.PlayerKey))
        {
            return;
        }

        bool changed = false;
        lock (gate)
        {
            if (set.Slot < 0 || set.Slot >= slots.Count) return;
            var slot = slots[set.Slot];

            if (set.Nation != null)
            {
                if (set.Nation == Constants.Random || set.Nation == Constants.Spectator)
                {
                    slot.Nation = set.Nation;
                    changed = true;
                }
                else
                {
                    // Check nation uniqueness among non-closed slots
                    var isTaken = slots.Any(s => s.Index != slot.Index && s.Kind != SlotKind.Closed
                        && string.Equals(s.Nation, set.Nation, StringComparison.OrdinalIgnoreCase));
                    if (!isTaken)
                    {
                        slot.Nation = set.Nation;
                        changed = true;
                    }
                }
            }

            if (set.Color != null)
            {
                slot.Color = set.Color;
                changed = true;
            }

            if (slot.Ready != set.Ready)
            {
                slot.Ready = set.Ready;
                changed = true;
            }
        }

        if (changed)
        {
            await BroadcastLobbyStateAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleLobbyChatAsync(LobbySession session, LobbyChat chat)
    {
        if (!session.IsAuthenticated) return;

        var broadcastMsg = new LobbyChat
        {
            Text = chat.Text,
            Sender = session.DisplayName ?? "Anonymous"
        };
        await BroadcastAsync(broadcastMsg).ConfigureAwait(false);
    }

    private async Task HandleDisconnectAsync(LobbySession session)
    {
        bool changed = false;
        lock (gate)
        {
            sessions.Remove(session.SessionId);
            if (session.SlotIndex.HasValue && session.SlotIndex.Value < slots.Count)
            {
                var slot = slots[session.SlotIndex.Value];
                if (slot.SessionId == session.SessionId)
                {
                    slot.Connected = false;
                    slot.Transport = null;
                    slot.SessionId = null;
                    slot.Ready = false;
                    changed = true;
                }
            }
        }

        if (changed && Status == LobbyStatus.InLobby)
        {
            await BroadcastLobbyStateAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Kicks a player from a slot and clears the slot binding.</summary>
    public async Task KickAsync(int slotIndex)
    {
        LobbySession? sessionToKick = null;
        lock (gate)
        {
            if (Status != LobbyStatus.InLobby) return; // after the start the host kicks through GameHost/ServerLobby, never by writing to a seat's raw transport
            if (slotIndex < 0 || slotIndex >= slots.Count) return;
            var slot = slots[slotIndex];

            if (slot.SessionId != null && sessions.TryGetValue(slot.SessionId, out sessionToKick))
            {
                sessions.Remove(slot.SessionId);
            }

            Bindings.Clear(slotIndex);
            slot.Kind = SlotKind.Open;
            slot.DisplayName = null;
            slot.PlayerKeyId = null;
            slot.Nation = Constants.Random;
            slot.Color = null;
            slot.Ready = false;
            slot.Connected = false;
            slot.SessionId = null;
            slot.Transport = null;
        }

        if (sessionToKick != null)
        {
            try
            {
                await SendAsync(sessionToKick.Transport, new JoinRefused { Code = JoinRefusedCodes.Kicked }).ConfigureAwait(false);
                await sessionToKick.Transport.CloseAsync().ConfigureAwait(false);
            }
            catch { }
        }

        await BroadcastLobbyStateAsync().ConfigureAwait(false);
    }

    /// <summary>Closes a slot so players cannot take it.</summary>
    public async Task CloseSlotAsync(int slotIndex)
    {
        lock (gate)
        {
            if (Status != LobbyStatus.InLobby) return;
            if (slotIndex < 0 || slotIndex >= slots.Count) return;
            var slot = slots[slotIndex];
            if (slot.Kind == SlotKind.Human && slot.Connected) return; // Must kick first
            slot.Kind = SlotKind.Closed;
        }
        await BroadcastLobbyStateAsync().ConfigureAwait(false);
    }

    /// <summary>Re-opens a closed slot.</summary>
    public async Task OpenSlotAsync(int slotIndex)
    {
        lock (gate)
        {
            if (Status != LobbyStatus.InLobby) return;
            if (slotIndex < 0 || slotIndex >= slots.Count) return;
            var slot = slots[slotIndex];
            if (slot.Kind == SlotKind.Closed)
            {
                slot.Kind = SlotKind.Open;
            }
        }
        await BroadcastLobbyStateAsync().ConfigureAwait(false);
    }

    /// <summary>Sets a slot's role to AI or Open.</summary>
    public async Task SetSlotKindAsync(int slotIndex, SlotKind kind)
    {
        lock (gate)
        {
            if (Status != LobbyStatus.InLobby) return;
            if (slotIndex < 0 || slotIndex >= slots.Count) return;
            var slot = slots[slotIndex];
            if (slot.Kind == SlotKind.Human && slot.Connected) return; // Must kick first

            Bindings.Clear(slotIndex);
            slot.Kind = kind;
            if (kind == SlotKind.AI)
            {
                slot.DisplayName = "AI Player";
                slot.PlayerKeyId = null;
                slot.Ready = true;
            }
            else if (kind == SlotKind.Open)
            {
                slot.DisplayName = null;
                slot.PlayerKeyId = null;
                slot.Ready = false;
            }
        }
        await BroadcastLobbyStateAsync().ConfigureAwait(false);
    }

    /// <summary>Updates lobby settings and broadcasts updated lobby state.</summary>
    public async Task UpdateSettingsAsync(LobbySettings newSettings)
    {
        lock (gate)
        {
            if (Status != LobbyStatus.InLobby) return;
            Settings = newSettings;
            while (slots.Count < Settings.MaxMajorCivs)
            {
                slots.Add(new Slot
                {
                    Index = slots.Count,
                    Kind = SlotKind.Open,
                    Nation = Constants.Random
                });
            }
        }
        await BroadcastLobbyStateAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the game: resolves Random nations, applies AI fill, validates the setup,
    /// constructs the GameInfo via NewGame.Start, and broadcasts GameStarting.
    /// </summary>
    public async Task<LobbyStartResult> StartGameAsync(bool force = false, long? seed = null, long? clockMillis = null)
    {
        GameSetupInfo setup;
        long actualSeed;
        long actualClock;
        List<Slot> activeSlots;
        List<ITransport> superseded = [];
        List<Slot> filled = [];
        TaskCompletionSource settled;

        lock (gate)
        {
            if (Status != LobbyStatus.InLobby)
                throw new InvalidOperationException($"Cannot start game in status {Status}");

            var humanSlots = slots.Where(s => s.Kind == SlotKind.Human).ToList();
            if (humanSlots.Count == 0)
                throw new InvalidOperationException("Cannot start game with no human players");

            if (!force && humanSlots.Any(s => !s.Ready))
                throw new InvalidOperationException("Cannot start game: not all human players are ready");

            if (Settings.AiFill)
            {
                foreach (var openSlot in slots.Where(s => s.Kind == SlotKind.Open))
                {
                    openSlot.Kind = SlotKind.AI;
                    openSlot.DisplayName = "AI Player";
                    openSlot.Ready = true;
                    filled.Add(openSlot);
                }
            }

            activeSlots = slots.Where(s => s.Kind is SlotKind.Human or SlotKind.AI).ToList();
            if (activeSlots.Count < 2)
                throw new InvalidOperationException("A game needs at least two players");

            actualSeed = seed ?? BitConverter.ToInt64(random.Bytes(8));
            actualClock = clockMillis ?? clock.NowMillis;

            setup = Settings.ToGameSetupInfo();

            setup.GameParameters.Players = activeSlots.Select(s =>
                s.Kind == SlotKind.Human
                    ? new Player(s.Nation, PlayerType.Human, s.PlayerKeyId ?? "")
                    : new Player(s.Nation, PlayerType.AI, "")
            ).ToList();

            var validation = new ValidateSetupQuery(setup, IsHostedGame: true).Run();
            if (!validation.Ok)
            {
                var errors = string.Join("; ", validation.Errors.Select(e => e.Message));
                throw new InvalidOperationException($"Lobby setup validation failed: {errors}");
            }

            Status = LobbyStatus.Starting;
            settled = startSettled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        GameInfo game;
        try
        {
            game = GameFactory != null ? GameFactory(setup, actualSeed, actualClock) : NewGame.Start(setup, actualSeed, actualClock, isHostedGame: true);
            ResolveNations(game, activeSlots);
            lock (gate)
            {
                Status = LobbyStatus.Started;
                HandOffSeats(superseded);
            }
        }
        catch (Exception)
        {
            lock (gate)
            {
                if (Status == LobbyStatus.Starting)
                {
                    Status = LobbyStatus.InLobby;
                    foreach (var slot in filled) // the AI fill did not happen: the seats are open again
                    {
                        slot.Kind = SlotKind.Open;
                        slot.DisplayName = null;
                        slot.Ready = false;
                    }
                }
            }
            settled.TrySetResult(); // waiting reconnects re-evaluate: back in the lobby they simply join
            throw;
        }

        settled.TrySetResult();
        foreach (var transport in superseded) await CloseQuietlyAsync(transport).ConfigureAwait(false);

        await BroadcastAsync(new GameStarting(), includeHandedOver: true).ConfigureAwait(false);

        return new LobbyStartResult(game, setup, [.. activeSlots]);
    }

    /// <summary>Under the lobby lock, as the game starts: every seated connection's stream goes to the game, connections replaced by a newer one of the same key are dropped.</summary>
    private void HandOffSeats(List<ITransport> superseded)
    {
        foreach (var session in sessions.Values.ToList())
        {
            if (session.SlotIndex is not int seat) continue;
            if (slots[seat].SessionId == session.SessionId)
            {
                HandOff(session);
            }
            else
            {
                sessions.Remove(session.SessionId); // superseded by a newer connection of the same key
                superseded.Add(session.Transport);
            }
        }
    }

    /// <summary>Writes the nation each active slot really got (Random resolved) back into the slots.</summary>
    private static void ResolveNations(GameInfo game, List<Slot> activeSlots)
    {
        var takenNations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in activeSlots)
        {
            if (slot.Kind == SlotKind.Human)
            {
                var civ = game.Civilizations.FirstOrDefault(c => c.PlayerId == slot.PlayerKeyId);
                if (civ != null)
                {
                    slot.Nation = civ.CivName;
                    takenNations.Add(civ.CivName);
                }
            }
        }
        foreach (var slot in activeSlots)
        {
            if (slot.Kind == SlotKind.AI)
            {
                var civ = game.Civilizations.FirstOrDefault(c => c.IsAI() && !c.IsCityState && !c.IsSpectator() && !takenNations.Contains(c.CivName));
                if (civ != null)
                {
                    slot.Nation = civ.CivName;
                    takenNations.Add(civ.CivName);
                }
            }
        }
    }

    /// <summary>Creates the current LobbyState wire message.</summary>
    public LobbyState CreateLobbyState()
    {
        lock (gate)
        {
            var settingsElement = JsonSerializer.SerializeToElement(new
            {
                gameName = Settings.GameName,
                speed = Settings.Speed,
                difficulty = Settings.Difficulty,
                era = Settings.Era,
                victoryTypes = Settings.VictoryTypes,
                maxMajorCivs = Settings.MaxMajorCivs,
                cityStateCount = Settings.CityStateCount,
                aiFill = Settings.AiFill,
                noBarbarians = Settings.NoBarbarians,
                ruleset = Settings.Ruleset,
                mods = Settings.Mods,
                turnMode = Settings.TurnMode,
                turnTimerSeconds = Settings.TurnTimerSeconds,
                onDisconnect = Settings.OnDisconnect,
                allowJoinInProgress = Settings.AllowJoinInProgress
            });

            return new LobbyState
            {
                Settings = settingsElement,
                Slots = slots.Select(s => s.ToWire()).ToList(),
                HostName = HostName
            };
        }
    }

    /// <summary>Broadcasts LobbyState to all connected sessions.</summary>
    public async Task BroadcastLobbyStateAsync()
    {
        var state = CreateLobbyState();
        await BroadcastAsync(state).ConfigureAwait(false);
    }

    /// <summary>Broadcasts a message to all connected sessions.</summary>
    public Task BroadcastAsync(Message message) => BroadcastAsync(message, includeHandedOver: false);

    /// <summary>Seats handed over to the game are skipped unless asked for: the game host owns their writes.</summary>
    private async Task BroadcastAsync(Message message, bool includeHandedOver)
    {
        List<ITransport> targets;
        lock (gate)
        {
            targets = sessions.Values
                .Where(s => s.IsAuthenticated && s.Transport.IsOpen && (includeHandedOver || s.Handoff == null))
                .Select(s => s.Transport)
                .ToList();
        }

        var frame = ProtocolCodec.Encode(message);
        foreach (var transport in targets)
        {
            try
            {
                await transport.SendAsync(frame).ConfigureAwait(false);
            }
            catch
            {
                // Ignore disconnected transport failures during broadcast
            }
        }
    }

    private static ValueTask SendAsync(ITransport transport, Message message)
    {
        if (!transport.IsOpen) return ValueTask.CompletedTask;
        var frame = ProtocolCodec.Encode(message);
        return transport.SendAsync(frame);
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            Status = LobbyStatus.Closed;
        }

        List<ITransport> targets;
        lock (gate)
        {
            targets = sessions.Values.Select(s => s.Transport).ToList();
            sessions.Clear();
        }

        foreach (var transport in targets)
        {
            try
            {
                await transport.CloseAsync().ConfigureAwait(false);
            }
            catch { }
        }
    }
}
