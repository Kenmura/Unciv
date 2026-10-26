namespace Unciv.Assets;

/// <summary>
/// C032 city models. Three size tiers (village, town, city), each a cluster of houses around a larger central building, standing on y = 0 and staying inside radius 0.8.
/// The tier is a key of its own (<c>improvement/City center</c>, <c>improvement/City center 2</c>, <c>improvement/City center 3</c>) so real art can replace each size by key.
/// The civ colour is not part of the model: the client adds a flag in the owner's colour on top.
/// </summary>
public static class CitySpecs
{
    public const int TierCount = 3;

    private const string BaseName = "City center";

    /// <summary>Highest tier a city of this population shows: 1-4 a village, 5-9 a town, 10 and up a city.</summary>
    public static int TierForPopulation(int population) => population >= 10 ? 3 : population >= 5 ? 2 : 1;

    /// <summary>The key of a tier's model.</summary>
    public static AssetKey KeyForTier(int tier) => AssetKey.Of(RulesetAssetKeys.Improvement, tier <= 1 ? BaseName : BaseName + " " + Math.Clamp(tier, 2, TierCount).ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static AssetKey KeyFor(int population) => KeyForTier(TierForPopulation(population));

    /// <summary>The tier a key name stands for, or null when the name is not a city model.</summary>
    public static int? TierOf(string name)
    {
        if (name == BaseName) return 1;
        for (var tier = 2; tier <= TierCount; tier++)
        {
            if (name == KeyForTier(tier).Name) return tier;
        }
        return null;
    }

    /// <summary>Height above the model's base at which the civ flag should fly, per tier.</summary>
    public static float FlagHeight(int tier) => tier switch { 1 => 0.75f, 2 => 0.95f, _ => 1.2f };

    private static readonly Rgb Wall = PlaceholderPalette.CityWall;
    private static readonly Rgb Roof = new(170, 95, 70);

    public static ModelSpec Build(int tier)
    {
        tier = Math.Clamp(tier, 1, TierCount);
        var parts = new List<PrimitiveSpec> { new(PrimitiveShape.Cylinder, 0f, 0.02f, 0f, 1.5f, 0.04f, 1.5f, Wall.Scale(70)) };
        // The keep in the middle grows with the tier
        var keepWidth = 0.34f + 0.08f * tier;
        var keepHeight = 0.3f + 0.16f * tier;
        parts.Add(new PrimitiveSpec(PrimitiveShape.Box, 0f, 0.04f + keepHeight / 2f, 0f, keepWidth, keepHeight, keepWidth, Wall));
        parts.Add(new PrimitiveSpec(PrimitiveShape.Cone, 0f, 0.04f + keepHeight + 0.12f, 0f, keepWidth + 0.1f, 0.24f, keepWidth + 0.1f, Roof));
        // Houses on a ring; more of them in bigger cities, each a little different
        var houses = 3 + 2 * tier;
        for (var i = 0; i < houses; i++)
        {
            var angle = MathF.Tau * i / houses + 0.3f;
            var radius = 0.5f + 0.04f * tier + (i % 2) * 0.1f;
            var size = 0.2f + (i % 3) * 0.03f;
            var height = 0.14f + (i % 3) * 0.04f + 0.03f * tier;
            var x = MathF.Cos(angle) * radius;
            var z = MathF.Sin(angle) * radius;
            parts.Add(new PrimitiveSpec(PrimitiveShape.Box, x, 0.04f + height / 2f, z, size, height, size, Wall.Scale(90 + (i % 4) * 6), angle * 57.29578f));
            parts.Add(new PrimitiveSpec(PrimitiveShape.Cone, x, 0.04f + height + 0.05f, z, size + 0.06f, 0.12f, size + 0.06f, Roof.Scale(90 + (i % 3) * 8)));
        }
        return new ModelSpec(parts, "", 0f);
    }
}
