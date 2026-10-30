using Unciv.Commands;
using Unciv.Commands.Log;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Persistence;
using Unciv.Net.Turns;

namespace Unciv.Server;

/// <summary>
/// Host-side server coordinator (SERVER.md 6.5).
/// Hosts a lobby using <see cref="LobbyHost"/> and transitions smoothly to
/// <see cref="GameHost"/> when the game starts, migrating active player sessions.
/// </summary>
public sealed class ServerLobby : IAsyncDisposable
{
    private readonly LobbyHost lobbyHost;
    private readonly string? gameDirectory;
    private readonly HostLimits? hostLimits;
    private IGameStore? store;
    private readonly IClock clock;
    private readonly TaskCompletionSource<GameHost> gameHostReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource disposed = new();
    private readonly HashSet<string> attached = new(StringComparer.Ordinal);
    private readonly Lock attachedGate = new();
    private readonly SemaphoreSlim seatGate = new(1, 1);
    private GameHost? activeGameHost;
    private int disposeStarted;

    public ServerLobby(
        LobbySettings? settings = null,
        SlotBindings? bindings = null,
        LobbyAdmission? admission = null,
        string? hostFingerprint = null,
        ISecureRandom? random = null,
        IClock? clock = null,
        IGameStore? store = null,
        string hostName = "Host",
        string? gameDirectory = null,
        TimeSpan? reprobeCheck = null,
        HostLimits? hostLimits = null)
    {
        this.reprobeCheck = reprobeCheck is { } check && check > TimeSpan.Zero ? check : TimeSpan.FromSeconds(5);
        this.hostLimits = hostLimits;
        this.gameDirectory = gameDirectory;
        this.clock = clock ?? SystemClock.Instance;
        this.store = store;
        lobbyHost = new LobbyHost(
            settings,
            bindings,
            admission,
            hostFingerprint,
            random,
            this.clock,
            hostName
        );
        lobbyHost.LateSeated = SeatLateAsync;
        lobbyHost.Standby.Prober = new Transport.TlsEndpointProber(); // T281: a backup host's endpoint is probed with a real TLS handshake
        reprobeLoop = RunReprobeLoopAsync();
    }

    /// <summary>How often the open lobby checks whether the backup host's re-probe is due (the coordinator's own interval decides; <see cref="GameHost.Tick"/> does this once the game runs).</summary>
    private readonly TimeSpan reprobeCheck;
    private readonly Task reprobeLoop;

    private async Task RunReprobeLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(reprobeCheck);
            while (true) // ends by the cancellation (WaitForNextTickAsync throws it); the timer is never stopped otherwise
            {
                await timer.WaitForNextTickAsync(disposed.Token).ConfigureAwait(false);
                await lobbyHost.ReprobeStandbyAsync(disposed.Token).ConfigureAwait(false); // sends to a dead connection are swallowed by the lobby itself
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException) { /* disposed, or a failed round: the loop ends here and DisposeAsync never sees a fault */ }
    }

    /// <summary>A known player reconnects after the start: their connection joins the running game (the lobby reader stays its only reader).</summary>
    private async Task SeatLateAsync(LobbyHost.LateSeat seat)
    {
        lock (attachedGate)
        {
            if (!attached.Add(seat.SessionId)) return; // the start loop already seated this connection
        }
        var gameHost = await gameHostReady.Task.WaitAsync(disposed.Token).ConfigureAwait(false);
        await AttachIfCurrentAsync(gameHost, seat.SessionId, seat.Nation, seat.Transport, seat.PlayerKeyId).ConfigureAwait(false);
    }

    /// <summary>
    /// Seats a connection unless a newer connection of the same key already replaced it. Seating is serialised, so an older
    /// connection whose seating ran late can never detach the newer one that was seated first.
    /// </summary>
    private async Task AttachIfCurrentAsync(GameHost gameHost, string sessionId, string nation, ITransport transport, string? playerKeyId)
    {
        await seatGate.WaitAsync(disposed.Token).ConfigureAwait(false);
        try
        {
            if (lobbyHost.GameTransportFor(sessionId) == null)
            {
                // Superseded (a newer connection of the key seats itself) or gone (nobody does): the game must not keep counting the seat as connected.
                await MarkAbsentIfUnseatedAsync(gameHost, nation).ConfigureAwait(false);
                return;
            }
            try
            {
                await gameHost.AttachSessionAsync(sessionId, nation, transport, playerKeyId, lobbyHost.AdvertisesHostMigration(sessionId)).ConfigureAwait(false);
            }
            catch (Exception) when (lobbyHost.GameTransportFor(sessionId) == null)
            {
                // A newer connection of the key replaced this one (closing it) while it was being seated: the newer one seats itself, this is not a failed start.
                await MarkAbsentIfUnseatedAsync(gameHost, nation).ConfigureAwait(false);
                return;
            }
            lobbyHost.MarkGameReading(sessionId);
        }
        finally
        {
            seatGate.Release();
            lock (attachedGate)
            {
                attached.RemoveWhere(id => id != sessionId && lobbyHost.GameTransportFor(id) == null); // keep the set to live connections
            }
        }
    }

    /// <summary>Decided on the game actor, so it cannot interleave with a seating: a civ nobody is bound to is a disconnected player (its grace period runs), a later connection of the key reconnects it.</summary>
    private static Task MarkAbsentIfUnseatedAsync(GameHost gameHost, string nation) =>
        gameHost.EnqueueAsync(() =>
        {
            if (!gameHost.Sessions.Values.Any(s => string.Equals(s.BoundCivId, nation, StringComparison.Ordinal)))
                gameHost.TurnTimer.Disconnect(nation);
            return Task.CompletedTask;
        });

    public LobbyHost Lobby => lobbyHost;

    /// <summary>
    /// Host administration after the start: <see cref="GameHost.KickCivAsync"/> plus the lobby's own binding, so the kicked key cannot reconnect through the lobby.
    /// Null on success, else the refusal code (<c>not-started</c> before the game exists).
    /// </summary>
    public async Task<string?> KickCivAsync(string civId)
    {
        var gameHost = activeGameHost;
        if (gameHost == null) return "not-started";
        var refusal = await gameHost.KickCivAsync(civId).ConfigureAwait(false);
        if (refusal == null) await lobbyHost.ReleaseSeatAsync(civId, null).ConfigureAwait(false);
        return refusal;
    }

    /// <summary>Host administration after the start: <see cref="GameHost.RebindCivAsync"/> plus the lobby's binding (old key refused, new key seated on its next connection).</summary>
    public async Task<string?> RebindCivAsync(string civId, string newPlayerKey)
    {
        var gameHost = activeGameHost;
        if (gameHost == null) return "not-started";
        var refusal = await gameHost.RebindCivAsync(civId, newPlayerKey).ConfigureAwait(false);
        if (refusal == null) await lobbyHost.ReleaseSeatAsync(civId, newPlayerKey).ConfigureAwait(false);
        return refusal;
    }

    public GameHost? ActiveGameHost => activeGameHost;

    /// <summary>Attaches an incoming transport connection to the lobby.</summary>
    public Task AttachConnectionAsync(ITransport transport, CancellationToken cancellationToken = default)
    {
        return lobbyHost.AttachConnectionAsync(transport, cancellationToken);
    }

    /// <summary>
    /// Starts the game from the lobby and creates the authoritative <see cref="GameHost"/>.
    /// All connected human player sessions in the lobby are attached to the new GameHost,
    /// receiving their initial Snapshot messages.
    /// </summary>
    public async Task<GameHost> StartGameAsync(bool force = false, long? seed = null, long? clockMillis = null)
    {
        var startResult = await lobbyHost.StartGameAsync(force, seed, clockMillis).ConfigureAwait(false);

        GameHost? gameHost = null;
        try
        {
            // T265: the lobby's NewGame.Start makes a Sequential game; the MVP plays simultaneous rounds (SIMULTANEOUS-TURNS.md), as every other host path does.
            if (string.Equals(lobbyHost.Settings.TurnMode, "Simultaneous", StringComparison.OrdinalIgnoreCase) && startResult.Game.TurnState == null)
                startResult.Game.StartSimultaneousTurns();
            var policy = lobbyHost.Settings.ToTurnPolicy();
            if (store == null && gameDirectory != null) store = CreateStore(startResult, policy); // T270: the game is saved from its first moment
            else if (store != null) store.WriteMeta(store.ReadMeta() with { TimerPolicy = policy }); // a resume must run the same turn rules
            gameHost = new GameHost(new GameSession(startResult.Game) { Clock = () => clock.NowMillis }, store, clock: clock, turnPolicy: policy, limits: hostLimits, standby: lobbyHost.Standby); // the designation made in the lobby carries over
            foreach (var slot in startResult.Slots)
            {
                if (slot.Kind != SlotKind.Human) continue;
                // The hand-over transport, never the raw one: the lobby reader keeps reading the socket and forwards to the game.
                // No hand-over (never connected, superseded or gone) means nobody seats it; a raw attach would bring back the two-reader race.
                var gameTransport = slot.Connected && slot.SessionId != null ? lobbyHost.GameTransportFor(slot.SessionId) : null;
                if (gameTransport == null)
                {
                    await MarkAbsentIfUnseatedAsync(gameHost, slot.Nation).ConfigureAwait(false);
                    continue;
                }
                lock (attachedGate)
                {
                    if (!attached.Add(slot.SessionId!)) continue; // a late seat in the start window already claimed it
                }
                await AttachIfCurrentAsync(gameHost, slot.SessionId!, slot.Nation, gameTransport, slot.PlayerKeyId).ConfigureAwait(false);
            }

            if (disposed.IsCancellationRequested) throw new ObjectDisposedException(nameof(ServerLobby));
        }
        catch (Exception ex)
        {
            if (gameHost == null) { store?.Dispose(); store = null; } // the host never took ownership of a store made for it
            gameHostReady.TrySetException(ex); // late seats waiting for the game must fail (and drop their connection), not hang
            if (gameHost != null)
            {
                try { await gameHost.DisposeAsync().ConfigureAwait(false); } catch (Exception) { /* the start failure is the news, not the cleanup */ }
            }
            // Seats that were handed over but never attached have no reader: close them so their clients see the drop.
            foreach (var slot in startResult.Slots)
            {
                if (slot.SessionId == null) continue;
                var orphan = lobbyHost.GameTransportFor(slot.SessionId);
                if (orphan == null) continue;
                try { await orphan.CloseAsync().ConfigureAwait(false); }
                catch (Exception) { /* already gone */ }
            }
            throw;
        }

        activeGameHost = gameHost;
        gameHostReady.TrySetResult(gameHost);
        return gameHost;
    }

    /// <summary>T270: creates <c>game.json</c>, the log header (with <c>TurnMode</c> when the game is simultaneous) and the first snapshot in <see cref="gameDirectory"/>. The join password is never written.</summary>
    private FileGameStore CreateStore(LobbyStartResult started, TurnPolicy policy)
    {
        var header = CommandLogHeader.Create(started.Setup, started.Seed, started.StartClockMillis);
        if (started.Game.TurnState != null) header = header with { TurnMode = CommandLogHeader.SimultaneousTurns };
        var saved = UncivJson.Deserialize<LobbySettings>(UncivJson.Serialize(lobbyHost.Settings))!;
        saved.Password = null;
        var seats = new SortedDictionary<string, SlotBinding>(StringComparer.Ordinal);
        foreach (var slot in started.Slots)
            if (slot.Kind == SlotKind.Human && slot.PlayerKeyId != null) seats[slot.Nation] = new SlotBinding { PlayerKey = slot.PlayerKeyId, DisplayName = slot.DisplayName ?? "" };
        using var settingsJson = System.Text.Json.JsonDocument.Parse(UncivJson.Serialize(saved));
        var meta = new GameMeta { Slots = seats, TimerPolicy = policy, HostFingerprint = lobbyHost.HostFingerprint, LobbySettings = settingsJson.RootElement.Clone() };
        return FileGameStore.Create(gameDirectory!, started.Game.GameId, header, meta, started.Game);
    }

    /// <summary>
    /// T270: reopens a saved game instead of starting one. The game directory's seats become the lobby's bound human slots (those keys, and only those, reconnect), the host gets the stored
    /// turn policy (<paramref name="policyOverride"/> wins when the operator gave one) and the clock a live game needs, and a round the stop interrupted continues. Only valid on a fresh lobby.
    /// </summary>
    public async Task<GameHost> ResumeGameAsync(OpenedGame opened, TurnPolicy? policyOverride = null, Action<string>? warn = null)
    {
        if (activeGameHost != null || store != null) throw new InvalidOperationException("A game is already running");
        try
        {
            return await ResumeCoreAsync(opened, policyOverride, warn).ConfigureAwait(false);
        }
        catch (Exception)
        {
            if (activeGameHost == null) opened.Dispose(); // nothing owns the store yet; once the host exists, DisposeAsync closes both
            throw;
        }
    }

    private async Task<GameHost> ResumeCoreAsync(OpenedGame opened, TurnPolicy? policyOverride, Action<string>? warn)
    {
        var meta = opened.Store.ReadMeta();
        var seated = meta.Slots.Where(x => x.Value.PlayerKey.Length > 0).ToList(); // a kicked seat has no key: nobody can reconnect to it
        lobbyHost.RestoreStarted(seated.Select(x => (x.Key, x.Value.PlayerKey, x.Value.DisplayName)));
        lobbyHost.Standby.Restore(meta.Standby); // before the host persists the coordinator: otherwise the stored designation would be overwritten with nothing
        var session = new GameSession(opened.Session.Game, lastSeq: opened.Session.LastSeq) { Clock = () => clock.NowMillis };
        var gameHost = new GameHost(session, opened.Store, clock: clock, turnPolicy: policyOverride ?? meta.TimerPolicy, limits: hostLimits, standby: lobbyHost.Standby);
        gameHost.Warn = warn; // before the pump below, so a round that cannot resume is logged
        store = opened.Store;
        activeGameHost = gameHost; // owned from here: a failure below is cleaned up by DisposeAsync
        if (session.Game.TurnState is { Phase: SimultaneousPhase.Resolving })
        {
            await gameHost.EnqueueAsync(() =>
            {
                gameHost.PumpResolving(); // a stop in the middle of a resolve: the log holds the steps done, the rest is derived from the state
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
        // Nobody is connected yet: every saved seat starts as a dropped player (grace period, force-end, absent turns), and its key's reconnect undoes that. Without this a player who never returns would hold the round forever.
        await gameHost.EnqueueAsync(() =>
        {
            foreach (var (civ, _) in seated) gameHost.TurnTimer.Disconnect(civ);
            // A seat kicked before the stop lost its key at once, but its civ is human until the next turn boundary and the pending takeover was only in memory: hand it to the AI again.
            foreach (var civ in meta.Slots.Where(x => x.Value.PlayerKey.Length == 0).Select(x => x.Key))
                if (session.Game.GetCivilizationOrNull(civ) is { } kickedCiv && kickedCiv.IsHuman()) gameHost.TurnTimer.Depart(civ);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        gameHostReady.TrySetResult(gameHost);
        return gameHost;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0) return; // the runner disposes early to report the saved state, then the scope ends
        await disposed.CancelAsync().ConfigureAwait(false);
        await reprobeLoop.ConfigureAwait(false); // never faults: it ends on the cancellation
        try
        {
            if (activeGameHost != null)
            {
                // The game first: it can still tell its players the host is closing before the lobby closes the sockets.
                await activeGameHost.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await lobbyHost.DisposeAsync().ConfigureAwait(false);
        }
    }
}
