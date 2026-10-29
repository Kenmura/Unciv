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
            if (lobbyHost.GameTransportFor(sessionId) == null) return; // superseded or gone
            await gameHost.AttachSessionAsync(sessionId, nation, transport, playerKeyId).ConfigureAwait(false);
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

    public LobbyHost Lobby => lobbyHost;

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

        var gameSession = new GameSession(startResult.Game);
        var gameHost = new GameHost(gameSession, store, clock: clock);

        try
        {
            foreach (var slot in startResult.Slots)
            {
                if (slot.Kind != SlotKind.Human || !slot.Connected || slot.SessionId == null) continue;
                // The hand-over transport, never the raw one: the lobby reader keeps reading the socket and forwards to the game.
                // No hand-over (superseded or gone) means nobody seats it; a raw attach would bring back the two-reader race.
                var gameTransport = lobbyHost.GameTransportFor(slot.SessionId);
                if (gameTransport == null) continue;
                lock (attachedGate)
                {
                    if (!attached.Add(slot.SessionId)) continue; // a late seat in the start window already claimed it
                }
                await AttachIfCurrentAsync(gameHost, slot.SessionId, slot.Nation, gameTransport, slot.PlayerKeyId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            gameHostReady.TrySetException(ex); // late seats waiting for the game must fail (and drop their connection), not hang
            await gameHost.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        activeGameHost = gameHost;
        gameHostReady.TrySetResult(gameHost);
        return gameHost;
    }

    public async ValueTask DisposeAsync()
    {
        await disposed.CancelAsync().ConfigureAwait(false);
        if (activeGameHost != null)
        {
            // The game first: it can still tell its players the host is closing before the lobby closes the sockets.
            await activeGameHost.DisposeAsync().ConfigureAwait(false);
        }
        await lobbyHost.DisposeAsync().ConfigureAwait(false);
    }
}
