using Unciv.Assets;
using Unciv.Models.Ruleset;
using Unciv.Models.StatTypes;
using Unciv.Net.Visibility;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Assets;

/// <summary>C032: improvement and city models and the in-world tile yield icons.</summary>
[Collection("RulesetCache")]
public sealed class WorldPropsTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public WorldPropsTests()
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

    private static TileDto Tile(string terrain = "Grassland", string[]? features = null, string? wonder = null, string? resource = null, string? improvement = null, bool? pillaged = null, bool visible = true) => new()
    {
        X = 0,
        Y = 0,
        Visible = visible,
        BaseTerrain = terrain,
        Features = [.. features ?? []],
        NaturalWonder = wonder,
        Resource = resource,
        Improvement = improvement,
        ImprovementPillaged = pillaged,
        RiverBottom = false,
        RiverBottomLeft = false,
        RiverBottomRight = false,
    };

    private static readonly string[] NonModels = ["Road", "Railroad", "Remove Forest", "Remove Jungle", "Remove Fallout", "Remove Marsh", "Remove Road", "Remove Railroad", "Cancel improvement order", "Repair"];

    [Fact]
    public void EveryVanillaImprovementThatIsAWorldObjectHasADesignedModel()
    {
        foreach (var name in ruleset.TileImprovements.Keys.Where(it => !NonModels.Contains(it)))
        {
            if (name == "City center") continue; // built by CitySpecs
            Assert.True(ImprovementSpecs.HasDesign(name), name);
        }
    }

    [Fact]
    public void DesignedModelsStandOnTheGroundInsideTheirFootprint()
    {
        foreach (var name in ruleset.TileImprovements.Keys.Where(it => !NonModels.Contains(it) && it != "City center"))
        {
            var spec = ModelSpecs.Prop(AssetKey.Of(RulesetAssetKeys.Improvement, name));
            Assert.NotEmpty(spec.Primitives);
            foreach (var p in spec.Primitives)
            {
                Assert.True(p.Y - p.SizeY / 2f >= -0.001f, $"{name} sinks below the ground");
                Assert.True(MathF.Sqrt(p.X * p.X + p.Z * p.Z) <= ImprovementSpecs.Footprint + 0.01f, $"{name} leaves its footprint at ({p.X}, {p.Z})");
            }
        }
    }

    [Fact]
    public void AModWithAnUnknownImprovementGetsTheGenericPropAndModelsAreDeterministic()
    {
        var mod = AssetKey.Of(RulesetAssetKeys.Improvement, "Orbital elevator");
        Assert.False(ImprovementSpecs.HasDesign("Orbital elevator"));
        Assert.Null(ImprovementSpecs.TryBuild(mod));
        Assert.NotEmpty(ModelSpecs.Prop(mod).Primitives);
        var farm = AssetKey.Of(RulesetAssetKeys.Improvement, "Farm");
        Assert.Equal(ModelSpecs.Prop(farm).Primitives, ModelSpecs.Prop(farm).Primitives);
        Assert.Null(ImprovementSpecs.TryBuild(AssetKey.Of(RulesetAssetKeys.Building, "Farm")));
    }

    [Fact]
    public void ImprovementsLookDifferentFromEachOther()
    {
        var names = new[] { "Farm", "Mine", "Pasture", "Camp", "Quarry", "Fort", "Citadel", "Fishing Boats", "Plantation" };
        var shapes = names.Select(it => string.Join("|", ModelSpecs.Prop(AssetKey.Of(RulesetAssetKeys.Improvement, it)).Primitives)).ToList();
        Assert.Equal(names.Length, shapes.Distinct().Count());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(9, 2)]
    [InlineData(10, 3)]
    [InlineData(30, 3)]
    public void CityTierFollowsPopulation(int population, int tier) => Assert.Equal(tier, CitySpecs.TierForPopulation(population));

    [Fact]
    public void CityModelsGrowWithTierAndAreKeyedByTier()
    {
        Assert.Equal("City center", CitySpecs.KeyForTier(1).Name);
        Assert.Equal("City center 2", CitySpecs.KeyForTier(2).Name);
        Assert.Equal("City center 3", CitySpecs.KeyForTier(3).Name);
        Assert.Equal(CitySpecs.KeyForTier(3), CitySpecs.KeyFor(12));
        Assert.Equal(CitySpecs.KeyForTier(1), CitySpecs.KeyForTier(0));
        var counts = Enumerable.Range(1, CitySpecs.TierCount).Select(t => ModelSpecs.Prop(CitySpecs.KeyForTier(t)).Primitives.Count).ToList();
        Assert.True(counts[0] < counts[1] && counts[1] < counts[2], string.Join(",", counts));
        Assert.Null(CitySpecs.TierOf("Farm"));
        Assert.Equal(1, CitySpecs.TierOf("City center"));
        Assert.Equal(2, CitySpecs.TierOf("City center 2"));
        Assert.True(CitySpecs.FlagHeight(1) < CitySpecs.FlagHeight(2) && CitySpecs.FlagHeight(2) < CitySpecs.FlagHeight(3));
        Assert.Equal(CitySpecs.FlagHeight(3), CitySpecs.FlagHeight(9));
        foreach (var p in CitySpecs.Build(3).Primitives.Skip(1)) Assert.True(MathF.Sqrt(p.X * p.X + p.Z * p.Z) <= 0.95f);
    }

    [Fact]
    public void LargerCityKeysAreAmongTheRulesetKeys() =>
        Assert.Contains(CitySpecs.KeyForTier(3), RulesetAssetKeys.KeysFor(ruleset));

    [Fact]
    public void GrasslandYieldsTwoFood()
    {
        var chips = TileYieldModel.Chips(ruleset, Tile());
        Assert.Equal([new YieldChip(Stat.Food, 2)], chips);
    }

    [Fact]
    public void ForestOverridesTheTerrainUnderIt()
    {
        var stats = TileYieldModel.BaseStats(ruleset, Tile("Plains", ["Forest"]));
        Assert.Equal(1f, stats.Production);
        Assert.Equal(1f, stats.Food);
    }

    [Fact]
    public void ImprovementAddsItsYieldUnlessPillaged()
    {
        var farm = TileYieldModel.BaseStats(ruleset, Tile(improvement: "Farm"));
        var bare = TileYieldModel.BaseStats(ruleset, Tile());
        Assert.True(farm.Food > bare.Food);
        var pillaged = TileYieldModel.BaseStats(ruleset, Tile(improvement: "Farm", pillaged: true));
        Assert.Equal(bare.Food, pillaged.Food);
    }

    [Fact]
    public void AResourceAddsItsBonusWithTheRightImprovementOnly()
    {
        var bare = TileYieldModel.BaseStats(ruleset, Tile("Plains", resource: "Wheat"));
        Assert.True(bare.Food >= 2f);
        var wrong = TileYieldModel.BaseStats(ruleset, Tile("Plains", resource: "Wheat", improvement: "Mine"));
        var right = TileYieldModel.BaseStats(ruleset, Tile("Plains", resource: "Wheat", improvement: "Farm"));
        Assert.True(right.Food > wrong.Food);
        Assert.Equal(TileYieldModel.BaseStats(ruleset, Tile("Plains", resource: "Wheat")).Food + ruleset.TileImprovements["Mine"].Food, wrong.Food);
        var unseen = TileYieldModel.BaseStats(ruleset, Tile("Plains", improvement: "Farm"));
        Assert.True(unseen.Food < right.Food);
    }

    [Fact]
    public void ACityCentreIsRaisedToTheMinimum()
    {
        var stats = TileYieldModel.BaseStats(ruleset, Tile("Desert", improvement: "City center"));
        Assert.True(stats.Food >= 2f && stats.Production >= 1f);
    }

    [Fact]
    public void UnknownNamesAndEmptyTilesGiveNoChips()
    {
        Assert.Empty(TileYieldModel.Chips(ruleset, Tile("Lava world", resource: "Unobtainium", improvement: "Orbital elevator")));
        Assert.Empty(TileYieldModel.Chips(ruleset, Tile("Mountain")));
    }

    [Fact]
    public void ChipsAreCappedAndSortedBiggestFirst()
    {
        var chips = TileYieldModel.Chips(ruleset, Tile("Plains", resource: "Wheat", improvement: "Farm"));
        Assert.InRange(chips.Count, 1, TileYieldModel.MaxChips);
        for (var i = 1; i < chips.Count; i++) Assert.True(System.Math.Abs(chips[i - 1].Amount) >= System.Math.Abs(chips[i].Amount));
    }

    [Fact]
    public void NegativeYieldsSortByMagnitudeNotSign()
    {
        // Desert gives nothing, Marsh -1 food, Oil +1 production: the loss ties the gain and keeps stat order, it does not sink to the end
        var chips = TileYieldModel.Chips(ruleset, Tile("Desert", features: ["Marsh"], resource: "Oil"));
        Assert.Equal([new YieldChip(Stat.Food, -1), new YieldChip(Stat.Production, 1)], chips);
    }

    [Theory]
    [InlineData(0.5f, 1)]
    [InlineData(-0.5f, -1)]
    [InlineData(1.4f, 1)]
    [InlineData(2.5f, 3)]
    public void YieldsRoundHalfAwayFromZero(float amount, int whole) => Assert.Equal(whole, TileYieldModel.Whole(amount));

    [Fact]
    public void ChipRowIsCentredOnTheTile()
    {
        foreach (var count in new[] { 1, 2, 3, 4 })
        {
            var xs = Enumerable.Range(0, count).Select(i => TileYieldModel.ChipOffset(i, count).X).ToList();
            Assert.Equal(0f, xs.Sum(), 1e-4);
            Assert.Equal(count, xs.Distinct().Count());
        }
    }
}
