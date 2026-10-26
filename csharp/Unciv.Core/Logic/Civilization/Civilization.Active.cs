namespace Unciv.Logic.Civilization;

// T201: the rules that asked "is this the current player" ask this instead, so simultaneous turns have no single current player.
public partial class Civilization
{
    /// <summary>
    /// "This civ is acting now". Sequential: the current player (the Kotlin quirk, so replays are unchanged).
    /// Simultaneous: every human whose turn is in the Playing phase and not ended, see <see cref="GameInfo.IsActive"/>.
    /// </summary>
    public bool IsActive() => GameInfo.IsSimultaneousTurns ? GameInfo.IsCivActive(this) : IsCurrentPlayer();
}
