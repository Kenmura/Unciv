using System.Text.RegularExpressions;
using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Validation;
using Unciv.Models.StatTypes;
using CityObject = Unciv.Logic.City.City;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Uniques;

// T154: the TestGame-based cases of tests/src/com/unciv/uniques/CountableTests.kt (T143 deferred them), over the real TestGame.
// The Kotlin reflection check (testCountableConventions) and the pure-ruleset cases live in Models/Ruleset/Unique/CountableTests.cs.
[Collection("RulesetCache")]
public sealed class CountableGameTests
{
    private TestGame game = null!;
    private Civilization civ = null!;
    private CityObject city = null!;

    private RulesetClass SetupModdedGame(string[]? addGlobalUniques = null, bool withCiv = true)
    {
        game = new TestGame(addGlobalUniques ?? []);
        game.MakeHexagonalMap(3);
        if (!withCiv) return game.Ruleset;
        civ = game.AddCiv();
        city = game.AddCity(civ, game.TileMap[2, 0]);
        return game.Ruleset;
    }

    [Fact]
    public void TestRulesetValidation()
    {
        // (unique, expected number of parameters failing UniqueParameterType validation)
        (string Text, int Expected)[] testData =
        [
            ("[+42 Faith] <when number of [turns] is less than [42]>", 0),
            ("[-1 Faith] <for every [turns]> <when number of [turns] is between [0] and [41]>", 0),
            ("[+1 Happiness] <for every [City-States]>", 0), // +1 deprecation
            ("[+1 Happiness] <for every [Remaining [City-State] Civilizations]>", 0),
            ("[+1 Happiness] <for every [[42] Monkeys]>", 1), // +1 monkeys
            ("[+1 Gold] <when number of [year] is equal to [countable]>", 1),
            ("[+1 Food] <when number of [-0] is different than [+0]>", 0),
            ("[+1 Food] <when number of [[~Nonexisting~] Cities] is between [[Annexed] Cities] and [Cities]>", 1),
            ("[+1 Food] <when number of [[Paratrooper] Units] is between [[Air] Units] and [Units]>", 0),
            ("[+1 Food] <when number of [[~Bogus~] Units] is between [[Land] Units] and [[Air] Units]>", 1),
            ("[+1 Food] <when number of [[Barbarian] Units] is between [[Japanese] Units] and [[Embarked] Units]>", 1),
            ("[+1 Food] <when number of [[Science] Buildings] is between [[Wonder] Buildings] and [[All] Buildings]>", 0),
            ("[+1 Food] <when number of [[42] Buildings] is between [[Universe] Buildings] and [[Library] Buildings]>", 2),
            ("[+1 Food] <when number of [Adopted [Tradition] Policies] is between [Adopted [[Tradition] branch] Policies] and [Adopted [all] Policies]>", 0),
            ("[+1 Food] <when number of [Adopted [[Legalism] branch] Policies] is between [Adopted [Folklore] Policies] and [Completed Policy branches]>", 2),
            ("[+1 Food] <when number of [Remaining [Human player] Civilizations] is between [Remaining [City-State] Civilizations] and [Remaining [Major] Civilizations]>", 0),
            ("[+1 Food] <when number of [Remaining [city-state] Civilizations] is between [Remaining [la la la] Civilizations] and [Remaining [all] Civilizations]>", 2),
            ("[+1 Food] <when number of [Owned [Land] Tiles] is between [Owned [Desert] Tiles] and [Owned [All] Tiles]>", 0),
            ("[+1 Food] <when number of [Owned [worked] Tiles] is between [Owned [Camp] Tiles] and [Owned [Forest] Tiles]>", 0),
            // `Barbarians` is technically a valid TileFilter because it's a CivFilter; `Food` is not a valid TileFilter; the last is a wrong-case filter.
            ("[+1 Food] <when number of [Owned [Barbarians] Tiles] is between [Owned [Food] Tiles] and [Owned [strategic Resource] Tiles]>", 2),
            ("[+1 Food] <when number of [Iron] is between [Whales] and [Crab]>", 0),
            ("[+1 Food] <when number of [Cocoa] is between [Bison] and [Maryjane]>", 3),
        ];
        int totalNotACountableExpected = testData.Sum(it => it.Expected);
        var notACountableRegex = new Regex($".*parameter \"(.*)\" {UniqueValidator.WhichDoesNotFitParameterType} countable.*", RegexOptions.Singleline);

        var ruleset = SetupModdedGame(testData.Select(it => it.Text).ToArray(), withCiv: false);
        ruleset.ModOptions.IsBaseRuleset = true; // To get ruleset-specific validation

        var errors = RulesetValidator.Create(ruleset).GetErrorList().ToList();
        var fails = new List<string>();

        foreach (var (uniqueText, expected) in testData)
        {
            var badOnes = new List<string>();
            foreach (var error in errors)
            {
                if (!error.Text.Contains(uniqueText)) continue;
                var match = notACountableRegex.Match(error.Text);
                if (!match.Success || match.Index != 0 || match.Length != error.Text.Length) continue;
                badOnes.Add(match.Groups[1].Value);
            }
            if (badOnes.Count != expected)
                fails.Add($"Test '{uniqueText}' should find {expected} errors, found: {badOnes.Count}, bad parameters: {string.Join(", ", badOnes)}");
        }

        (string Label, Regex Regex, int Expected)[] coarseChecks =
        [
            ("deprecated", new Regex("Countable.*deprecated.*", RegexOptions.Singleline), 1),
            ("monkeys", new Regex(".*Monkeys.*not fit.*countable.*", RegexOptions.Singleline), 1),
            ("not a countable", notACountableRegex, totalNotACountableExpected),
        ];
        foreach (var (label, regex, expected) in coarseChecks)
        {
            int actual = errors.Count(it => { var m = regex.Match(it.Text); return m.Success && m.Index == 0 && m.Length == it.Text.Length; });
            if (actual != expected) fails.Add($"Test '{label}' should find {expected} errors, found: {actual}");
        }

        Assert.Empty(fails);
    }

    [Fact]
    public void TestForEveryWithInvalidCountable()
    {
        SetupModdedGame(
        [
            "[+42 Faith] <when number of [turns] is less than [42]>",
            "[+1 Happiness] <for every [City-States]>",
            "[+1 Happiness] <for every [[42] Monkeys]>",
        ]);

        var cityState = game.AddCiv(cityStateType: game.Ruleset.CityStateTypes.Keys.First());
        game.AddCity(cityState, game.TileMap[-2, 0], true);
        civ.UpdateStatsForNextTurn();

        int happiness = Countables.GetCountableAmount("Happiness", new GameContext(civ, city))!.Value;
        // Base 9, -1 city, -3 population +1 deprecated countable should still work, but the bogus one should not
        Assert.Equal(6, happiness);
    }

    [Fact]
    public void TestPerCountableForGlobalAndLocalResources()
    {
        SetupModdedGame();
        // one coal provided locally
        city.CityConstructions.AddBuilding(game.CreateBuilding("Provides [1] [Coal]"));
        // one globally
        UniqueTriggerActivation.TriggerUnique(new Unique("Provides [1] [Coal] <for [2] turns>"), civ);
        city.CityConstructions.AddBuilding(game.CreateBuilding("[+1 Faith] [in this city] <for every [Coal]>"));
        Assert.Equal(2f, city.CityStats.CurrentCityStats.Faith);
    }

    [Fact]
    public void TestStatOrResourcePerTurn()
    {
        SetupModdedGame();
        city.CityConstructions.AddBuilding(game.CreateBuilding("Provides [5] [Coal]", "[+1 Gold, +3 Culture] [in this city]"));
        var context = new GameContext(civ);

        TestParcours.Run("Stat or resource per turn countable", (string test) => Countables.GetCountableAmount(test, context),
            ("[Gold] Per Turn", 4), // Palace provides 3
            ("[Culture] Per Turn", 4), // Palace provides 1
            ("[Coal] Per Turn", 5),
            ("[Iron] Per Turn", 0),
            ("[Null] Per Turn", (int?)null));
    }

    [Fact]
    public void TestStatsCountables()
    {
        SetupModdedGame();
        void VerifyStats(GameContext context)
        {
            foreach (var stat in Enum.GetValues<Stat>())
            {
                int? countableResult = Countables.Stats.Eval(stat.ToString(), context);
                int expected = stat == Stat.Happiness ? civ.GetHappiness() : context.GetStatAmount(stat);
                Assert.Equal(expected, countableResult);
            }
        }

        city.CityConstructions.AddBuilding(game.CreateBuilding(
            "[+1 Gold, +2 Food, +3 Production, +4 Happiness, +3 Science, +2 Culture, +1 Faith] [in this city] <when number of [Cities] is equal to [1]>"));
        VerifyStats(new GameContext(civ, city));

        var city2 = game.AddCity(civ, game.TileMap[-2, 0]);
        city2.CityConstructions.AddBuilding(game.CreateBuilding(
            "[+3 Gold, +2 Food, +1 Production, -4 Happiness, +1 Science, +2 Culture, +3 Faith] [in this city] <when number of [Cities] is more than [1]>"));
        VerifyStats(new GameContext(civ, city2));
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

        Assert.Equal(0, Known("Major")); // Nobody met yet
        ourCiv.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        Assert.Equal(0, Known("Major")); // A City-State is not a major civ
        Assert.Equal(1, Known("City-State"));
        ourCiv.DiplomacyFunctions.MakeCivilizationsMeet(otherMajorCiv);
        Assert.Equal(1, Known("Major"));
        // "all" includes neither ourselves nor civilizations we have not met
        Assert.Equal(2, Known("all"));
    }

    [Fact]
    public void TestOwnedTilesCountable()
    {
        SetupModdedGame();
        UniqueTriggerActivation.TriggerUnique(new Unique("Turn this tile into a [Coast] tile"), civ, tile: game.TileMap[-3, 0]);
        UniqueTriggerActivation.TriggerUnique(new Unique("Turn this tile into a [Coast] tile"), civ, tile: game.TileMap[3, 0]);

        game.AddCity(civ, game.TileMap[-2, 0], initialPopulation: 9);
        var context = new GameContext(civ);

        TestParcours.Run("Owned tiles countable", (string test) => Countables.GetCountableAmount(test, context),
            ("Owned [All] Tiles", 14),
            ("Owned [worked] Tiles", 8),
            ("Owned [Coastal] Tiles", 6),
            ("Owned [Coast] Tiles", 2),
            ("Owned [Land] Tiles", 12),
            ("Owned [Farm] Tiles", 0));
    }

    [Fact]
    public void TestFilteredCitiesCountable()
    {
        SetupModdedGame();
        UniqueTriggerActivation.TriggerUnique(new Unique("Turn this tile into a [Coast] tile"), civ, tile: game.TileMap[-3, 0]);

        var city2 = game.AddCity(civ, game.TileMap[-2, 0], initialPopulation: 9);
        city2.IsPuppet = true;
        var context = new GameContext(civ);

        TestParcours.Run("Filtered cities countable", (string test) => Countables.GetCountableAmount(test, context),
            ("[Capital] Cities", 1),
            ("[Puppeted] Cities", 1),
            ("[Coastal] Cities", 1),
            ("[Your] Cities", 2));
    }

    [Fact]
    public void TestFilteredBuildingsCountable()
    {
        SetupModdedGame(withCiv: false);
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
        float capitalCulture = city.CityStats.CurrentCityStats.Culture;
        // Expect: (1 Base Ancestor Tree + 2 for-every) * 1.5 = 4.5
        float city2Culture = city2.CityStats.CurrentCityStats.Culture;
        // Expect: capitalCulture + city2Culture = 10.5
        float civCulture = civ.Stats.StatsForNextTurn.Culture;

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
            Assert.Equal(expected, Countables.GetCountableAmount(test, new GameContext(civ)));
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

        TestParcours.Run("Filtered policies by filtered civs countable", (string test) => Countables.GetCountableAmount(test, context),
            ("Completed Policy branches", 2), // Tradition and taggedPolicyBranch
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

        TestParcours.Run("Era number countable", (string tech) =>
            {
                civ.Tech.AddTechnology(tech, false);
                return Countables.GetCountableAmount("Era number", context);
            },
            ("Agriculture", 0),
            ("Optics", 1),
            ("Archery", 1),
            ("Navigation", 3));
    }

    [Fact]
    public void TestTimeCountables()
    {
        SetupModdedGame();
        var context = new GameContext(civ);

        TestParcours.Run("Year and turns countables", ((int Turns, string Text) input) =>
            {
                game.GameInfo.Turns = input.Turns;
                return Countables.GetCountableAmount(input.Text, context);
            },
            ((7, "[year] - ([-4000] + [40] * [turns])"), 0),
            ((42, "[year] - (-4000 + 40 * [turns])"), 0),
            ((124, "[year]"), 225));
    }

    [Fact]
    public void TestSpeedModifierCountable()
    {
        SetupModdedGame();
        var context = new GameContext(civ);

        TestParcours.Run("Game speed modifier countable", ((string Speed, string Stat) input) =>
            {
                game.GameInfo.GameParameters.Speed = input.Speed;
                game.GameInfo.SetGlobalTransients();
                return Countables.GetCountableAmount($"Speed modifier for [{input.Stat}]", context);
            },
            (("Quick", "Production"), 67),
            (("Standard", "Gold"), 100),
            (("Marathon", "Faith"), 300));
    }

    [Fact]
    public void CountableCanUseContext()
    {
        SetupModdedGame();
        int? cityCount = Countables.GetCountableAmount("Cities", civ.State);
        Assert.Equal(1, cityCount);

        var unit = game.CreateBaseUnit();
        var fakeUnit = game.CreateBaseUnit(null, "[This Unit] is destroyed", $"Free [{unit.Name}] appears <for every [[Cities] + [3]]>");
        civ.Units.PlaceUnitNearTile(city.Location, fakeUnit);
        int? unitCount = Countables.GetCountableAmount("Units", civ.State);
        Assert.Equal(4, unitCount); // Units should get a context implicitly with a countable

        SetupModdedGame(); // reset
        var resource = game.CreateResource("Stockpiled");
        var localResource = game.CreateResource("Stockpiled", "City-level resource");
        city.CityConstructions.AddBuilding(game.CreateBuilding($"Instantly provides [5] [{resource.Name}]"));
        city.CityConstructions.AddBuilding(game.CreateBuilding($"Instantly provides [1] [{localResource.Name}] <for every [[{resource.Name}] - [3]]>"));
        int? resourceAmount = Countables.GetCountableAmount(localResource.Name, civ.State);
        Assert.Equal(0, resourceAmount); // Civ state alone shouldn't see citywide resources
        resourceAmount = Countables.GetCountableAmount(localResource.Name, city.State);
        Assert.Equal(2, resourceAmount); // City state should see city resources
    }
}
