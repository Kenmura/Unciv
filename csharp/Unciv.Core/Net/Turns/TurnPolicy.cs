namespace Unciv.Net.Turns;

/// <summary>Action to take when a human player disconnects or stays absent (SERVER.md 4.1).</summary>
public enum DisconnectAction
{
    /// <summary>The server issues <c>ForceEndTurn</c> after the disconnect grace period expires.</summary>
    ForceEndTurn,

    /// <summary>
    /// The server issues <c>ForceEndTurn</c> during grace period; if the player remains absent for
    /// <see cref="TurnPolicy.AbsentTurnsBeforeAiTakeover"/> consecutive turns, flips controller to AI via <c>SetController</c>.
    /// </summary>
    AiTakeover,
}

/// <summary>
/// Policy governing turn timers, disconnect grace periods, and absent player handling for simultaneous turns (SERVER.md 4).
/// </summary>
public sealed record TurnPolicy
{
    /// <summary>Default policy: timer disabled (0), 120s disconnect grace, ForceEndTurn on disconnect, 3 turns for AI takeover.</summary>
    public static readonly TurnPolicy Default = new();

    /// <summary>Seconds per turn. 0 = disabled (wait for everyone).</summary>
    public int TurnTimerSeconds { get; init; }

    /// <summary>Seconds before a disconnected player's turn is force-ended. Default 120s.</summary>
    public int DropGraceSeconds { get; init; } = 120;

    /// <summary>Action when a player disconnects: <see cref="DisconnectAction.ForceEndTurn"/> or <see cref="DisconnectAction.AiTakeover"/>.</summary>
    public DisconnectAction OnDisconnect { get; init; } = DisconnectAction.ForceEndTurn;

    /// <summary>Number of consecutive absent turns before AI takeover under <see cref="DisconnectAction.AiTakeover"/>. Default 3.</summary>
    public int AbsentTurnsBeforeAiTakeover { get; init; } = 3;
}
