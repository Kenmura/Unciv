using Unciv.Assets;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Assets;

public class MapOverlaysTests
{
    private static TileDto Tile(int x, int y, string? owner = null, bool visible = true, string? road = null, bool? roadPillaged = null, string? improvement = null, bool? improvementPillaged = null) => new()
    {
        X = x,
        Y = y,
        Visible = visible,
        BaseTerrain = "Grassland",
        Features = [],
        RiverBottom = false,
        RiverBottomLeft = false,
        RiverBottomRight = false,
        Owner = owner,
        Road = road,
        RoadPillaged = roadPillaged,
        Improvement = improvement,
        ImprovementPillaged = improvementPillaged,
    };

    private static CityDto City(int x, int y, string name = "Ur", int population = 3, bool capital = false, int health = 20, OwnCityDto? own = null) => new()
    {
        Name = name,
        Owner = "Babylon",
        X = x,
        Y = y,
        Population = population,
        IsCapital = capital,
        IsPuppet = false,
        IsBeingRazed = false,
        Health = health,
        MaxHealth = 20,
        VisibleBuildings = [],
        Own = own,
    };

    private static OwnCityDto Own(params string[] queue) => new() { Id = "c", FoodStored = 0, CultureStored = 0, Buildings = [], ConstructionQueue = [.. queue] };

    [Fact]
    public void ASingleOwnedTileHasSixBorderEdges()
    {
        var edges = MapOverlays.BorderEdges([Tile(0, 0, "Babylon")]);
        Assert.Equal(6, edges.Count);
        Assert.All(edges, it => Assert.Equal("Babylon", it.Owner));
        Assert.Equal(6, edges.Select(it => (it.Dx, it.Dy)).Distinct().Count());
    }

    [Fact]
    public void TwoTilesOfOneOwnerShareNoEdge()
    {
        var edges = MapOverlays.BorderEdges([Tile(0, 0, "Babylon"), Tile(1, 0, "Babylon")]);
        Assert.Equal(10, edges.Count);
        Assert.DoesNotContain(edges, it => it is { X: 0, Y: 0, Dx: 1, Dy: 0 } or { X: 1, Y: 0, Dx: -1, Dy: 0 });
    }

    [Fact]
    public void TwoOwnersEachDrawTheirSideOfTheirCommonEdge()
    {
        var edges = MapOverlays.BorderEdges([Tile(0, 0, "Babylon"), Tile(1, 0, "Greece")]);
        Assert.Contains(new BorderEdge(0, 0, 1, 0, "Babylon"), edges);
        Assert.Contains(new BorderEdge(1, 0, -1, 0, "Greece"), edges);
    }

    [Fact]
    public void AFoggedTileWithNoMemoryDrawsNoEdgeAgainstItsVisibleNeighbour()
    {
        // Visibility changes along that side, not ownership: nothing is known, so nothing is drawn (T298)
        var edges = MapOverlays.BorderEdges([Tile(0, 0, "Babylon"), Tile(1, 0, owner: null, visible: false), Tile(0, 1)]);
        Assert.All(edges, it => Assert.Equal((0, 0), (it.X, it.Y)));
        Assert.Equal(5, edges.Count);
        Assert.DoesNotContain(edges, it => it is { Dx: 1, Dy: 0 });
    }

    [Fact]
    public void ATerritoryRunningIntoTheFogIsNotClosedThereAndTheFoggedPartKeepsItsOwnBorder()
    {
        // Osaka: (0,0) visible, (1,0) and (2,0) explored earlier and now fogged, all Japan; (1,1) visible and unowned
        var memory = new TileOwnerMemory();
        memory.Remember([Tile(0, 0, "Japan"), Tile(1, 0, "Japan"), Tile(2, 0, "Japan"), Tile(1, 1)]);
        var tiles = new[] { Tile(0, 0, "Japan"), Tile(1, 0, owner: null, visible: false), Tile(2, 0, owner: null, visible: false), Tile(1, 1) };
        var edges = MapOverlays.BorderEdges(tiles, memory);
        Assert.DoesNotContain(new BorderEdge(0, 0, 1, 0, "Japan"), edges);
        Assert.DoesNotContain(new BorderEdge(1, 0, -1, 0, "Japan"), edges);
        Assert.DoesNotContain(edges, it => it is { X: 1, Y: 0, Dx: 1, Dy: 0 } or { X: 2, Y: 0, Dx: -1, Dy: 0 });
        Assert.Contains(new BorderEdge(1, 0, 0, 1, "Japan"), edges); // fogged tile's side facing the visible unowned tile
        Assert.Equal(14, edges.Count); // 5 + 4 + 5 sides facing unowned or missing tiles, none along the lit/fogged line
    }

    [Fact]
    public void TheMemoryKeepsTheLastSeenOwnerAndNeverLearnsFromFoggedTiles()
    {
        var memory = new TileOwnerMemory();
        memory.Remember([Tile(0, 0, "Japan"), Tile(1, 0)]);
        // Now both fogged (the snapshot carries no owner for them) and the world moved on: remembering a fogged tile changes nothing
        memory.Remember([Tile(0, 0, owner: null, visible: false), Tile(1, 0, owner: "Greece", visible: false)]);
        Assert.True(memory.TryOwner(Tile(0, 0, owner: null, visible: false), out var first));
        Assert.Equal("Japan", first);
        Assert.True(memory.TryOwner(Tile(1, 0, owner: null, visible: false), out var second));
        Assert.Null(second); // known unowned
        Assert.False(memory.TryOwner(Tile(5, 5, owner: null, visible: false), out _)); // never seen
        Assert.False(MapOverlays.BorderEdges([Tile(5, 5, owner: null, visible: false)], memory).Any());
        // Seeing a tile again updates it
        memory.Remember([Tile(0, 0, "Greece")]);
        Assert.True(memory.TryOwner(Tile(0, 0, owner: null, visible: false), out var third));
        Assert.Equal("Greece", third);
    }

    [Fact]
    public void ClearingTheMemoryMakesFoggedTilesUnknownAgain()
    {
        var memory = new TileOwnerMemory();
        memory.Remember([Tile(0, 0, "Japan")]);
        memory.Clear();
        Assert.False(memory.TryOwner(Tile(0, 0, owner: null, visible: false), out _));
        // A visible tile facing a never-seen fogged one is not outlined against it (a load leaves the outline open there until the tile is seen)
        Assert.Equal(5, MapOverlays.BorderEdges([Tile(0, 0, "Japan"), Tile(1, 0, owner: null, visible: false)], memory).Count);
    }

    [Fact]
    public void FoggedNeighbourOfAnotherOwnerStillClosesTheOutlineWhenItsOwnerIsRemembered()
    {
        var memory = new TileOwnerMemory();
        memory.Remember([Tile(1, 0, "Greece")]);
        var edges = MapOverlays.BorderEdges([Tile(0, 0, "Babylon"), Tile(1, 0, owner: null, visible: false)], memory);
        Assert.Contains(new BorderEdge(0, 0, 1, 0, "Babylon"), edges);
        Assert.Contains(new BorderEdge(1, 0, -1, 0, "Greece"), edges);
    }

    [Fact]
    public void UnexploredTilesNeverGetABorderButTheMapEdgeOfAnExploredTerritoryIsOutlined()
    {
        var edges = MapOverlays.BorderEdges([Tile(0, 0, "Babylon")]);
        Assert.All(edges, it => Assert.Equal((0, 0), (it.X, it.Y)));
        Assert.Empty(MapOverlays.BorderEdges([]));
    }

    [Fact]
    public void AnInsetMovesTheSideTowardsItsTileCentreKeepingCornersSharedWithTheNextSide()
    {
        var (a, b) = MapOverlays.EdgeEnds(0, 0, 1, 0);
        var (ia, ib) = MapOverlays.EdgeEnds(0, 0, 1, 0, 0.05f);
        var (n1a, n1b) = MapOverlays.EdgeEnds(0, 0, 1, 1, 0.05f);
        var (n2a, n2b) = MapOverlays.EdgeEnds(0, 0, 0, -1, 0.05f);
        var centre = HexMath.Hex2WorldCoords(new HexCoord(0, 0));
        static float Dist((float X, float Z) p, float cx, float cz) => MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz));
        Assert.True(Dist(ia, centre.x, -centre.y) < Dist(a, centre.x, -centre.y));
        Assert.True(Dist(ib, centre.x, -centre.y) < Dist(b, centre.x, -centre.y));
        // The side's midpoint moves by exactly the inset
        var mid = ((a.X + b.X) / 2f, (a.Z + b.Z) / 2f);
        var imid = ((ia.X + ib.X) / 2f, (ia.Z + ib.Z) / 2f);
        Assert.Equal(0.05f, MathF.Sqrt(((mid.Item1 - imid.Item1) * (mid.Item1 - imid.Item1)) + ((mid.Item2 - imid.Item2) * (mid.Item2 - imid.Item2))), 4);
        // Both ends are shared with the next side round the hexagon on each hand
        Assert.True(new[] { n1a, n1b, n2a, n2b }.Count(it => Corner(ia, it)) == 1 && new[] { n1a, n1b, n2a, n2b }.Count(it => Corner(ib, it)) == 1);
    }

    private static bool Corner((float X, float Z) p, (float X, float Z) q) => MathF.Abs(p.X - q.X) < 1e-4f && MathF.Abs(p.Z - q.Z) < 1e-4f;

    [Fact]
    public void TheTwoSidesOfASharedEdgeAreInsetOntoTheirOwnTilesSoTheyDoNotOverlap()
    {
        var edges = MapOverlays.BorderEdges([Tile(0, 0, "Babylon"), Tile(1, 0, "Greece")]);
        var babylon = edges.Single(it => it is { Owner: "Babylon", Dx: 1, Dy: 0 });
        var greece = edges.Single(it => it is { Owner: "Greece", Dx: -1, Dy: 0 });
        var (ba, bb) = MapOverlays.EdgeEnds(babylon.X, babylon.Y, babylon.Dx, babylon.Dy, 0.05f);
        var (ga, gb) = MapOverlays.EdgeEnds(greece.X, greece.Y, greece.Dx, greece.Dy, 0.05f);
        // Measure along the world direction from Babylon's tile to Greece's (z is minus the engine's y)
        var to = HexMath.Hex2WorldCoords(new HexCoord(1, 0));
        var dir = (X: to.x, Z: -to.y);
        var len = MathF.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
        float Along((float X, float Z) p, (float X, float Z) q) => ((p.X + q.X) / 2f * dir.X + (p.Z + q.Z) / 2f * dir.Z) / len;
        var raw = MapOverlays.EdgeEnds(0, 0, 1, 0);
        var rawMid = Along(raw.A, raw.B);
        var babylonMid = Along(ba, bb);
        var greeceMid = Along(ga, gb);
        Assert.True(babylonMid < rawMid && greeceMid > rawMid); // each on its own side of the common edge
        Assert.Equal(0.1f, greeceMid - babylonMid, 4); // two ribbons of width 0.1 now touch instead of coinciding
    }

    [Fact]
    public void AdjacentRoadTilesAreLinkedOnceAndACityCentreCountsAsRoad()
    {
        var tiles = new[] { Tile(0, 0), Tile(1, 0, road: "Road"), Tile(2, 0, road: "Road") };
        var links = MapOverlays.RoadLinks(tiles, [City(0, 0)]);
        Assert.Equal(2, links.Count);
        Assert.Contains(new RoadLink(0, 0, 1, 0, false), links);
        Assert.Contains(new RoadLink(1, 0, 1, 0, false), links);
    }

    [Fact]
    public void ALoneRoadTileIsAStubAndAPillagedRoadIsNothing()
    {
        var links = MapOverlays.RoadLinks([Tile(0, 0, road: "Road"), Tile(5, 5, road: "Road", roadPillaged: true), Tile(1, 0, road: "Road", roadPillaged: true)], []);
        Assert.Equal([new RoadLink(0, 0, 0, 0, false)], links);
    }

    [Fact]
    public void NoneIsNotARoad()
    {
        Assert.Empty(MapOverlays.RoadLinks([Tile(0, 0, road: "None"), Tile(1, 0, road: "None")], []));
    }

    [Fact]
    public void RailOnBothEndsMakesARailLinkElseARoadLink()
    {
        var both = MapOverlays.RoadLinks([Tile(0, 0, road: "Railroad"), Tile(1, 0, road: "Railroad")], []);
        var mixed = MapOverlays.RoadLinks([Tile(0, 0, road: "Railroad"), Tile(1, 0, road: "Road")], []);
        Assert.True(both.Single().Railroad);
        Assert.False(mixed.Single().Railroad);
        Assert.True(MapOverlays.RoadLinks([Tile(0, 0, road: "Railroad")], []).Single().Railroad);
    }

    [Fact]
    public void ImprovementsSkipTheCityCentreAndFlagPillaging()
    {
        var markers = MapOverlays.Improvements([Tile(0, 0, improvement: "City center"), Tile(1, 0, improvement: "Farm"), Tile(2, 0, improvement: "Mine", improvementPillaged: true), Tile(3, 0)]);
        Assert.Equal([new ImprovementMarker(1, 0, "Farm", false), new ImprovementMarker(2, 0, "Mine", true)], markers);
    }

    [Fact]
    public void ABannerNamesTheCityItsSizeAndOwnProduction()
    {
        var own = MapOverlays.Banner(City(0, 0, capital: true, own: Own("Warrior", "Scout")));
        Assert.Equal("* Ur (3)", own.Title);
        Assert.Equal(["Building Warrior"], own.Details);
        Assert.Equal(["Nothing in production"], MapOverlays.Banner(City(0, 0, own: Own())).Details);
        var foreign = MapOverlays.Banner(City(0, 0));
        Assert.Equal("Ur (3)", foreign.Title);
        Assert.Empty(foreign.Details);
    }

    [Fact]
    public void ABannerShowsHealthWhenHurtAndTheCityState()
    {
        var banner = MapOverlays.Banner(City(0, 0, health: 5) with { IsPuppet = true, IsBeingRazed = true });
        Assert.Equal(["Puppet", "Razing", "Health 5/20"], banner.Details);
    }

    [Fact]
    public void TheCityBlockGrowsWithPopulationUpToACap()
    {
        Assert.True(MapOverlays.CityBlockHeight(1) < MapOverlays.CityBlockHeight(6));
        Assert.Equal(MapOverlays.CityBlockHeight(12), MapOverlays.CityBlockHeight(40));
        Assert.Equal(MapOverlays.CityBlockHeight(1), MapOverlays.CityBlockHeight(0));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(-1, 0)]
    [InlineData(-1, -1)]
    [InlineData(0, -1)]
    public void AnEdgeIsOneRadiusLongAndHalfAWayToTheNeighbour(int dx, int dy)
    {
        var (a, b) = MapOverlays.EdgeEnds(2, -1, dx, dy);
        Assert.Equal(1f, MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z)), 3);
        var centre = HexMath.Hex2WorldCoords(new HexCoord(2, -1));
        var neighbour = HexMath.Hex2WorldCoords(new HexCoord(2 + dx, -1 + dy));
        var mid = ((a.X + b.X) / 2f, (a.Z + b.Z) / 2f);
        Assert.Equal((centre.x + neighbour.x) / 2f, mid.Item1, 3);
        Assert.Equal(-(centre.y + neighbour.y) / 2f, mid.Item2, 3);
    }

    [Fact]
    public void TheSixNeighboursAreDistinctAndOneStepAway()
    {
        Assert.Equal(6, MapOverlays.Neighbours.Distinct().Count());
        Assert.All(MapOverlays.Neighbours, it => Assert.Equal(1, HexMath.GetDistance(0, 0, it.Dx, it.Dy)));
    }
}
