using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// Ported from tests/src/com/unciv/uniques/CountableTests.kt: the cases that do not need TestGame, a loaded Vanilla ruleset or
/// RulesetValidator. Those are deferred to T035/T036/T037 (listed in the T028 notes). The evaluation cases that Kotlin runs on
/// TestGame are run here on the data-driven fakes of UniqueTestFakes.cs.
/// </summary>
public class CountableTests
{
    private static (FakeGameInfo Game, FakeCiv Civ, FakeCity City) Setup()
    {
        var game = new FakeGameInfo();
        var civ = new FakeCiv("civ", game);
        game.FakeCivs.Add(civ);
        var city = new FakeCity("city", civ, new FakeTile(new HexCoord(2, 0)));
        civ.FakeCities.Add(city);
        return (game, civ, city);
    }

    //region Meta and General tests
    [Fact]
    public void TestCountableConventions()
    {
        var fails = new List<string>();
        foreach (var instance in Countables.Entries)
        {
            bool matches1 = instance.OverridesMatches, matches2 = instance.OverridesMatchesWithRuleset;
            if (matches1 && matches2)
                fails.Add($"`{instance}` overrides both `matches` overloads. You either need a Ruleset to match or you don't.");
            else if (matches1 && instance.MatchesWithRuleset)
                fails.Add($"`{instance}` overrides the `matches` overload without ruleset but has `matchesWithRuleset` true.");
            else if (matches2 && !instance.MatchesWithRuleset)
                fails.Add($"`{instance}` overrides the `matches` overload with ruleset but has `matchesWithRuleset` false.");

            if (instance.Text.Length == 0 && !(matches1 || matches2))
                fails.Add($"`{instance}` has no `text` but fails to override `matches`.");

            if (instance.NoPlaceholders && instance.OverridesGetErrorSeverity)
                fails.Add($"`{instance}` has no placeholders but overrides `getErrorSeverity` which is likely an error.");
            else if (!instance.NoPlaceholders && !instance.OverridesGetErrorSeverity)
                fails.Add($"`{instance}` has placeholders that must be treated and therefore **must** override `getErrorSeverity` but does not.");
        }
        Assert.Empty(fails);
    }

    [Fact]
    public void TestAllCountableParametersAreUniqueParameterTypes()
    {
        foreach (var countable in Countables.Entries)
            foreach (var parameter in countable.Text.GetPlaceholderParameters())
                // Kotlin: assertNotEquals(safeValueOf(parameter), Unknown), where safeValueOf is nullable (the "[stat/resource]" placeholder has no type)
                Assert.NotEqual(UniqueParameterType.Unknown, UniqueParameterTypeExtensions.SafeValueOf(parameter));
    }

    [Fact]
    public void TestAllCountableAutocompleteValuesMatch()
    {
        // Kotlin runs this over Vanilla; with the empty ruleset only the ruleset-independent values exist.
        var ruleset = EmptyRulesetParameterView.Instance;
        foreach (var countable in Countables.Entries)
            foreach (string value in countable.GetKnownValuesForAutocomplete(ruleset))
                Assert.Same(countable, Countables.GetMatching(value, ruleset));
    }

    [Fact]
    public void AutocompleteValuesIncludeRulesetResourcesAndAreCachedPerRuleset()
    {
        var ruleset = new FakeRuleset();
        ruleset.AddResource("Iron");
        ruleset.AddResource("Coal");
        var values = Countables.GetAllKnownValuesForAutocomplete(ruleset);
        Assert.Contains("Iron", values);
        Assert.Contains("[Coal] Per Turn", values);
        Assert.Contains("turns", values);
        Assert.Contains("Speed modifier for [Production]", values);
        Assert.Same(values, Countables.GetAllKnownValuesForAutocomplete(ruleset));
        foreach (string value in values)
            Assert.NotNull(Countables.GetMatching(value, ruleset));
        // declaration order: the Integer entry contributes nothing, Turns comes first
        Assert.Equal("turns", values.First());
    }

    [Fact]
    public void TestPlaceholderParams() =>
        Assert.Equal("when number of [] is equal to []",
            "when number of [Iron] is equal to [3 * 2 + [Iron] + [bob]]".GetPlaceholderText());

    [Fact]
    public void EntriesAreInKotlinDeclarationOrder() =>
        Assert.Equal(
        [
            "Integer", "Turns", "Year", "Cities", "Units", "Stats", "StatOrResourcePerTurn", "PolicyBranches", "FilteredCities", "FilteredUnits",
            "Carried", "FilteredBuildings", "FilteredBuildingsByCivs", "FilteredPopulation", "FilteredCitiesByCivs", "FilteredPolicies",
            "FilteredPoliciesByCivs", "FilteredTechnologies", "KnownCivs", "RemainingCivs", "WorkedTilesCity", "WorkedTiles", "OwnedTiles",
            "TileFilterTiles", "TileResources", "TileResourcesByCivs", "CityStates", "EraNumber", "GameSpeedModifier", "Expression",
        ], Countables.Entries.Select(it => it.Name).ToArray());

    [Fact]
    public void ValidationClassifiesCountableParameters()
    {
        var ruleset = new FakeRuleset();
        ruleset.AddResource("Iron");
        // known values
        Assert.True(UniqueParameterType.Countable.IsKnownValue("turns", ruleset));
        Assert.True(UniqueParameterType.Countable.IsKnownValue("-0", ruleset));
        Assert.True(UniqueParameterType.Countable.IsKnownValue("Science", ruleset));
        Assert.True(UniqueParameterType.Countable.IsKnownValue("Iron", ruleset));
        Assert.True(UniqueParameterType.Countable.IsKnownValue("[Iron] Per Turn", ruleset));
        Assert.True(UniqueParameterType.Countable.IsKnownValue("[Science] Per Turn", ruleset));
        Assert.True(UniqueParameterType.Countable.IsKnownValue("[Iron] + 2", ruleset));
        Assert.False(UniqueParameterType.Countable.IsKnownValue("countable", ruleset));
        Assert.False(UniqueParameterType.Countable.IsKnownValue("Maryjane", ruleset));
        Assert.False(UniqueParameterType.Countable.IsKnownValue("[Maryjane] Per Turn", ruleset));
        Assert.Null(Countables.GetErrorSeverityOfText("turns", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetSpecific, Countables.GetErrorSeverityOfText("Maryjane", ruleset));
        // a bad stat name in "Speed modifier for [stat]" can never be right
        Assert.Equal(UniqueParameterErrorSeverity.RulesetInvariant, Countables.GetErrorSeverityOfText("Speed modifier for [Bogus]", ruleset));
        Assert.Null(Countables.GetErrorSeverityOfText("Speed modifier for [Gold]", ruleset));
        // "[42] Monkeys" fits no countable, nor does a malformed expression
        Assert.Equal(UniqueParameterErrorSeverity.RulesetSpecific, Countables.GetErrorSeverityOfText("[42] Monkeys", ruleset));
        // the deprecated entry still matches
        Assert.Same(Countables.CityStates, Countables.GetMatching("City-States", ruleset));
        Assert.Equal("Remaining [City-State] Civilizations", Countables.CityStates.GetDeprecationAnnotation()!.ReplaceWith);
        Assert.Null(Countables.Turns.GetDeprecationAnnotation());
    }

    [Fact]
    public void ProviderIsWiredIntoUniqueParameterType() =>
        Assert.True(UniqueParameterType.Countable.IsKnownValue("Era number", EmptyRulesetParameterView.Instance));

    [Fact]
    public void DocumentationTextsFollowKotlin()
    {
        Assert.Equal("`turns` - Number of turns played", Countables.Turns.DocumentationHeader);
        Assert.Equal("`Completed Policy branches`", Countables.PolicyBranches.DocumentationHeader);
        Assert.Equal("Integer constant - any positive or negative integer number", Countables.Integer.DocumentationHeader);
        Assert.Equal("Stat name (`Production`, `Food`, `Gold`, `Science`, `Culture`, `Happiness` or `Faith`)", Countables.Stats.DocumentationHeader);
        Assert.Equal("Evaluate expressions!", Countables.Expression.DocumentationHeader);
        Assert.Contains("Supported operations between 2 values are: +, -, *, /, %, ^", Countables.Expression.DocumentationStrings);
        Assert.Contains("Supported operations on 1 value are: - (negation), √ (square root), abs (absolute value - turns negative into positive), sqrt (square root), floor (round down), ceil (round up)",
            Countables.Expression.DocumentationStrings);
        Assert.Contains(" - `max(expression,expression,...)`", Countables.Expression.DocumentationStrings);
        Assert.Equal("123", Countables.Integer.Example);
        Assert.Equal("turns", Countables.Turns.Example);
    }
    //endregion

    //region Coverage for specific Countables
    [Fact]
    public void SimpleCountablesReadTheGameState()
    {
        var (game, civ, city) = Setup();
        game.Turns = 124;
        civ.FakeUnits.Add(new FakeUnit("Warrior", 2f, civ));
        civ.CompletedPolicyBranches = 2;
        civ.EraNumber = 3;
        var context = new GameContext(civInfo: civ, city: city);
        Assert.Equal(124, Countables.GetCountableAmount("turns", context));
        Assert.Equal(1, Countables.GetCountableAmount("Cities", context));
        Assert.Equal(1, Countables.GetCountableAmount("Units", context));
        Assert.Equal(2, Countables.GetCountableAmount("Completed Policy branches", context));
        Assert.Equal(3, Countables.GetCountableAmount("Era number", context));
        Assert.Equal(-4000 + 124 * 40, Countables.GetCountableAmount("year", context));
        Assert.Equal(-42, Countables.GetCountableAmount("-42", context));
        Assert.Null(Countables.GetCountableAmount("Bogus", context));
        // no game: everything that reads gameInfo or civInfo is null, constants still work
        Assert.Null(Countables.GetCountableAmount("turns", GameContext.EmptyState));
        Assert.Null(Countables.GetCountableAmount("Cities", GameContext.EmptyState));
        Assert.Equal(7, Countables.GetCountableAmount("7", GameContext.EmptyState));
    }

    [Fact]
    public void TimeCountablesInExpressions()
    {
        var (game, civ, _) = Setup();
        var context = new GameContext(civInfo: civ);
        game.Turns = 7;
        Assert.Equal(0, Countables.GetCountableAmount("[year] - ([-4000] + [40] * [turns])", context));
        game.Turns = 42;
        Assert.Equal(0, Countables.GetCountableAmount("[year] - (-4000 + 40 * [turns])", context));
    }

    [Fact]
    public void StatsCountablePrefersTheCityReserveAndFallsBackToTheCiv()
    {
        var (_, civ, city) = Setup();
        civ.StatReserves[Stat.Gold] = 100;
        civ.StatReserves[Stat.Food] = 5;
        city.StatReserves[Stat.Gold] = 7;
        Assert.Equal(7, Countables.GetCountableAmount("Gold", new GameContext(civInfo: civ, city: city)));
        Assert.Equal(100, Countables.GetCountableAmount("Gold", new GameContext(civInfo: civ)));
        Assert.Equal(5, Countables.Stats.Eval("Food", new GameContext(civInfo: civ)));
        Assert.Null(Countables.Stats.Eval("Bogus", new GameContext(civInfo: civ)));
        Assert.Null(Countables.Stats.Eval("Gold", GameContext.EmptyState));
    }

    [Fact]
    public void StatOrResourcePerTurnCountable()
    {
        var (game, civ, city) = Setup();
        game.FakeRulesetData.AddResource("Coal");
        game.FakeRulesetData.AddResource("Iron");
        city.CurrentCityStats[Stat.Production] = 3.9f;
        civ.StatMapValues[Stat.Gold] = [3f, 1.5f];
        civ.StatMapValues[Stat.Production] = [9f];
        civ.Resources["Coal"] = 5;
        city.Generated["Coal"] = 2;
        var civContext = new GameContext(civInfo: civ);
        var cityContext = new GameContext(civInfo: civ, city: city);
        Assert.Equal(4, Countables.GetCountableAmount("[Gold] Per Turn", civContext)); // 4.5f.toInt() truncates
        Assert.Equal(4, Countables.GetCountableAmount("[Gold] Per Turn", cityContext)); // Gold is civ-wide even with a city
        Assert.Equal(3, Countables.GetCountableAmount("[Production] Per Turn", cityContext)); // city-wide stat, 3.9f.toInt()
        Assert.Equal(9, Countables.GetCountableAmount("[Production] Per Turn", civContext));
        Assert.Equal(5, Countables.GetCountableAmount("[Coal] Per Turn", civContext));
        Assert.Equal(2, Countables.GetCountableAmount("[Coal] Per Turn", cityContext));
        Assert.Equal(0, Countables.GetCountableAmount("[Iron] Per Turn", civContext));
        Assert.Null(Countables.GetCountableAmount("[Null] Per Turn", civContext)); // not a stat/resource: does not match any countable
        Assert.Null(Countables.GetCountableAmount("[Gold] Per Turn", new GameContext(gameInfo: game)));
    }

    [Fact]
    public void ResourceCountableSeesLocalResourcesOnlyThroughACity()
    {
        var (game, civ, city) = Setup();
        game.FakeRulesetData.AddResource("Stockpiled");
        civ.Resources["Stockpiled"] = 5;
        city.Resources["Stockpiled"] = 2;
        Assert.Equal(5, Countables.GetCountableAmount("Stockpiled", new GameContext(civInfo: civ)));
        Assert.Equal(2, Countables.GetCountableAmount("Stockpiled", new GameContext(civInfo: civ, city: city)));
        // the expression form from countableCanUseContext
        Assert.Equal(2, Countables.GetCountableAmount("[Stockpiled] - 3", new GameContext(civInfo: civ)));
    }

    [Fact]
    public void FilteredCitiesAndUnitsCountables()
    {
        var (_, civ, city) = Setup();
        city.CityFilters.Add("Capital");
        city.CityFilters.Add("Your");
        var city2 = new FakeCity("city2", civ, new FakeTile(new HexCoord(-2, 0)));
        city2.CityFilters.Add("Your");
        civ.FakeCities.Add(city2);
        var unit = new FakeUnit("Warrior", 2f, civ);
        unit.UnitFilters.Add("Melee");
        civ.FakeUnits.Add(unit);
        civ.FakeUnits.Add(new FakeUnit("Archer", 2f, civ));
        var context = new GameContext(civInfo: civ);
        Assert.Equal(1, Countables.GetCountableAmount("[Capital] Cities", context));
        Assert.Equal(2, Countables.GetCountableAmount("[Your] Cities", context));
        Assert.Equal(1, Countables.GetCountableAmount("[Melee] Units", context));
        Assert.Equal(0, Countables.GetCountableAmount("[Air] Units", context));
        Assert.Null(Countables.GetCountableAmount("[Your] Cities", GameContext.EmptyState));
    }

    [Fact]
    public void FilteredBuildingsCountableUsesTheFilterCacheAndBothIntersections()
    {
        var (game, civ, city) = Setup();
        // the filter cache is keyed by the filter alone and lives for the process: use a filter no other test uses
        const string filter = "FilteredBuildingsCountableUsesTheFilterCache";
        game.FakeRulesetData.BuildingNames.AddRange(["Ancestor Tree", "Library", "Temple"]);
        game.FakeRulesetData.BuildingsByFilter[filter] = ["Ancestor Tree", "Temple"];
        city.Buildings.UnionWith(["Ancestor Tree", "Library"]);
        var city2 = new FakeCity("city2", civ);
        city2.Buildings.UnionWith(["Ancestor Tree", "Temple", "Library", "Granary", "Walls"]);
        civ.FakeCities.Add(city2);
        var context = new GameContext(civInfo: civ);
        // city: matching (2) > built (2)? no, equal -> matching side; city2: matching (2) <= built (5) -> matching side
        Assert.Equal(1 + 2, Countables.GetCountableAmount($"[{filter}] Buildings", context));
        // later changes to the ruleset are ignored: Kotlin never invalidates this cache
        game.FakeRulesetData.BuildingsByFilter[filter] = ["Library"];
        Assert.Equal(3, Countables.GetCountableAmount($"[{filter}] Buildings", context));
    }

    [Fact]
    public void FilteredBuildingsByCivsCountsOnlyLivingMatchingCivs()
    {
        var (game, civ, city) = Setup();
        city.Buildings.Add("Ancestor Tree");
        var civ2 = new FakeCiv("civ2", game);
        var city2 = new FakeCity("city2", civ2);
        city2.Buildings.Add("Ancestor Tree");
        civ2.FakeCities.Add(city2);
        var dead = new FakeCiv("dead", game) { Alive = false };
        var cityDead = new FakeCity("cityDead", dead);
        cityDead.Buildings.Add("Ancestor Tree");
        dead.FakeCities.Add(cityDead);
        game.FakeCivs.AddRange([civ2, dead]);
        foreach (var c in game.FakeCivs) c.MatchingCivFilters.Add("All");
        civ.MatchingCivFilters.Add("civ");
        var context = new GameContext(civInfo: civ);
        Assert.Equal(2, Countables.GetCountableAmount("[Ancestor Tree] Buildings by [All] Civilizations", context));
        Assert.Equal(1, Countables.GetCountableAmount("[Ancestor Tree] Buildings by [civ] Civilizations", context));
        Assert.Equal(0, Countables.GetCountableAmount("[Ancestor Tree] Buildings by [City-State] Civilizations", context));
    }

    [Fact]
    public void KnownAndRemainingCivsCountables()
    {
        var (game, civ, _) = Setup();
        var major = new FakeCiv("major", game);
        var cityState = new FakeCiv("cs", game) { IsCityState = true, Major = false };
        game.FakeCivs.AddRange([major, cityState]);
        foreach (var c in game.FakeCivs) c.MatchingCivFilters.Add("all");
        cityState.MatchingCivFilters.Add("City-State");
        major.MatchingCivFilters.Add("Major");
        civ.MatchingCivFilters.Add("Major");
        var context = new GameContext(civInfo: civ);
        Assert.Equal(0, Countables.GetCountableAmount("Known [Major] Civilizations", context));
        civ.KnownCivs.Add(cityState);
        Assert.Equal(1, Countables.GetCountableAmount("Known [City-State] Civilizations", context));
        civ.KnownCivs.Add(major);
        Assert.Equal(2, Countables.GetCountableAmount("Known [all] Civilizations", context));
        Assert.Equal(2, Countables.GetCountableAmount("Remaining [Major] Civilizations", context));
        Assert.Equal(1, Countables.GetCountableAmount("Remaining [City-State] Civilizations", context));
        major.Alive = false;
        Assert.Equal(1, Countables.GetCountableAmount("Remaining [Major] Civilizations", context));
        Assert.Equal(1, Countables.GetCountableAmount("City-States", context));
    }

    [Fact]
    public void TileCountables()
    {
        var (game, civ, city) = Setup();
        var worked = new FakeTile(new HexCoord(0, 0));
        worked.Matching.UnionWith(["Land", "worked"]);
        var coast = new FakeTile(new HexCoord(1, 0));
        coast.Matching.Add("Coast");
        city.WorkedTiles.Add(worked);
        city.OwnedTiles.AddRange([worked, coast]);
        var city2 = new FakeCity("city2", civ);
        city2.OwnedTiles.Add(worked);
        civ.FakeCities.Add(city2);
        game.FakeTileList = [worked, coast, new FakeTile(new HexCoord(5, 5))];
        var civContext = new GameContext(civInfo: civ);
        var cityContext = new GameContext(civInfo: civ, city: city);
        Assert.Equal(2, Countables.GetCountableAmount("Owned [worked] Tiles", civContext));
        Assert.Equal(1, Countables.GetCountableAmount("Owned [Coast] Tiles", civContext));
        Assert.Equal(0, Countables.GetCountableAmount("Owned [Farm] Tiles", civContext));
        Assert.Equal(1, Countables.GetCountableAmount("Worked [worked] Tiles", civContext));
        Assert.Equal(1, Countables.GetCountableAmount("Worked [worked] Tiles in this city", cityContext));
        Assert.Null(Countables.GetCountableAmount("Worked [worked] Tiles in this city", civContext));
        Assert.Equal(1, Countables.GetCountableAmount("[Land] Tiles", civContext));
        game.FakeTileList = null;
        Assert.Null(Countables.GetCountableAmount("[Land] Tiles", civContext));
    }

    [Fact]
    public void PopulationPolicyTechnologyAndCarriedCountables()
    {
        var (game, civ, city) = Setup();
        city.PopulationByFilter["Specialists"] = 3;
        var city2 = new FakeCity("city2", civ);
        city2.PopulationByFilter["Specialists"] = 4;
        city.CityFilters.Add("Capital");
        city2.CityFilters.Add("Coastal");
        civ.FakeCities.Add(city2);
        var civContext = new GameContext(civInfo: civ);
        var cityContext = new GameContext(civInfo: civ, city: city);
        Assert.Equal(3, Countables.GetCountableAmount("[Specialists] in [in this city] Cities", cityContext));
        Assert.Equal(0, Countables.GetCountableAmount("[Specialists] in [in this city] Cities", civContext)); // no city: falls to the filter
        Assert.Equal(3, Countables.GetCountableAmount("[Specialists] in [Capital] Cities", civContext));
        Assert.Equal(4, Countables.GetCountableAmount("[Specialists] in [Coastal] Cities", civContext));

        civ.AdoptedPolicies.UnionWith(["Tradition", "Liberty"]);
        Assert.Equal(1, Countables.GetCountableAmount("Adopted [Tradition] Policies", civContext));
        Assert.Equal(2, Countables.GetCountableAmount("Adopted [all] Policies", civContext));

        game.FakeRulesetData.AddTech("Agriculture");
        game.FakeRulesetData.AddTech("Optics");
        civ.ResearchedTechs.UnionWith(["Agriculture", "Optics", "Unknown Tech"]);
        Assert.Equal(1, Countables.GetCountableAmount("Researched [Optics] Technologies", civContext));
        Assert.Equal(2, Countables.GetCountableAmount("Researched [all] Technologies", civContext)); // "Unknown Tech" is not in the ruleset

        var carrier = new FakeUnit("Carrier", 2f, civ);
        var tile = new FakeTile(new HexCoord(0, 0));
        var carried = new FakeUnit("Fighter", 2f, civ, tile) { IsTransported = true };
        carried.UnitFilters.Add("Air");
        var parked = new FakeUnit("Bomber", 2f, civ, tile);
        parked.UnitFilters.Add("Air");
        tile.FakeAirUnits.AddRange([carried, parked]);
        var unitContext = new GameContext(civInfo: civ, unit: carried);
        Assert.Equal(1, Countables.GetCountableAmount("Carried [Air] units", unitContext));
        Assert.Null(Countables.GetCountableAmount("Carried [Air] units", civContext)); // no relevant unit
        _ = carrier;
    }

    [Fact]
    public void ResourcesByCivsCountable()
    {
        var (game, civ, _) = Setup();
        game.FakeRulesetData.AddResource("Iron");
        game.FakeRulesetData.AddResource("Coal");
        game.FakeRulesetData.AddResource("Wine");
        game.FakeRulesetData.ResourcesByFilter["Strategic"] = ["Iron", "Coal"];
        var civ2 = new FakeCiv("civ2", game);
        game.FakeCivs.Add(civ2);
        civ.MatchingCivFilters.Add("All");
        civ2.MatchingCivFilters.Add("All");
        civ.Resources["Iron"] = 2;
        civ.Resources["Wine"] = 9;
        civ2.Resources["Coal"] = 4;
        civ2.Resources["Iron"] = 1;
        Assert.Equal(7, Countables.GetCountableAmount("[Strategic] resource of [All] Civilizations", new GameContext(civInfo: civ)));
        civ2.Alive = false;
        Assert.Equal(2, Countables.GetCountableAmount("[Strategic] resource of [All] Civilizations", new GameContext(civInfo: civ)));
    }

    [Fact]
    public void SpeedModifierCountableMultipliesTheFloatByOneHundred()
    {
        var (game, civ, _) = Setup();
        var context = new GameContext(civInfo: civ);
        game.FakeSpeedData.ProductionCostModifier = 0.67f;
        game.FakeSpeedData.GoldCostModifier = 1f;
        game.FakeSpeedData.FaithCostModifier = 3f;
        game.FakeSpeedData.Modifier = 0.5f;
        Assert.Equal(67, Countables.GetCountableAmount("Speed modifier for [Production]", context));
        Assert.Equal(100, Countables.GetCountableAmount("Speed modifier for [Gold]", context));
        Assert.Equal(300, Countables.GetCountableAmount("Speed modifier for [Faith]", context));
        Assert.Equal(50, Countables.GetCountableAmount("Speed modifier for [Food]", context));
        Assert.Equal(50, Countables.GetCountableAmount("Speed modifier for [Happiness]", context));
        Assert.Null(Countables.GetCountableAmount("Speed modifier for [Bogus]", context));
        Assert.Null(Countables.GetCountableAmount("Speed modifier for [Gold]", GameContext.EmptyState));
    }
    //endregion
}
