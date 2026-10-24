using Unciv.Logic.City;
using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;

namespace Unciv.Core.Tests.Support;

// T035: the CountableTests cases deferred by T028 that only need TestGame (tests/src/com/unciv/uniques/CountableTests.kt).
// Still deferred, with the owning ticket: testPerCountableForGlobalAndLocalResources, testOwnedTilesCountable, testFilteredCitiesCountable
// and the second half of countableCanUseContext (UniqueTriggerActivation, T052);
// private testRulesetValidation(RulesetValidator over a modded game, T036/T037 owners);
// countableCanUseContext's unit part (placeUnitNearTile with triggered uniques, T052).
[Collection("RulesetCache")]
public class CountableGameTests
{
    private TestGame game = null!;
    private Civilization civ = null!;
    private City city = null!;

    private void SetupModdedGame(params string[] addGlobalUniques)
    {
        game = new TestGame(addGlobalUniques);
        game.MakeHexagonalMap(3);
        civ = game.AddCiv();
        city = game.AddCity(civ, game.TileMap[2, 0]);
    }

    private static void RunParcours<TIn, TOut>(string name, Func<TIn, TOut> test, params (TIn Input, TOut Expected)[] cases)
    {
        foreach (var (input, expected) in cases)
            Assert.True(Equals(expected, test(input)), $"{name}: input {input}: expected {expected}, got {test(input)}");
    }

    [Fact]
    public void TestStatOrResourcePerTurn()
    {
        SetupModdedGame();
        city.CityConstructions.AddBuilding(game.CreateBuilding("Provides [5] [Coal]", "[+1 Gold, +3 Culture] [in this city]"));
        var context = new GameContext(civ);
        RunParcours("Stat or resource per turn countable", (string it) => Countables.GetCountableAmount(it, context),
            ("[Gold] Per Turn", (int?)4), // Palace provides 3
            ("[Culture] Per Turn", 4), // Palace provides 1
            ("[Coal] Per Turn", 5),
            ("[Iron] Per Turn", 0),
            ("[Null] Per Turn", null));
    }

    private void VerifyStats(GameContext context)
    {
        foreach (var stat in Enum.GetValues<Stat>())
        {
            var countableResult = Countables.Stats.Eval(stat.ToString(), context);
            int expected = stat == Stat.Happiness ? civ.GetHappiness() : context.GetStatAmount(stat);
            Assert.True(expected == countableResult, $"Testing {stat} countable: {countableResult} expected {expected}");
        }
    }

    [Fact]
    public void TestStatsCountables()
    {
        SetupModdedGame();
        var providesStats = game.CreateBuilding("[+1 Gold, +2 Food, +3 Production, +4 Happiness, +3 Science, +2 Culture, +1 Faith] [in this city] <when number of [Cities] is equal to [1]>");
        city.CityConstructions.AddBuilding(providesStats);
        VerifyStats(new GameContext(civ, city));

        var city2 = game.AddCity(civ, game.TileMap[-2, 0]);
        var providesStats2 = game.CreateBuilding("[+3 Gold, +2 Food, +1 Production, -4 Happiness, +1 Science, +2 Culture, +3 Faith] [in this city] <when number of [Cities] is more than [1]>");
        city2.CityConstructions.AddBuilding(providesStats2);
        VerifyStats(new GameContext(civ, city2));
    }

    [Fact]
    public void TestPoliciesCountables()
    {
        SetupModdedGame();
        var policies = civ.Policies;
        foreach (var name in new[] { "Tradition", "Aristocracy", "Legalism", "Oligarchy", "Landed Elite", "Monarchy", "Liberty", "Citizenship", "Honor", "Piety" })
        {
            policies.FreePolicies++;
            policies.Adopt(policies.GetPolicyByName(name));
        }
        // Don't use a fake Policy without a branch, the policyFilter would stumble over a lateinit.
        var taggedPolicyBranch = game.CreatePolicyBranch(["Some marker"]);
        policies.FreePolicies++;
        policies.Adopt(taggedPolicyBranch); // Will be completed as it has no member policies

        // Add a second Civilization
        var civ2 = game.AddCiv();
        game.AddCity(civ2, game.TileMap[2, 2]);
        civ2.Policies.FreePolicies += 2;
        civ2.Policies.Adopt(civ2.Policies.GetPolicyByName("Tradition"));
        civ2.Policies.Adopt(civ2.Policies.GetPolicyByName("Oligarchy"));
        var context = new GameContext(civ);

        RunParcours("Filtered policies by filtered civs countable", (string it) => Countables.GetCountableAmount(it, context),
            ("Completed Policy branches", (int?)2), // Tradition and taggedPolicyBranch
            ("Adopted [Tradition Complete] Policies", 1),
            ("Adopted [[Tradition] branch] Policies", 7), // Branch start and completion plus 5 members
            ("Adopted [Liberty Complete] Policies", 0),
            ("Adopted [[Liberty] branch] Policies", 2), // Liberty has only 1 member adopted
            ("Adopted [Some marker] Policies", 1),
            ("Adopted [Military Tradition] Policies by [All] Civilizations", 0),
            ("Adopted [Some marker] Policies by [All] Civilizations", 1),
            ("Adopted [Oligarchy] Policies by [All] Civilizations", 2),
            ("Adopted [Oligarchy] Policies by [City-State] Civilizations", 0));
    }

    [Fact]
    public void TestEraNumberCountable()
    {
        SetupModdedGame();
        var context = new GameContext(civ);
        RunParcours("Era number countable", (string tech) =>
            {
                civ.Tech.AddTechnology(tech, false);
                return Countables.GetCountableAmount("Era number", context);
            },
            ("Agriculture", (int?)0),
            ("Optics", 1),
            ("Archery", 1),
            ("Navigation", 3));
    }

    [Fact]
    public void TestTimeCountables()
    {
        SetupModdedGame();
        var context = new GameContext(civ);
        RunParcours("Year and turns countables", ((int Turn, string Text) input) =>
            {
                game.GameInfo.Turns = input.Turn;
                return Countables.GetCountableAmount(input.Text, context);
            },
            ((7, "[year] - ([-4000] + [40] * [turns])"), (int?)0),
            ((42, "[year] - (-4000 + 40 * [turns])"), 0),
            ((124, "[year]"), 225));
    }

    [Fact]
    public void TestSpeedModifierCountable()
    {
        SetupModdedGame();
        var context = new GameContext(civ);
        RunParcours("Game speed modifier countable", ((string Speed, string Stat) input) =>
            {
                game.SetSpeed(input.Speed);
                return Countables.GetCountableAmount($"Speed modifier for [{input.Stat}]", context);
            },
            (("Quick", "Production"), (int?)67),
            (("Standard", "Gold"), 100),
            (("Marathon", "Faith"), 300));
    }

    [Fact]
    public void TestForEveryWithInvalidCountable()
    {
        game = new TestGame(
            "[+42 Faith] <when number of [turns] is less than [42]>",
            "[+1 Happiness] <for every [City-States]>",
            "[+1 Happiness] <for every [[42] Monkeys]>");
        game.MakeHexagonalMap(3);
        civ = game.AddCiv();
        city = game.AddCity(civ, game.TileMap[2, 0]);

        var cityState = game.AddCiv(cityStateType: game.Ruleset.CityStateTypes.Keys.First());
        game.AddCity(cityState, game.TileMap[-2, 0], true);
        civ.UpdateStatsForNextTurn();

        var happiness = Countables.GetCountableAmount("Happiness", new GameContext(civ, city));
        // Base 9, -1 city, -3 population +1 deprecated countable should still work, but the bogus one should not
        Assert.Equal(6, happiness);
    }

    [Fact]
    public void TestKnownCivsCountable()
    {
        // A big map with distant cities: civilizations must not meet by seeing each other
        game = new TestGame();
        game.MakeHexagonalMap(12);
        var ourCiv = game.AddCiv();
        var otherMajorCiv = game.AddCiv();
        var cityState = game.AddCiv(cityStateType: game.Ruleset.CityStateTypes.Keys.First());
        game.AddCity(ourCiv, game.TileMap[0, 0]);
        game.AddCity(otherMajorCiv, game.TileMap[10, 0]);
        game.AddCity(cityState, game.TileMap[-10, 0]);
        var context = new GameContext(ourCiv);
        int? Known(string filter) => Countables.GetCountableAmount($"Known [{filter}] Civilizations", context);

        Assert.Equal(0, Known("Major"));
        ourCiv.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        Assert.Equal(0, Known("Major")); // A City-State is not a major civ
        Assert.Equal(1, Known("City-State"));
        ourCiv.DiplomacyFunctions.MakeCivilizationsMeet(otherMajorCiv);
        Assert.Equal(1, Known("Major"));
        // "all" includes neither ourselves nor civilizations we have not met
        Assert.Equal(2, Known("all"));
    }

    [Fact]
    public void CountableCanUseContextCityCount()
    {
        SetupModdedGame();
        Assert.Equal(1, Countables.GetCountableAmount("Cities", civ.State));
    }

    [Fact]
    public void TestFilteredBuildingsCountable()
    {
        game = new TestGame();
        game.MakeHexagonalMap(3);
        var building = game.CreateBuilding("Ancestor Tree"); // That's a filtering Unique, not the building name
        building[Stat.Culture] = 1f;

        civ = game.AddCiv(
        [
            "[+1 Culture] from all [Ancestor Tree] buildings <for every [1] [[Ancestor Tree] Buildings]> <when number of [[Ancestor Tree] Buildings] is more than [1]>",
            "[+50]% [Culture] from every [Ancestor Tree] <when number of [[Ancestor Tree] Buildings] is more than [0]>",
        ]);
        city = game.AddCity(civ, game.TileMap[2, 0]);
        city.CityConstructions.AddBuilding(building);
        var city2 = game.AddCity(civ, game.TileMap[-2, 0]);
        city2.CityConstructions.AddBuilding(building);

        // updateStatsForNextTurn won't run the city part because no happiness change across a boundary
        foreach (var city3 in civ.Cities)
            city3.CityStats.Update(updateCivStats: false);
        civ.UpdateStatsForNextTurn();

        // Expect: (1 Palace + 1 Base Ancestor Tree + 2 for-every) * 1.5 = 6
        var capitalCulture = city.CityStats.CurrentCityStats.Culture;
        // Expect: (1 Base Ancestor Tree + 2 for-every) * 1.5 = 4.5
        var city2Culture = city2.CityStats.CurrentCityStats.Culture;
        // Expect: capitalCulture + city2Culture = 10.5
        var civCulture = civ.Stats.StatsForNextTurn.Culture;

        Assert.Equal(6f, capitalCulture, 0.005f);
        Assert.Equal(4.5f, city2Culture, 0.005f);
        Assert.Equal(10.5f, civCulture, 0.005f);

        // FilteredBuildingsByCivs
        var civ2 = game.AddCiv();
        var civ2city = game.AddCity(civ2, game.TileMap[-2, -2]);
        game.AddCity(civ2, game.TileMap[2, 2]);
        civ2city.CityConstructions.AddBuilding(building);
        (string Test, int Expected)[] tests =
        [
            ("[Ancestor Tree] Buildings", 2),
            ("[Ancestor Tree] Buildings by [All] Civilizations", 3),
            ($"[Ancestor Tree] Buildings by [{civ.CivName}] Civilizations", 2),
            ($"[Ancestor Tree] Buildings by [{civ2.CivName}] Civilizations", 1),
            ("[Ancestor Tree] Buildings by [City-State] Civilizations", 0),
        ];
        foreach (var (test, expected) in tests)
            Assert.True(expected == Countables.GetCountableAmount(test, new GameContext(civ)), $"Testing `{test}` countable:");
    }
}
