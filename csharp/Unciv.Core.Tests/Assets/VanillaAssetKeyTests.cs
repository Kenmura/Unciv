using Unciv.Assets;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Assets;

/// <summary>C007: every key the client can derive from the Vanilla ruleset resolves, and its procedural asset can be generated.</summary>
[Collection("RulesetCache")]
public sealed class VanillaAssetKeyTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public VanillaAssetKeyTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void KeySetCoversTheMainCategories()
    {
        var keys = RulesetAssetKeys.KeysFor(ruleset);
        Assert.True(keys.Count > 500, $"only {keys.Count} keys");
        foreach (var category in new[] { "terrain", "unit", "unitType", "resource", "resourceType", "improvement", "building", "icon/stat", "icon/tech", "icon/policy", "icon/promotion" })
        {
            Assert.Contains(keys, k => k.Category == category && !k.IsDefault);
        }
        Assert.Contains(new AssetKey("terrain", "Grassland"), keys);
        Assert.Contains(new AssetKey("unit", "Warrior"), keys);
        Assert.Contains(new AssetKey("resource", "Horses"), keys);
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void UnitsFallBackToTheirUnitType()
    {
        var chain = AssetFallbacks.Chain(new AssetKey("unit", "Warrior"), RulesetAssetKeys.FallbacksFor(ruleset));
        Assert.Equal(["unit/Warrior", "unitType/Sword", "unit/_default"], chain.Select(k => k.ToString()));
        var resource = AssetFallbacks.Chain(new AssetKey("resource", "Horses"), RulesetAssetKeys.FallbacksFor(ruleset));
        Assert.Equal(["resource/Horses", "resourceType/Strategic", "resource/_default"], resource.Select(k => k.ToString()));
    }

    [Fact]
    public void EveryVanillaKeyResolvesAndGenerates()
    {
        var resolver = new AssetResolver(new MemoryAssetFiles(), RulesetAssetKeys.FallbacksFor(ruleset));
        foreach (var key in RulesetAssetKeys.KeysFor(ruleset))
        {
            var resolution = resolver.Resolve(key);
            Assert.Equal(AssetSourceKind.Procedural, resolution.Source);
            switch (AssetKinds.Of(key))
            {
                case AssetKind.Icon:
                    Assert.Contains(ProceduralImages.Icon(key).Rgba.Where((_, i) => i % 4 == 3), a => a == 255);
                    break;
                case AssetKind.UnitBlock:
                    Assert.NotEmpty(ModelSpecs.Unit(key).Primitives);
                    break;
                case AssetKind.Prop:
                    Assert.NotEmpty(ModelSpecs.Prop(key).Primitives);
                    break;
                case AssetKind.TerrainMaterial:
                    Assert.NotEmpty(ProceduralImages.Noise(key).Rgba);
                    break;
            }
        }
    }

    [Fact]
    public void PlaceholdersAreDistinguishableAcrossVanillaUnits()
    {
        var colours = ruleset.Units.Keys.Select(n => ModelSpecs.Unit(AssetKey.Of("unit", n)).Primitives[0].Colour).Distinct().Count();
        Assert.True(colours > ruleset.Units.Count / 2, $"{colours} distinct colours for {ruleset.Units.Count} units");
    }

    [Fact]
    public void EveryVanillaTerrainHasAPaletteColour()
    {
        foreach (var name in ruleset.Terrains.Values.Where(t => t.Type is Unciv.Models.Ruleset.Tile.TerrainType.Land or Unciv.Models.Ruleset.Tile.TerrainType.Water).Select(t => t.Name))
        {
            Assert.NotEqual(default, PlaceholderPalette.ForTerrain(name));
        }
    }

    [Fact]
    public void ScoutAndSiegeTypesGetTheirTableShapes()
    {
        Assert.Equal(Unciv.Assets.PrimitiveShape.Cone, ModelSpecs.Unit(AssetKey.Of("unitType", "Scout")).Primitives[0].Shape);
        Assert.Equal(Unciv.Assets.PrimitiveShape.Box, ModelSpecs.Unit(AssetKey.Of("unitType", "Sword")).Primitives[0].Shape);
    }
}
