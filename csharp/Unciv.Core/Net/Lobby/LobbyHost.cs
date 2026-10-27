using System.Text.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Models.Metadata;
using Unciv.Net.Identity;
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
            await HandleDisconnectAsync(session).ConfigureAwait(false);
        }
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
            await SendAsync(session.Transport, new JoinRefused { Code = JoinRefusedCodes.RulesetMismatch }).ConfigureAwait(false);
            return;
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

        var admissionResult = Admission.Admit(join.PlayerKey, join.Password, join.InviteToken);
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

        bool stateChanged = false;
        lock (gate)
        {
            session.IsAuthenticated = true;
            session.PlayerKey = session.PendingPlayerKey;
            session.DisplayName = session.PendingDisplayName;

            var existingSlotIndex = Bindings.SlotOf(session.PlayerKey);
            if (existingSlotIndex.HasValue && existingSlotIndex.Value < slots.Count)
            {
                var slot = slots[existingSlotIndex.Value];
                slot.Connected = true;
                slot.Transport = session.Transport;
                slot.SessionId = session.SessionId;
                slot.DisplayName = session.DisplayName;
                session.SlotIndex = existingSlotIndex.Value;
                stateChanged = true;
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

        if (stateChanged)
        {
            await BroadcastLobbyStateAsync().ConfigureAwait(false);
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
        }

        var game = NewGame.Start(setup, actualSeed, actualClock, isHostedGame: true);

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

        lock (gate)
        {
            Status = LobbyStatus.Started;
        }

        await BroadcastAsync(new GameStarting()).ConfigureAwait(false);

        return new LobbyStartResult(game, setup, [.. activeSlots]);
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
    public async Task BroadcastAsync(Message message)
    {
        List<ITransport> targets;
        lock (gate)
        {
            targets = sessions.Values
                .Where(s => s.IsAuthenticated && s.Transport.IsOpen)
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
