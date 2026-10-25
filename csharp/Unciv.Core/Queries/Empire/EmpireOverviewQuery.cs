using Unciv.UI.Screens.VictoryScreen;
using Unciv.View;

namespace Unciv.Queries.Empire;

// Rankings: Value per RankingType name; empty for civs when the game hides rankings
public sealed record OverviewCiv(string CivName, bool IsCityState, bool IsDefeated, bool AtWar, IReadOnlyDictionary<string, int> Rankings);

public sealed record EmpireOverviewResult(int Turn, string CivName, int Gold, float GoldPerTurn, float SciencePerTurn, float CulturePerTurn,
    float FaithPerTurn, int Happiness, int Cities, int Units, int TechsResearched, int PoliciesAdopted, int Score,
    IReadOnlyDictionary<string, double> ScoreBreakdown, IReadOnlyDictionary<string, int> Rankings, IReadOnlyList<OverviewCiv> KnownCivs);

// T197: the numbers of the empire overview header and stats page. What to decide next is PendingDecisions (T184), not here.
// Other civs' rankings are listed only when GameParameters.ShowRankings is on, and never for civs the viewer has not met.
[QueryType("empire.Overview")]
public sealed record EmpireOverviewQuery : IQuery<EmpireOverviewResult>
{
    public string Type => "empire.Overview";

    public EmpireOverviewResult Run(GameView view)
    {
        var civView = view.CivView;
        var civ = civView.Unwrap();
        bool showOthers = civ.GameInfo.GameParameters.ShowRankings;
        var stats = civ.Stats.StatsForNextTurn;
        var known = civView.GetKnownCivs()
            .Where(it => !it.IsBarbarian())
            .OrderBy(it => it.CivName, StringComparer.Ordinal)
            .Select(it => new OverviewCiv(it.CivName, it.IsCityState, it.IsDefeated(), civView.IsAtWarWith(it),
                showOthers && it.IsMajorCiv() ? Rankings(it) : new Dictionary<string, int>()))
            .ToList();
        return new EmpireOverviewResult(view.GetTurn(), civView.CivName, civView.Gold, stats.Gold, stats.Science, stats.Culture, stats.Faith,
            civView.GetHappiness(), civ.Cities.Count, civView.GetUnits().Count, civ.Tech.GetNumberOfTechsResearched(),
            civ.Policies.AdoptedPolicies.Count, civView.GetStatForRanking(RankingType.Score),
            civView.CalculateScoreBreakdown().OrderBy(it => it.Key, StringComparer.Ordinal).ToDictionary(it => it.Key, it => it.Value),
            Rankings(civView), known);
    }

    private static Dictionary<string, int> Rankings(Unciv.View.ForeignCivView civ) =>
        Enum.GetValues<RankingType>().ToDictionary(it => it.ToString(), civ.GetStatForRanking);
}
