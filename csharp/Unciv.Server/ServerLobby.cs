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

        foreach (var slot in startResult.Slots)
        {
            if (slot.Kind == SlotKind.Human && slot.Connected && slot.SessionId != null && slot.Transport != null)
            {
                await gameHost.AttachSessionAsync(
                    slot.SessionId,
                    slot.Nation,
                    slot.Transport,
                    slot.PlayerKeyId
                ).ConfigureAwait(false);
            }
        }

        activeGameHost = gameHost;
        return gameHost;
    }

    public async ValueTask DisposeAsync()
    {
        await lobbyHost.DisposeAsync().ConfigureAwait(false);
        if (activeGameHost != null)
        {
            await activeGameHost.DisposeAsync().ConfigureAwait(false);
        }
    }
}
