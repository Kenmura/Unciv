using Unciv.Assets;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Assets;

public class BorderChainsTests
{
    private static TileDto Tile(int x, int y, string? owner, bool visible = true) => new()
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
    };

    private static int Dist(int dx, int dy) => System.Math.Max(System.Math.Abs(dx), System.Math.Max(System.Math.Abs(dy), System.Math.Abs(dx - dy)));

    private static List<TileDto> Disc(int radius, Func<int, int, string?> ownerOf)
    {
        var tiles = new List<TileDto>();
        for (var x = -radius; x <= radius; x++)
            for (var y = -radius; y <= radius; y++)
                if (Dist(x, y) <= radius) tiles.Add(Tile(x, y, ownerOf(x, y)));
        return tiles;
    }

    [Fact]
    public void ASingleTileIsOneClosedLoopOfSixSides()
    {
        var chains = BorderChains.Build(MapOverlays.BorderEdges([Tile(0, 0, "A")]));
        var chain = Assert.Single(chains);
        Assert.True(chain.Closed);
        Assert.Equal(6, chain.Sides.Count);
        Assert.Equal(6, chain.Points.Count);
    }

    [Fact]
    public void ATerritoryAroundATileOfAnotherCivIsAnOuterLoopAndAHole()
    {
        var tiles = Disc(3, (x, y) => x == 0 && y == 0 ? "B" : Dist(x, y) <= 2 ? "A" : null);
        var chains = BorderChains.Build(MapOverlays.BorderEdges(tiles));
        Assert.All(chains, it => Assert.True(it.Closed));
        Assert.Equal(2, chains.Count(it => it.Owner == "A")); // the outside and the hole round B
        Assert.Single(chains, it => it.Owner == "B");
    }

    [Fact]
    public void EverySideIsInExactlyOneChainAndConsecutivePointsShareACorner()
    {
        var tiles = Disc(6, (x, y) => (x * 7 + y * 3) % 5 == 0 ? "A" : Dist(x - 3, y) <= 2 ? "B" : null);
        var edges = MapOverlays.BorderEdges(tiles);
        var chains = BorderChains.Build(edges);
        Assert.Equal(edges.Count, chains.Sum(it => it.Sides.Count));
        Assert.Equal(edges.Count, chains.SelectMany(it => it.Sides).Distinct().Count());
        foreach (var chain in chains)
        {
            for (var i = 0; i < chain.Sides.Count; i++)
            {
                var side = chain.Sides[i];
                var (a, b) = MapOverlays.EdgeEnds(side.X, side.Y, side.Dx, side.Dy);
                var from = chain.Points[i];
                var to = chain.Points[(i + 1) % chain.Points.Count];
                Assert.True(System.Math.Abs(from.X - a.X) < 1e-4f && System.Math.Abs(from.Z - a.Z) < 1e-4f);
                Assert.True(System.Math.Abs(to.X - b.X) < 1e-4f && System.Math.Abs(to.Z - b.Z) < 1e-4f);
            }
        }
    }

    [Fact]
    public void ATerritoryWhoseNeighboursAreAllKnownHasOnlyClosedChains()
    {
        var tiles = Disc(8, (x, y) => Dist(x + 2, y) <= 2 ? "A" : Dist(x - 3, y - 1) <= 2 ? "B" : Dist(x, y - 5) <= 1 ? "C" : null);
        var chains = BorderChains.Build(MapOverlays.BorderEdges(tiles));
        Assert.All(chains, it => Assert.True(it.Closed));
    }

    [Fact]
    public void ATerritoryRunningIntoUnknownFoggedTilesIsAnOpenChain()
    {
        var tiles = new List<TileDto> { Tile(0, 0, "A"), Tile(1, 0, "A"), Tile(0, 1, null, visible: false) };
        var chains = BorderChains.Build(MapOverlays.BorderEdges(tiles));
        var chain = Assert.Single(chains);
        Assert.False(chain.Closed);
        Assert.Equal(chain.Sides.Count + 1, chain.Points.Count);
    }

    [Fact]
    public void TwoTilesTouchingInOneCornerStillGiveEverySideOnce()
    {
        // A's two tiles are not neighbours but meet B's tiles so that corners are shared by sides of both civs
        var tiles = new List<TileDto> { Tile(0, 0, "A"), Tile(1, 2, "A"), Tile(1, 1, "B"), Tile(0, 1, "B") };
        var edges = MapOverlays.BorderEdges(tiles);
        var chains = BorderChains.Build(edges);
        Assert.Equal(edges.Count, chains.Sum(it => it.Sides.Count));
        Assert.Equal(edges.Count, chains.SelectMany(it => it.Sides).Distinct().Count());
    }

    [Fact]
    public void TheResultIsTheSameForTheSameInputAndKeepsOwnersApart()
    {
        var tiles = Disc(4, (x, y) => x < 0 ? "A" : x > 0 ? "B" : null);
        var edges = MapOverlays.BorderEdges(tiles);
        var first = BorderChains.Build(edges);
        var second = BorderChains.Build(edges);
        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Sides, second[i].Sides);
            Assert.Equal(first[i].Owner, second[i].Owner);
        }
        Assert.All(first, chain => Assert.All(chain.Sides, it => Assert.Equal(chain.Owner, it.Owner)));
    }

    [Fact]
    public void ABigMapChainsEverySideWithoutGaps()
    {
        var tiles = Disc(30, (x, y) => ((x + 100) / 4 + (y + 100) / 5) % 4 is var v && v == 3 ? null : "Civ" + v);
        var edges = MapOverlays.BorderEdges(tiles);
        var chains = BorderChains.Build(edges);
        Assert.Equal(edges.Count, chains.Sum(it => it.Sides.Count));
        // Only a civ that reaches the rim of the map (where the neighbours are unknown) can have an open chain
        var rimOwners = tiles.Where(it => Dist(it.X, it.Y) == 30 && it.Owner != null).Select(it => it.Owner).ToHashSet();
        Assert.All(chains.Where(it => !rimOwners.Contains(it.Owner)), it => Assert.True(it.Closed, "an inland territory has no open chain"));
    }

    [Fact]
    public void ANoEdgeListHasNoChains() => Assert.Empty(BorderChains.Build([]));
}

public class BorderMeshTests
{
    private static BorderMeshData SingleTile()
    {
        var chains = BorderChains.Build([.. Neighbours().Select(it => new BorderEdge(0, 0, it.Dx, it.Dy, "A"))]);
        return BorderMesh.Build(chains, _ => 0.25f, _ => 1f);
    }

    private static IEnumerable<(int Dx, int Dy)> Neighbours() => MapOverlays.Neighbours;

    [Fact]
    public void ASideIsFourVerticesAndTwoTriangles()
    {
        var mesh = SingleTile();
        Assert.Equal(24, mesh.Vertices.Count);
        Assert.Equal(36, mesh.Indices.Count);
        Assert.All(mesh.Indices, it => Assert.InRange(it, 0, 23));
    }

    [Fact]
    public void InwardNormalsPointTowardsTheOwnedTileCentre()
    {
        var mesh = SingleTile();
        foreach (var vertex in mesh.Vertices.Where(it => it.Across == 0f))
        {
            // the corner is at distance 1 from the origin; stepping along the outgoing inward normal gets closer to it
            var before = System.Math.Sqrt(vertex.X * vertex.X + vertex.Z * vertex.Z);
            var after = System.Math.Sqrt(System.Math.Pow(vertex.X + vertex.OutX * 0.1f, 2) + System.Math.Pow(vertex.Z + vertex.OutZ * 0.1f, 2));
            Assert.True(after < before);
            Assert.Equal(1.0, System.Math.Sqrt(vertex.OutX * vertex.OutX + vertex.OutZ * vertex.OutZ), 3);
        }
    }

    [Fact]
    public void AClosedLoopJoinsItsSidesAtEveryCorner()
    {
        var mesh = SingleTile();
        // the end of each side and the start of the next are the same corner with the same normals
        for (var side = 0; side < 6; side++)
        {
            var end = mesh.Vertices[side * 4 + 2];
            var nextStart = mesh.Vertices[((side + 1) % 6) * 4];
            Assert.Equal(end.X, nextStart.X, 3);
            Assert.Equal(end.Z, nextStart.Z, 3);
            Assert.Equal(end.OutX, nextStart.OutX, 3);
            Assert.Equal(end.OutZ, nextStart.OutZ, 3);
            Assert.Equal(1f, end.Fade);
        }
    }

    [Fact]
    public void AnOpenChainFadesOutAtBothEnds()
    {
        var tiles = new List<BorderEdge> { new(0, 0, 1, 0, "A"), new(0, 0, 1, 1, "A") };
        var chains = BorderChains.Build(tiles);
        var chain = Assert.Single(chains);
        Assert.False(chain.Closed);
        var mesh = BorderMesh.Build(chains, _ => 0f, _ => 0.5f);
        Assert.Equal(8, mesh.Vertices.Count);
        Assert.Equal(0f, mesh.Vertices[0].Fade);
        Assert.Equal(1f, mesh.Vertices[2].Fade);
        Assert.Equal(1f, mesh.Vertices[4].Fade);
        Assert.Equal(0f, mesh.Vertices[6].Fade);
        Assert.All(mesh.Vertices, it => Assert.Equal(0.5f, it.Alpha));
        // every side runs between its own two consecutive corners (the last side of an open chain must not wrap to the first point)
        for (var side = 0; side < 2; side++)
        {
            Assert.Equal(chain.Points[side].X, mesh.Vertices[side * 4].X, 4);
            Assert.Equal(chain.Points[side + 1].X, mesh.Vertices[side * 4 + 2].X, 4);
            Assert.Equal(chain.Points[side + 1].Z, mesh.Vertices[side * 4 + 2].Z, 4);
        }
    }

    [Fact]
    public void NoChainsGiveAnEmptyMesh()
    {
        var mesh = BorderMesh.Build([], _ => 0f, _ => 1f);
        Assert.Empty(mesh.Vertices);
        Assert.Empty(mesh.Indices);
    }
}

public class BorderStyleTests
{
    [Theory]
    [InlineData(110f, 1f, 3)]
    [InlineData(154f, 1.5f, 5)]
    [InlineData(308f, 2.5f, 9)]
    [InlineData(720f, 1f, 4)] // closest zoom: capped at 4 times the scale
    [InlineData(38f, 1f, 2)] // farthest zoom: never under 2 px
    [InlineData(54f, 1.5f, 2)]
    [InlineData(108f, 2.5f, 4)]
    public void WidthFollowsTheTileSizeWithinTheScaledBounds(float tile, float scale, int expected) => Assert.Equal(expected, BorderStyle.WidthPixels(tile, scale));

    [Fact]
    public void BadInputsStillGiveAVisibleWidth()
    {
        Assert.Equal(2, BorderStyle.WidthPixels(float.NaN, 1f));
        Assert.Equal(3, BorderStyle.WidthPixels(100f, 0f));
        Assert.Equal(3, BorderStyle.WidthPixels(100f, float.PositiveInfinity));
    }

    [Fact]
    public void ACasingThatContrastsIsKeptAndOneThatDoesNotBecomesBlackOrWhite()
    {
        Assert.Equal(new Rgb(255, 255, 255), BorderStyle.CasingFor(new Rgb(10, 10, 10), new Rgb(255, 255, 255)));
        Assert.Equal(new Rgb(255, 255, 255), BorderStyle.CasingFor(new Rgb(15, 15, 15), new Rgb(20, 20, 20)));
        Assert.Equal(new Rgb(0, 0, 0), BorderStyle.CasingFor(new Rgb(240, 240, 240), new Rgb(235, 235, 235)));
    }
}
