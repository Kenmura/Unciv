using Unciv.AI.Data;

namespace Unciv.AI.GrandStrategy.Scorers;

/// <summary>BNW diplomatic scorer (SPEC.md 4.3): a World Congress vote race.</summary>
public sealed class DiplomaticScorer
{
    // Clamp magnitudes of the lead bonus, from the decompile; SPEC.md 4.3 asks for them to be tunables.
    public const int LeadLimit = 40;
    public const int UncertainLeadLimit = 20;
    public const int TrailingLimit = 40;
    private const int ThreeQuartersBonus = 40;
    private const int WinScore = 1000;

    private readonly int eachMinorAttackedWeight;

    public DiplomaticScorer(GlobalAIDefines defines, AIEdition edition = AIEdition.BNW)
    {
        ArgumentNullException.ThrowIfNull(defines);
        eachMinorAttackedWeight = defines.GetInt("AI_GRAND_STRATEGY_UN_EACH_MINOR_ATTACKED_WEIGHT", edition);
    }

    public int Score(DiplomaticInputs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.VictoryEnabled) return GrandStrategyEvaluator.DisabledVictoryScore;

        int s = eachMinorAttackedWeight * input.MinorsAttacked;
        if (!input.LeagueExists)
        {
            // No vote race exists yet, so none of the vote terms apply.
            s += (10 - input.UnknownX) * input.DiplomacyFlavor * 150 / 100;
        }
        else if (input.VotesNeeded > 0)
        {
            if (input.MyVotes >= input.VotesNeeded) return WinScore;
            if (input.MyVotes * 4 >= input.VotesNeeded * 3) s += ThreeQuartersBonus;

            int lead = input.MyVotes - input.BestRivalVotes;
            int adjusted = lead + 2 * input.UnknownRivals;
            if (lead > 0) s += Clamp5(lead, LeadLimit);
            else if (adjusted > 0) s += Clamp5(adjusted, UncertainLeadLimit);
            else if (adjusted < 0) s += Clamp5(adjusted, TrailingLimit);
        }
        return s + input.Modifier1 - input.Modifier3 + input.Modifier2;
    }

    // 5 * x bounded to [-limit, limit] (the decompiled compare-and-select is a clamp artifact, see SPEC.md 4.3).
    private static int Clamp5(int x, int limit) => Math.Clamp(5 * x, -limit, limit);
}
