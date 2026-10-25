using Unciv.Assets;
using Unciv.Models.Ruleset;
using Unciv.Net.Visibility;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Assets;

/// <summary>C004: figure count by strength tier, formations, health bar, status marks and the per-type models.</summary>
[Collection("RulesetCache")]
public sealed class UnitVisualsTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public UnitVisualsTests()
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

    private static UnitDto Unit(int bucket = 4, bool fortified = false, OwnUnitDto? own = null) =>
        new() { Id = 1, Name = "Warrior", Owner = "Rome", X = 0, Y = 0, HealthBucket = bucket, IsEmbarked = false, IsFortified = fortified, Own = own };

    private static OwnUnitDto Own(int health = 100, string? action = null, bool automated = false, bool promotable = false) =>
        new() { Health = health, CurrentMovement = 2, Xp = 0, Action = action, Promotions = [], Automated = automated, CanBePromoted = promotable };

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(11, 0, 2)]
    [InlineData(25, 0, 2)]
    [InlineData(26, 0, 3)]
    [InlineData(45, 0, 3)]
    [InlineData(46, 0, 4)]
    [InlineData(200, 0, 4)]
    [InlineData(5, 30, 3)] // ranged strength counts when higher
    [InlineData(40, 12, 3)] // and melee when higher
    public void LandMilitaryFiguresFollowTheStrengthTiers(int strength, int ranged, int expected) =>
        Assert.Equal(expected, UnitVisuals.FigureCount(strength, ranged, isLandMilitary: true));

    [Fact]
    public void NonLandMilitaryUnitsAreOneFigureWhateverTheirStrength() =>
        Assert.Equal(1, UnitVisuals.FigureCount(80, 80, isLandMilitary: false));

    [Fact]
    public void TheTierThresholdsAreAscendingAndSizeTheMaximum()
    {
        Assert.Equal(TierOrder(UnitVisuals.TierThresholds), UnitVisuals.TierThresholds);
        Assert.Equal(UnitVisuals.TierThresholds.Count + 1, UnitVisuals.MaxFigures);
    }

    private static IReadOnlyList<int> TierOrder(IReadOnlyList<int> list) => [.. list.Order()];

    [Fact]
    public void VanillaUnitsGetTheFiguresOfTheirKind()
    {
        int Figures(string name) => UnitVisuals.FigureCount(ruleset.Units[name], ruleset);
        Assert.Equal(1, Figures("Settler"));
        Assert.Equal(1, Figures("Worker"));
        Assert.Equal(1, Figures("Warrior")); // 6
        Assert.Equal(1, Figures("Archer")); // ranged 6
        Assert.Equal(1, Figures("Spearman")); // 7
        Assert.Equal(2, Figures("Longswordsman")); // 16
        Assert.Equal(4, Figures("Tank")); // 50
        Assert.Equal(1, Figures("Trireme"));
        Assert.Equal(1, Figures("Destroyer"));
        Assert.Equal(1, Figures("Fighter"));
        Assert.Equal(1, Figures("Great Artist")); // civilian
        Assert.Equal(4, Figures("Giant Death Robot"));
        foreach (var unit in ruleset.Units.Values)
            Assert.InRange(UnitVisuals.FigureCount(unit, ruleset), 1, UnitVisuals.MaxFigures);
    }

    [Fact]
    public void FormationHasOnePlacementPerFigureInsideTheTile()
    {
        for (var count = 1; count <= UnitVisuals.MaxFigures; count++)
        {
            var formation = UnitVisuals.Formation(count);
            Assert.Equal(count, formation.Count);
            Assert.Equal(count, formation.Select(it => (it.X, it.Z)).Distinct().Count());
            Assert.All(formation, it =>
            {
                Assert.InRange(MathF.Sqrt(it.X * it.X + it.Z * it.Z), 0f, 0.4f);
                Assert.InRange(it.Scale, 0.5f, 1f);
            });
        }
        Assert.Equal(new FigurePlacement(0f, 0f, 1f), Assert.Single(UnitVisuals.Formation(1)));
    }

    [Fact]
    public void FiguresShrinkAsTheyMultiplyAndTheCountIsClamped()
    {
        Assert.True(UnitVisuals.Formation(2)[0].Scale < UnitVisuals.Formation(1)[0].Scale);
        Assert.True(UnitVisuals.Formation(4)[0].Scale < UnitVisuals.Formation(3)[0].Scale);
        Assert.Single(UnitVisuals.Formation(0));
        Assert.Single(UnitVisuals.Formation(-5));
        Assert.Equal(UnitVisuals.MaxFigures, UnitVisuals.Formation(99).Count);
    }

    [Fact]
    public void FormationPlacementsAreExact()
    {
        Assert.Equal([new FigurePlacement(-0.2f, 0f, 0.8f), new FigurePlacement(0.2f, 0f, 0.8f)], UnitVisuals.Formation(2));
        Assert.Equal([new FigurePlacement(-0.22f, 0.14f, 0.72f), new FigurePlacement(0.22f, 0.14f, 0.72f), new FigurePlacement(0f, -0.2f, 0.72f)], UnitVisuals.Formation(3));
        Assert.Equal(0.65f, UnitVisuals.Formation(4)[3].Scale);
        Assert.Equal(new FigurePlacement(0.22f, -0.2f, 0.65f), UnitVisuals.Formation(4)[3]);
    }

    [Fact]
    public void ABucketOfZeroIsAnEmptyBar()
    {
        var bar = UnitVisuals.HealthBar(Unit(bucket: 0));
        Assert.True(bar.Visible);
        Assert.Equal(0f, bar.Fraction);
    }

    [Fact]
    public void StatusIconsAreOrderedAndOwnerOnlyExceptFortified()
    {
        Assert.Empty(UnitVisuals.StatusIcons(Unit()));
        Assert.Equal([UnitStatusIcon.Fortified], UnitVisuals.StatusIcons(Unit(fortified: true)));
        Assert.Equal([UnitStatusIcon.Sleeping], UnitVisuals.StatusIcons(Unit(own: Own(action: "Sleep"))));
        Assert.Equal([UnitStatusIcon.Sleeping], UnitVisuals.StatusIcons(Unit(own: Own(action: "Sleep until healed"))));
        Assert.Equal([UnitStatusIcon.Automated], UnitVisuals.StatusIcons(Unit(own: Own(automated: true))));
        Assert.Equal([UnitStatusIcon.Automated], UnitVisuals.StatusIcons(Unit(own: Own(action: "Explore"))));
        Assert.Equal([UnitStatusIcon.PromotionAvailable], UnitVisuals.StatusIcons(Unit(own: Own(promotable: true))));
        Assert.Equal([UnitStatusIcon.Fortified, UnitStatusIcon.PromotionAvailable], UnitVisuals.StatusIcons(Unit(fortified: true, own: Own(action: "Fortify", promotable: true))));
        Assert.Equal([UnitStatusIcon.Sleeping, UnitStatusIcon.Automated, UnitStatusIcon.PromotionAvailable], UnitVisuals.StatusIcons(Unit(own: Own(action: "Sleep", automated: true, promotable: true))));
        Assert.Empty(UnitVisuals.StatusIcons(Unit(own: Own(action: "moveTo 3,4"))));
        Assert.Empty(UnitVisuals.StatusIcons(Unit(own: Own(action: "Slept"))));
    }

    [Fact]
    public void EveryStatusIconHasAnAssetKeyAndItsOwnColour()
    {
        var keys = Enum.GetValues<UnitStatusIcon>().Select(UnitVisuals.IconKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, key => Assert.Equal(RulesetAssetKeys.StatusIcon, key.Category));
        Assert.Equal(keys.Count, keys.Select(ProceduralImages.IconColour).Distinct().Count());
        Assert.All(RulesetAssetKeys.KeysFor(ruleset).Where(it => keys.Contains(it)), _ => { });
        Assert.All(keys, key => Assert.Contains(key, RulesetAssetKeys.KeysFor(ruleset)));
    }

    [Fact]
    public void HealthBarIsHiddenAtFullHealthAndColouredByHowHurtTheUnitIs()
    {
        Assert.False(UnitVisuals.HealthBar(Unit(bucket: 4)).Visible);
        Assert.False(UnitVisuals.HealthBar(Unit(own: Own(health: 100))).Visible);
        var three = UnitVisuals.HealthBar(Unit(bucket: 3));
        Assert.True(three.Visible);
        Assert.Equal(0.75f, three.Fraction);
        Assert.Equal(PlaceholderPalette.HealthGood, three.Fill);
        Assert.Equal(PlaceholderPalette.HealthWarn, UnitVisuals.HealthBar(Unit(bucket: 2)).Fill);
        Assert.Equal(PlaceholderPalette.HealthBad, UnitVisuals.HealthBar(Unit(bucket: 1)).Fill);
    }

    [Fact]
    public void OwnHealthIsExactAndTheColourBreaksAtThirds()
    {
        Assert.Equal(0.67f, UnitVisuals.HealthBar(Unit(own: Own(health: 67))).Fraction);
        Assert.Equal(PlaceholderPalette.HealthGood, UnitVisuals.HealthBar(Unit(own: Own(health: 67))).Fill);
        Assert.Equal(PlaceholderPalette.HealthWarn, UnitVisuals.HealthBar(Unit(own: Own(health: 66))).Fill);
        Assert.Equal(PlaceholderPalette.HealthWarn, UnitVisuals.HealthBar(Unit(own: Own(health: 34))).Fill);
        Assert.Equal(PlaceholderPalette.HealthBad, UnitVisuals.HealthBar(Unit(own: Own(health: 33))).Fill);
        Assert.Equal(0f, UnitVisuals.HealthBar(Unit(own: Own(health: -7))).Fraction);
        Assert.False(UnitVisuals.HealthBar(Unit(own: Own(health: 250))).Visible);
    }

    [Fact]
    public void HealthBarPictureFillsTheLeftPartOfTheInside()
    {
        int FilledPixels(float fraction)
        {
            var image = ProceduralImages.HealthBar(fraction, PlaceholderPalette.HealthGood);
            var row = image.Height / 2;
            return Enumerable.Range(1, image.Width - 2).Count(x =>
            {
                var o = (row * image.Width + x) * 4;
                return image.Rgba[o] == PlaceholderPalette.HealthGood.R && image.Rgba[o + 1] == PlaceholderPalette.HealthGood.G && image.Rgba[o + 2] == PlaceholderPalette.HealthGood.B;
            });
        }
        Assert.Equal(0, FilledPixels(0f));
        Assert.Equal(1, FilledPixels(0.001f));
        Assert.Equal(19, FilledPixels(0.5f));
        Assert.Equal(38, FilledPixels(1f));
        Assert.Equal(38, FilledPixels(7f));
        var half = ProceduralImages.HealthBar(0.5f, PlaceholderPalette.HealthGood);
        Assert.Equal(40 * 6 * 4, half.Rgba.Length);
        Assert.Equal(PlaceholderPalette.IconOutline.R, half.Rgba[0]); // rim
        Assert.Equal(PlaceholderPalette.HealthGood.G, half.Rgba[(3 * 40 + 1) * 4 + 1]); // first inner pixel is filled
        Assert.Equal(PlaceholderPalette.HealthBack.G, half.Rgba[(3 * 40 + 38) * 4 + 1]); // last inner pixel is empty
    }

    [Fact]
    public void EveryVanillaUnitTypeHasItsOwnAccessoriesAndStaysInsideTheFigureBounds()
    {
        var signatures = new Dictionary<string, string>();
        foreach (var type in ruleset.UnitTypes.Keys)
        {
            var spec = ModelSpecs.Unit(new AssetKey("unitType", type));
            Assert.True(spec.Primitives.Count >= 2, type + " has no accessories");
            Assert.All(spec.Primitives, p =>
            {
                Assert.True(p.Y - p.SizeY / 2f >= -0.001f, type + " sinks below the tile");
                Assert.True(p.Y + p.SizeY / 2f <= spec.BillboardHeight + 0.001f, type + " reaches the billboard");
                Assert.InRange(MathF.Abs(p.X) + p.SizeX / 2f, 0f, 0.5f);
                Assert.InRange(MathF.Abs(p.Z) + p.SizeZ / 2f, 0f, 0.5f);
            });
            signatures[type] = string.Join("|", spec.Primitives.Select(p => $"{p.Shape}{p.X}{p.Y}{p.Z}{p.SizeX}{p.SizeY}{p.SizeZ}"));
        }
        // Rulesets name the same type two ways (Melee/Sword, WaterMelee/Melee Water, ...): the pairs share a model, every other pair of families differs
        Assert.True(signatures.Values.Distinct().Count() >= 17, "only " + signatures.Values.Distinct().Count() + " distinct models");
        foreach (var (a, b) in new[] { ("Melee", "Sword"), ("Ranged", "Archery"), ("Armor", "Armored"), ("WaterMelee", "Melee Water"), ("WaterSubmarine", "Submarine"), ("WaterCivilian", "Civilian Water"), ("AtomicBomber", "Atomic Bomber") })
            Assert.Equal(signatures[a], signatures[b]);
    }

    [Fact]
    public void UnitModelBodyStaysFirstAndIsDeterministic()
    {
        var a = ModelSpecs.Unit(new AssetKey("unit", "Archer"), "Archery");
        var b = ModelSpecs.Unit(new AssetKey("unit", "Archer"), "Archery");
        Assert.Equal(a.Primitives, b.Primitives);
        Assert.Equal(PrimitiveShape.Cylinder, a.Primitives[0].Shape);
        Assert.Equal(PlaceholderPalette.UnitSkin, a.Primitives[1].Colour);
        Assert.Single(ModelSpecs.Unit(new AssetKey("unit", "Strange"), "No such type").Primitives);
        Assert.Single(ModelSpecs.Unit(new AssetKey("unit", "Strange")).Primitives);
    }

    [Fact]
    public void ShipsAndAircraftDifferFromLandUnitsOfTheSameBody()
    {
        var sword = ModelSpecs.Unit(new AssetKey("unitType", "Melee")).Primitives;
        var ship = ModelSpecs.Unit(new AssetKey("unitType", "WaterMelee")).Primitives;
        var plane = ModelSpecs.Unit(new AssetKey("unitType", "Fighter")).Primitives;
        Assert.NotEqual(sword.Skip(1), ship.Skip(1));
        Assert.DoesNotContain(ship, p => p.Colour == PlaceholderPalette.UnitSkin); // nobody stands on the deck
        Assert.Contains(plane, p => p.Shape == PrimitiveShape.Box && p.SizeX > 0.6f); // wings
    }
}
