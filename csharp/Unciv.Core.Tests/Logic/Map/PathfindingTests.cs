using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Map;

// T040: BFS, AStar, MapPathing, CarrierSlotMatcher, MapVisualization. The Kotlin PathfindingTests/pathingmap fixtures all run on
// TestGame with real units and civilizations (UnitMovement, T067; TestGame, T035/T072), so they are ported there. These tests are
// hand-derived expectations for the classes in this ticket, against a hand-built hex map and fakes for the seams.
// The AStar queue order is checked against java.util.PriorityQueue output (generated with the JDK, see the arrays below).
public class PathfindingTests
{
    private static Ruleset PathRuleset()
    {
        var ruleset = new Ruleset();
        void Add(string name, TerrainType type, bool impassable = false) =>
            ruleset.Terrains.Put(name, new Terrain { Name = name, Type = type, Impassable = impassable });
        Add("Grassland", TerrainType.Land);
        Add("Mountain", TerrainType.Land, impassable: true);
        Add("Ocean", TerrainType.Water);
        ruleset.TileImprovements.Put("Road", new TileImprovement { Name = "Road", TechRequired = "The Wheel", Uniques = ["Can be built outside your borders"] });
        ruleset.TileImprovements.Put("Railroad", new TileImprovement { Name = "Railroad", TechRequired = "Railroad", Uniques = ["Can be built outside your borders"] });
        return ruleset;
    }

    private static TileMap HexMap(int radius = 5)
    {
        var map = new TileMap(radius, PathRuleset()) { MapParameters = { MapSize = new MapSize(radius) } };
        foreach (var tile in map.Values)
        {
            tile.BaseTerrain = "Grassland";
            tile.SetTerrainTransients();
        }
        return map;
    }

    private static void SetTerrain(TileClass tile, string baseTerrain)
    {
        tile.BaseTerrain = baseTerrain;
        tile.SetTerrainTransients();
    }

    private static string Positions(IEnumerable<TileClass> tiles) => string.Join(" ", tiles.Select(it => $"{it.Position.x},{it.Position.y}"));

    // ---- BFS

    [Fact]
    public void Bfs_ReachesEveryTileOfTheMap()
    {
        var map = HexMap(3);
        var bfs = new BFS(map[HexCoord.Zero], _ => true);
        bfs.StepToEnd();
        Assert.True(bfs.HasEnded());
        Assert.Equal(map.Values.Count(), bfs.Size());
        Assert.Equal(37, bfs.Size());
        Assert.Equal(37, bfs.GetReachedTiles().Count);
    }

    [Fact]
    public void Bfs_PathGoesFromDestinationBackToStart()
    {
        var map = HexMap(4);
        var start = map[HexCoord.Zero];
        var destination = map[HexCoord.Of(3, 0)];
        var bfs = new BFS(start, _ => true).StepUntilDestination(destination);
        var path = bfs.GetPathTo(destination).ToList();
        Assert.Equal(4, path.Count);
        Assert.Same(destination, path[0]);
        Assert.Same(start, path[^1]);
        for (int i = 0; i < path.Count - 1; i++)
            Assert.Contains(path[i + 1], path[i].Neighbors);
    }

    [Fact]
    public void Bfs_StepUntilDestination_StopsEarly()
    {
        var map = HexMap(4);
        var bfs = new BFS(map[HexCoord.Zero], _ => true).StepUntilDestination(map[HexCoord.Of(1, 0)]);
        Assert.False(bfs.HasEnded());
        Assert.True(bfs.HasReachedTile(map[HexCoord.Of(1, 0)]));
        Assert.False(bfs.HasReachedTile(map[HexCoord.Of(3, 0)]));
    }

    [Fact]
    public void Bfs_PredicateBlocksTiles_AndUnreachedDestinationHasEmptyPath()
    {
        var map = HexMap(4);
        var wall = map.Values.Where(it => it.Position.x == 2).ToHashSet();
        var bfs = new BFS(map[HexCoord.Zero], tile => !wall.Contains(tile));
        bfs.StepToEnd();
        Assert.All(wall, tile => Assert.False(bfs.HasReachedTile(tile)));
        var beyond = map[HexCoord.Of(3, 0)];
        Assert.False(bfs.HasReachedTile(beyond));
        Assert.Empty(bfs.GetPathTo(beyond));
    }

    [Fact]
    public void Bfs_PathToStart_IsJustStart()
    {
        var map = HexMap(2);
        var start = map[HexCoord.Zero];
        var bfs = new BFS(start, _ => true);
        Assert.Equal([start], bfs.GetPathTo(start));
    }

    [Fact]
    public void Bfs_MaxSize_StopsTheSearchAndClearsTheQueue()
    {
        var map = HexMap(4);
        var bfs = new BFS(map[HexCoord.Zero], _ => true) { MaxSize = 7 };
        Assert.NotNull(bfs.NextStep()); // reaches the 6 neighbours, size 7
        Assert.Equal(7, bfs.Size());
        Assert.Null(bfs.NextStep()); // size >= MaxSize: clears
        Assert.True(bfs.HasEnded());
        Assert.Equal(7, bfs.Size());
    }

    [Fact]
    public void Bfs_NextStep_ReturnsNullWhenNothingLeft()
    {
        var map = HexMap(1);
        var bfs = new BFS(map[HexCoord.Zero], _ => false);
        Assert.Same(map[HexCoord.Zero], bfs.NextStep());
        Assert.Null(bfs.NextStep());
    }

    // ---- AStar

    [Fact]
    public void AStar_Dijkstra_FindsCheapestPathAroundExpensiveTiles()
    {
        var map = HexMap(4);
        var expensive = new HashSet<TileClass> { map[HexCoord.Of(1, 0)], map[HexCoord.Of(2, 0)] };
        var start = map[HexCoord.Zero];
        var destination = map[HexCoord.Of(3, 0)];
        var astar = new AStar(start, _ => true, (_, to) => expensive.Contains(to) ? 10f : 1f, (_, _) => 0f)
            .StepUntilDestination(destination);
        var path = astar.GetPathTo(destination).Reverse().ToList();
        Assert.Same(start, path[0]);
        Assert.Same(destination, path[^1]);
        Assert.DoesNotContain(path, expensive.Contains);
        Assert.Equal(5, path.Count);
    }

    [Fact]
    public void AStar_NoPath_WhenBlocked()
    {
        var map = HexMap(3);
        var astar = new AStar(map[HexCoord.Zero], tile => tile.Position.x != 1, (_, _) => 1f, (_, _) => 0f);
        astar.StepToEnd();
        Assert.True(astar.HasEnded());
        Assert.Empty(astar.GetPathTo(map[HexCoord.Of(2, 0)]));
        Assert.False(astar.HasReachedTile(map[HexCoord.Of(2, 0)]));
    }

    [Fact]
    public void AStar_CostIsCachedPerTilePair()
    {
        var map = HexMap(3);
        var calls = new Dictionary<(TileClass, TileClass), int>();
        var astar = new AStar(map[HexCoord.Zero], _ => true, (from, to) =>
        {
            calls[(from, to)] = calls.GetValueOrDefault((from, to)) + 1;
            return 1f;
        }, (_, _) => 0f);
        astar.StepToEnd();
        Assert.All(calls.Values, count => Assert.Equal(1, count));
        Assert.Equal(37, astar.Size());
    }

    [Fact]
    public void AStar_MaxSize_ClearsTheQueue()
    {
        var map = HexMap(4);
        var astar = new AStar(map[HexCoord.Zero], _ => true, (_, _) => 1f, (_, _) => 0f) { MaxSize = 1 };
        Assert.Equal(1, astar.Size());
        astar.NextStep();
        Assert.True(astar.HasEnded());
        Assert.Equal(1, astar.Size());
    }

    [Fact]
    public void AStar_PathToStart_IsJustStart()
    {
        var map = HexMap(2);
        var start = map[HexCoord.Zero];
        Assert.Equal([start], new AStar(start, _ => true, (_, _) => 1f, (_, _) => 0f).GetPathTo(start));
    }

    [Fact]
    public void AStar_TiedPriorities_UseJavaPriorityQueueOrder()
    {
        // Expected polling orders (item index) from java.util.PriorityQueue<float[]>(27, Float.compare on [0]) for the same adds.
        var map = HexMap(3);
        var tiles = map.Values.ToList();
        (int[] priorities, int[] expected)[] cases =
        [
            ([1, 4, 0, 4, 0, 4, 3, 4, 0, 4, 0, 2], [2, 4, 8, 10, 0, 11, 6, 3, 1, 5, 7, 9]),
            ([3, 1, 0, 2, 1, 3, 2, 4, 2, 4, 3, 1, 3, 4, 4, 0, 1], [2, 15, 4, 16, 1, 11, 8, 3, 6, 0, 10, 12, 5, 14, 13, 9, 7]),
            ([1, 3, 3, 4, 1, 0, 2, 3, 1, 1, 4, 0, 0, 3, 2, 3, 0, 2, 4, 1, 2, 2],
                [5, 16, 11, 12, 4, 8, 9, 19, 0, 17, 21, 14, 20, 6, 7, 1, 15, 13, 2, 3, 10, 18]),
        ];
        foreach (var (priorities, expected) in cases)
        {
            var queue = new AStar.JavaPriorityQueue();
            for (int i = 0; i < priorities.Length; i++) queue.Add(new TilePriority(tiles[i], priorities[i]));
            var polled = new List<int>();
            while (queue.Poll() is { } entry) polled.Add(tiles.IndexOf(entry.Tile));
            Assert.Equal(expected, polled);
        }
    }

    [Fact]
    public void AStar_InterleavedAddAndPoll_UseJavaPriorityQueueOrder()
    {
        // Operation log from java.util.PriorityQueue: A<priority>:<id> = add, P<id> = poll returned id.
        const string log = "A1:0 A3:1 A1:2 P0 P2 P1 A3:3 P3 A2:4 P4 A2:5 A3:6 A0:7 P7 A0:8 P8 P5 A3:9 A1:10 P10 P6 A1:11 A3:12 P11 A3:13 A2:14 A0:15 A1:16 P15 P16 A1:17 P17 P14 P9 A0:18 A1:19 P18 A1:20 A3:21 A3:22";
        var tiles = HexMap(3).Values.ToList();
        var queue = new AStar.JavaPriorityQueue();
        foreach (var op in log.Split(' '))
        {
            if (op[0] == 'A')
            {
                var parts = op[1..].Split(':');
                queue.Add(new TilePriority(tiles[int.Parse(parts[1])], int.Parse(parts[0])));
            }
            else
            {
                Assert.Equal(int.Parse(op[1..]), tiles.IndexOf(queue.Poll()!.Value.Tile));
            }
        }
    }

    [Fact]
    public void AStar_QueueUsesJavaFloatOrder_NegativeZeroBeforeZeroAndNaNLast()
    {
        var tiles = HexMap(1).Values.ToList();
        var queue = new AStar.JavaPriorityQueue();
        queue.Add(new TilePriority(tiles[0], float.NaN));
        queue.Add(new TilePriority(tiles[1], 0f));
        queue.Add(new TilePriority(tiles[2], float.PositiveInfinity));
        queue.Add(new TilePriority(tiles[3], -0f));
        queue.Add(new TilePriority(tiles[4], float.NegativeInfinity));
        var order = new List<int>();
        while (queue.Poll() is { } entry) order.Add(tiles.IndexOf(entry.Tile));
        Assert.Equal([4, 3, 1, 2, 0], order);
    }

    [Fact]
    public void AStar_EqualCostPaths_AreDeterministic()
    {
        string Run()
        {
            var map = HexMap(4);
            var destination = map[HexCoord.Of(2, 3)];
            var astar = new AStar(map[HexCoord.Zero], _ => true, (_, _) => 1f, (from, _) => from.AerialDistanceTo(destination))
                .StepUntilDestination(destination);
            return Positions(astar.GetPathTo(destination));
        }
        var first = Run();
        Assert.Equal(first, Run());
        Assert.Equal(4, first.Split(' ').Length); // aerial distance 3 -> 4 tiles including both ends
    }

    // ---- MapPathing

    private static CivilizationClass ExploredCiv(TileMap map)
    {
        var civ = MapUnitFactory.NewCiv("Rome");
        civ.Tech.TechsResearched.Add("The Wheel");
        civ.Tech.TechsResearched.Add("Railroad");
        foreach (var tile in map.Values)
            tile.SetExplored(civ, true);
        return civ;
    }

    [Fact]
    public void RoadPreferredMovementCost_HalfOnRoadAndRailroad_OneOtherwise()
    {
        var map = HexMap(2);
        var civ = MapUnitFactory.NewCiv("Rome");
        var from = map[HexCoord.Zero];
        var plain = map[HexCoord.Of(1, 0)];
        var road = map[HexCoord.Of(0, 1)];
        var rail = map[HexCoord.Of(1, 1)];
        road.RoadStatus = RoadStatus.Road;
        rail.RoadStatus = RoadStatus.Railroad;
        Assert.Equal(1f, MapPathing.RoadPreferredMovementCost(civ, from, plain));
        Assert.Equal(.5f, MapPathing.RoadPreferredMovementCost(civ, from, road));
        Assert.Equal(.5f, MapPathing.RoadPreferredMovementCost(civ, from, rail));
        road.RoadIsPillaged = true; // mustBeUnpillaged is false: still a road
        Assert.Equal(.5f, MapPathing.RoadPreferredMovementCost(civ, from, road));
    }

    [Fact]
    public void RoadPreferredMovementCost_ForestsAreRoadsForSomeCivs()
    {
        var map = HexMap(2);
        var info = new FakeGameInfoState { Map = map };
        var civ = MapUnitFactory.NewCiv("Rome", info);
        civ.Nation.ForestsAndJunglesAreRoads = true;
        var forest = map[HexCoord.Of(1, 0)];
        forest.SetTerrainFeatures(["Forest"]);
        map.GameInfo = info;
        forest.SetOwningCity(MapUnitFactory.NewCity(civ, "Rome", HexCoord.Zero)); // forests only count as roads in friendly territory
        Assert.Equal(.5f, MapPathing.RoadPreferredMovementCost(civ, map[HexCoord.Zero], forest));
        civ.Nation.ForestsAndJunglesAreRoads = false;
        Assert.Equal(1f, MapPathing.RoadPreferredMovementCost(civ, map[HexCoord.Zero], forest));
    }

    [Fact]
    public void IsValidRoadPathTile_RejectsWaterImpassableUnexploredAndUnbuildable()
    {
        var map = HexMap(2);
        var civ = ExploredCiv(map);
        var water = map[HexCoord.Of(1, 0)];
        var mountain = map[HexCoord.Of(0, 1)];
        var unexplored = map[HexCoord.Of(1, 1)];
        var plain = map[HexCoord.Of(-1, 0)];
        SetTerrain(water, "Ocean");
        SetTerrain(mountain, "Mountain");
        unexplored.SetExplored(civ, false);
        Assert.False(MapPathing.IsValidRoadPathTile(civ, water));
        Assert.False(MapPathing.IsValidRoadPathTile(civ, mountain));
        Assert.False(MapPathing.IsValidRoadPathTile(civ, unexplored));
        Assert.True(MapPathing.IsValidRoadPathTile(civ, plain));
        civ.Tech.TechsResearched.Clear(); // civ cannot build roads without the tech
        Assert.False(MapPathing.IsValidRoadPathTile(civ, plain));
        plain.RoadStatus = RoadStatus.Road; // existing road needs no buildability
        Assert.True(MapPathing.IsValidRoadPathTile(civ, plain));
        plain.RoadStatus = RoadStatus.Railroad;
        Assert.True(MapPathing.IsValidRoadPathTile(civ, plain));
    }

    [Fact]
    public void IsValidRoadPathTile_WithoutRoadImprovementsInRuleset_NeedsExistingRoad()
    {
        var ruleset = PathRuleset();
        ruleset.TileImprovements.Clear();
        var map = new TileMap(2, ruleset) { MapParameters = { MapSize = new MapSize(2) } };
        foreach (var tile in map.Values) { tile.BaseTerrain = "Grassland"; tile.SetTerrainTransients(); }
        var civ = ExploredCiv(map);
        var tile0 = map[HexCoord.Zero];
        Assert.False(MapPathing.IsValidRoadPathTile(civ, tile0));
        tile0.RoadStatus = RoadStatus.Road;
        Assert.True(MapPathing.IsValidRoadPathTile(civ, tile0));
    }

    [Fact]
    public void GetRoadPath_IncludesBothEndpoints_AndIsContiguous()
    {
        var map = HexMap(4);
        var civ = ExploredCiv(map);
        var start = map[HexCoord.Zero];
        var end = map[HexCoord.Of(3, 0)];
        var path = MapPathing.GetRoadPath(civ, start, end)!;
        Assert.Same(start, path[0]);
        Assert.Same(end, path[^1]);
        Assert.Equal(start.AerialDistanceTo(end) + 1, path.Count);
        for (int i = 0; i < path.Count - 1; i++)
            Assert.Contains(path[i + 1], path[i].Neighbors);
    }

    [Fact]
    public void GetRoadPath_PrefersExistingRoadOverShorterPath()
    {
        var map = HexMap(4);
        var civ = ExploredCiv(map);
        var start = map[HexCoord.Zero];
        var end = map[HexCoord.Of(2, 0)];
        // Entering a road tile costs .5, any other tile 1. The direct route enters 2 plain tiles (2.0); the only 3-step route
        // over roads costs 1.5 and must win although it is longer.
        var direct = start.Neighbors.Where(it => it.Neighbors.Contains(end)).ToList();
        Assert.NotEmpty(direct);
        var viaTiles = new List<TileClass>();
        var a = start.Neighbors.First(it => !direct.Contains(it) && it.AerialDistanceTo(end) == 2);
        var b = a.Neighbors.First(it => it.AerialDistanceTo(end) == 1 && !direct.Contains(it));
        viaTiles.AddRange([a, b, end]);
        foreach (var tile in viaTiles) tile.RoadStatus = RoadStatus.Road;
        var path = MapPathing.GetRoadPath(civ, start, end)!;
        Assert.Equal([start, a, b, end], path);
    }

    [Fact]
    public void GetRoadPath_ToStart_IsJustStart_AndNullWhenUnreachable()
    {
        var map = HexMap(3);
        var civ = ExploredCiv(map);
        var start = map[HexCoord.Zero];
        Assert.Equal([start], MapPathing.GetRoadPath(civ, start, start));
        foreach (var tile in map.Values.Where(it => it.Position.x == 1)) SetTerrain(tile, "Ocean");
        Assert.Null(MapPathing.GetRoadPath(civ, start, map[HexCoord.Of(2, 0)]));
    }

    [Fact]
    public void GetConnection_DefaultsAreUnitCostAndAerialDistance()
    {
        var map = HexMap(4);
        var civ = MapUnitFactory.NewCiv("Rome");
        var start = map[HexCoord.Zero];
        var end = map[HexCoord.Of(3, 3)];
        var path = MapPathing.GetConnection(civ, start, end, (_, _) => true)!;
        Assert.Equal(start.AerialDistanceTo(end) + 1, path.Count);
        Assert.Same(start, path[0]);
        Assert.Same(end, path[^1]);
    }

    // ---- CarrierSlotMatcher

    private static int Capacity(CarrierSlotMatcher.SlotRule[] rules, string[] carried, string newUnit)
    {
        var civ = MapUnitFactory.NewCiv("Rome");
        var carriedUnits = carried.Select(it => MapUnitFactory.NewUnit(civ, it)).ToList();
        return CarrierSlotMatcher.AvailableCapacity(rules, carriedUnits, MapUnitFactory.NewUnit(civ, newUnit));
    }

    [Fact]
    public void Carrier_NoRules_NoCapacity() => Assert.Equal(0, Capacity([], [], "Fighter"));

    [Fact]
    public void Carrier_EmptyCarrier_CountsAllMatchingSlots()
    {
        CarrierSlotMatcher.SlotRule[] rules = [new("Fighter", 1), new("all", 2)];
        Assert.Equal(3, Capacity(rules, [], "Fighter"));
        Assert.Equal(2, Capacity(rules, [], "Bomber"));
    }

    [Fact]
    public void Carrier_ReassignsOccupantsToKeepCapacityOptimal()
    {
        CarrierSlotMatcher.SlotRule[] rules = [new("all", 1), new("Fighter", 1)];
        // The loaded Fighter must sit in the Fighter slot (reassigned from "all") so the Bomber still fits.
        Assert.Equal(1, Capacity(rules, ["Fighter"], "Bomber"));
        Assert.Equal(1, Capacity(rules, ["Bomber"], "Fighter"));
    }

    [Fact]
    public void Carrier_Full_ReportsZeroOrNegativeOverAllocation()
    {
        CarrierSlotMatcher.SlotRule[] rules = [new("Fighter", 1)];
        Assert.Equal(0, Capacity(rules, ["Fighter"], "Fighter"));
        Assert.Equal(0, Capacity(rules, [], "Bomber"));
        Assert.Equal(-1, Capacity(rules, ["Fighter", "Fighter"], "Fighter"));
        Assert.Equal(-2, Capacity(rules, ["Fighter", "Fighter", "Fighter"], "Fighter"));
    }

    [Fact]
    public void Carrier_ZeroCapacityRule_HasNoSlots()
    {
        CarrierSlotMatcher.SlotRule[] rules = [new("Fighter", 0)];
        Assert.Equal(0, Capacity(rules, [], "Fighter"));
    }

    // ---- MapVisualization

    private sealed class VisGameInfo(TileMap map) : FakeGameInfo
    {
        public override TileMap TileMap => map;
    }

    [Fact]
    public void MapVisualization_OwnUnitPastIsAlwaysVisible()
    {
        var map = HexMap(2);
        var me = MapUnitFactory.NewCiv("Me");
        var unit = MapUnitFactory.NewUnit(me, "Warrior", tile: map[HexCoord.Zero]);
        Assert.True(new MapVisualization(new VisGameInfo(map), me).IsUnitPastVisible(unit));
    }

    [Fact]
    public void MapVisualization_ForeignUnitPastNeedsVisibleUnitAndAllTilesViewable()
    {
        var map = HexMap(2);
        var me = MapUnitFactory.NewCiv("Me");
        var other = MapUnitFactory.NewCiv("Them");
        var unit = MapUnitFactory.NewUnit(other, "Warrior", tile: map[HexCoord.Zero]);
        unit.MovementMemories.Add(new MapUnitClass.UnitMovementMemory(HexCoord.Of(1, 0), Unciv.Logic.Map.MapUnits.UnitMovementMemoryType.UnitMoved));
        var visualization = new MapVisualization(new VisGameInfo(map), me);
        var meViewable = new HashSet<TileClass> { map[HexCoord.Zero] };
        me.ViewableTiles = meViewable;
        Assert.False(visualization.IsUnitPastVisible(unit)); // memory tile not viewable
        meViewable.Add(map[HexCoord.Of(1, 0)]);
        Assert.True(visualization.IsUnitPastVisible(unit));
        var invisible = MapUnitFactory.NewUnit(other, "Submarine", tile: map[HexCoord.Zero], uniques: "Invisible to others");
        invisible.MovementMemories.Add(new MapUnitClass.UnitMovementMemory(HexCoord.Of(1, 0), Unciv.Logic.Map.MapUnits.UnitMovementMemoryType.UnitMoved));
        Assert.False(visualization.IsUnitPastVisible(invisible));
    }

    [Fact]
    public void MapVisualization_ForeignUnitWithoutMemories_ChecksOnlyCurrentTile()
    {
        var map = HexMap(2);
        var me = MapUnitFactory.NewCiv("Me");
        var other = MapUnitFactory.NewCiv("Them");
        var unit = MapUnitFactory.NewUnit(other, "Warrior", tile: map[HexCoord.Zero]);
        var visualization = new MapVisualization(new VisGameInfo(map), me);
        Assert.False(visualization.IsUnitPastVisible(unit));
        me.ViewableTiles = new HashSet<TileClass> { map[HexCoord.Zero] };
        Assert.True(visualization.IsUnitPastVisible(unit));
    }

    [Fact]
    public void MapVisualization_AttackVisible_IfOwnOrEitherEndViewable()
    {
        var map = HexMap(2);
        var me = MapUnitFactory.NewCiv("Me");
        var other = MapUnitFactory.NewCiv("Them");
        var visualization = new MapVisualization(new VisGameInfo(map), me);
        var source = HexCoord.Of(1, 0);
        var target = HexCoord.Of(0, 1);
        Assert.True(visualization.IsAttackVisible(me, source, target));
        Assert.False(visualization.IsAttackVisible(other, source, target));
        me.ViewableTiles = new HashSet<TileClass> { map[source] };
        Assert.True(visualization.IsAttackVisible(other, source, target));
        me.ViewableTiles = new HashSet<TileClass> { map[target] };
        Assert.True(visualization.IsAttackVisible(other, source, target));
    }
}
