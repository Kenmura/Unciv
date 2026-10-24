using Unciv.Core.Tests.Support;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Fixtures.CityFixtures;

// T072: port of tests/src/com/unciv/logic/city/managers/CityExpansionManagerTest.kt over the real TestGame.
// The Kotlin test builds an extra CityExpansionManager and points it at the city; here the city's own manager is
// used (it is the same class with the same state), and the extra managers of the other cities likewise.
[Collection("RulesetCache")]
public sealed class CityExpansionFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization civ;
    private readonly CityObject city;
    private readonly CityExpansionManager cityExpansionManager;

    public CityExpansionFixtureTests()
    {
        testGame.MakeHexagonalMap(6);
        civ = testGame.AddCiv();
        city = testGame.AddCity(civ, testGame.GetTile(HexCoord.Zero), initialPopulation: 1);
        cityExpansionManager = city.Expansion;
    }

    [Fact]
    public void ShouldClaimNewTileWhenEnoughCultureIsStored()
    {
        var cultureToNextTile = cityExpansionManager.GetCultureToNextTile();
        Assert.Equal(7, city.Tiles.Count); // city center + surrounding tiles

        cityExpansionManager.NextTurn(cultureToNextTile);

        Assert.Equal(8, city.Tiles.Count);
    }

    [Fact]
    public void ShouldClaimMaxOneTilePerTurnDueToCulture()
    {
        Assert.Equal(7, city.Tiles.Count);

        cityExpansionManager.NextTurn(10_000f);

        Assert.Equal(8, city.Tiles.Count);
    }

    [Fact]
    public void ShouldStoreExcessCultureForNextTile()
    {
        var cultureToNextTile = cityExpansionManager.GetCultureToNextTile();

        cityExpansionManager.NextTurn(cultureToNextTile + 10f);

        Assert.Equal(10, cityExpansionManager.CultureStored);
    }

    [Fact]
    public void ShouldIncreaseTileCultureCostTheMoreTilesACityHasClaimedByCulture()
    {
        var cultureToNextTile = cityExpansionManager.GetCultureToNextTile();

        cityExpansionManager.NextTurn(cultureToNextTile);

        Assert.True(cultureToNextTile < cityExpansionManager.GetCultureToNextTile());
    }

    [Fact]
    public void ShouldIncreaseTileCultureCostTheMoreTilesACityHasBought()
    {
        var cultureToNextTile = cityExpansionManager.GetCultureToNextTile();
        civ.AddGold(500);

        cityExpansionManager.BuyTile(testGame.GetTile(2, 0));

        Assert.True(cultureToNextTile < cityExpansionManager.GetCultureToNextTile());
    }

    [Fact]
    public void ShouldIncreaseTileCultureCostsForCityStates()
    {
        var cityStateCiv = testGame.AddCiv(cityStateType: "Militaristic");
        var cityState = testGame.AddCity(cityStateCiv, testGame.GetTile(4, 0));

        var cultureToNextTileCiv = cityExpansionManager.GetCultureToNextTile();
        var cultureToNextTileCityState = cityState.Expansion.GetCultureToNextTile();

        Assert.True(cultureToNextTileCityState > cultureToNextTileCiv);
    }

    [Fact]
    public void ShouldChangeTileCultureCostDueToUniques()
    {
        var uniqueCiv = testGame.AddCiv(["[-25]% Culture cost of natural border growth [in all cities]"]);
        var uniqueCity = testGame.AddCity(uniqueCiv, testGame.GetTile(4, 0));

        var cultureToNextTile = cityExpansionManager.GetCultureToNextTile();
        var cultureToNextTileUnique = uniqueCity.Expansion.GetCultureToNextTile();

        Assert.True(cultureToNextTileUnique < cultureToNextTile);
    }

    [Fact]
    public void ShouldChangeTileGoldCostDueToUniques()
    {
        var uniqueCiv = testGame.AddCiv(["[-25]% Gold cost of acquiring tiles [in all cities]"]);
        var uniqueCity = testGame.AddCity(uniqueCiv, testGame.GetTile(4, 0));

        var goldCost2TileDistance = cityExpansionManager.GetGoldCostOfTile(testGame.GetTile(2, 0));
        var goldCost2TileDistanceWithUniques = uniqueCity.Expansion.GetGoldCostOfTile(testGame.GetTile(4, -2));

        Assert.True(goldCost2TileDistanceWithUniques < goldCost2TileDistance);
    }

    [Fact]
    public void ShouldBeAbleToBuyNeighbourTile()
    {
        var tileToBuy = testGame.GetTile(2, 0);
        civ.AddGold(500);
        Assert.DoesNotContain(new HexCoord(2, 0), city.Tiles);

        cityExpansionManager.BuyTile(tileToBuy);

        Assert.Equal(8, city.Tiles.Count);
        Assert.Contains(new HexCoord(2, 0), city.Tiles);
    }

    [Fact]
    public void ShouldNotBeAbleToBuyNotNeighbourTile()
    {
        var tileToBuy = testGame.GetTile(3, 0);

        var e = Assert.ThrowsAny<Exception>(() => cityExpansionManager.BuyTile(tileToBuy));

        Assert.Equal($"{city} tried to buy {tileToBuy}, but it owns none of the neighbors", e.Message);
    }

    [Fact]
    public void ShouldNotBeAbleToBuyTileWithoutEnoughGold()
    {
        var tileToBuy = testGame.GetTile(2, 0);

        var e = Assert.ThrowsAny<Exception>(() => cityExpansionManager.BuyTile(tileToBuy));

        Assert.Contains($"{city} tried to buy {tileToBuy}, but lacks gold", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldIncreaseGoldCostTheMoreATileIsFartherFromTheCityCenter()
    {
        var nearTileCost = cityExpansionManager.GetGoldCostOfTile(testGame.GetTile(2, 0)); // two tiles distance
        var farTileCost = cityExpansionManager.GetGoldCostOfTile(testGame.GetTile(3, 0)); // three tiles distance

        Assert.True(nearTileCost < farTileCost);
    }

    [Fact]
    public void ShouldIncreaseGoldCostTheMoreTilesACityHasClaimed()
    {
        var tileBought = testGame.GetTile(-2, 0); // two tiles distance
        var tileToBuy = testGame.GetTile(2, 0); // still two tiles distance
        var tileBoughtCost = cityExpansionManager.GetGoldCostOfTile(tileBought);
        civ.AddGold(500);

        cityExpansionManager.BuyTile(tileBought);

        Assert.True(tileBoughtCost < cityExpansionManager.GetGoldCostOfTile(tileToBuy));
    }
}
