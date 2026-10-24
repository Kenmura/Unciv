using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unit;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Map;

// T067: UnitMovement and MovementCost over a real Civilization / TileMap / MapUnit. The Kotlin fixture UnitMovementTests.kt is TestGame-based
// (cities, diplomacy, carriers, barbarian camps: T072); the cases that need only terrain, units and a civ are ported here under the same names.
// A* pathfinding (the second parameter set of the Kotlin fixture) is T093.
public class UnitMovementTests
{
    private sealed record World(Civilization Civ, FakeGameInfoState Info, TileMap Map, Ruleset Ruleset)
    {
        public TileClass TileAt(int x, int y) => Map[HexCoord.Of(x, y)];

        public MapUnitClass Unit(string name = "Warrior", string type = "Land", int x = 2, int y = 2, Civilization? civ = null, params string[] uniques)
        {
            var baseUnit = Ruleset.Units.TryGetValue(name, out var existing) ? existing : MapUnitFactory.NewBaseUnit(Ruleset, name, type, uniques: uniques);
            return RealUnit(civ ?? Civ, baseUnit, TileAt(x, y));
        }

        public void SetTerrain(int x, int y, string terrain)
        {
            var tile = TileAt(x, y);
            tile.BaseTerrain = terrain;
            tile.SetTerrainTransients();
        }
    }

    private static MapUnitClass RealUnit(Civilization civ, BaseUnit baseUnit, TileClass? tile)
    {
        var unit = new MapUnitClass { Name = baseUnit.Name, Owner = civ.CivId, Civ = civ, BaseUnit = baseUnit, Promotions = new TestUnitPromotions() };
        unit.Promotions.SetTransients(unit);
        if (tile != null) unit.CurrentTile = tile;
        unit.UpdateUniques();
        if (tile != null)
        {
            if (unit.IsCivilian()) tile.CivilianUnit = unit;
            else tile.MilitaryUnit = unit;
        }
        unit.CurrentMovement = unit.GetMaxMovement();
        return unit;
    }

    private static World NewWorld(params string[] civUniques)
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain { Name = "Grassland", Type = TerrainType.Land });
        ruleset.Terrains.Put("Plains", new Terrain { Name = "Plains", Type = TerrainType.Land, MovementCost = 2 });
        ruleset.Terrains.Put("Mountain", new Terrain { Name = "Mountain", Type = TerrainType.Land, Impassable = true });
        ruleset.Terrains.Put("Hill", new Terrain { Name = "Hill", Type = TerrainType.Land, MovementCost = 2, Uniques = ["Occurs in groups around high elevations"] });
        ruleset.Terrains.Put("Forest", new Terrain { Name = "Forest", Type = TerrainType.TerrainFeature, MovementCost = 2 });
        ruleset.Terrains.Put("Coast", new Terrain { Name = "Coast", Type = TerrainType.Water });
        ruleset.Terrains.Put("Ocean", new Terrain { Name = "Ocean", Type = TerrainType.Water });
        MapUnitFactory.AddUnitTypes(ruleset);
        var info = new FakeGameInfoState { RulesetData = ruleset };
        var map = new TileMap(25) { GameInfo = info };
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < 5; y++)
                map.TileList.Add(new TileClass { Position = HexCoord.Of(x, y), BaseTerrain = "Grassland" });
        }
        map.SetTransients(ruleset);
        info.Map = map;
        var civ = MapUnitFactory.NewCiv("Rome", info, civUniques);
        civ.Tech.UnitsCanEmbark = true;
        civ.Tech.EmbarkedUnitsCanEnterOcean = true;
        return new World(civ, info, map, ruleset);
    }

    private static void Explore(World w)
    {
        foreach (var tile in w.Map.TileList) tile.SetExplored(w.Civ, true);
    }

    // ---- canPassThrough (UnitMovementTests.kt: canPassThroughPassableTerrains, waterUnitCanNOTEnterLand, ...)

    [Fact]
    public void CanPassThroughPassableTerrains()
    {
        var w = NewWorld();
        var unit = w.Unit();
        foreach (var terrain in new[] { "Grassland", "Plains", "Mountain", "Hill" })
        {
            w.SetTerrain(0, 0, terrain);
            Assert.True(w.Ruleset.Terrains[terrain].Impassable != unit.Movement.CanPassThrough(w.TileAt(0, 0)), terrain);
        }
    }

    [Fact]
    public void WaterUnitCanNotEnterLand()
    {
        var w = NewWorld();
        var ship = w.Unit("Frigate", "Water");
        var warrior = w.Unit("Warrior", "Land", 3, 3);
        foreach (var terrain in new[] { "Grassland", "Plains", "Hill", "Coast", "Ocean" })
        {
            w.SetTerrain(0, 0, terrain);
            var tile = w.TileAt(0, 0);
            Assert.True(!tile.IsLand == ship.Movement.CanPassThrough(tile), terrain);
            Assert.True(warrior.Movement.CanPassThrough(tile), terrain);
        }
        w.SetTerrain(0, 0, "Grassland");
        Assert.Equal(UnitMovement.CannotMoveToReason.BoatCannotGoOnLand, ship.Movement.CannotPassThroughReason(w.TileAt(0, 0)));
    }

    [Fact]
    public void CanNotEnterCoastUntilProperTechIsResearched()
    {
        var w = NewWorld();
        w.Civ.Tech.UnitsCanEmbark = false;
        w.SetTerrain(0, 0, "Coast");
        var land = w.Unit("Warrior", "Land", 3, 3);
        var water = w.Unit("Frigate", "Water", 3, 4);
        Assert.False(land.Movement.CanPassThrough(w.TileAt(0, 0)));
        Assert.Equal(UnitMovement.CannotMoveToReason.CannotEmbark, land.Movement.CannotPassThroughReason(w.TileAt(0, 0)));
        Assert.True(water.Movement.CanPassThrough(w.TileAt(0, 0)));
        w.Civ.Tech.UnitsCanEmbark = true;
        Assert.True(land.Movement.CanPassThrough(w.TileAt(0, 0)));
    }

    [Fact]
    public void CanNotEnterOceanUntilProperTechIsResearched()
    {
        var w = NewWorld();
        w.Civ.Tech.EmbarkedUnitsCanEnterOcean = false;
        w.SetTerrain(0, 0, "Ocean");
        var land = w.Unit("Warrior", "Land", 3, 3);
        Assert.False(land.Movement.CanPassThrough(w.TileAt(0, 0)));
        Assert.Equal(UnitMovement.CannotMoveToReason.CannotEnterOcean, land.Movement.CannotPassThroughReason(w.TileAt(0, 0)));
        w.Civ.Tech.EmbarkedUnitsCanEnterOcean = true;
        Assert.True(land.Movement.CanPassThrough(w.TileAt(0, 0)));
    }

    [Fact]
    public void CanNotEnterOceanWithLimitations()
    {
        var w = NewWorld();
        w.SetTerrain(0, 0, "Ocean");
        var unit = w.Unit("Pilgrim", "Water", 3, 3, null, "Cannot enter ocean tiles");
        Assert.False(unit.Movement.CanPassThrough(w.TileAt(0, 0)));
        var plain = w.Unit("Frigate", "Water", 3, 4);
        Assert.True(plain.Movement.CanPassThrough(w.TileAt(0, 0)));
    }

    [Fact]
    public void CanNotPassThroughTileWithEnemyUnits()
    {
        var w = NewWorld();
        var barbarians = MapUnitFactory.NewCiv(Constants.Barbarians, w.Info);
        w.Unit("Brute", "Land", 0, 0, barbarians);
        var ours = w.Unit("Warrior", "Land", 3, 3);
        Assert.False(ours.Movement.CanPassThrough(w.TileAt(0, 0)));
        Assert.Equal(UnitMovement.CannotMoveToReason.TileIsNotEmpty, ours.Movement.CannotPassThroughReason(w.TileAt(0, 0)));
    }

    [Fact]
    public void CanNotMoveToATileWithAnOwnMilitaryUnit_ButCanSwapWhenAllowed()
    {
        var w = NewWorld();
        var first = w.Unit("Warrior", "Land", 1, 1);
        var second = w.Unit("Archer", "Land", 2, 1);
        Assert.False(first.Movement.CanMoveTo(second.CurrentTile));
        Assert.Equal(UnitMovement.CannotMoveToReason.TileIsNotEmpty, first.Movement.GetCannotMoveToReason(second.CurrentTile));
        Assert.True(first.Movement.CanMoveTo(second.CurrentTile, allowSwap: true));
        Assert.True(first.Movement.CanMoveTo(w.TileAt(3, 3)));
    }

    // ---- MovementCost

    [Fact]
    public void MovementCost_IsTheTerrainCost_AndHalvedByDoubleMovementUniques()
    {
        var w = NewWorld();
        var plain = w.Unit("Warrior", "Land", 1, 1);
        var from = plain.CurrentTile;
        w.SetTerrain(1, 2, "Grassland");
        w.SetTerrain(2, 1, "Plains");
        Assert.Equal(1f, MovementCost.GetMovementCostBetweenAdjacentTiles(plain, from, w.TileAt(1, 2)));
        Assert.Equal(2f, MovementCost.GetMovementCostBetweenAdjacentTiles(plain, from, w.TileAt(2, 1)));

        var skier = w.Unit("Skier", "Land", 3, 3, null, "Double movement in [Plains]");
        Assert.Equal(1f, MovementCost.GetMovementCostBetweenAdjacentTiles(skier, from, w.TileAt(2, 1)));
        Assert.Equal(1f, MovementCost.GetMovementCostBetweenAdjacentTiles(skier, from, w.TileAt(1, 2)));
    }

    [Fact]
    public void MovementCost_SpecialUniquesAndEmbarking()
    {
        var w = NewWorld();
        w.SetTerrain(2, 1, "Plains");
        w.SetTerrain(1, 2, "Coast");
        var from = w.TileAt(1, 1);
        var allOne = w.Unit("Hover", "Land", 3, 3, null, "All tiles cost 1 movement");
        var ignoring = w.Unit("Scout", "Land", 3, 4, null, "Ignores terrain cost");
        var cannotMove = w.Unit("Fort", "Land", 4, 4, null, "Cannot move");
        var land = w.Unit("Warrior", "Land", 4, 3);
        Assert.Equal(1f, MovementCost.GetMovementCostBetweenAdjacentTiles(allOne, from, w.TileAt(2, 1)));
        Assert.Equal(1f, MovementCost.GetMovementCostBetweenAdjacentTiles(ignoring, from, w.TileAt(2, 1)));
        Assert.Equal(100f, MovementCost.GetMovementCostBetweenAdjacentTiles(cannotMove, from, w.TileAt(2, 1)));
        // land -> water without a disembark/embark unique: the whole turn
        Assert.Equal(100f, MovementCost.GetMovementCostBetweenAdjacentTiles(land, from, w.TileAt(1, 2)));
    }

    [Fact]
    public void MovementCost_RailroadBetweenTwoRailroadTiles()
    {
        var w = NewWorld();
        var unit = w.Unit("Warrior", "Land", 1, 1);
        w.TileAt(1, 1).RoadStatus = Unciv.Logic.Map.Tiles.RoadStatus.Railroad;
        w.TileAt(2, 1).RoadStatus = Unciv.Logic.Map.Tiles.RoadStatus.Railroad;
        Assert.Equal(0.1f, MovementCost.GetMovementCostBetweenAdjacentTiles(unit, w.TileAt(1, 1), w.TileAt(2, 1)));
    }

    [Fact]
    public void MovementCostEscort_TakesTheLargerCostOfBothUnits()
    {
        var w = NewWorld();
        w.SetTerrain(2, 1, "Plains");
        var warrior = w.Unit("Warrior", "Land", 1, 1);
        var skierCivilian = RealUnit(w.Civ, MapUnitFactory.NewBaseUnit(w.Ruleset, "Settler", "Land", strength: 0, uniques: "Double movement in [Plains]"), w.TileAt(1, 1));
        Assert.Equal(1f, MovementCost.GetMovementCostBetweenAdjacentTilesEscort(skierCivilian, w.TileAt(1, 1), w.TileAt(2, 1), includeEscortUnit: false));
        warrior.StartEscorting();
        Assert.Equal(2f, MovementCost.GetMovementCostBetweenAdjacentTilesEscort(skierCivilian, w.TileAt(1, 1), w.TileAt(2, 1)));
        Assert.Equal(1f, MovementCost.GetMovementCostBetweenAdjacentTilesEscort(skierCivilian, w.TileAt(1, 1), w.TileAt(2, 1), includeEscortUnit: false));
    }

    // ---- GetDistanceToTiles / GetShortestPath / MoveToTile

    [Fact]
    public void GetDistanceToTiles_ReachesTilesWithinTheMovementOnly()
    {
        var w = NewWorld();
        Explore(w);
        var unit = w.Unit("Warrior", "Land", 2, 2);
        var paths = unit.Movement.GetDistanceToTiles();
        Assert.Same(unit.CurrentTile, paths.Keys.First()); // the own tile comes first (insertion order)
        Assert.Equal(0f, paths[unit.CurrentTile].TotalMovement);
        foreach (var neighbor in unit.CurrentTile.Neighbors)
            Assert.Equal(1f, paths[neighbor].TotalMovement);
        Assert.Contains(w.TileAt(4, 2), paths.Keys); // two steps away
        Assert.All(paths.Values, it => Assert.True(it.TotalMovement <= unit.CurrentMovement));
        Assert.DoesNotContain(w.TileAt(4, 0), paths.Keys);
    }

    [Fact]
    public void GetDistanceToTiles_OnlyTheOwnTileWithoutMovement()
    {
        var w = NewWorld();
        Explore(w);
        var unit = w.Unit("Warrior", "Land", 2, 2);
        unit.CurrentMovement = 0f;
        Assert.Single(unit.Movement.GetDistanceToTiles());
        var fort = w.Unit("Fort", "Land", 1, 1, null, "Cannot move");
        Assert.Single(fort.Movement.GetDistanceToTiles());
    }

    [Fact]
    public void GetPathToTile_ListsTheTilesInOrder_AndThrowsForUnreachable()
    {
        var w = NewWorld();
        Explore(w);
        var unit = w.Unit("Warrior", "Land", 0, 0);
        var paths = unit.Movement.GetDistanceToTiles();
        var target = paths.Keys.Last();
        var path = paths.GetPathToTile(target);
        Assert.Same(target, path[^1]);
        Assert.True(path.Count is 1 or 2);
        Assert.Throws<InvalidOperationException>(() => paths.GetPathToTile(w.TileAt(4, 4)));
    }

    [Fact]
    public void MoveToTile_SpendsTheMovementAlongThePath()
    {
        var w = NewWorld();
        Explore(w);
        var unit = w.Unit("Warrior", "Land", 0, 0);
        var target = unit.Movement.GetDistanceToTiles().Keys.First(it => it.AerialDistanceTo(unit.CurrentTile) == 2);
        unit.Movement.MoveToTile(target);
        Assert.Same(target, unit.CurrentTile);
        Assert.Same(unit, target.MilitaryUnit);
        Assert.Equal(0f, unit.CurrentMovement);
    }

    [Fact]
    public void MoveToTile_StopsAtTheLastEnterableTile()
    {
        var w = NewWorld();
        Explore(w);
        var unit = w.Unit("Warrior", "Land", 0, 0);
        var target = unit.Movement.GetDistanceToTiles().Keys.First(it => it.AerialDistanceTo(unit.CurrentTile) == 2);
        var blocker = w.Unit("Archer", "Land", target.Position.x, target.Position.y);
        unit.Movement.MoveToTile(target);
        Assert.NotSame(target, unit.CurrentTile);
        Assert.Same(blocker, target.MilitaryUnit);
    }

    [Fact]
    public void GetShortestPath_FindsMultiTurnPaths_AndEmptyWhenBlocked()
    {
        var w = NewWorld();
        Explore(w);
        var unit = w.Unit("Warrior", "Land", 0, 0);
        var path = unit.Movement.GetShortestPath(w.TileAt(4, 4));
        Assert.NotEmpty(path);
        Assert.Same(w.TileAt(4, 4), path[^1]);
        Assert.True(unit.Movement.CanReach(w.TileAt(4, 4)));
        Assert.Equal([unit.CurrentTile], unit.Movement.GetShortestPath(unit.CurrentTile));

        foreach (var neighbor in w.TileAt(4, 4).Neighbors) w.SetTerrain(neighbor.Position.x, neighbor.Position.y, "Mountain");
        var blocked = w.Unit("Warrior", "Land", 0, 1);
        Assert.Empty(blocked.Movement.GetShortestPath(w.TileAt(4, 4)));
        Assert.False(blocked.Movement.CanReach(w.TileAt(4, 4)));
    }

    [Fact]
    public void GetTileToMoveToThisTurn_HeadsTowardsADistantDestination()
    {
        var w = NewWorld();
        Explore(w);
        var unit = w.Unit("Warrior", "Land", 0, 0);
        var step = unit.Movement.GetTileToMoveToThisTurn(w.TileAt(4, 4));
        Assert.Contains(step, unit.Movement.GetDistanceToTiles().Keys);
        Assert.Same(unit.CurrentTile, unit.Movement.GetTileToMoveToThisTurn(unit.CurrentTile));
        var reached = unit.Movement.HeadTowards(w.TileAt(4, 4));
        Assert.Same(reached, unit.CurrentTile);
        Assert.NotSame(w.TileAt(0, 0), reached);
    }

    [Fact]
    public void GetTileToMoveToThisTurn_ThrowsWhenUnreachable()
    {
        var w = NewWorld();
        Explore(w);
        foreach (var neighbor in w.TileAt(4, 4).Neighbors) w.SetTerrain(neighbor.Position.x, neighbor.Position.y, "Mountain");
        var unit = w.Unit("Warrior", "Land", 0, 0);
        Assert.Throws<UnitMovement.UnreachableDestinationException>(() => unit.Movement.GetTileToMoveToThisTurn(w.TileAt(4, 4)));
    }

    // ---- teleportToClosestMoveableTile

    [Fact]
    public void TeleportToClosestMoveableTile_LeavesAUnitThatCanStay()
    {
        var w = NewWorld();
        var unit = w.Unit("Warrior", "Land", 2, 2);
        unit.Movement.TeleportToClosestMoveableTile();
        Assert.Same(w.TileAt(2, 2), unit.CurrentTile);
        Assert.False(unit.IsDestroyed);
    }

    [Fact]
    public void TeleportToClosestMoveableTile_MovesAWaterUnitFromLandToTheClosestWater_AndDestroysItWhenThereIsNone()
    {
        var w = NewWorld();
        Explore(w);
        var ship = w.Unit("Frigate", "Water", 2, 2);
        w.SetTerrain(2, 2, "Grassland"); // stranded
        w.SetTerrain(3, 2, "Coast");
        w.SetTerrain(4, 4, "Coast"); // not connected: farther away
        ship.Movement.TeleportToClosestMoveableTile();
        Assert.False(ship.IsDestroyed);
        Assert.Same(w.TileAt(3, 2), ship.CurrentTile);
        Assert.Null(w.TileAt(2, 2).MilitaryUnit);

        var doomed = w.Unit("Galley", "Water", 0, 0);
        w.SetTerrain(0, 0, "Grassland");
        w.SetTerrain(3, 2, "Grassland");
        w.SetTerrain(4, 4, "Grassland");
        doomed.Movement.TeleportToClosestMoveableTile();
        Assert.True(doomed.IsDestroyed);
    }

    // ---- swap moves

    [Fact]
    public void CanUnitSwapTo_AndSwapMoveToTile_ExchangeTwoOwnUnits()
    {
        var w = NewWorld();
        Explore(w);
        var first = w.Unit("Warrior", "Land", 1, 1);
        var second = w.Unit("Archer", "Land", 2, 1);
        Assert.True(first.Movement.CanUnitSwapTo(second.CurrentTile));
        Assert.Contains(second.CurrentTile, first.Movement.GetUnitSwappableTiles());
        first.Movement.SwapMoveToTile(second.CurrentTile);
        Assert.Same(w.TileAt(2, 1), first.CurrentTile);
        Assert.Same(w.TileAt(1, 1), second.CurrentTile);
        Assert.False(first.Movement.CanUnitSwapTo(first.CurrentTile));
    }

    // ---- caches

    [Fact]
    public void PathfindingCache_IsOnlyUsedForNonHumanCivs_AndInvalidatedByMovement()
    {
        var w = NewWorld();
        var unit = w.Unit("Warrior", "Land", 0, 0);
        var cache = new PathfindingCache(unit);
        var target = w.TileAt(3, 3);
        w.Civ.PlayerType = PlayerType.AI;
        cache.Clear();
        cache.SetShortestPathCache(target, [target]);
        Assert.Equal([target], cache.GetShortestPathCache(target));
        Assert.Empty(cache.GetShortestPathCache(w.TileAt(3, 2)));
        unit.CurrentMovement = 1f;
        Assert.Empty(cache.GetShortestPathCache(target)); // movement changed
        w.Civ.PlayerType = PlayerType.Human;
        cache.Clear();
        cache.SetShortestPathCache(target, [target]);
        Assert.Empty(cache.GetShortestPathCache(target));
    }

    [Fact]
    public void ClearPathfindingCache_WithoutAStarDoesNotNeedPathingMaps()
    {
        var w = NewWorld();
        var unit = w.Unit("Warrior", "Land", 0, 0);
        unit.Movement.ClearPathfindingCache();
        Assert.Null(unit.Movement.GetRoadPath(w.TileAt(0, 0))?.FirstOrDefault(it => it == w.TileAt(4, 4)));
    }
}
