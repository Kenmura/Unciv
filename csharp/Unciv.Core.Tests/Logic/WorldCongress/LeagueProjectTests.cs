using Unciv.Commands.Events;
using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.WorldCongress;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.WorldCongress;

// B017 (bnw-spec/SPEC.md 4.7.7): the league projects, proven on the real generated BNW ruleset. A, B are humans, C is an AI, each with a city; S is a city-state.
// With 3 living members and the Standard speed the World's Fair costs 350 x 3 = 1050, so a share is 350: tier 1 from 175, tier 2 from 350.
[Collection("RulesetCache")]
public sealed class LeagueProjectTests
{
    private const string Fair = "World's Fair";
    private const string Games = "International Games";
    private const string Station = "International Space Station";

    private readonly TestGame game = new(BaseRuleset.Civ_V_BNW);
    private readonly CivilizationObject a, b, c, s;
    private readonly CityObject cityA, cityB, cityC;
    private readonly League league;
    private readonly GameInfoEventSink sink = new();
    private GameInfo G => game.GameInfo;
    private int nextId = 100;

    public LeagueProjectTests()
    {
        game.MakeHexagonalMap(9);
        a = game.AddCiv(isPlayer: true);
        b = game.AddCiv(isPlayer: true);
        c = game.AddCiv();
        s = game.AddCiv(cityStateType: "Cultured");
        cityA = game.AddCity(a, game.GetTile(-5, 0));
        cityB = game.AddCity(b, game.GetTile(0, 0));
        cityC = game.AddCity(c, game.GetTile(5, 0));
        game.AddCity(s, game.GetTile(0, 6));
        foreach (var civ in new[] { a, b, c, s })
            foreach (var other in new[] { a, b, c, s })
                if (civ != other) civ.DiplomacyFunctions.MakeCivilizationsMeet(other);
        a.Tech.AddTechnology("Printing Press");
        a.Tech.Era = game.Ruleset.Eras["Renaissance era"];
        LeagueRules.ProcessTurn(G);
        league = G.League.NotNull();
        league.Proposals.Clear();
        league.InSession = false;
        G.EventSink = sink;
    }

    private List<ProjectCompleted> Completed() => sink.Drain().OfType<ProjectCompleted>().ToList();

    private ActiveResolution Enact(string resolution)
    {
        var active = new ActiveResolution { Id = nextId++, Resolution = resolution, Proposer = a.CivId, Turn = G.Turns };
        league.Active.Add(active);
        LeagueEffects.OnChanged(G, league);
        return active;
    }

    private static string Process(string project) => project + " Project";

    private void Put(string project, CivilizationObject civ, int production)
    {
        var city = civ == a ? cityA : civ == b ? cityB : cityC;
        LeagueProjects.Contribute(city, project, production);
    }

    // ---- the process

    [Fact]
    public void TheProcessIsBuildableOnlyWhileTheProjectIsEnabledAndOnlyForMembers()
    {
        var process = PerpetualConstruction.LeagueProcesses[Process(Fair)];
        Assert.False(process.IsBuildable(cityA.CityConstructions));
        var active = Enact(Fair);
        Assert.True(process.IsBuildable(cityA.CityConstructions));
        Assert.False(process.IsBuildable(s.GetCapital()!.CityConstructions));
        Assert.False(PerpetualConstruction.LeagueProcesses[Process(Games)].IsBuildable(cityA.CityConstructions));
        league.Active.Remove(active);
        LeagueEffects.OnChanged(G, league);
        Assert.False(process.IsBuildable(cityA.CityConstructions));
    }

    [Fact]
    public void TheProcessIsAPerpetualConstructionTheQueueUnderstands()
    {
        Enact(Fair);
        Assert.True(PerpetualConstruction.IsNamePerpetual(Process(Fair)));
        cityA.CityConstructions.AddToQueue(Process(Fair));
        Assert.Equal(Process(Fair), cityA.CityConstructions.CurrentConstructionName());
        Assert.IsType<PerpetualConstruction.LeagueProcess>(cityA.CityConstructions.GetCurrentConstruction());
    }

    [Fact]
    public void ACityPutsItsProductionOfTheTurnIntoTheProjectAndKeepsNothing()
    {
        Enact(Fair);
        cityA.CityConstructions.AddToQueue(Process(Fair));
        cityA.CityConstructions.ProductionOverflow = 3;
        cityA.CityConstructions.EndTurn(new Stats(production: 40f));
        cityA.CityConstructions.EndTurn(new Stats(production: 12.5f)); // rounds half up to 13
        var project = LeagueProjects.Find(league, Fair).NotNull();
        Assert.Equal(43 + 13, project.ContributionOf(a.CivId));
        Assert.Equal(0, cityA.CityConstructions.ProductionOverflow);
        Assert.Equal(0, project.ContributionOf(b.CivId));
    }

    [Fact]
    public void AProjectThatIsNotEnabledTakesNothing()
    {
        LeagueProjects.Contribute(cityA, Fair, 50);
        Assert.Empty(league.Projects);
    }

    [Fact]
    public void TheQueueDropsTheProcessWhenTheProjectIsGone()
    {
        var active = Enact(Fair);
        cityA.CityConstructions.AddToQueue(Process(Fair));
        league.Active.Remove(active);
        LeagueEffects.OnChanged(G, league);
        cityA.CityConstructions.EndTurn(new Stats(production: 5f)); // ValidateConstructionQueue drops it first
        Assert.NotEqual(Process(Fair), cityA.CityConstructions.CurrentConstructionName());
        Assert.Empty(league.Projects);
    }

    // ---- cost and tiers (hand computed)

    [Fact]
    public void TheCostIsThePerPlayerCostTimesTheLivingMembers()
    {
        Assert.Equal(1.0f, G.Speed.ProductionCostModifier);
        Assert.Equal(350 * 3, LeagueProjects.Cost(G, league, Fair));
        Assert.Equal(720 * 3, LeagueProjects.Cost(G, league, Games));
        Assert.Equal(850 * 3, LeagueProjects.Cost(G, league, Station));
    }

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(174, false, 0)]
    [InlineData(175, false, 1)]
    [InlineData(349, false, 1)]
    [InlineData(350, false, 2)]
    [InlineData(1000, false, 2)]
    [InlineData(1, true, 3)]
    [InlineData(0, true, 0)]
    public void TheTierFollowsTheThresholdsAndTheTopContributor(int contribution, bool top, int tier) =>
        Assert.Equal(tier, LeagueProjects.Tier(G.Ruleset, contribution, 1050, 3, top));

    [Fact]
    public void TheRealBnwRulesetHasTheThresholds()
    {
        Assert.Equal(0.5f, G.Ruleset.ModOptions.Constants.LeagueProjectRewardTier1Threshold);
        Assert.Equal(1, G.Ruleset.ModOptions.Constants.LeagueProjectRewardTier2Threshold);
    }

    // ---- completion and rewards

    [Fact]
    public void ABelowCostProjectKeepsRunning()
    {
        Enact(Fair);
        Put(Fair, a, 1049);
        LeagueProjects.ProcessTurn(G, league);
        Assert.NotNull(LeagueProjects.Find(league, Fair));
        Assert.Empty(Completed());
    }

    [Fact]
    public void TheWorldsFairRewardsEveryTierAndConsumesTheResolution()
    {
        Enact(Fair);
        Put(Fair, a, 701); // top, tier 3 (701 + 175 + 174 = 1050, the cost)
        Put(Fair, b, 175); // exactly the tier 1 threshold
        Put(Fair, c, 174); // just below it (AI)
        int policiesA = a.Policies.FreePolicies, goldenA = a.GoldenAges.StoredHappiness, goldenB = b.GoldenAges.StoredHappiness, goldenC = c.GoldenAges.StoredHappiness;
        float cultureBefore = ProductionCulture(cityA);
        LeagueProjects.ProcessTurn(G, league);

        var completed = Assert.Single(Completed());
        Assert.Equal(Fair, completed.Project);
        Assert.Equal([KeyValuePair.Create(a.CivId, 3), KeyValuePair.Create(b.CivId, 1), KeyValuePair.Create(c.CivId, 0)], completed.Tiers);
        Assert.Equal(goldenA + 500, a.GoldenAges.StoredHappiness);
        Assert.Equal(policiesA + 1, a.Policies.FreePolicies);
        Assert.Equal(20, a.Culture.NotNull().CultureBonusTurns);
        Assert.Equal(goldenB + 500, b.GoldenAges.StoredHappiness);
        Assert.Equal(0, b.Policies.FreePolicies);
        Assert.Equal(0, b.Culture?.CultureBonusTurns ?? 0);
        Assert.Equal(goldenC, c.GoldenAges.StoredHappiness);
        Assert.Empty(league.Projects);
        Assert.Empty(league.Active);
        Assert.False(PerpetualConstruction.LeagueProcesses[Process(Fair)].IsBuildable(cityA.CityConstructions));
        // +100% culture: the capital's culture doubles while the boost lasts
        Assert.Equal(cultureBefore * 2f, ProductionCulture(cityA));
    }

    private static float ProductionCulture(CityObject city)
    {
        city.CityStats.Update();
        return city.CityStats.CurrentCityStats.Culture;
    }

    [Fact]
    public void TheCultureBoostRunsOutAfterTwentyTurns()
    {
        var culture = (a.Culture ??= new());
        culture.AddCultureBonusTurns(2);
        LeagueProjects.ProcessTurn(G, league);
        Assert.Equal(1, culture.CultureBonusTurns);
        LeagueProjects.ProcessTurn(G, league);
        Assert.Equal(0, culture.CultureBonusTurns);
    }

    [Fact]
    public void TheInternationalGamesGiveHappinessInfluenceAndATourismBoost()
    {
        Enact(Games);
        Put(Games, a, 1000); // top: tiers 1 to 3
        Put(Games, b, 1160); // 2160 total; b is the top, a is tier 2 (1000 >= 720)
        float happinessA = a.Stats.GetHappinessBreakdown().TryGetValue("World Congress", out var h) ? h : 0f;
        Assert.Equal(0f, happinessA);
        LeagueProjects.ProcessTurn(G, league);

        var tiers = Assert.Single(Completed()).Tiers;
        Assert.Equal([KeyValuePair.Create(a.CivId, 2), KeyValuePair.Create(b.CivId, 3), KeyValuePair.Create(c.CivId, 0)], tiers);
        // tier 2 = +3 and +3 happiness, +30 influence with the known city-state
        Assert.Equal(6f, a.Stats.GetHappinessBreakdown()["World Congress"]);
        Assert.Equal(6f, b.Stats.GetHappinessBreakdown()["World Congress"]);
        Assert.Equal(30f, s.GetDiplomacyManager(a).NotNull().GetInfluence());
        Assert.Equal(0, a.Policies.FreePolicies);
        Assert.Equal(0, a.Culture?.TourismBonusTurns ?? 0);
        // tier 3 adds a free policy and 20 turns of +100% tourism
        Assert.Equal(1, b.Policies.FreePolicies);
        Assert.Equal(20, b.Culture.NotNull().TourismBonusTurns);
        Assert.False(c.Stats.GetHappinessBreakdown().ContainsKey("World Congress"));
    }

    [Fact]
    public void TheSpaceStationGivesScienceAGreatScientistAndTheBuilding()
    {
        Enact(Station);
        Put(Station, a, 2000); // top, tier 3
        Put(Station, b, 1200); // 1200 >= 850: tier 2
        Put(Station, c, 450); // 450 >= 425 (half of a share of 850): tier 1
        a.Tech.TechsToResearch.Add("Theology");
        b.Tech.TechsToResearch.Add("Theology");
        c.Tech.TechsToResearch.Add("Theology");
        int bulbsC = c.Tech.ResearchOfTech("Theology");
        LeagueProjects.ProcessTurn(G, league);

        Assert.True(cityA.CityConstructions.IsBuilt(Station));
        Assert.False(cityB.CityConstructions.IsBuilt(Station));
        Assert.Contains(b.Units.GetCivUnits(), it => it.Name == "Great Scientist");
        Assert.Contains(a.Units.GetCivUnits(), it => it.Name == "Great Scientist"); // tier 3 includes tier 2
        Assert.DoesNotContain(c.Units.GetCivUnits(), it => it.Name == "Great Scientist");
        float science = c.Stats.StatsForNextTurn.Science; // recomputed at the reward
        Assert.True(science > 0f);
        Assert.Equal(bulbsC + KotlinMath.RoundToInt(science * 4), c.Tech.ResearchOfTech("Theology")); // 4 turns of science
    }

    [Fact]
    public void TiedTopContributorsAllGetTheTopTier()
    {
        Enact(Fair);
        Put(Fair, a, 600);
        Put(Fair, b, 600);
        LeagueProjects.ProcessTurn(G, league);
        var tiers = Assert.Single(Completed()).Tiers;
        Assert.Equal([3, 3, 0], tiers.Select(it => it.Value));
    }

    [Fact]
    public void ARepealedProjectLosesItsProgressAndAFinishedOneCanBeStartedAgain()
    {
        var active = Enact(Fair);
        Put(Fair, a, 100);
        league.Active.Remove(active);
        LeagueEffects.OnChanged(G, league);
        Assert.Empty(league.Projects);

        Enact(Fair);
        Put(Fair, a, 1050);
        LeagueProjects.ProcessTurn(G, league);
        Assert.Empty(league.Active);
        Enact(Fair); // enacted again after completion
        Put(Fair, b, 10);
        Assert.Equal(10, LeagueProjects.Find(league, Fair).NotNull().Total);
    }

    [Fact]
    public void TheCostFollowsTheLivingMembers()
    {
        Enact(Fair);
        Put(Fair, a, 700);
        foreach (var city in b.Cities.ToList()) city.DestroyCity(overrideSafeties: true);
        Assert.Equal(700, LeagueProjects.Cost(G, league, Fair));
        LeagueProjects.ProcessTurn(G, league);
        var tiers = Assert.Single(Completed()).Tiers;
        Assert.Equal([KeyValuePair.Create(a.CivId, 3), KeyValuePair.Create(c.CivId, 0)], tiers);
    }

    [Fact]
    public void TheLeagueWithProjectsCloneKeepsThem()
    {
        Enact(Fair);
        Put(Fair, a, 7);
        league.GetMember(a.CivId)!.ProjectHappiness = 3;
        var clone = league.Clone();
        Assert.Equal(7, LeagueProjects.Find(clone, Fair).NotNull().ContributionOf(a.CivId));
        Assert.Equal(3, clone.GetMember(a.CivId)!.ProjectHappiness);
        Put(Fair, a, 1);
        Assert.Equal(7, LeagueProjects.Find(clone, Fair).NotNull().Total);
    }

    [Fact]
    public void TheQueryListsTheEnabledProjects()
    {
        Enact(Games);
        Put(Games, a, 30);
        var result = new Unciv.Queries.Empire.LeagueQuery().Run(new Unciv.View.GameView(G, a));
        var row = Assert.Single(result.Projects.NotNull());
        Assert.Equal(new Unciv.Queries.Empire.LeagueProjectRow(Games, 2160, 30, 30), row);
    }
}
