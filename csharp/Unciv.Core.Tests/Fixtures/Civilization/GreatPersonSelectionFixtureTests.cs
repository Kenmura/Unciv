using Unciv.Core.Tests.Support;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T152: engine-level cases of tests/src/com/unciv/logic/civilization/GreatPersonSelectionTest.kt over the real TestGame.
// The two automation cases live in GreatPersonSelectionAutomationFixtureTests (T157).
// Not ported here (see backlog notes): "deferred pending choice remains the next turn action and reopens
// its picker" mocks WorldScreen/NextTurnButton/AutoPlay (libGDX UI, Won't port).
[Collection("RulesetCache")]
public sealed class GreatPersonSelectionFixtureTests
{
    internal sealed record Scenario(TestGame Game, CivilizationObject Civ);

    internal static Scenario CreateScenario(BaseRuleset baseRuleset, string nationName, bool addCity = true)
    {
        var game = new TestGame();
        game.MakeHexagonalMap(1, Constants.Grassland);
        var ruleset = baseRuleset switch
        {
            BaseRuleset.Civ_V_GnK => game.Ruleset,
            _ => RulesetCache.Get(baseRuleset.FullName())!.Clone(),
        };

        game.GameInfo.Ruleset = ruleset;
        game.GameInfo.GameParameters.BaseRuleset = baseRuleset.FullName();
        game.TileMap.Ruleset = ruleset;
        foreach (var unit in ruleset.Units.Values) unit.SetRuleset(ruleset);
        foreach (var building in ruleset.Buildings.Values) building.Ruleset = ruleset;
        foreach (var tile in game.TileMap.TileList) tile.SetTerrainTransients();
        game.GameInfo.SetGlobalTransients();

        var civ = new CivilizationObject(ruleset.Nations[nationName]) { GameInfo = game.GameInfo };
        civ.Cache.UpdateState();
        game.GameInfo.Civilizations.Add(civ);
        civ.SetTransients();
        civ.Tech.AddTechnology(ruleset.Technologies.Values.MinBy(it => it.Era(), Comparer<string>.Create(string.CompareOrdinal))!.Name);
        if (addCity) game.AddCity(civ, game.GetTile(HexCoord.Zero));
        return new Scenario(game, civ);
    }

    [Fact]
    public void NormalChoiceGrantsTheCivilizationEquivalentInBothBaseRulesets()
    {
        foreach (var baseRuleset in Enum.GetValues<BaseRuleset>().Where(it => it != BaseRuleset.Civ_V_BNW))
        {
            var scenario = CreateScenario(baseRuleset, "Mongolia");
            var manager = scenario.Civ.GreatPeople;
            manager.FreeGreatPeople = 1;
            manager.LongCountGPPool = JavaHashSet<string>.ToHashSet(["Great Scientist", "stored value"]);
            var originalPool = manager.LongCountGPPool.ToHashSet();

            var options = manager.GetFreeGreatPersonOptions();

            Assert.True(options.Any(it => it.Name == "Khan"), baseRuleset.FullName() + " should offer the Khan");
            Assert.DoesNotContain(options, it => it.Name == "Great General");
            Assert.Null(manager.ChooseFreeGreatPerson("Great General"));
            Assert.Equal(1, manager.FreeGreatPeople);

            var placed = manager.ChooseFreeGreatPerson("Khan");

            Assert.NotNull(placed);
            Assert.Equal("Khan", placed.Name);
            Assert.Same(scenario.Civ, placed.Civ);
            Assert.Equal(scenario.Civ.CivId, placed.Owner);
            Assert.Contains(scenario.Civ.Units.GetCivUnits(), it => ReferenceEquals(it, placed));
            Assert.Same(placed, placed.GetTile().CivilianUnit);
            Assert.Same(placed.GetTile(), scenario.Game.GetTile(placed.GetTile().Position));
            Assert.Equal(0, manager.FreeGreatPeople);
            Assert.Equal(0, manager.MayaLimitedFreeGP);
            Assert.True(originalPool.SetEquals(manager.LongCountGPPool));

            var liveUnits = scenario.Civ.Units.GetCivUnits().ToList();
            Assert.Null(manager.ChooseFreeGreatPerson("Great Scientist"));
            Assert.Equal(liveUnits, scenario.Civ.Units.GetCivUnits().ToList());
        }
    }

    [Fact]
    public void MayaChoiceUsesOnlyTheCurrentPoolAndConsumesItAfterPlacement()
    {
        var scenario = CreateScenario(BaseRuleset.Civ_V_GnK, "The Maya");
        var manager = scenario.Civ.GreatPeople;
        manager.FreeGreatPeople = 2;
        manager.MayaLimitedFreeGP = 1;
        manager.LongCountGPPool = JavaHashSet<string>.ToHashSet(["Great Scientist", "stale choice"]);

        Assert.True(new HashSet<string> { "Great Scientist" }.SetEquals(manager.GetFreeGreatPersonOptions().Select(it => it.Name)));

        var unitsBeforeRejectedChoice = scenario.Civ.Units.GetCivUnits().ToList();
        Assert.Null(manager.ChooseFreeGreatPerson("Great Engineer"));
        Assert.Equal(2, manager.FreeGreatPeople);
        Assert.Equal(1, manager.MayaLimitedFreeGP);
        Assert.True(new HashSet<string> { "Great Scientist", "stale choice" }.SetEquals(manager.LongCountGPPool));
        Assert.Equal(unitsBeforeRejectedChoice, scenario.Civ.Units.GetCivUnits().ToList());

        var staleOptions = manager.GetFreeGreatPersonOptions();
        manager.LongCountGPPool.Remove("Great Scientist");
        Assert.Contains(staleOptions, it => it.Name == "Great Scientist");
        Assert.Null(manager.ChooseFreeGreatPerson("Great Scientist"));
        Assert.Equal(2, manager.FreeGreatPeople);
        Assert.Equal(1, manager.MayaLimitedFreeGP);
        manager.LongCountGPPool.Add("Great Scientist");

        var placed = manager.ChooseFreeGreatPerson("Great Scientist");

        Assert.NotNull(placed);
        Assert.Equal("Great Scientist", placed.Name);
        Assert.Equal(1, manager.FreeGreatPeople);
        Assert.Equal(0, manager.MayaLimitedFreeGP);
        Assert.True(new HashSet<string> { "stale choice" }.SetEquals(manager.LongCountGPPool));
        Assert.Contains(manager.GetFreeGreatPersonOptions(), it => it.Name == "Great Engineer");
    }

    [Fact]
    public void InvalidExhaustedStaleAndCitylessChoicesPreserveState()
    {
        var scenario = CreateScenario(BaseRuleset.Civ_V_GnK, "Rome");
        var manager = scenario.Civ.GreatPeople;
        manager.FreeGreatPeople = 1;
        manager.LongCountGPPool = JavaHashSet<string>.ToHashSet(["stored value"]);
        var originalPool = manager.LongCountGPPool.ToHashSet();
        var originalNotifications = scenario.Civ.Notifications.ToList();
        var originalUnits = scenario.Civ.Units.GetCivUnits().ToList();

        Assert.Null(manager.ChooseFreeGreatPerson("does not exist"));
        Assert.Null(manager.ChooseFreeGreatPerson("Warrior"));
        Assert.Equal(1, manager.FreeGreatPeople);
        Assert.True(originalPool.SetEquals(manager.LongCountGPPool));
        Assert.Equal(originalNotifications, scenario.Civ.Notifications);
        Assert.Equal(originalUnits, scenario.Civ.Units.GetCivUnits().ToList());

        var staleName = manager.GetFreeGreatPersonOptions().First().Name;
        manager.FreeGreatPeople = 0;
        Assert.Empty(manager.GetFreeGreatPersonOptions());
        Assert.Null(manager.ChooseFreeGreatPerson(staleName));
        Assert.Equal(0, manager.FreeGreatPeople);
        Assert.Equal(originalUnits, scenario.Civ.Units.GetCivUnits().ToList());

        manager.FreeGreatPeople = -1;
        Assert.Empty(manager.GetFreeGreatPersonOptions());
        Assert.Null(manager.ChooseFreeGreatPerson("Great Scientist"));
        Assert.Equal(-1, manager.FreeGreatPeople);

        var cityless = CreateScenario(BaseRuleset.Civ_V_GnK, "Rome", addCity: false);
        cityless.Civ.GreatPeople.FreeGreatPeople = 1;
        Assert.Empty(cityless.Civ.GreatPeople.GetFreeGreatPersonOptions());
        Assert.Null(cityless.Civ.GreatPeople.ChooseFreeGreatPerson("Great Scientist"));
        Assert.Equal(1, cityless.Civ.GreatPeople.FreeGreatPeople);
        Assert.Empty(cityless.Civ.Units.GetCivUnits());
    }

    [Fact]
    public void BlockedPlacementPreservesCountersPoolLiveUnitsAndNotifications()
    {
        var game = new TestGame();
        var civ = game.AddCiv(isPlayer: true);
        var city = game.AddCity(civ, game.GetTile(HexCoord.Zero));
        var blocker = game.AddUnit("Worker", civ, city.GetCenterTile());
        var manager = civ.GreatPeople;
        manager.FreeGreatPeople = 1;
        manager.MayaLimitedFreeGP = 1;
        manager.LongCountGPPool = JavaHashSet<string>.ToHashSet(["Great Scientist"]);
        Assert.True(new HashSet<string> { "Great Scientist" }.SetEquals(manager.GetFreeGreatPersonOptions().Select(it => it.Name)));
        var originalUnits = civ.Units.GetCivUnits().ToList();
        var originalMapUnits = game.TileMap.TileList.SelectMany(it => it.GetUnits().ToList()).ToList();
        var originalNotifications = civ.Notifications.ToList();

        var placed = manager.ChooseFreeGreatPerson("Great Scientist");

        Assert.Null(placed);
        Assert.Equal(1, manager.FreeGreatPeople);
        Assert.Equal(1, manager.MayaLimitedFreeGP);
        Assert.True(new HashSet<string> { "Great Scientist" }.SetEquals(manager.LongCountGPPool));
        Assert.Equal(originalUnits, civ.Units.GetCivUnits().ToList());
        Assert.Equal(originalMapUnits, game.TileMap.TileList.SelectMany(it => it.GetUnits().ToList()).ToList());
        Assert.Same(blocker, city.GetCenterTile().CivilianUnit);
        Assert.Equal(originalNotifications, civ.Notifications);
    }

    [Fact]
    public void OptionQueryFollowsSettingsAndReturnsIndependentSetsWithoutMutation()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(1, Constants.Grassland);
        var civ = game.AddCiv();
        game.AddCity(civ, game.GetTile(HexCoord.Zero));
        var conditionalGreatPerson = game.CreateBaseUnit(
            "Civilian",
            "Great Person - [Test]",
            "Only available <when espionage is enabled>");
        var manager = civ.GreatPeople;
        manager.FreeGreatPeople = 1;
        manager.LongCountGPPool = JavaHashSet<string>.ToHashSet(["stored value"]);

        Assert.DoesNotContain(conditionalGreatPerson, manager.GetFreeGreatPersonOptions());
        game.GameInfo.GameParameters.EspionageEnabled = true;
        var unitsBefore = civ.Units.GetCivUnits().ToList();
        var notificationsBefore = civ.Notifications.ToList();
        var options = manager.GetFreeGreatPersonOptions();
        var repeatedOptions = manager.GetFreeGreatPersonOptions();

        Assert.Contains(conditionalGreatPerson, options);
        Assert.True(options.SetEquals(repeatedOptions));
        Assert.NotSame(options, repeatedOptions);
        try
        {
            ((ISet<BaseUnit>)options).Clear();
        }
        catch (Exception)
        {
            // Kotlin runCatching: a read-only view may reject the mutation.
        }
        Assert.Contains(conditionalGreatPerson, manager.GetFreeGreatPersonOptions());
        Assert.Equal(1, manager.FreeGreatPeople);
        Assert.Equal(0, manager.MayaLimitedFreeGP);
        Assert.True(new HashSet<string> { "stored value" }.SetEquals(manager.LongCountGPPool));
        Assert.Equal(unitsBefore, civ.Units.GetCivUnits().ToList());
        Assert.Equal(notificationsBefore, civ.Notifications);
    }
}
