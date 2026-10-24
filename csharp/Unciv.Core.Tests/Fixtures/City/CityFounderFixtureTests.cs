using Unciv.Core.Tests.Support;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;

namespace Unciv.Core.Tests.Fixtures.CityFixtures;

// T072: port of tests/src/com/unciv/logic/city/managers/CityFounderTest.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class CityFounderFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization civ;
    private readonly CityFounder cityFounder = new();

    public CityFounderFixtureTests()
    {
        testGame.MakeHexagonalMap(3);
        civ = testGame.AddCiv();
    }

    [Fact]
    public void ShouldSaveOriginalFoundingCiv()
    {
        var foundedCity = cityFounder.FoundCity(civ, HexCoord.Zero);
        Assert.Same(civ, foundedCity.FoundingCivObject);
    }

    [Fact]
    public void ShouldGenerateNewCityNamesBasedOnNationCityNameList()
    {
        civ.Nation.Cities = ["1", "2", "3"];

        var foundedCity1 = cityFounder.FoundCity(civ, HexCoord.Zero);
        var foundedCity2 = cityFounder.FoundCity(civ, new HexCoord(1, 0));
        var foundedCity3 = cityFounder.FoundCity(civ, new HexCoord(0, 1));

        Assert.Equal("1", foundedCity1.Name);
        Assert.Equal("2", foundedCity2.Name);
        Assert.Equal("3", foundedCity3.Name);
    }

    [Fact]
    public void ShouldBorrowCityNamesBasedOnAliveCivs()
    {
        var borrowerCiv = testGame.AddCiv(["\"Borrows\" city names from other civilizations in the game"]);
        borrowerCiv.Nation.Cities = [];
        civ.Nation.Cities = ["1", "2", "3"];
        cityFounder.FoundCity(civ, new HexCoord(1, 0)); // otherwise "civ" is considered dead

        var foundedCity = cityFounder.FoundCity(borrowerCiv, HexCoord.Zero);

        Assert.Equal("3", foundedCity.Name);
    }

    [Fact]
    public void ShouldMakeOnlyFirstFoundedCityCapital()
    {
        var capitalCity = cityFounder.FoundCity(civ, HexCoord.Zero);
        var nonCapitalCity = cityFounder.FoundCity(civ, new HexCoord(1, 0));

        Assert.True(capitalCity.IsCapital());
        Assert.True(capitalCity.CityConstructions.ContainsBuildingOrEquivalent("Palace"));
        Assert.False(nonCapitalCity.IsCapital());
        Assert.False(nonCapitalCity.CityConstructions.ContainsBuildingOrEquivalent("Palace"));
    }

    [Fact]
    public void ShouldGetFreeBuildingsDueToUniquesUponCityFounding()
    {
        var freeBuildingCiv = testGame.AddCiv(["Gain a free [Granary] [in all cities]"]);
        var foundedCity = cityFounder.FoundCity(freeBuildingCiv, HexCoord.Zero);
        Assert.True(foundedCity.CityConstructions.ContainsBuildingOrEquivalent("Granary"));
    }

    [Fact]
    public void ShouldRemoveRemovableTerrainFeaturesUponCityFounding()
    {
        testGame.SetTileFeatures(HexCoord.Zero, Constants.Forest);
        Assert.Contains(Constants.Forest, testGame.GetTile(HexCoord.Zero).TerrainFeatures);

        var foundedCity = cityFounder.FoundCity(civ, HexCoord.Zero);

        Assert.DoesNotContain(Constants.Forest, foundedCity.GetCenterTile().TerrainFeatures);
    }

    [Fact]
    public void ShouldKeepNonRemovableTerrainFeaturesUponCityFounding()
    {
        testGame.SetTileFeatures(HexCoord.Zero, Constants.Hill);
        Assert.Contains(Constants.Hill, testGame.GetTile(HexCoord.Zero).TerrainFeatures);

        var foundedCity = cityFounder.FoundCity(civ, HexCoord.Zero);

        Assert.Contains(Constants.Hill, foundedCity.GetCenterTile().TerrainFeatures);
    }

    [Fact]
    public void ShouldRemoveImprovementsUponCityFounding()
    {
        var tile = testGame.GetTile(HexCoord.Zero);
        tile.SetImprovementBasic("Farm");

        cityFounder.FoundCity(civ, HexCoord.Zero);

        Assert.NotEqual("Farm", tile.Improvement);
    }

    [Fact]
    public void ShouldExploitCityCenterYieldsWithoutRequiringPop()
    {
        testGame.SetTileTerrainAndFeatures(HexCoord.Zero, Constants.Desert, Constants.Hill); // 2 hammers expected
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Desert); // no yields expected

        var city = cityFounder.FoundCity(civ, HexCoord.Zero);
        city.ClearWorkedTiles();
        var tile10 = city.TileMap[new HexCoord(1, 0)];
        city.WorkTile(tile10);
        city.LockTile(tile10);
        city.CityStats.Update();

        Assert.False(city.IsWorked(city.GetCenterTile())); // no pop required
        Assert.Equal(2.0f, city.CityStats.StatsFromTiles.Production);
    }

    [Fact]
    public void ShouldAutoassignNewPopulation()
    {
        var city = cityFounder.FoundCity(civ, HexCoord.Zero);
        Assert.Equal(0, city.Population.GetFreePopulation());
    }

    [Fact]
    public void ShouldAcquireOwnershipOfTilesSurroundingTheCityCenter()
    {
        var city = cityFounder.FoundCity(civ, HexCoord.Zero);
        Assert.Equal(7, city.Tiles.Count); // 6 surrounding + 1 due to city center itself
    }

    [Fact]
    public void ShouldTriggerUniquesFromCityFounding()
    {
        var uniqueCiv = testGame.AddCiv(["Gain [+5] [Gold] <upon founding a city>"]);
        Assert.Equal(0, uniqueCiv.Gold);

        cityFounder.FoundCity(uniqueCiv, HexCoord.Zero);

        Assert.Equal(5, uniqueCiv.Gold);
    }

    [Fact]
    public void ShouldNotBeAbleToFoundCityInSameTileOfAnotherCity()
    {
        cityFounder.FoundCity(civ, HexCoord.Zero);

        var e = Assert.Throws<InvalidOperationException>(() => cityFounder.FoundCity(civ, HexCoord.Zero));
        Assert.Equal("Trying to found a city in a tile that already has one", e.Message);
    }
}
