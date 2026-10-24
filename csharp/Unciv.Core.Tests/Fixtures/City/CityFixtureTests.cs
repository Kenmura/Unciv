using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Fixtures.CityFixtures;

// T072: port of tests/src/com/unciv/logic/city/CityTest.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class CityFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization testCiv;
    private readonly CityObject capitalCity;

    public CityFixtureTests()
    {
        testGame.MakeHexagonalMap(4);
        testCiv = testGame.AddCiv();
        capitalCity = testGame.AddCity(testCiv, testGame.GetTile(HexCoord.Zero));
    }

    private Unciv.Models.Ruleset.Building CreateFarmPlacingBuilding() =>
        testGame.CreateBuilding("Creates a [Farm] improvement on a specific tile");

    [Fact]
    public void ShouldNotDestroyCityWhenItsAnOriginalCapital()
    {
        capitalCity.DestroyCity();
        Assert.Contains(capitalCity, testCiv.Cities);
    }

    [Fact]
    public void ShouldDestroyCityWhenItsNotOriginalCapital()
    {
        var nonCapitalCity = testGame.AddCity(testCiv, testGame.GetTile(1, 1));
        nonCapitalCity.DestroyCity();
        Assert.Contains(capitalCity, testCiv.Cities);
        Assert.DoesNotContain(nonCapitalCity, testCiv.Cities);
    }

    [Fact]
    public void ShouldMoveCapitalWhenDestroyed()
    {
        var nonCapitalCity = testGame.AddCity(testCiv, testGame.GetTile(1, 1));
        nonCapitalCity.Name = "Not capital";

        capitalCity.DestroyCity(overrideSafeties: true);

        Assert.DoesNotContain(capitalCity, testCiv.Cities);
        Assert.True(testCiv.Cities[0].IsCapital());
        Assert.Equal("Not capital", testCiv.Cities[0].Name);
    }

    [Fact]
    public void ShouldSetTerrainAsCityRuinWhenCityIsDestroyed()
    {
        capitalCity.DestroyCity(overrideSafeties: true);
        Assert.Equal("City ruins", testGame.GetTile(HexCoord.Zero).Improvement);
    }

    [Fact]
    public void ShouldSellBuilding()
    {
        const int expectedMonumentSellingGoldGain = 4;
        capitalCity.CityConstructions.AddBuilding("Monument");

        capitalCity.SellBuilding("Monument");

        Assert.Equal(expectedMonumentSellingGoldGain, testCiv.Gold);
    }

    [Fact]
    public void ShouldSellOnlyOneBuildingPerTurn()
    {
        capitalCity.CityConstructions.AddBuilding("Monument");
        capitalCity.SellBuilding("Monument");
        Assert.True(capitalCity.HasSoldBuildingThisTurn);
    }

    [Fact]
    public void ShouldMarkSelectedOwnedTileWhenQueueingCreatesOneImprovementBuilding()
    {
        var farm = testGame.Ruleset.TileImprovements["Farm"];
        testCiv.Tech.TechsResearched.Add(farm.TechRequired!);
        var building = CreateFarmPlacingBuilding();
        var targetTile = testGame.SetTileTerrain(new HexCoord(1, 0), Constants.Grassland);

        capitalCity.CityConstructions.AddToQueue(building, tile: targetTile);

        Assert.True(capitalCity.CityConstructions.IsBeingConstructedOrEnqueued(building.Name));
        Assert.True(targetTile.IsMarkedForCreatesOneImprovement("Farm"));
    }

    [Fact]
    public void ShouldRequireSelectedTileWhenQueueingCreatesOneImprovementBuilding()
    {
        var building = CreateFarmPlacingBuilding();

        var ex = Assert.Throws<ArgumentException>(() => capitalCity.CityConstructions.AddToQueue(building));
        Assert.Contains("target tile", ex.Message, StringComparison.Ordinal);

        Assert.False(capitalCity.CityConstructions.IsBeingConstructedOrEnqueued(building.Name));
    }

    [Fact]
    public void ShouldFailLoudlyWhenMarkingCreatesOneImprovementTileOwnedByAnotherCity()
    {
        var secondCity = testGame.AddCity(testCiv, testGame.GetTile(0, 2));
        var targetTile = testGame.SetTileTerrain(new HexCoord(0, 1), Constants.Grassland);
        var farm = testGame.Ruleset.TileImprovements["Farm"];

        Assert.Same(capitalCity, targetTile.GetCity());
        Assert.False(secondCity.CityConstructions.CanPlaceCreateOneImprovementOn(farm, targetTile));

        var ex = Assert.Throws<InvalidOperationException>(() => secondCity.CityConstructions.TryPlaceCreateOneImprovementMarker(farm, targetTile));
        Assert.Contains("tile is owned by", ex.Message, StringComparison.Ordinal);

        Assert.Same(capitalCity, targetTile.GetCity());
        Assert.False(targetTile.IsMarkedForCreatesOneImprovement());
    }

    [Fact]
    public void ShouldClearMarkerWhenRemovingCreatesOneImprovementBuildingFromQueue()
    {
        var farm = testGame.Ruleset.TileImprovements["Farm"];
        testCiv.Tech.TechsResearched.Add(farm.TechRequired!);
        var building = CreateFarmPlacingBuilding();
        var targetTile = testGame.SetTileTerrain(new HexCoord(1, 0), Constants.Grassland);
        capitalCity.CityConstructions.AddToQueue(building, tile: targetTile);

        capitalCity.CityConstructions.RemoveFromQueue(0, false);

        Assert.False(targetTile.IsMarkedForCreatesOneImprovement());
        Assert.False(capitalCity.CityConstructions.IsBeingConstructedOrEnqueued(building.Name));
    }

    [Fact]
    public void ShouldNotMarkCreatesOneImprovementTileOutsideCityRange()
    {
        var secondCity = testGame.AddCity(testCiv, testGame.GetTile(0, 2));
        var targetTile = testGame.SetTileTerrain(new HexCoord(0, -4), Constants.Grassland);
        var farm = testGame.Ruleset.TileImprovements["Farm"];
        secondCity.Expansion.TakeOwnership(targetTile);

        var markerPlaced = secondCity.CityConstructions.TryPlaceCreateOneImprovementMarker(farm, targetTile);

        Assert.False(markerPlaced);
        Assert.Same(secondCity, targetTile.GetCity());
        Assert.False(targetTile.IsMarkedForCreatesOneImprovement());
    }

    [Fact]
    public void ShouldCompleteCreatesOneImprovementPurchaseOnOwnedTile()
    {
        testGame.GameInfo.GameParameters.GodMode = true;
        var secondCity = testGame.AddCity(testCiv, testGame.GetTile(0, 2));
        var targetTile = testGame.SetTileTerrain(new HexCoord(0, 3), Constants.Grassland);
        var building = CreateFarmPlacingBuilding();

        var purchased = secondCity.CityConstructions.PurchaseConstruction(building, queuePosition: -1, automatic: false, tile: targetTile);

        Assert.True(purchased);
        Assert.True(secondCity.CityConstructions.IsBuilt(building.Name));
        Assert.Same(secondCity, targetTile.GetCity());
        Assert.Equal("Farm", targetTile.Improvement);
        Assert.False(targetTile.IsMarkedForCreatesOneImprovement());
    }
}
