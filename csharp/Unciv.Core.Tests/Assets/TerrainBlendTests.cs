using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

public class TerrainBlendTests
{
    private static readonly Tint Own = new(0.8f, 0.2f, 0.1f);
    private static readonly Tint Other = new(0.2f, 0.6f, 0.9f);
    private static readonly Tint Third = new(0.1f, 0.1f, 0.1f);

    private static Tint?[] Around(params (int Edge, Tint Colour)[] set)
    {
        var result = new Tint?[6];
        foreach (var (edge, colour) in set) result[edge] = colour;
        return result;
    }

    private static void AssertTint(Tint expected, Tint actual)
    {
        Assert.Equal(expected.R, actual.R, 1e-5f);
        Assert.Equal(expected.G, actual.G, 1e-5f);
        Assert.Equal(expected.B, actual.B, 1e-5f);
    }

    [Fact]
    public void EdgeNormalsAreUnitLengthAndSixtyDegreesApart()
    {
        for (var i = 0; i < 6; i++)
        {
            var (x, z) = TerrainBlend.EdgeNormals[i];
            Assert.Equal(1f, x * x + z * z, 1e-5f);
            var (nx, nz) = TerrainBlend.EdgeNormals[(i + 1) % 6];
            Assert.Equal(0.5f, x * nx + z * nz, 1e-5f);
        }
    }

    [Fact]
    public void EdgeMidpointsAreTheApothemAlongTheirNormalAndNeighboursAreDoubleThat()
    {
        for (var i = 0; i < 6; i++)
        {
            var (x, z) = TerrainBlend.EdgeNormals[i];
            Assert.Equal(1f, TerrainBlend.EdgeWeight(x * TerrainBlend.Apothem, z * TerrainBlend.Apothem, i), 1e-5f);
            Assert.Equal(TerrainBlend.NeighbourDistance, 2f * TerrainBlend.Apothem, 1e-5f);
        }
    }

    [Fact]
    public void WeightIsZeroInsideTheStartFractionAndRisesMonotonically()
    {
        var (nx, nz) = TerrainBlend.EdgeNormals[1];
        Assert.Equal(0f, TerrainBlend.EdgeWeight(0f, 0f, 1));
        Assert.Equal(0f, TerrainBlend.EdgeWeight(nx * TerrainBlend.Apothem * TerrainBlend.StartFraction, nz * TerrainBlend.Apothem * TerrainBlend.StartFraction, 1));
        var last = 0f;
        for (var k = 7; k <= 10; k++)
        {
            var d = TerrainBlend.Apothem * k / 10f;
            var w = TerrainBlend.EdgeWeight(nx * d, nz * d, 1);
            Assert.True(w > last, $"{k}: {w} <= {last}");
            last = w;
        }
        // half way through the ramp the smoothstep is exactly 1/2
        var mid = TerrainBlend.Apothem * (TerrainBlend.StartFraction + 1f) / 2f;
        Assert.Equal(0.5f, TerrainBlend.EdgeWeight(nx * mid, nz * mid, 1), 1e-5f);
    }

    [Fact]
    public void WeightIsZeroOnTheFarSideOfTheTile()
    {
        var (nx, nz) = TerrainBlend.EdgeNormals[1];
        Assert.Equal(0f, TerrainBlend.EdgeWeight(-nx * 0.8f, -nz * 0.8f, 1));
    }

    [Fact]
    public void CentreKeepsOwnColourWhateverTheNeighbours()
    {
        AssertTint(Own, TerrainBlend.Mix(Own, Around((0, Other), (3, Third)), 0f, 0f));
    }

    [Fact]
    public void EdgeMidpointIsTheMeanOfBothTilesAndSymmetric()
    {
        for (var i = 0; i < 6; i++)
        {
            var (x, z) = TerrainBlend.EdgeNormals[i];
            var p = (X: x * TerrainBlend.Apothem, Z: z * TerrainBlend.Apothem);
            var mine = TerrainBlend.Mix(Own, Around((i, Other)), p.X, p.Z);
            AssertTint(new Tint((Own.R + Other.R) / 2f, (Own.G + Other.G) / 2f, (Own.B + Other.B) / 2f), mine);
            // The other tile sees the same point from its opposite edge and must agree.
            var theirs = TerrainBlend.Mix(Other, Around(((i + 3) % 6, Own)), -p.X, -p.Z);
            AssertTint(mine, theirs);
        }
    }

    [Fact]
    public void CornerIsTheMeanOfTheThreeTilesMeetingThere()
    {
        // Corner at 0 degrees (1, 0) lies on edges 0 (30 degrees) and 5 (330 degrees).
        var mixed = TerrainBlend.Mix(Own, Around((0, Other), (5, Third)), 1f, 0f);
        AssertTint(new Tint((Own.R + Other.R + Third.R) / 3f, (Own.G + Other.G + Third.G) / 3f, (Own.B + Other.B + Third.B) / 3f), mixed);
    }

    [Fact]
    public void MissingNeighbourFadesTheRimToTheMissingShareOfTheOwnColour()
    {
        var (x, z) = TerrainBlend.EdgeNormals[2];
        var mixed = TerrainBlend.Mix(Own, Around(), x * TerrainBlend.Apothem, z * TerrainBlend.Apothem);
        var k = (1f + TerrainBlend.MissingShare) / 2f;
        AssertTint(new Tint(Own.R * k, Own.G * k, Own.B * k), mixed);
    }

    [Fact]
    public void MixRequiresSixSlots() => Assert.Throws<ArgumentException>(() => TerrainBlend.Mix(Own, new Tint?[5], 0f, 0f));

    [Fact]
    public void TintArithmetic()
    {
        Assert.Equal(new Tint(0.5f, 0.25f, 1f / 255f), Tint.Of(new Rgb(0, 0, 0)) + new Tint(0.5f, 0.25f, 1f / 255f));
        Assert.Equal(new Tint(1f, 0f, 20f / 255f), Tint.Of(new Rgb(255, 0, 20)));
        AssertTint(new Tint(0.4f, 0.1f, 0.05f), new Tint(0.8f, 0.2f, 0.1f) * 0.5f);
        AssertTint(new Tint(0.32f, 0.02f, 0.05f), new Tint(0.8f, 0.2f, 0.1f) * new Tint(0.4f, 0.1f, 0.5f));
        AssertTint(Own, Own.Lerp(Other, 0f));
        AssertTint(Other, Own.Lerp(Other, 1f));
        AssertTint(new Tint(0.5f, 0.4f, 0.5f), Own.Lerp(Other, 0.5f));
    }

    [Fact]
    public void SrgbToLinearUsesTheStandardCurve()
    {
        AssertTint(new Tint(0f, 0f, 0f), new Tint(0f, 0f, 0f).SrgbToLinear());
        AssertTint(new Tint(1f, 1f, 1f), new Tint(1f, 1f, 1f).SrgbToLinear());
        // below the knee the curve is linear (c / 12.92), above it a 2.4 power
        AssertTint(new Tint(0.02f / 12.92f, 0.04f / 12.92f, 0.5f), new Tint(0.02f, 0.04f, 0.735357f).SrgbToLinear());
        Assert.Equal(0.21404f, new Tint(0.5f, 0.5f, 0.5f).SrgbToLinear().R, 1e-4f);
        Assert.Equal(0.04045f / 12.92f, new Tint(0.04045f, 0f, 0f).SrgbToLinear().R, 1e-6f);
        Assert.Equal(MathF.Pow((0.0405f + 0.055f) / 1.055f, 2.4f), new Tint(0.0405f, 0f, 0f).SrgbToLinear().R, 1e-6f);
        // just below the knee the linear branch applies (it differs from the power branch by about 1e-5 here)
        Assert.Equal(0.0403f / 12.92f, new Tint(0.0403f, 0f, 0f).SrgbToLinear().R, 1e-7f);
    }

    [Fact]
    public void TunedConstantsArePinned()
    {
        Assert.Equal(0.6f, TerrainBlend.StartFraction);
        Assert.Equal(0.55f, TerrainBlend.MissingShare);
        Assert.Equal(7f, TerrainChunks.CellSize);
        // 55% of the way to the edge is still entirely the tile's own colour, 65% already blends
        var (nx, nz) = TerrainBlend.EdgeNormals[0];
        Assert.Equal(0f, TerrainBlend.EdgeWeight(nx * TerrainBlend.Apothem * 0.55f, nz * TerrainBlend.Apothem * 0.55f, 0));
        Assert.True(TerrainBlend.EdgeWeight(nx * TerrainBlend.Apothem * 0.65f, nz * TerrainBlend.Apothem * 0.65f, 0) > 0f);
    }
}

public class TerrainChunksTests
{
    [Fact]
    public void CellsUseFloorDivisionSoNegativeCoordinatesGetTheirOwnCells()
    {
        var s = TerrainChunks.CellSize;
        Assert.Equal(new ChunkId(0, 0), TerrainChunks.Of(0f, 0f));
        Assert.Equal(new ChunkId(0, 0), TerrainChunks.Of(s - 0.01f, s - 0.01f));
        Assert.Equal(new ChunkId(1, 1), TerrainChunks.Of(s, s));
        Assert.Equal(new ChunkId(-1, -1), TerrainChunks.Of(-0.01f, -0.01f));
        Assert.Equal(new ChunkId(-2, 0), TerrainChunks.Of(-s - 0.01f, 1f));
    }

    [Fact]
    public void GroupSortsChunksKeepsTileOrderAndCoversEveryTileOnce()
    {
        var s = TerrainChunks.CellSize;
        var centres = new (float X, float Z)[] { (s + 1f, 0f), (0f, 0f), (-1f, 0f), (1f, 1f), (s + 2f, 1f) };
        var groups = TerrainChunks.Group(centres);
        Assert.Equal([new ChunkId(-1, 0), new ChunkId(0, 0), new ChunkId(1, 0)], groups.Select(it => it.Id));
        Assert.Equal([2], groups[0].Tiles);
        Assert.Equal([1, 3], groups[1].Tiles);
        Assert.Equal([0, 4], groups[2].Tiles);
        Assert.Empty(TerrainChunks.Group([]));
    }

    [Fact]
    public void GroupSortsByXThenZ()
    {
        var s = TerrainChunks.CellSize;
        var groups = TerrainChunks.Group([(0f, s + 1f), (0f, 0f), (s + 1f, 0f)]);
        Assert.Equal([new ChunkId(0, 0), new ChunkId(0, 1), new ChunkId(1, 0)], groups.Select(it => it.Id));
    }

    [Fact]
    public void BoundsReachOneTileRadiusBeyondTheOutermostCentres()
    {
        var b = TerrainChunks.BoundsOf([(1f, 2f), (4f, -3f)]);
        Assert.Equal(new GroundRect(0f, -4f, 5f, 3f), b);
    }

    [Fact]
    public void VisibilityIsIntersectionWithTheFootprintPaddedByOneCell()
    {
        var s = TerrainChunks.CellSize;
        var bounds = new GroundRect(0f, 0f, 5f, 5f);
        Assert.True(TerrainChunks.IsVisible(bounds, new GroundRect(1f, 1f, 2f, 2f)));
        Assert.True(TerrainChunks.IsVisible(bounds, new GroundRect(5f + s - 0.1f, 0f, 20f, 5f)));
        Assert.False(TerrainChunks.IsVisible(bounds, new GroundRect(5f + s + 0.1f, 0f, 20f, 5f)));
        Assert.True(TerrainChunks.IsVisible(bounds, new GroundRect(-20f, 0f, -s + 0.1f, 5f)));
        Assert.False(TerrainChunks.IsVisible(bounds, new GroundRect(-20f, 0f, -s - 0.1f, 5f)));
        Assert.False(TerrainChunks.IsVisible(bounds, new GroundRect(0f, 5f + s + 0.1f, 5f, 20f)));
        Assert.False(TerrainChunks.IsVisible(bounds, new GroundRect(0f, -20f, 5f, -s - 0.1f)));
    }

    [Fact]
    public void RectAroundPointsAndEmptyCase()
    {
        Assert.Equal(new GroundRect(-1f, -2f, 3f, 4f), GroundRect.Around([(3f, -2f), (-1f, 4f), (0f, 0f)]));
        Assert.Equal(new GroundRect(0f, 0f, 0f, 0f), GroundRect.Around([]));
        Assert.Equal(new GroundRect(-1f, -1f, 3f, 3f), new GroundRect(0f, 0f, 2f, 2f).Expand(1f));
    }

    [Fact]
    public void RectsTouchingOnAnEdgeIntersect()
    {
        var a = new GroundRect(0f, 0f, 1f, 1f);
        Assert.True(a.Intersects(new GroundRect(1f, 1f, 2f, 2f)));
        Assert.True(a.Intersects(new GroundRect(-1f, -1f, 0f, 0f)));
        Assert.False(a.Intersects(new GroundRect(1.01f, 0f, 2f, 1f)));
        Assert.False(a.Intersects(new GroundRect(0f, 1.01f, 1f, 2f)));
        Assert.False(a.Intersects(new GroundRect(-2f, 0f, -0.01f, 1f)));
        Assert.False(a.Intersects(new GroundRect(0f, -2f, 1f, -0.01f)));
    }
}

public class FrameBudgetTests
{
    [Fact]
    public void MeanAndBudget()
    {
        Assert.Equal(20.0, FrameBudget.MeanMs([10.0, 20.0, 30.0]));
        Assert.True(FrameBudget.WithinBudget([10.0, 30.0], 20.0));
        Assert.False(FrameBudget.WithinBudget([10.0, 30.0], 19.9));
    }

    [Fact]
    public void NoFramesIsNotAMeasurement() => Assert.Throws<ArgumentException>(() => FrameBudget.MeanMs([]));
}
