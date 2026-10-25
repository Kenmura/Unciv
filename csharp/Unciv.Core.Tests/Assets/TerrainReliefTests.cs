using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

public class TerrainReliefTests
{
    [Theory]
    [InlineData("Mountain", ReliefKind.Mountain)]
    [InlineData("Hill", ReliefKind.Hill)]
    [InlineData("Ocean", ReliefKind.Ocean)]
    [InlineData("Coast", ReliefKind.Coast)]
    [InlineData("Lakes", ReliefKind.Lake)]
    [InlineData("Grassland", ReliefKind.Flat)]
    [InlineData("SomeModTerrain", ReliefKind.Flat)]
    public void KindFollowsName(string name, ReliefKind kind) => Assert.Equal(kind, TerrainRelief.KindOf(name));

    [Fact]
    public void ElevationOrdersWaterLandHillsMountains()
    {
        var order = new[] { ReliefKind.Ocean, ReliefKind.Coast, ReliefKind.Flat, ReliefKind.Hill, ReliefKind.Mountain }.Select(TerrainRelief.TopHeight).ToList();
        Assert.Equal(order.OrderBy(it => it), order);
        Assert.Equal(1f, TerrainRelief.Elevation(ReliefKind.Flat));
    }

    [Theory]
    [InlineData("Mountain")]
    [InlineData("Hill")]
    [InlineData("Ocean")]
    [InlineData("Coast")]
    [InlineData("Grassland")]
    public void MeshIsValidDeterministicAndPeaksAtItsTopHeight(string name)
    {
        var key = TerrainRelief.MeshKey(name);
        var mesh = TerrainRelief.Build(key);
        Assert.Equal(mesh.Positions, TerrainRelief.Build(key).Positions);
        Assert.Equal(mesh.VertexCount * 4, mesh.Colors!.Length);
        Assert.Equal(mesh.VertexCount * 3, mesh.Normals.Length);
        Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.VertexCount - 1));
        var kind = TerrainRelief.KindOf(name);
        var heights = Enumerable.Range(0, mesh.VertexCount).Select(i => mesh.Positions[i * 3 + 1]).ToList();
        Assert.Equal(TerrainRelief.TopHeight(kind), heights.Max(), 1e-4f);
        Assert.Equal(0f, heights.Min());
        Assert.Equal(6 * 16 + 6 * TerrainRelief.Divisions * 2, mesh.TriangleCount);
    }

    [Fact]
    public void RimVerticesStayAtRimHeightSoNeighboursMeet()
    {
        var mesh = TerrainRelief.Build(TerrainRelief.MeshKey("Mountain"));
        for (var i = 0; i < mesh.VertexCount; i++)
        {
            float x = mesh.Positions[i * 3], y = mesh.Positions[i * 3 + 1], z = mesh.Positions[i * 3 + 2];
            if (y > 0f && System.Math.Abs(TerrainRelief.RimFraction(x, z) - 1f) < 1e-4f) Assert.Equal(TerrainRelief.LandHeight, y, 1e-4f);
        }
    }

    [Fact]
    public void SurfaceHeightFallsFromApexToRim()
    {
        Assert.Equal(TerrainRelief.TopHeight(ReliefKind.Mountain), TerrainRelief.SurfaceHeight(ReliefKind.Mountain, 0f), 1e-5f);
        Assert.Equal(TerrainRelief.LandHeight, TerrainRelief.SurfaceHeight(ReliefKind.Hill, 1f), 1e-5f);
        Assert.True(TerrainRelief.SurfaceHeight(ReliefKind.Hill, 0.3f) > TerrainRelief.SurfaceHeight(ReliefKind.Hill, 0.7f));
        Assert.True(TerrainRelief.SurfaceHeight(ReliefKind.Ocean, 0f) <= TerrainRelief.RimHeight(ReliefKind.Ocean));
    }

    [Fact]
    public void OceanIsShadedDarkerThanCoastAtTheCentre()
    {
        float Centre(string name)
        {
            var mesh = TerrainRelief.Build(TerrainRelief.MeshKey(name));
            for (var i = 0; i < mesh.VertexCount; i++)
            {
                if (mesh.Positions[i * 3] == 0f && mesh.Positions[i * 3 + 2] == 0f && mesh.Positions[i * 3 + 1] > 0f) return mesh.Colors![i * 4];
            }
            throw new InvalidOperationException();
        }
        Assert.True(Centre("Ocean") < Centre("Coast"));
    }

    [Fact]
    public void KeyRoundTripsAndTuftsScatterInsideTheTile()
    {
        Assert.Equal("Hill", TerrainRelief.TerrainOf(TerrainRelief.MeshKey("Hill")));
        Assert.Null(TerrainRelief.TerrainOf(RulesetAssetKeys.HexMesh));
        Assert.Equal(AssetKind.Mesh, AssetKinds.Of(VegetationGeometry.TuftMeshKey));
        Assert.Equal(7, VegetationGeometry.TuftsFor("Grassland"));
        Assert.Equal(0, VegetationGeometry.TuftsFor("Ocean"));
        for (var i = 0; i < 7; i++)
        {
            var (x, z, _, scale) = VegetationGeometry.Placement(3, -2, i);
            Assert.True(TerrainRelief.RimFraction(x, z) < 0.9f);
            Assert.InRange(scale, 0.8f, 1.4f);
            Assert.Equal(VegetationGeometry.Placement(3, -2, i), VegetationGeometry.Placement(3, -2, i));
        }
        Assert.Equal(3, VegetationGeometry.Tuft().TriangleCount);
    }
}
