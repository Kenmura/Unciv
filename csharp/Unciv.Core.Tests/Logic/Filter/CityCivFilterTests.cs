using Unciv.Core.Tests.Support;
using Unciv.Logic.City;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Core.Tests.Logic.Filter;

// T103: port of tests/src/com/unciv/logic/filter/CityCivFilterTests.kt.
[Collection("RulesetCache")]
public class CityCivFilterTests
{
    private readonly TestGame game = new();
    private readonly Civilization civ;
    private readonly City city;

    public CityCivFilterTests()
    {
        game.MakeHexagonalMap(6);
        civ = game.AddCiv();
        city = game.AddCity(civ, game.GetTile(HexCoord.Zero));
    }

    [Fact]
    public void TestFilteredCities()
    {
        UniqueTriggerActivation.TriggerUnique(new Unique("Turn this tile into a [Coast] tile"), civ, tile: game.TileMap[-3, 0]);

        var city2 = game.AddCity(civ, game.TileMap[-2, 0], initialPopulation: 9);
        city2.IsPuppet = true;
        var enemy = game.AddCiv();
        game.AddCity(enemy, game.TileMap[3, 0]);

        TestParcours.Run("Filtered cities countable", (string test) => game.GameInfo.GetCities().Count(it => it.MatchesFilter(test, civ)),
            ("All", 3),
            ("Capital", 2),
            ("Puppeted", 1),
            ("Coastal", 1),
            ("Your", 2),
            ("{Your} {Capital}", 1));
    }

    [Fact]
    public void TestBuildingFilters()
    {
        var testCultureBuilding = game.CreateBuilding("[+1 Culture]");
        var testFilterBuilding = game.CreateBuilding("hasFilter");
        var testCultureWithOtherBuilding = game.CreateBuilding("[+1 Culture] <in cities with a [hasFilter]>");
        // Note: This does not work with normal building filters. Only names and tags
        var testFilterWithOtherBuilding = game.CreateBuilding($"hasFilter <in cities with a [{testCultureBuilding.Name}]>");
        var testWonder = game.CreateBuilding();
        testWonder.IsWonder = true;
        var buildingsToTest = new[] { testCultureBuilding, testFilterBuilding, testCultureWithOtherBuilding, testFilterWithOtherBuilding, testWonder };
        TestParcours.Run("", (string filter) => buildingsToTest.Count(it => it.MatchesFilter(filter, city.State)),
            ("hasFilter", 1), // Other buildings hasn't been added
            ("Culture", 1),
            ("Wonder", 1),
            ("All", 5));
        city.CityConstructions.AddBuilding(testCultureBuilding);
        city.CityConstructions.AddBuilding(testFilterBuilding);
        city.CityConstructions.AddBuilding(testCultureWithOtherBuilding); // Note: cannot correctly determine that it gives culture until it's added
        TestParcours.Run("", (string filter) => buildingsToTest.Count(it => it.MatchesFilter(filter, city.State)),
            ("hasFilter", 2),
            ("Culture", 1), // Known upstream bug (per the Kotlin comment): cities are not given a city context for filtering
            ("Wonder", 1),
            ("All", 5));
    }
}
