using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map.Tiles;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T142: port of tests/src/com/unciv/logic/civilization/CityMovingTests.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class CityMovingFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization civInfo;
    private readonly Civilization enemy;

    public CityMovingFixtureTests()
    {
        testGame.MakeHexagonalMap(5); // enough space for lots of cities
        civInfo = testGame.AddCiv();
        enemy = testGame.AddCiv();

        // Required for enemy to utilize roads
        foreach (var tech in testGame.Ruleset.Technologies.Keys)
            enemy.Tech.AddTechnology(tech);
        foreach (var tech in testGame.Ruleset.Technologies.Keys)
            civInfo.Tech.AddTechnology(tech);

        civInfo.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
        civInfo.GetDiplomacyManager(enemy)!.DeclareWar();
    }

    private void BuildRoadForEnemy()
    {
        foreach (var i in new[] { -1, 0, 1 })
            testGame.TileMap[-2, i].RoadStatus = RoadStatus.Road;
        enemy.Cache.UpdateCitiesConnectedToCapital();
    }

    [Fact]
    public void MoveOtherCityToUs()
    {
        testGame.AddCity(civInfo, testGame.TileMap[2, 2]);
        testGame.AddCity(enemy, testGame.TileMap[-2, -2]);
        var theirOtherCity = testGame.AddCity(enemy, testGame.TileMap[-2, 2]);

        theirOtherCity.MoveToCiv(civInfo);
        Assert.False(theirOtherCity.IsCapital());
        Assert.Equal(civInfo, theirOtherCity.Civ);
    }

    [Fact]
    public void MoveCapitalToUs()
    {
        testGame.AddCity(civInfo, testGame.TileMap[2, 2]);
        var theirCapital = testGame.AddCity(enemy, testGame.TileMap[-2, -2]);
        var theirOtherCity = testGame.AddCity(enemy, testGame.TileMap[-2, 2]);

        BuildRoadForEnemy();
        Assert.True(theirOtherCity.IsConnectedToCapital());

        theirCapital.MoveToCiv(civInfo);
        Assert.True(theirOtherCity.IsCapital());
        Assert.False(theirCapital.IsCapital());
        Assert.Equal(civInfo, theirCapital.Civ);
    }

    [Fact]
    public void MoveCapitalToUsWhenWeHaveNoCities()
    {
        var theirCapital = testGame.AddCity(enemy, testGame.TileMap[-2, -2]);
        var theirOtherCity = testGame.AddCity(enemy, testGame.TileMap[-2, 2]);

        BuildRoadForEnemy();
        Assert.True(theirOtherCity.IsConnectedToCapital());

        theirCapital.MoveToCiv(civInfo);
        Assert.True(theirOtherCity.IsCapital());
        Assert.Equal(enemy, theirOtherCity.Civ);
        Assert.True(theirCapital.IsCapital());
        Assert.Equal(civInfo, theirCapital.Civ);
    }

    [Fact]
    public void MoveNonCapitalToUsWhenWeHaveNoCities()
    {
        var theirCapital = testGame.AddCity(enemy, testGame.TileMap[-2, -2]);
        var theirOtherCity = testGame.AddCity(enemy, testGame.TileMap[-2, 2]);

        BuildRoadForEnemy();
        Assert.True(theirOtherCity.IsConnectedToCapital());

        theirOtherCity.MoveToCiv(civInfo);
        Assert.True(theirOtherCity.IsCapital());
        Assert.True(theirCapital.IsCapital());
        Assert.Equal(civInfo, theirOtherCity.Civ);
    }

    [Fact]
    public void MoveTheirOnlyCityToUsWhenWeHaveNoCities()
    {
        var theirCapital = testGame.AddCity(enemy, testGame.TileMap[-2, -2]);
        enemy.Cache.UpdateCitiesConnectedToCapital();

        theirCapital.MoveToCiv(civInfo);
        Assert.True(theirCapital.IsCapital());
        Assert.Equal(civInfo, theirCapital.Civ);
    }

    [Fact]
    public void MoveTheirCityToUsWhenTheyHaveResources()
    {
        var theirCapital = testGame.AddCity(enemy, testGame.TileMap[2, 0]);

        theirCapital.GetCenterTile().SetTileResource("Salt");
        theirCapital.GetCenterTile().ResourceAmount = 1;

        var resourceTile = testGame.TileMap[0, 1];
        resourceTile.SetTileResource("Iron");
        resourceTile.ResourceAmount = 3;
        resourceTile.SetImprovement("Mine");
        theirCapital.Expansion.TakeOwnership(resourceTile);

        theirCapital.MoveToCiv(civInfo);
        Assert.True(theirCapital.IsCapital());
        Assert.Equal(civInfo, theirCapital.Civ);
        Assert.True(civInfo.HasResource("Salt"));
        Assert.Equal(3, civInfo.GetResourceAmount("Iron"));
    }
}
