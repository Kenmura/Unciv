using Unciv.Core.Tests.Support;
using Unciv.Logic.Map.PathingMap;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Map;

// T093: City.getLandAttackPath / getAmphibiousAttackPath / getRoadPath / getRoadPathToAny / clearCaches and the AStarPathing seam implementation
// (no Kotlin fixtures exist for these; the expectations are the structural ones: the path ends at the destination, caches are reused and dropped).
[Collection("RulesetCache")]
public sealed class CityPathingTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject civ;
    private readonly CivilizationObject enemy;
    private readonly CityObject home;
    private readonly CityObject sameCivCity;
    private readonly CityObject enemyCity;

    public CityPathingTests()
    {
        testGame.MakeHexagonalMap(8);
        civ = testGame.AddCiv();
        enemy = testGame.AddCiv();
        civ.Tech.AddTechnology("The Wheel", false);
        foreach (var tile in testGame.TileMap.TileList)
            tile.SetExplored(civ, true);
        home = testGame.AddCity(civ, testGame.GetTile(0, 0));
        sameCivCity = testGame.AddCity(civ, testGame.GetTile(4, 0));
        enemyCity = testGame.AddCity(enemy, testGame.GetTile(-4, 0));
    }

    [Fact]
    public void AttackPathsEndAtTheDestinationCenterAndAreCachedPerTargetCiv()
    {
        var land = home.GetLandAttackPath(enemyCity)!;
        var amphibious = home.GetAmphibiousAttackPath(enemyCity)!;

        Assert.Same(enemyCity.GetCenterTile(), land[^1]);
        Assert.Same(enemyCity.GetCenterTile(), amphibious[^1]);
        Assert.Equal(land, home.GetLandAttackPath(enemyCity)); // second call reads the cached pathing map
    }

    [Fact]
    public void RoadPathIsSymmetricAndReachesBothCities()
    {
        var forward = home.GetRoadPath(sameCivCity)!;
        var backward = sameCivCity.GetRoadPath(home)!;

        Assert.Same(sameCivCity.GetCenterTile(), forward[^1]);
        Assert.Equal(forward, backward); // the city with the smaller id owns the pathing map, the other delegates
    }

    [Fact]
    public void RoadPathToAnyFindsTheNearestDestinationAndNullWhenNone()
    {
        var found = home.GetRoadPathToAny(new HashSet<Unciv.Logic.Map.Tiles.Tile> { sameCivCity.GetCenterTile() }, 2)!;
        Assert.Same(sameCivCity.GetCenterTile(), found[^1]);

        var water = testGame.GetTile(7, 7);
        testGame.SetTileTerrain(water.Position, "Ocean");
        Assert.Null(home.GetRoadPathToAny(new HashSet<Unciv.Logic.Map.Tiles.Tile> { water }, 2));
    }

    [Fact]
    public void ClearCachesDropsTheMapsAndTheyAreRebuilt()
    {
        var before = home.GetRoadPath(sameCivCity);
        home.GetLandAttackPath(enemyCity);
        home.GetAmphibiousAttackPath(enemyCity);

        home.ClearCaches();
        new CityObject().ClearCaches(); // nothing built yet: nothing to clear

        Assert.Equal(before, home.GetRoadPath(sameCivCity));
    }

    [Fact]
    public void UseAStarPathfindingInstallsTheAStarSeamsAndRemovesThem()
    {
        var clientState = testGame.GameInfo.EngineOptions;
        Assert.False(clientState.UseAStarPathfinding);

        clientState.UseAStarPathfinding = true;

        Assert.True(clientState.UseAStarPathfinding);
        Assert.Same(AStarPathing.Instance, clientState.UnitPathing);
        Assert.Same(AStarPathing.Instance, clientState.AttackPathing);
        Assert.Same(AStarPathing.Instance, clientState.RoadPathing);
        Assert.NotNull(clientState.AttackPathing!.GetLandAttackPath(home, enemyCity, 17));
        Assert.NotNull(clientState.AttackPathing.GetAmphibiousAttackPath(home, enemyCity, 17));
        // the BFS is asked first: over an already searched cache it would find nothing (see PathingMapTests)
        Assert.NotNull(clientState.RoadPathing!.GetRoadPathToAny(home, new HashSet<Unciv.Logic.Map.Tiles.Tile> { sameCivCity.GetCenterTile() }, 2));
        Assert.NotNull(clientState.RoadPathing.GetRoadPath(home, sameCivCity));
        var unit = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 1));
        Assert.NotNull(clientState.UnitPathing!.CreateUnitPathingMap(unit));
        Assert.NotNull(clientState.UnitPathing.CreateRoadPathingMap(civ, unit.GetTile()));

        clientState.UseAStarPathfinding = false;

        Assert.False(clientState.UseAStarPathfinding);
        Assert.Null(clientState.UnitPathing);
    }

    // City.MatchesSingleFilter branches that no other test reaches (the City.cs coverage baseline predates the T128/T159 test moves).
    [Fact]
    public void CityFiltersRelativeToAViewingCiv()
    {
        Assert.False(enemyCity.MatchesFilter("in enemy cities", civ));
        Assert.True(enemyCity.MatchesFilter("in non-enemy foreign cities", civ));
        civ.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
        civ.GetDiplomacyManager(enemy)!.DeclareWar();

        Assert.True(enemyCity.MatchesFilter("in enemy cities", civ));
        Assert.False(enemyCity.MatchesFilter("in non-enemy foreign cities", civ));
        Assert.False(home.MatchesFilter("in non-enemy foreign cities", civ));
        Assert.False(home.MatchesFilter("in non-enemy foreign cities", null));
        Assert.Equal(home.IsConnectedToCapital(), home.MatchesFilter("in all cities connected to capital", civ));
    }
}
