using System.Text.Json.Serialization;

namespace Unciv.Logic;

// T200: the state of simultaneous human turns (design/SIMULTANEOUS-TURNS.md sections 2-3). Kotlin has none of it: Unciv rotates through the civs.
// Serialized as the optional GameInfo member "turnState"; a Sequential game never has one, so its saves are unchanged, and a reader that does
// not know the member ignores it (System.Text.Json skips unmapped members).

/// <summary>How the humans of a game take their turns.</summary>
public enum TurnMode
{
    /// <summary>One current player at a time, <see cref="GameInfo.NextTurn"/>: Kotlin's behaviour.</summary>
    Sequential,

    /// <summary>All living humans act at once, then the turn is resolved in a fixed civ-index order.</summary>
    Simultaneous,
}

/// <summary>Where a simultaneous game is: humans acting, or the round being resolved.</summary>
public enum SimultaneousPhase
{
    /// <summary>Every living human is active (until it ends its turn).</summary>
    Playing,

    /// <summary>Everybody ended the turn; the steps of <see cref="GameInfo.NextResolveStep"/> run.</summary>
    Resolving,
}

/// <summary>The stage of the resolution sequence a <see cref="TurnState"/> is in, see <see cref="ResolveStep"/>.</summary>
public enum ResolveStage
{
    /// <summary>Step 1: <c>ProcessEndTurn</c> for each human.</summary>
    EndTurns,

    /// <summary>Step 2: <c>RunAiTurn</c> for each civ that plays automatically.</summary>
    AiRound,

    /// <summary>Step 3: <c>StartNextTurn</c> once.</summary>
    NextTurn,

    /// <summary>Step 4: <c>BeginTurn</c> for each human.</summary>
    BeginTurns,
}

/// <summary>The saved state of simultaneous turns; see <see cref="GameInfo.TurnState"/>.</summary>
public sealed class TurnState
{
    [JsonPropertyName("mode")]
    public TurnMode Mode { get; set; } = TurnMode.Sequential;

    [JsonPropertyName("phase")]
    public SimultaneousPhase Phase { get; set; } = SimultaneousPhase.Playing;

    /// <summary>The humans that ended their turn this round, in the order they did (Playing; cleared when the round is resolved).</summary>
    [JsonPropertyName("ended")]
    public List<string> Ended { get; set; } = [];

    /// <summary>The civs whose turn has begun and not ended (<see cref="GameInfo.IsTurnStarted"/>), in civ-index order.</summary>
    [JsonPropertyName("started")]
    public List<string> Started { get; set; } = [];

    /// <summary>Resolving: the stage whose steps run next.</summary>
    [JsonPropertyName("stage")]
    public ResolveStage Stage { get; set; } = ResolveStage.EndTurns;

    /// <summary>Resolving: the civ index the scan for the next step of <see cref="Stage"/> starts at.</summary>
    [JsonPropertyName("next")]
    public int Next { get; set; }

    /// <summary>T228: the host allows StartAutoPlay in this simultaneous game (auto-play changes outcomes, so it is refused by default).</summary>
    [JsonPropertyName("autoPlayAllowed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutoPlayAllowed { get; set; }

    public TurnState Clone() => new()
    {
        AutoPlayAllowed = AutoPlayAllowed,
        Mode = Mode,
        Phase = Phase,
        Ended = [.. Ended],
        Started = [.. Started],
        Stage = Stage,
        Next = Next,
    };
}

/// <summary>One step of resolving a simultaneous round. <see cref="GameInfo.NextResolveStep"/> names it; the matching GameInfo method performs it.</summary>
public abstract record ResolveStep
{
    private ResolveStep()
    {
    }

    /// <summary>Step 1: the human ends its turn (<c>TurnManager.EndTurn</c>), see <see cref="GameInfo.ProcessEndTurn"/>.</summary>
    public sealed record ProcessEndTurn(string CivId) : ResolveStep;

    /// <summary>Step 2: StartTurn, AutomateTurn, EndTurn of a civ that plays automatically, see <see cref="GameInfo.RunAiTurn"/>.</summary>
    public sealed record RunAiTurn(string CivId) : ResolveStep;

    /// <summary>Step 3: record ranking stats and advance <c>Turns</c>, see <see cref="GameInfo.StartNextTurn"/>.</summary>
    public sealed record StartNextTurn : ResolveStep;

    /// <summary>Step 4: the human's next turn begins, see <see cref="GameInfo.BeginTurn"/>.</summary>
    public sealed record BeginTurn(string CivId) : ResolveStep;
}
