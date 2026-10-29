using Unciv.Assets;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C036: implemented by a session of a simultaneous (multiplayer) game: the humans the round waits for and whether each is connected. Its presence is what makes
/// the game screen show the turn bar and end the turn without waiting for the other humans. Who ended and the deadline come from the snapshot's TurnStatus.
/// </summary>
public interface ITurnRoster
{
    /// <summary>The humans of the game, in lobby order.</summary>
    IReadOnlyList<TurnRosterEntry> Roster { get; }

    /// <summary>Raised (on the same context as the session's other events) after <see cref="Roster"/> changed.</summary>
    event Action? RosterChanged;
}
