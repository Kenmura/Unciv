using Unciv.Assets;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C045: implemented by a session of a hosted multiplayer game: the chat and the player list, and for the host's own session the two admin actions. Its presence is what makes
/// the game screen show the Players button. Events are raised on the session's context (the main thread in the game).
/// </summary>
public interface IGameSocial
{
    /// <summary>The chat lines received so far (the newest <see cref="SocialModel.MaxChatLines"/>).</summary>
    IReadOnlyList<ChatLine> Chat { get; }

    event Action<ChatLine>? ChatReceived;

    /// <summary>Sends a chat line; the host stamps the sender's name and rebroadcasts it (also to the sender).</summary>
    Task SendChatAsync(string text);

    /// <summary>The players from the host's latest roster.</summary>
    IReadOnlyList<PlayerRow> Players { get; }

    event Action? PlayersChanged;

    /// <summary>True for the session of the player who hosts the game; only then do <see cref="KickAsync"/> and <see cref="RebindAsync"/> work.</summary>
    bool IsHost { get; }

    /// <summary>Disconnects the player of <paramref name="civId"/> and releases the seat; null on success, else a refusal code (see <see cref="SocialModel.Describe"/>).</summary>
    Task<string?> KickAsync(string civId);

    /// <summary>Gives the seat to another key (a lost key or a new device); null on success, else a refusal code.</summary>
    Task<string?> RebindAsync(string civId, string newPlayerKey);
}
