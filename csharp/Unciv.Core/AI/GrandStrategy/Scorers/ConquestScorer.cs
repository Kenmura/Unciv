using Unciv.AI.Data;

namespace Unciv.AI.GrandStrategy.Scorers;

/// <summary>BNW conquest scorer (SPEC.md 4.1): stat base term, power ratio, war and cramped bonuses, met-rival tail.</summary>
public sealed class ConquestScorer
{
    private readonly int nobodyMetFirstTurn;
    private readonly int nobodyMetWeight;
    private readonly int strengthFirstTurn;
    private readonly int powerRatioMultiplier;
    private readonly int atWarWeight;
    private readonly int crampedWeight;

    public ConquestScorer(GlobalAIDefines defines, AIEdition edition = AIEdition.BNW)
    {
        ArgumentNullException.ThrowIfNull(defines);
        nobodyMetFirstTurn = defines.GetInt("AI_GS_CONQUEST_NOBODY_MET_FIRST_TURN", edition);
        nobodyMetWeight = defines.GetInt("AI_GRAND_STRATEGY_CONQUEST_NOBODY_MET_WEIGHT", edition);
        strengthFirstTurn = defines.GetInt("AI_GS_CONQUEST_MILITARY_STRENGTH_FIRST_TURN", edition);
        powerRatioMultiplier = defines.GetInt("AI_GRAND_STRATEGY_CONQUEST_POWER_RATIO_MULTIPLIER", edition);
        atWarWeight = defines.GetInt("AI_GRAND_STRATEGY_CONQUEST_AT_WAR_WEIGHT", edition);
        crampedWeight = defines.GetInt("AI_GRAND_STRATEGY_CONQUEST_CRAMPED_WEIGHT", edition);
    }

    // The tail's -50 is a literal in the decompile, not a define.
    private const int TailPenalty = 50;

    public int Score(ConquestInputs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.VictoryEnabled) return GrandStrategyEvaluator.DisabledVictoryScore;

        int s = input.BaseTerm;
        if (input.Turn >= nobodyMetFirstTurn && !input.HasMetMajor) s += nobodyMetWeight;
        if (input.Turn >= strengthFirstTurn && input.ReferenceStrength > 0)
        {
            int r = (input.MyStrength - input.ReferenceStrength) * powerRatioMultiplier / input.ReferenceStrength;
            if (r > 0) r /= 2; // only the upside is halved
            s += r;
        }
        if (input.AtWar) s += atWarWeight;
        if (input.Cramped) s += crampedWeight;
        if (input.MetMajorTriggersTail) s -= TailPenalty;
        return s;
    }
}
