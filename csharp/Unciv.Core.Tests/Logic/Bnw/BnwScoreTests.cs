using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using PolicyObject = Unciv.Models.Ruleset.Policy;

namespace Unciv.Core.Tests.Logic.Bnw;

// B025 (bnw-spec 4.15): Brave New World score on the REAL generated ruleset: +4 per policy, +4 per Great Work, +20 per belief and +1 per city
// following the civ's religion. The G&K ruleset (multipliers 0) keeps its six lines.
[Collection("RulesetCache")]
public sealed class BnwScoreTests : IDisposable
{
    public BnwScoreTests() => RulesetCache.Clear();

    public void Dispose() => RulesetCache.Clear();

    private static (GameInfo Game, CivilizationObject Civ, CityObject Capital) NewCapital(BaseRuleset ruleset, string first = "Brazil", string second = "Poland")
    {
        Models.Ruleset.BnwRulesetTests.LoadBnw();
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.FullName(),
                    NumberOfCityStates = 0,
                    Players = [new Player(first, PlayerType.Human), new Player(second)],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            5, 1000L);
        var civ = game.Civilizations.First(it => it.IsMajorCiv());
        var settler = civ.Units.GetCivUnits().First(it => it.HasUnique(UniqueType.FoundCity));
        var capital = civ.AddCity(settler.GetTile().Position, settler);
        foreach (var unit in civ.Units.GetCivUnits().Where(it => ReferenceEquals(it.GetTile(), capital.GetCenterTile())).ToList()) unit.Destroy();
        return (game, civ, capital);
    }

    private static string BeliefOf(Ruleset ruleset, BeliefType type) =>
        ruleset.Beliefs.Values.First(it => it.Type == type && !it.Uniques.Any(u => u.Contains("pressure", StringComparison.OrdinalIgnoreCase) || u.Contains("spread", StringComparison.OrdinalIgnoreCase))).Name;

    [Fact]
    public void TheBnwRulesetCarriesTheFourScoreMultipliers()
    {
        var c = Models.Ruleset.BnwRulesetTests.LoadBnw().ModOptions.Constants;
        Assert.Equal((4, 4, 20, 1), (c.ScorePolicyMultiplier, c.ScoreGreatWorkMultiplier, c.ScoreBeliefMultiplier, c.ScoreReligionCitiesMultiplier));
    }

    [Fact]
    public void ABnwCivWithNothingScoresNoExtraLines()
    {
        var (_, civ, _) = NewCapital(BaseRuleset.Civ_V_BNW);
        var breakdown = civ.CalculateScoreBreakdown();
        Assert.Equal(0.0, breakdown["Policies"]);
        Assert.Equal(0.0, breakdown["Great Works"]);
        Assert.Equal(0.0, breakdown["Beliefs"]);
        Assert.Equal(0.0, breakdown["Religion"]);
    }

    [Fact]
    public void EachPolicyScoresFourButTheBranchCompleteBonusDoesNot()
    {
        var (game, civ, _) = NewCapital(BaseRuleset.Civ_V_BNW);
        var branch = game.Ruleset.PolicyBranches.Values.First();
        var member = branch.Policies.First(it => it.PolicyBranchType == PolicyObject.PolicyBranchTypes.Member);
        var before = civ.CalculateTotalScore();
        civ.Policies.AdoptedPolicies.Add(branch.Name);
        civ.Policies.AdoptedPolicies.Add(member.Name);
        Assert.Equal(8.0, civ.CalculateScoreBreakdown()["Policies"]);
        civ.Policies.AdoptedPolicies.Add(branch.Name + PolicyObject.BranchCompleteSuffix);
        Assert.Equal(8.0, civ.CalculateScoreBreakdown()["Policies"]);
        Assert.Equal(before + 8.0, civ.CalculateTotalScore());
    }

    [Fact]
    public void EachGreatWorkInTheCivsCitiesScoresFour()
    {
        var (game, civ, capital) = NewCapital(BaseRuleset.Civ_V_BNW);
        capital.CityConstructions.AddBuilding("Parthenon");
        Assert.Equal(1, capital.GetNumberOfGreatWorks());
        Assert.Equal(4.0, civ.CalculateScoreBreakdown()["Great Works"]);
        var other = game.Civilizations.First(it => it.IsMajorCiv() && it != civ);
        Assert.Equal(0.0, other.CalculateScoreBreakdown()["Great Works"]);
    }

    [Fact]
    public void EachBeliefScoresTwentyAndEachCityFollowingTheReligionScoresOne()
    {
        var (game, civ, capital) = NewCapital(BaseRuleset.Civ_V_BNW);
        var ruleset = game.Ruleset;
        var manager = civ.ReligionManager;
        var prophet = civ.Units.AddUnit("Great Prophet", capital).NotNull();
        manager.FoundReligion(prophet);
        manager.FoundReligion(civ.CivName, "Religion of Brazil");
        manager.ChooseBeliefs([ruleset.Beliefs[BeliefOf(ruleset, BeliefType.Founder)], ruleset.Beliefs[BeliefOf(ruleset, BeliefType.Follower)]]);
        manager.Religion.NotNull().AddBelief(BeliefOf(ruleset, BeliefType.Pantheon));
        int beliefs = manager.Religion.NotNull().GetAllBeliefsOrdered().Count();
        Assert.Equal(3, beliefs);
        capital.Religion.AddPressure(manager.Religion.Name, 100000);
        var breakdown = civ.CalculateScoreBreakdown();
        Assert.Equal(60.0, breakdown["Beliefs"]);
        Assert.Equal(1.0, breakdown["Religion"]);
        Assert.Equal(0.0, game.Civilizations.First(it => it.IsMajorCiv() && it != civ).CalculateScoreBreakdown()["Beliefs"]);
    }

    [Fact]
    public void GodsAndKingsScoreKeepsItsSixLines()
    {
        var (_, civ, _) = NewCapital(BaseRuleset.Civ_V_GnK, "Rome", "Greece");
        var breakdown = civ.CalculateScoreBreakdown();
        Assert.Equal(6, breakdown.Count);
        Assert.False(breakdown.ContainsKey("Policies"));
    }
}
