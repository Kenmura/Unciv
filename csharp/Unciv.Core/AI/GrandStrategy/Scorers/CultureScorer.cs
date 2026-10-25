using Unciv.AI.Data;

namespace Unciv.AI.GrandStrategy.Scorers;

/// <summary>BNW culture scorer (SPEC.md 4.2): tourism/culture lead over rivals plus an influential-civ bonus.</summary>
public sealed class CultureScorer
{
    private readonly int aheadWeight;
    private readonly int tourismAheadWeight;
    private readonly int influentialCivMod;

    public CultureScorer(GlobalAIDefines defines, AIEdition edition = AIEdition.BNW)
    {
        ArgumentNullException.ThrowIfNull(defines);
        aheadWeight = defines.GetInt("AI_GS_CULTURE_AHEAD_WEIGHT", edition);
        tourismAheadWeight = defines.GetInt("AI_GS_CULTURE_TOURISM_AHEAD_WEIGHT", edition);
        influentialCivMod = defines.GetInt("AI_GS_CULTURE_INFLUENTIAL_CIV_MOD", edition);
    }

    public int Score(CultureInputs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.VictoryEnabled) return GrandStrategyEvaluator.DisabledVictoryScore;

        int s = (10 - input.UnknownX) * input.CultureFlavor * 200 / 100;
        int cultureAhead = 0, cultureNotAhead = 0, tourismAhead = 0, tourismNotAhead = 0;
        foreach (var rival in input.Rivals)
        {
            if (rival.Culture < input.MyCulture) cultureAhead++; else cultureNotAhead++;
            if (rival.Tourism < input.MyTourism) tourismAhead++; else tourismNotAhead++;
        }
        int n = input.Rivals.Count;
        if (n > 0)
        {
            if (cultureNotAhead < cultureAhead) s += (cultureAhead - cultureNotAhead) * aheadWeight / n;
            if (tourismNotAhead < tourismAhead) s += (tourismAhead - tourismNotAhead) * tourismAheadWeight / n;
        }
        return s + influentialCivMod * input.InfluentialCivCount;
    }
}
