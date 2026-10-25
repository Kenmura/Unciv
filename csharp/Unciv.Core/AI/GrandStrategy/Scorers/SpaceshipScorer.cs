using Unciv.AI.Data;

namespace Unciv.AI.GrandStrategy.Scorers;

/// <summary>Spaceship scorer (SPEC.md 4.4), identical in all editions.</summary>
public sealed class SpaceshipScorer
{
    private const int ProgressWeight = 5;

    private readonly int apolloWeight;

    public SpaceshipScorer(GlobalAIDefines defines, AIEdition edition = AIEdition.BNW)
    {
        ArgumentNullException.ThrowIfNull(defines);
        apolloWeight = defines.GetInt("AI_GS_SS_HAS_APOLLO_PROGRAM", edition);
    }

    public int Score(SpaceshipInputs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.VictoryEnabled) return GrandStrategyEvaluator.DisabledVictoryScore;
        int s = ProgressWeight * input.ProgressCount;
        if (input.HasApolloProgram) s += apolloWeight;
        return s;
    }
}
