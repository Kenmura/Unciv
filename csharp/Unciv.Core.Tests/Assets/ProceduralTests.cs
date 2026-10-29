using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

public class ProceduralTests
{
    [Fact]
    public void StableHashMatchesKnownFnvVectors()
    {
        Assert.Equal(2166136261u, StableHash.Of(""));
        Assert.Equal(0xE40C292Cu, StableHash.Of("a"));
        Assert.Equal(0xBF9CF968u, StableHash.Of("foobar"));
    }

    [Fact]
    public void MixIsDeterministicAndSpreads()
    {
        Assert.Equal(StableHash.Mix(5, 1, 2), StableHash.Mix(5, 1, 2));
        Assert.NotEqual(StableHash.Mix(5, 1, 2), StableHash.Mix(5, 2, 1));
    }

    [Theory]
    [InlineData(0, 255, 255, 255, 0, 0)]
    [InlineData(120, 255, 255, 0, 255, 0)]
    [InlineData(240, 255, 255, 0, 0, 255)]
    [InlineData(60, 255, 255, 255, 255, 0)]
    [InlineData(180, 255, 255, 0, 255, 255)]
    [InlineData(300, 255, 255, 255, 0, 255)]
    [InlineData(0, 0, 128, 128, 128, 128)]
    [InlineData(-60, 255, 255, 255, 0, 255)]
    [InlineData(420, 255, 255, 255, 255, 0)]
    public void HsvConversion(int h, int s, int v, int r, int g, int b)
    {
        var c = Rgb.FromHsv(h, s, v);
        Assert.Equal(((byte)r, (byte)g, (byte)b), (c.R, c.G, c.B));
    }

    [Fact]
    public void HsvClampsInputs() => Assert.Equal(new Rgb(255, 0, 0), Rgb.FromHsv(0, 999, 999));

    [Fact]
    public void ScaleClamps()
    {
        Assert.Equal(new Rgb(255, 100, 0), new Rgb(200, 80, 0).Scale(125) with { R = 255 });
        Assert.Equal(new Rgb(100, 50, 0), new Rgb(200, 100, 0).Scale(50));
        Assert.Equal(new Rgb(255, 255, 255), new Rgb(200, 200, 200).Scale(500));
    }

    [Fact]
    public void KnownTerrainUsesTableOthersUseHash()
    {
        Assert.Equal(new Rgb(100, 170, 70), PlaceholderPalette.ForTerrain("Grassland"));
        Assert.Equal(PlaceholderPalette.ForTerrain("Modded Swamp"), PlaceholderPalette.ForTerrain("Modded Swamp"));
        Assert.NotEqual(PlaceholderPalette.ForTerrain("Modded Swamp"), PlaceholderPalette.ForTerrain("Other Mod Terrain"));
        Assert.Equal(new Rgb(235, 190, 40), PlaceholderPalette.ForStat("Gold"));
        Assert.NotEqual(PlaceholderPalette.ForStat("Gold"), PlaceholderPalette.ForStat("Whatever"));
    }

    [Fact]
    public void HexPrismTopFaceIsFlatAndWindingIsClockwise()
    {
        var mesh = HexGeometry.Prism(2f, 0.5f);
        Assert.Equal(7 + 6 * 4, mesh.VertexCount);
        Assert.Equal(6 + 6 * 2, mesh.TriangleCount);
        Assert.Equal(mesh.VertexCount * 2, mesh.Uvs.Length);
        Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.VertexCount - 1));
        // Godot treats clockwise-from-outside as front, so (b-a)x(c-a) must oppose the declared normal for every triangle.
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            int a = mesh.Indices[t * 3], b = mesh.Indices[t * 3 + 1], c = mesh.Indices[t * 3 + 2];
            float[] u = [mesh.Positions[b * 3] - mesh.Positions[a * 3], mesh.Positions[b * 3 + 1] - mesh.Positions[a * 3 + 1], mesh.Positions[b * 3 + 2] - mesh.Positions[a * 3 + 2]];
            float[] v = [mesh.Positions[c * 3] - mesh.Positions[a * 3], mesh.Positions[c * 3 + 1] - mesh.Positions[a * 3 + 1], mesh.Positions[c * 3 + 2] - mesh.Positions[a * 3 + 2]];
            var cross = new[] { u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0] };
            var dot = cross[0] * mesh.Normals[a * 3] + cross[1] * mesh.Normals[a * 3 + 1] + cross[2] * mesh.Normals[a * 3 + 2];
            Assert.True(dot < 0f, $"triangle {t} is not clockwise from outside");
        }
    }

    [Fact]
    public void HexPrismSpansRequestedExtent()
    {
        var mesh = HexGeometry.Prism(3f, 0.25f);
        var xs = Enumerable.Range(0, mesh.VertexCount).Select(i => mesh.Positions[i * 3]).ToList();
        var ys = Enumerable.Range(0, mesh.VertexCount).Select(i => mesh.Positions[i * 3 + 1]).ToList();
        Assert.Equal(3f, xs.Max(), 4);
        Assert.Equal(-3f, xs.Min(), 4);
        Assert.Equal(0.25f, ys.Max());
        Assert.Equal(0f, ys.Min());
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(float.NaN, 1f)]
    [InlineData(1f, float.NaN)]
    [InlineData(-1f, 1f)]
    public void HexPrismRejectsBadSizes(float radius, float height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HexGeometry.Prism(radius, height));

    [Fact]
    public void NoiseIsDeterministicGreyInRange()
    {
        var a = ProceduralImages.Noise(new AssetKey("terrain", "Grassland"));
        var b = ProceduralImages.Noise(new AssetKey("terrain", "Grassland"));
        Assert.Equal(a.Rgba, b.Rgba);
        Assert.Equal(16 * 16 * 4, a.Rgba.Length);
        for (var i = 0; i < a.Rgba.Length; i += 4)
        {
            Assert.InRange(a.Rgba[i], (byte)200, (byte)255);
            Assert.Equal(a.Rgba[i], a.Rgba[i + 1]);
            Assert.Equal(255, a.Rgba[i + 3]);
        }
        Assert.NotEqual(a.Rgba, ProceduralImages.Noise(new AssetKey("terrain", "Plains")).Rgba);
        Assert.True(a.Rgba.Where((_, i) => i % 4 == 0).Distinct().Count() > 10);
    }

    [Fact]
    public void IconsAreDeterministicNonBlankAndDistinct()
    {
        var gold = ProceduralImages.Icon(new AssetKey("icon/stat", "Gold"));
        Assert.Equal(64, gold.Width);
        Assert.Equal(64 * 64 * 4, gold.Rgba.Length);
        Assert.Equal(gold.Rgba, ProceduralImages.Icon(new AssetKey("icon/stat", "Gold")).Rgba);
        Assert.NotEqual(gold.Rgba, ProceduralImages.Icon(new AssetKey("icon/stat", "Faith")).Rgba);
        Assert.Contains(gold.Rgba.Where((_, i) => i % 4 == 3), a => a == 255);
        Assert.Contains(gold.Rgba.Where((_, i) => i % 4 == 3), a => a == 0);
    }

    [Fact]
    public void EveryIconShapeDrawsSomethingInside()
    {
        var seen = new HashSet<ProceduralImages.IconShape>();
        for (var i = 0; seen.Count < 5 && i < 500; i++)
        {
            var key = new AssetKey("icon/tech", "Tech" + i);
            var shape = ProceduralImages.ShapeFor(key);
            if (!seen.Add(shape)) continue;
            var opaque = ProceduralImages.Icon(key).Rgba.Where((_, j) => j % 4 == 3).Count(a => a == 255);
            Assert.InRange(opaque, 300, 64 * 64);
        }
        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public void DefaultIconIsASquare()
    {
        var img = ProceduralImages.Icon(new AssetKey("icon/tech", "_default"));
        Assert.Equal(255, img.Rgba[(2 * 64 + 2) * 4 + 3]);
    }

    [Fact]
    public void InitialsRules()
    {
        Assert.Equal("WA", GlyphFont.Initials("Warrior"));
        Assert.Equal("GW", GlyphFont.Initials("Great Wall"));
        Assert.Equal("?", GlyphFont.Initials("!!!"));
        Assert.Equal("?", GlyphFont.Initials(""));
        Assert.Equal("F1", GlyphFont.Initials("Flood-1 (x)"));
        Assert.Equal("A", GlyphFont.Initials("A"));
        Assert.Equal("A", GlyphFont.Initials("Ça"));
    }

    [Fact]
    public void EveryGlyphIsFiveBySevenAndNonEmpty()
    {
        foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789?")
        {
            var rows = GlyphFont.Rows(c);
            Assert.Equal(GlyphFont.Height, rows.Length);
            Assert.All(rows, r => Assert.Equal(GlyphFont.Width, r.Length));
            Assert.Contains(rows, r => r.Contains('#', StringComparison.Ordinal));
        }
        Assert.Equal(GlyphFont.Rows('?'), GlyphFont.Rows('~'));
        Assert.Equal(GlyphFont.Rows('A'), GlyphFont.Rows('a'));
    }

    [Fact]
    public void GlyphsAreMutuallyDistinct()
    {
        var all = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789?".Select(c => string.Concat(GlyphFont.Rows(c))).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void DrawClipsInsteadOfThrowing()
    {
        var buffer = new byte[8 * 8 * 4];
        GlyphFont.Draw(buffer, 8, 8, "WWW", new Rgb(1, 2, 3), 4);
        Assert.Contains(buffer, b => b == 3);
    }

    [Fact]
    public void UnitModelsAreDeterministicAndShapedByUnitType()
    {
        Assert.Equal(PrimitiveShape.Cone, ModelSpecs.Unit(new AssetKey("unitType", "Mounted")).Primitives[0].Shape);
        Assert.Equal(PrimitiveShape.Sphere, ModelSpecs.Unit(new AssetKey("unitType", "Siege")).Primitives[0].Shape);
        Assert.Equal("WA", ModelSpecs.Unit(new AssetKey("unit", "Warrior")).Initials);
    }

    [Fact]
    public void ModelSpecEqualityIsReferenceForListSoCompareFields()
    {
        var a = ModelSpecs.Unit(new AssetKey("unit", "Warrior"));
        var b = ModelSpecs.Unit(new AssetKey("unit", "Warrior"));
        Assert.Equal(a.Primitives, b.Primitives);
        Assert.Equal(a.Initials, b.Initials);
    }

    [Fact]
    public void HorsesAreAHerdOfThreeToFivePosedAnimals()
    {
        var key = new AssetKey("resource", "Horses");
        var horses = ModelSpecs.Prop(key).Primitives;
        Assert.InRange(PropSpecs.HerdSize(key), PropSpecs.MinHerd, PropSpecs.MaxHerd);
        Assert.Equal(PropSpecs.HerdSize(key) * 3, horses.Count);
        Assert.Equal(PropSpecs.HerdSize(key), horses.Count(p => p.Shape == PrimitiveShape.Sphere));
        Assert.True(horses.Select(p => p.Yaw).Distinct().Count() > 1, "animals face different ways");
    }

    [Fact]
    public void LivestockHerdSizesStayInRangeForEveryHerdKey()
    {
        foreach (var name in new[] { "Horses", "Cattle", "Sheep", "Deer", "Bison" })
        {
            var key = new AssetKey("resource", name);
            Assert.Equal(PropSpecs.HerdSize(key) * 3, ModelSpecs.Prop(key).Primitives.Count);
        }
    }

    [Fact]
    public void PropsAreDeterministicAndStandOnTheGround()
    {
        foreach (var key in new[] { "feature/Forest", "feature/Jungle", "feature/Marsh", "resource/Horses", "resource/Wheat", "resource/Iron", "resource/Fish" })
        {
            var a = ModelSpecs.Prop(AssetKey.Parse(key)).Primitives;
            Assert.Equal(a, ModelSpecs.Prop(AssetKey.Parse(key)).Primitives);
            Assert.All(a, p => Assert.True(p.Y - p.SizeY / 2f >= -0.001f, key + " sinks below the tile"));
        }
    }

    [Fact]
    public void FeatureAndResourcePropsShareATileWithoutOverlapping()
    {
        foreach (var feature in new[] { "Forest", "Jungle", "Marsh" })
        {
            Assert.True(PropSpecs.HasFeatureProp(feature));
            foreach (var p in ModelSpecs.Prop(new AssetKey("feature", feature)).Primitives.Where(p => p.Shape != PrimitiveShape.Cylinder || p.SizeX < 0.2f))
                Assert.InRange(MathF.Sqrt(p.X * p.X + p.Z * p.Z), 0.45f, 0.8f);
        }
        foreach (var resource in new[] { "Horses", "Cattle", "Sheep", "Wheat", "Iron", "Fish" })
            Assert.All(ModelSpecs.Prop(new AssetKey("resource", resource)).Primitives, p => Assert.InRange(MathF.Sqrt(p.X * p.X + p.Z * p.Z), 0f, 0.45f));
        Assert.False(PropSpecs.HasFeatureProp("Oasis"));
    }

    [Fact]
    public void OverridingHorsesSwapsOnlyHorses()
    {
        var files = new MemoryAssetFiles().Add("assets/override/resource/Horses.tres");
        var resolver = new AssetResolver(files, new AssetFallbacks());
        Assert.Equal(AssetSourceKind.Override, resolver.Resolve(new AssetKey("resource", "Horses")).Source);
        Assert.Equal(AssetSourceKind.Procedural, resolver.Resolve(new AssetKey("resource", "Cattle")).Source);
        Assert.Equal(AssetSourceKind.Procedural, resolver.Resolve(new AssetKey("feature", "Forest")).Source);
        Assert.Equal(AssetSourceKind.Override, new AssetResolver(new MemoryAssetFiles().Add("assets/override/feature/Forest.glb"), new AssetFallbacks()).Resolve(new AssetKey("feature", "Forest")).Source);
    }

    [Fact]
    public void ResourceKindsLookDifferent()
    {
        var shapes = new[] { "Horses", "Wheat", "Iron", "Fish" }.Select(n => ModelSpecs.Prop(new AssetKey("resource", n)).Primitives.Select(p => p.Colour).Distinct().OrderBy(c => c.R).ThenBy(c => c.G).ThenBy(c => c.B).ToList()).ToList();
        Assert.Equal(4, shapes.Select(s => string.Join(",", s)).Distinct().Count());
        Assert.Equal(AssetKind.Prop, AssetKinds.Of(new AssetKey("feature", "Forest")));
    }

    [Fact]
    public void GenericPropsHaveOneToThreePrimitives()
    {
        for (var i = 0; i < 50; i++)
        {
            var spec = ModelSpecs.Prop(new AssetKey("improvement", "Imp" + i));
            Assert.InRange(spec.Primitives.Count, 1, 3);
            Assert.All(spec.Primitives, p => Assert.True(p.SizeY > 0f && p.Y > 0f));
        }
    }

    [Fact]
    public void KindsFollowCategory()
    {
        Assert.Equal(AssetKind.TerrainMaterial, AssetKinds.Of(new AssetKey("terrain", "X")));
        Assert.Equal(AssetKind.Mesh, AssetKinds.Of(RulesetAssetKeys.HexMesh));
        Assert.Equal(AssetKind.UnitBlock, AssetKinds.Of(new AssetKey("unitType", "X")));
        Assert.Equal(AssetKind.Prop, AssetKinds.Of(new AssetKey("resourceType", "X")));
        Assert.Equal(AssetKind.Theme, AssetKinds.Of(RulesetAssetKeys.UiTheme));
        Assert.Equal(AssetKind.StyleBox, AssetKinds.Of(RulesetAssetKeys.UiPanel));
        Assert.Equal(AssetKind.StyleBox, AssetKinds.Of(new AssetKey("ui", "_default")));
        Assert.Equal(AssetKind.Font, AssetKinds.Of(RulesetAssetKeys.DefaultFont));
        Assert.Equal(AssetKind.Icon, AssetKinds.Of(new AssetKey("icon/stat", "Gold")));
    }

    [Fact]
    public void BillboardIsOpaqueBackedAndSizedByText()
    {
        var one = ProceduralImages.Billboard("A");
        var two = ProceduralImages.Billboard("AB");
        Assert.True(two.Width > one.Width);
        Assert.Equal(one.Width * one.Height * 4, one.Rgba.Length);
        Assert.All(Enumerable.Range(0, one.Rgba.Length / 4), i => Assert.True(one.Rgba[i * 4 + 3] > 0));
        Assert.Equal(one.Rgba, ProceduralImages.Billboard("A").Rgba);
    }

    [Fact]
    public void ThemeMetricsArePositive() => Assert.True(UiTokens.RadiusControl > 0 && UiTokens.BorderWidth > 0 && UiTokens.S3 > 0 && UiTokens.FontSize > 0);
}
