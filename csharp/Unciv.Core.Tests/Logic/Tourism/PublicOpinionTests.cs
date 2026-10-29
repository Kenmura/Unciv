using Unciv.Commands;
using Unciv.Commands.Empire;
using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Tourism;
using Unciv.Models.Metadata;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Tourism;

// B014 (bnw-spec/SPEC.md 4.6.4 and 4.6.5): public opinion, opinion unhappiness, switching ideology and anarchy. All values are computed by hand.
// Influence is set so that a civ's percentage over another is its influence (every civ has 100 lifetime culture): 10 Exotic(1), 30 Familiar(2),
// 60 Popular(3), 100 Influential(4), 200 Dominant(5).
[Collection("RulesetCache")]
public sealed class PublicOpinionTests
{
    private const int Exotic = 10, Familiar = 30, Popular = 60, Influential = 100, Dominant = 200;

    private sealed class World
    {
        public TestGame Game { get; } = new(BaseRuleset.Civ_V_BNW);
        public List<CivilizationObject> Civs { get; } = [];

        public World(int civCount, string[] ideologies, bool human = false, string[]? uniques = null)
        {
            var c = Game.Ruleset.ModOptions.Constants;
            (c.CultureLevelExotic, c.CultureLevelFamiliar, c.CultureLevelPopular, c.CultureLevelInfluential, c.CultureLevelDominant) = (10, 30, 60, 100, 200);
            Game.MakeHexagonalMap(12);
            for (int i = 0; i < civCount; i++)
            {
                var civ = Game.AddCiv(i == 0 ? uniques : null, isPlayer: human && i == 0);
                civ.Tech.Era = Game.Ruleset.Eras["Modern era"];
                civ.TotalCultureForContests = 100;
                Game.AddCity(civ, Game.GetTile(-8 + 4 * i, -6), initialPopulation: 1);
                Civs.Add(civ);
            }
            foreach (var x in Civs) foreach (var y in Civs) if (x != y) x.DiplomacyFunctions.MakeCivilizationsMeet(y);
            for (int i = 0; i < civCount && i < ideologies.Length; i++)
                if (ideologies[i] != "") Civs[i].Policies.ChooseIdeology(Game.Ruleset.PolicyBranches[ideologies[i]]);
        }

        public void Influence(int influencer, int target, int percent) =>
            (Civs[influencer].Culture ??= new()).GetOrAdd(Civs[target].CivId).Influence = percent;

        public void GiveCities(int civ, params int[] populations)
        {
            var existing = Civs[civ].Cities.ToList();
            for (int i = 0; i < populations.Length; i++)
            {
                var city = i < existing.Count ? existing[i] : Game.AddCity(Civs[civ], Game.GetTile(-8 + 4 * civ, -6 + 3 * i));
                city.Population.Population = populations[i];
            }
        }
    }

    private static PublicOpinion Opinion(World w, int civ = 0) => PublicOpinionRules.Evaluate(w.Civs[civ]);

    [Fact]
    public void ACivWithoutIdeologyIsContentAndGodsAndKingsHasNoPublicOpinion()
    {
        var w = new World(2, ["", "Order"]);
        w.Influence(1, 0, Dominant);
        Assert.Equal(PublicOpinion.Content, Opinion(w));
        Assert.Equal(0, PublicOpinionRules.Unhappiness(w.Civs[0]));
        Assert.DoesNotContain("Public opinion", w.Civs[0].Stats.GetHappinessBreakdown().Keys);

        var gk = new TestGame();
        gk.MakeHexagonalMap(3);
        var civ = gk.AddCiv();
        Assert.Equal(PublicOpinion.Content, PublicOpinionRules.Evaluate(civ));
        Assert.Null(civ.Policies.IdeologyHistory);
        Assert.Equal(0, civ.Policies.AnarchyTurns);
    }

    [Theory]
    [InlineData(0, PublicOpinionLevel.Content, 0)]       // no influence at all
    [InlineData(Exotic, PublicOpinionLevel.Dissidents, 1)]   // d = 1
    [InlineData(Familiar, PublicOpinionLevel.Dissidents, 2)] // d = 2: the last Dissidents value (D < 3)
    [InlineData(Popular, PublicOpinionLevel.CivilResistance, 3)] // d = 3: the first Civil Resistance
    [InlineData(Influential, PublicOpinionLevel.CivilResistance, 4)] // d = 4: the last one (3 <= D <= 4)
    [InlineData(Dominant, PublicOpinionLevel.RevolutionaryWave, 5)] // d = 5
    public void TheLevelOfTheForeignInfluenceDecidesTheOpinion(int percent, PublicOpinionLevel expected, int d)
    {
        var w = new World(2, ["Freedom", "Order"]);
        w.Influence(1, 0, percent);
        var opinion = Opinion(w);
        Assert.Equal(expected, opinion.Level);
        if (expected == PublicOpinionLevel.Content)
        {
            Assert.Null(opinion.Preferred);
            Assert.Equal(0, opinion.Unhappiness);
            return;
        }
        Assert.Equal("Order", opinion.Preferred);
        Assert.Equal(d, opinion.Dissatisfaction);
        Assert.Equal([new IdeologyPressure(w.Civs[1].CivId, "Order", d)], opinion.Pressures);
    }

    [Fact]
    public void OnlyTheExcessOfTheirInfluenceOverOursCounts()
    {
        var w = new World(2, ["Freedom", "Order"]);
        w.Influence(1, 0, Dominant); // 5
        w.Influence(0, 1, Influential); // we have 4 over them: d = 5 - 4 = 1
        Assert.Equal(1, Opinion(w).Dissatisfaction);
        w.Influence(0, 1, Dominant); // equal: no pressure
        Assert.Equal(PublicOpinionLevel.Content, Opinion(w).Level);
        Assert.Empty(Opinion(w).Pressures);
        w.Influence(0, 1, 0);
        w.Influence(1, 0, Familiar); // they have 2, we 0
        w.Influence(0, 1, Popular); // we are ahead by 1: nothing
        Assert.Equal(PublicOpinionLevel.Content, Opinion(w).Level);
    }

    [Fact]
    public void CivsWithMyOwnIdeologyPushTheOpinionBack()
    {
        var w = new World(3, ["Freedom", "Order", "Freedom"]);
        w.Influence(1, 0, Popular); // Order pushes 3
        Assert.Equal(3, Opinion(w).Dissatisfaction);
        w.Influence(2, 0, Influential); // a fellow Freedom civ pushes 4 on my own side: 3 against 4
        Assert.Equal(PublicOpinionLevel.Content, Opinion(w).Level);
        Assert.Equal(0, Opinion(w).Unhappiness);
        w.Influence(2, 0, Familiar); // 3 against 2: D = 1
        var opinion = Opinion(w);
        Assert.Equal((PublicOpinionLevel.Dissidents, 1), (opinion.Level, opinion.Dissatisfaction));
        w.Influence(2, 0, Popular); // 3 against 3: still content
        Assert.Equal(PublicOpinionLevel.Content, Opinion(w).Level);
    }

    [Fact]
    public void TwoOtherIdeologiesAddUpAndTheLargerIsPreferredWithTiesInTheObservedOrder()
    {
        var w = new World(3, ["Freedom", "Order", "Autocracy"]);
        w.Influence(1, 0, Popular);
        w.Influence(2, 0, Popular); // 3 + 3 = 6 against 0, tie: Freedom, Autocracy, Order -> Autocracy before Order
        var opinion = Opinion(w);
        Assert.Equal((PublicOpinionLevel.RevolutionaryWave, 6, "Autocracy"), (opinion.Level, opinion.Dissatisfaction, opinion.Preferred));
        w.Influence(1, 0, Influential); // Order 4 > Autocracy 3
        Assert.Equal("Order", Opinion(w).Preferred);
        Assert.Equal(7, Opinion(w).Dissatisfaction);

        var order = new World(3, ["Order", "Freedom", "Autocracy"]);
        order.Influence(1, 0, Familiar);
        order.Influence(2, 0, Familiar);
        Assert.Equal("Freedom", Opinion(order).Preferred); // Freedom wins the tie against Autocracy
        Assert.Equal(PublicOpinionLevel.CivilResistance, Opinion(order).Level); // D = 4
    }

    [Fact]
    public void DeadCivsMinorCivsAndNonFollowersDoNotPressure()
    {
        var w = new World(3, ["Freedom", "Order", ""]);
        w.Influence(1, 0, Dominant);
        w.Influence(2, 0, Dominant); // a civ without an ideology pushes nothing
        Assert.Equal(5, Opinion(w).Dissatisfaction);
        w.Civs[1].Cities = []; // a civ without cities is defeated
        var opinion = Opinion(w);
        Assert.Equal((PublicOpinionLevel.Content, 0, null), (opinion.Level, opinion.Unhappiness, opinion.Preferred));
        Assert.Empty(opinion.Pressures);
    }

    [Theory]
    // level (Familiar d=2, Popular d=3, Dominant d=5), city populations, expected unhappiness = max(totalPopulation / popDiv, cities * perCity)
    [InlineData(Familiar, new[] { 1 }, 1)]            // Dissidents (1,10): max(0, 1) = 1
    [InlineData(Familiar, new[] { 9, 10 }, 2)]        // 19/10 = 1 against 2 cities
    [InlineData(Familiar, new[] { 20, 25 }, 4)]       // 45/10 = 4 against 2
    [InlineData(Popular, new[] { 4 }, 2)]             // CivilResistance (2,5): max(0, 2) = 2
    [InlineData(Popular, new[] { 5, 5, 5 }, 6)]       // 15/5 = 3 against 6
    [InlineData(Popular, new[] { 12, 14 }, 5)]        // 26/5 = 5 against 4
    [InlineData(Dominant, new[] { 1, 1 }, 8)]         // RevolutionaryWave (4,3): 0 against 8
    [InlineData(Dominant, new[] { 11, 12 }, 8)]       // 23/3 = 7 against 8
    [InlineData(Dominant, new[] { 12, 13 }, 8)]       // 25/3 = 8 against 8
    [InlineData(Dominant, new[] { 14, 13 }, 9)]       // 27/3 = 9 against 8
    public void OpinionUnhappinessIsTheLargerOfThePopulationShareAndThePerCityAmount(int percent, int[] populations, int expected)
    {
        var w = new World(2, ["Freedom", "Order"]);
        w.GiveCities(0, populations);
        w.Influence(1, 0, percent);
        Assert.Equal(expected, Opinion(w).Unhappiness);
        Assert.Equal(expected, PublicOpinionRules.Unhappiness(w.Civs[0]));
        // it is part of the empire happiness breakdown
        Assert.Equal(-expected, w.Civs[0].Stats.GetHappinessBreakdown()["Public opinion"]);
    }

    // --- switching and anarchy

    private static World UnhappyWorld(bool human = true, string[]? uniques = null)
    {
        var w = new World(3, ["Freedom", "Order", "Order"], human, uniques);
        w.Influence(1, 0, Popular);
        w.Influence(2, 0, Popular); // D = 6, Revolutionary Wave, prefers Order
        return w;
    }

    [Fact]
    public void SwitchingNeedsOpinionUnhappinessAndIsRefusedWhileContentOrInAnarchy()
    {
        var w = new World(2, ["Freedom", "Order"]);
        Assert.False(w.Civs[0].Policies.CanSwitchIdeology());
        Assert.Throws<InvalidOperationException>(() => w.Civs[0].Policies.SwitchIdeology());
        w.Influence(1, 0, Exotic); // d = 1, unhappiness max(0, 1) = 1 > 0
        Assert.True(w.Civs[0].Policies.CanSwitchIdeology());
        w.Civs[0].Policies.AnarchyTurns = 1;
        Assert.False(w.Civs[0].Policies.CanSwitchIdeology());
        Assert.False(w.Civs[1].Policies.CanSwitchIdeology()); // the Order civ is content
        var none = new World(2, ["", "Order"]);
        none.Influence(1, 0, Dominant);
        Assert.False(none.Civs[0].Policies.CanSwitchIdeology());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)] // the boundary: all but two come back
    [InlineData(3, 1)]
    [InlineData(5, 3)]
    public void SwitchingLosesTheTenetsGivesBackAllButTwoAndStartsAnarchy(int tenets, int expectedFree)
    {
        var w = UnhappyWorld();
        var civ = w.Civs[0];
        var freedom = w.Game.Ruleset.PolicyBranches["Freedom"];
        civ.Policies.FreeTenets = tenets + 2; // 2 more stay unspent and are lost as well
        foreach (var tenet in freedom.Policies.Where(it => it.Level == 1).Take(tenets)) civ.Policies.Adopt(tenet);
        Assert.Equal(2, civ.Policies.FreeTenets);
        Assert.Equal(tenets, freedom.Policies.Count(it => civ.Policies.IsAdopted(it.Name)));

        civ.Policies.SwitchIdeology();

        Assert.Equal("Order", civ.Policies.Ideology);
        Assert.True(civ.Policies.IsAdopted("Order"));
        Assert.False(civ.Policies.IsAdopted("Freedom"));
        Assert.DoesNotContain(freedom.Policies, it => civ.Policies.IsAdopted(it.Name));
        Assert.Equal(expectedFree, civ.Policies.FreeTenets);
        Assert.Equal(w.Game.Ruleset.ModOptions.Constants.SwitchPolicyBranchesAnarchyTurns, civ.Policies.AnarchyTurns);
        Assert.Equal(2, civ.Policies.AnarchyTurns);
        Assert.Equal(["Freedom", "Order"], civ.Policies.IdeologyHistory);
        Assert.True(civ.Policies.HasFollowed("Freedom"));
        Assert.Contains(civ.Notifications, it => it.Text.Contains("anarchy"));
        // the people got what they wanted: nobody pressures an Order civ with Order civs
        Assert.Equal(PublicOpinionLevel.Content, Opinion(w).Level);
    }

    [Fact]
    public void AnarchyBlocksPoliciesAndCultureAndScienceAndEndsAfterTwoTurns()
    {
        var w = UnhappyWorld(uniques: ["[+3 Culture] [in all cities]", "[+2 Science] [in all cities]"]);
        var civ = w.Civs[0];
        civ.Policies.StoredCulture = 100000;
        civ.UpdateStatsForNextTurn();
        var baseline = (civ.Stats.StatsForNextTurn.Culture, civ.Stats.StatsForNextTurn.Science);
        Assert.True(baseline.Culture >= 3f && baseline.Science >= 2f);

        civ.Policies.SwitchIdeology();
        Assert.True(civ.Policies.InAnarchy);
        Assert.False(civ.Policies.CanAdoptPolicy());
        Assert.False(civ.Policies.CanAdoptPolicy(w.Game.Ruleset.PolicyBranches["Tradition"]));
        Assert.False(civ.Policies.ShouldShowPolicyPicker());
        civ.UpdateStatsForNextTurn();
        Assert.Equal((0f, 0f), (civ.Stats.StatsForNextTurn.Culture, civ.Stats.StatsForNextTurn.Science));

        civ.Policies.EndTurn(0);
        Assert.Equal(1, civ.Policies.AnarchyTurns);
        civ.UpdateStatsForNextTurn();
        Assert.Equal(0f, civ.Stats.StatsForNextTurn.Culture);
        civ.Policies.EndTurn(0);
        Assert.Equal(0, civ.Policies.AnarchyTurns);
        civ.Policies.EndTurn(0);
        Assert.Equal(0, civ.Policies.AnarchyTurns); // never below 0
        civ.UpdateStatsForNextTurn();
        Assert.Equal(baseline, (civ.Stats.StatsForNextTurn.Culture, civ.Stats.StatsForNextTurn.Science));
        Assert.True(civ.Policies.CanAdoptPolicy());
    }

    [Fact]
    public void ADepartedIdeologyStillCountsAsAnEarlierAdopter()
    {
        var w = UnhappyWorld();
        var freedom = w.Game.Ruleset.PolicyBranches["Freedom"];
        Assert.Equal(2, w.Civs[0].Policies.FreeTenets); // first Freedom civ
        w.Civs[0].Policies.SwitchIdeology();
        Assert.Null(w.Civs.Skip(1).FirstOrDefault(it => it.Policies.Ideology == "Freedom"));

        var late = w.Game.AddCiv();
        late.Tech.Era = w.Game.Ruleset.Eras["Modern era"];
        late.Policies.ChooseIdeology(freedom);
        Assert.Equal(1, late.Policies.FreeTenets); // the second adopter, though nobody follows it now
        var third = w.Game.AddCiv();
        third.Tech.Era = w.Game.Ruleset.Eras["Modern era"];
        third.Policies.ChooseIdeology(freedom);
        Assert.Equal(0, third.Policies.FreeTenets);
    }

    [Fact]
    public void RemovingAnIdeologyKeepsTheHistoryAndSavesWithoutBnwStateStayUnchanged()
    {
        var w = new World(2, ["Freedom", "Order"]);
        var policies = w.Civs[0].Policies;
        Assert.Equal(["Freedom"], policies.IdeologyHistory);
        var json = System.Text.Json.JsonSerializer.Serialize(policies, Unciv.Json.UncivJson.DefaultOptions);
        Assert.Contains("\"ideologyHistory\":[\"Freedom\"]", json);
        Assert.DoesNotContain("anarchyTurns", json);
        var clone = policies.Clone();
        clone.AnarchyTurns = 2;
        Assert.Equal(["Freedom"], clone.IdeologyHistory);
        Assert.Contains("\"anarchyTurns\":2", System.Text.Json.JsonSerializer.Serialize(clone, Unciv.Json.UncivJson.DefaultOptions));
        var plain = new TestGame();
        plain.MakeHexagonalMap(2);
        var gk = System.Text.Json.JsonSerializer.Serialize(plain.AddCiv().Policies, Unciv.Json.UncivJson.DefaultOptions);
        Assert.DoesNotContain("ideologyHistory", gk);
        Assert.DoesNotContain("anarchyTurns", gk);
    }

    // --- command

    /// <summary>A game in which the current player may switch ideology, for the command round-trip test of GameCommandTests.</summary>
    internal static (GameInfo Game, string Actor) SwitchSample()
    {
        var w = UnhappyWorld();
        w.Game.GameInfo.CurrentPlayerCiv = w.Civs[0];
        w.Game.GameInfo.CurrentPlayer = w.Civs[0].CivId;
        return (w.Game.GameInfo, w.Civs[0].CivId);
    }

    [Fact]
    public void TheSwitchIdeologyCommandIsRegisteredValidatedAndApplied()
    {
        var w = UnhappyWorld();
        var session = new GameSession(w.Game.GameInfo);
        w.Game.GameInfo.CurrentPlayerCiv = w.Civs[0];
        w.Game.GameInfo.CurrentPlayer = w.Civs[0].CivId;
        Assert.Contains(GameCommandRegistry.Scan(), it => it.Name == "switch-ideology" && it.CommandClass == typeof(SwitchIdeology));

        var rejected = session.Submit("Nobody", new SwitchIdeology());
        Assert.False(rejected.Accepted);
        var ok = session.Submit(w.Civs[0].CivId, new SwitchIdeology());
        Assert.True(ok.Accepted, ok.Validation.Message);
        Assert.Equal("Order", w.Civs[0].Policies.Ideology);
        var again = session.Submit(w.Civs[0].CivId, new SwitchIdeology());
        Assert.False(again.Accepted);
        Assert.Equal(CommandErrors.NotAllowed, again.Validation.Code);
    }

    // --- AI

    [Fact]
    public void TheAiSwitchesWhenVeryUnhappyAndTheOpinionUnhappinessIsTenOrMore()
    {
        var w = UnhappyWorld(human: false);
        var ai = w.Civs[0];
        w.Game.Ruleset.ModOptions.Constants.SuperUnhappyThreshold = 1000; // always super unhappy
        w.GiveCities(0, 10, 10); // 20 / 3 = 6 against 8: 8 < 10
        Assert.Equal(8, Opinion(w).Unhappiness);
        NextTurnAutomation.AdoptPolicyForced(ai);
        Assert.Equal("Freedom", ai.Policies.Ideology);

        w.GiveCities(0, 15, 15); // 30 / 3 = 10, the boundary
        Assert.Equal(10, Opinion(w).Unhappiness);
        w.Game.Ruleset.ModOptions.Constants.SuperUnhappyThreshold = -1000; // never that unhappy
        NextTurnAutomation.AdoptPolicyForced(ai);
        Assert.Equal("Freedom", ai.Policies.Ideology);

        w.Game.Ruleset.ModOptions.Constants.SuperUnhappyThreshold = 1000;
        NextTurnAutomation.AdoptPolicyForced(ai);
        Assert.Equal("Order", ai.Policies.Ideology);
        Assert.Equal(2, ai.Policies.AnarchyTurns);
    }
}
