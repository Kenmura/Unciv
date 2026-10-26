namespace Unciv.Commands;

/// <summary>
/// Stable failure codes of the game commands (design/COMMANDS-IMPLEMENTATION.md section 2). Clients may switch on these;
/// the message next to them is for people. <see cref="CommandCodes"/> holds the two codes the processor itself produces.
/// </summary>
public static class CommandErrors
{
    public const string UnknownCiv = "unknown-civ";
    public const string NotYourTurn = "not-your-turn";
    public const string NotHuman = "not-human";
    public const string GameOver = "game-over";
    public const string UnknownUnit = "unknown-unit";
    public const string NotYourUnit = "not-your-unit";
    public const string UnknownCity = "unknown-city";
    public const string NotYourCity = "not-your-city";
    public const string UnknownTile = "unknown-tile";
    public const string PendingDecisions = "pending-decisions";
    public const string InvalidTarget = "invalid-target";
    public const string NotAllowed = "not-allowed";
    public const string StaleReference = "stale-reference";
    /// <summary>T202, simultaneous turns: the round is being resolved (steps of <c>NextResolveStep</c>), nobody may act until it is Playing again.</summary>
    public const string TurnResolving = "turn-resolving";

    /// <summary>T202, simultaneous turns: the actor already ended its turn (take it back with <c>undo-end-turn</c>, or answer incoming requests only).</summary>
    public const string AlreadyEnded = "already-ended";

    /// <summary>T202, simultaneous turns: a server step command that is not the step <c>NextResolveStep</c> names (also: no round is being resolved).</summary>
    public const string OutOfSequence = "out-of-sequence";

    public const string PurchaseBlockedByUnit = "purchase-blocked-by-unit";

    /// <summary>The code for a missing stat, e.g. <c>not-enough-gold</c>. <paramref name="stat"/> is lower-cased invariantly.</summary>
    public static string NotEnough(string stat) => "not-enough-" + stat.ToLowerInvariant();
}
