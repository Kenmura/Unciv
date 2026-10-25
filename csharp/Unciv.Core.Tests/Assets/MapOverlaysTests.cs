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

    private static OwnCityDto Own(params string[] queue) => new() { FoodStored = 0, CultureStored = 0, Buildings = [], ConstructionQueue = [.. queue] };

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
    public void FoggedAndUnownedTilesGetNoBorderOfTheirOwnButCloseTheNeighboursOutline()
    {
        var edges = MapOverlays.BorderEdges([Tile(0, 0, "Babylon"), Tile(1, 0, "Babylon", visible: false), Tile(0, 1)]);
        Assert.All(edges, it => Assert.Equal((0, 0), (it.X, it.Y)));
        Assert.Equal(6, edges.Count);
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
