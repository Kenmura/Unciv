using Unciv.Core.Tests.Support;
using Unciv.Logic.City;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Fixtures.CityFixtures;

// T072: port of tests/src/com/unciv/logic/city/managers/CityTurnManagerWltkTest.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class CityTurnManagerWltkFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization civ;
    private readonly CityObject city;

    public CityTurnManagerWltkFixtureTests()
    {
        testGame.MakeHexagonalMap(3);
        civ = testGame.AddCiv();
        city = testGame.AddCity(civ, testGame.GetTile(HexCoord.Zero));
    }

    [Fact]
    public void ResourceDemandExpiryDuringWltkdDoesNotRewriteDemandedResource()
    {
        city.DemandedResource = "Salt";
        city.SetFlag(CityFlags.WeLoveTheKing, 5);
        city.SetFlag(CityFlags.ResourceDemand, 1);

        new CityTurnManager(city).StartTurn();

        Assert.Equal("Salt", city.DemandedResource);
        Assert.True(city.HasFlag(CityFlags.WeLoveTheKing));
        Assert.False(city.HasFlag(CityFlags.ResourceDemand));
    }

    [Fact]
    public void StartingWltkdClearsPendingResourceDemand()
    {
        // City-center Salt needs Mining researched to count as extracted
        civ.Tech.AddTechnology("Mining");
        city.GetCenterTile().SetTileResource("Salt");
        city.GetCenterTile().ResourceAmount = 1;
        civ.Cache.UpdateCivResources();

        city.DemandedResource = "Salt";
        city.SetFlag(CityFlags.ResourceDemand, 10);
        Assert.False(city.HasFlag(CityFlags.WeLoveTheKing));

        new CityTurnManager(city).StartTurn();

        Assert.True(city.HasFlag(CityFlags.WeLoveTheKing));
        Assert.False(city.HasFlag(CityFlags.ResourceDemand));
        Assert.Equal("Salt", city.DemandedResource);
    }
}
