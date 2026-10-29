using Unciv.Commands;
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
    private readonly IGameStore? store;
    private readonly IClock clock;
    private readonly TaskCompletionSource<GameHost> gameHostReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource disposed = new();
    private readonly HashSet<string> attached = new(StringComparer.Ordinal);
    private readonly Lock attachedGate = new();
    private readonly SemaphoreSlim seatGate = new(1, 1);
    private GameHost? activeGameHost;

    public ServerLobby(
        LobbySettings? settings = null,
        SlotBindings? bindings = null,
        LobbyAdmission? admission = null,
        string? hostFingerprint = null,
        ISecureRandom? random = null,
        IClock? clock = null,
        IGameStore? store = null,
        string hostName = "Host")
    {
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
                await gameHost.AttachSessionAsync(sessionId, nation, transport, playerKeyId).ConfigureAwait(false);
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
            var policy = lobbyHost.Settings.ToTurnPolicy();
            if (store != null) store.WriteMeta(store.ReadMeta() with { TimerPolicy = policy }); // a resume must run the same turn rules
            gameHost = new GameHost(new GameSession(startResult.Game), store, clock: clock, turnPolicy: policy);
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
            gameHostReady.TrySetException(ex); // late seats waiting for the game must fail (and drop their connection), not hang
            if (gameHost != null)
            {
                try { await gameHost.DisposeAsync().ConfigureAwait(false); }
                catch (Exception) { /* the start failure is the news, not the cleanup */ }
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

    public async ValueTask DisposeAsync()
    {
        await disposed.CancelAsync().ConfigureAwait(false);
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
