namespace Unciv.Assets;

/// <summary>
/// C003 designed placeholder props: feature props (forest, jungle, marsh: a ring of trees or reeds around the tile centre) and resource props
/// (livestock herds of 3-5 posed animal blocks, crop stalks, ore chunks, sea shapes) built from primitives, in tile units (hex radius 1) standing on y = 0.
/// A resource prop sits on the tile centre and stays inside radius 0.45; a feature prop keeps its centres between 0.45 and 0.8, so both can share a tile.
/// Everything is a pure function of the key; real art replaces any of it by key (<c>feature/Forest</c>, <c>resource/Horses</c>, ...) with no code change.
/// </summary>
public static class PropSpecs
{
    public const int MinHerd = 3;
    public const int MaxHerd = 5;

    /// <summary>Features that get a prop; other features only tint the terrain.</summary>
    public static bool HasFeatureProp(string feature) => feature is "Forest" or "Jungle" or "Marsh";

    private static readonly Dictionary<string, (Rgb Colour, float Scale)> Herds = new(StringComparer.Ordinal)
    {
        ["Horses"] = (PlaceholderPalette.HorseBrown, 1f),
        ["Cattle"] = (new Rgb(235, 225, 205), 0.95f),
        ["Sheep"] = (new Rgb(245, 245, 240), 0.7f),
        ["Deer"] = (new Rgb(175, 125, 75), 0.85f),
        ["Bison"] = (new Rgb(95, 65, 45), 1f),
    };

    private static readonly Dictionary<string, Rgb> Crops = new(StringComparer.Ordinal)
    {
        ["Wheat"] = new(225, 190, 70),
        ["Bananas"] = new(235, 215, 80),
        ["Citrus"] = new(240, 160, 40),
        ["Sugar"] = new(205, 225, 150),
        ["Cotton"] = new(245, 245, 240),
        ["Spices"] = new(190, 90, 40),
        ["Wine"] = new(120, 40, 110),
        ["Cocoa"] = new(115, 75, 45),
        ["Dyes"] = new(150, 45, 160),
        ["Truffles"] = new(115, 95, 75),
        ["Incense"] = new(205, 175, 105),
        ["Silk"] = new(230, 200, 230),
    };

    private static readonly Dictionary<string, Rgb> Ores = new(StringComparer.Ordinal)
    {
        ["Iron"] = new(115, 95, 95),
        ["Coal"] = new(45, 45, 50),
        ["Stone"] = new(150, 150, 150),
        ["Marble"] = new(225, 225, 230),
        ["Gold Ore"] = new(230, 190, 40),
        ["Silver"] = new(200, 200, 215),
        ["Gems"] = new(60, 190, 170),
        ["Copper"] = new(190, 110, 60),
        ["Salt"] = new(240, 240, 245),
        ["Oil"] = new(35, 35, 40),
        ["Aluminum"] = new(170, 180, 200),
        ["Uranium"] = new(110, 210, 90),
    };

    private static readonly Dictionary<string, Rgb> Sea = new(StringComparer.Ordinal)
    {
        ["Fish"] = new(90, 150, 200),
        ["Whales"] = new(70, 90, 135),
        ["Pearls"] = new(240, 235, 245),
        ["Crab"] = new(210, 90, 60),
    };

    /// <summary>The designed model for <paramref name="key"/> (<c>feature/...</c> or <c>resource/...</c>), or null when the key has no design.</summary>
    public static ModelSpec? TryBuild(AssetKey key)
    {
        var hash = StableHash.Of(key.ToString());
        List<PrimitiveSpec>? parts = null;
        if (key.Category == RulesetAssetKeys.Feature)
        {
            parts = key.Name switch
            {
                "Forest" => Trees(hash, 7, 0.52f, new Rgb(40, 110, 50), new Rgb(95, 65, 40), jungle: false),
                "Jungle" => Trees(hash, 8, 0.55f, new Rgb(25, 130, 60), new Rgb(85, 60, 35), jungle: true),
                "Marsh" => Marsh(hash),
                _ => null,
            };
        }
        else if (key.Category == RulesetAssetKeys.Resource)
        {
            if (Herds.TryGetValue(key.Name, out var herd)) parts = Herd(hash, herd.Colour, herd.Scale);
            else if (Crops.TryGetValue(key.Name, out var crop)) parts = CropStalks(hash, crop);
            else if (Ores.TryGetValue(key.Name, out var ore)) parts = OreChunks(hash, ore);
            else if (Sea.TryGetValue(key.Name, out var sea)) parts = SeaShapes(hash, sea);
        }
        else if (key.Category == RulesetAssetKeys.Improvement)
        {
            return ImprovementSpecs.TryBuild(key);
        }
        return parts == null ? null : new ModelSpec(parts, "", 0f);
    }

    /// <summary>Number of animals in a herd prop: 3 to 5, fixed per key.</summary>
    public static int HerdSize(AssetKey key) => MinHerd + (int)(StableHash.Of(key.ToString()) / 5 % (MaxHerd - MinHerd + 1));

    private static float Unit(uint hash, int salt) => (StableHash.Mix(hash, salt, 5) & 0xFFFF) / 65535f;

    /// <summary>Rotates (x, z) about the vertical axis by <paramref name="degrees"/> the way Godot's <c>RotationDegrees.Y</c> does, so a primitive turned by Yaw and its offset agree.</summary>
    private static (float X, float Z) Rotate(float x, float z, float degrees)
    {
        var r = degrees * MathF.PI / 180f;
        var (s, c) = MathF.SinCos(r);
        return (x * c + z * s, -x * s + z * c);
    }

    private static void Animal(List<PrimitiveSpec> into, float cx, float cz, float yaw, float s, Rgb colour)
    {
        void Part(PrimitiveShape shape, float x, float y, float w, float h, float d, Rgb c)
        {
            var (dx, dz) = Rotate(x * s, 0f, yaw);
            into.Add(new PrimitiveSpec(shape, cx + dx, y * s, cz + dz, w * s, h * s, d * s, c, yaw));
        }
        Part(PrimitiveShape.Box, 0f, 0.05f, 0.22f, 0.10f, 0.08f, colour.Scale(70));
        Part(PrimitiveShape.Box, 0f, 0.17f, 0.30f, 0.14f, 0.12f, colour);
        Part(PrimitiveShape.Sphere, 0.19f, 0.26f, 0.13f, 0.13f, 0.13f, colour.Scale(85));
    }

    /// <summary>A herd of <see cref="HerdSize"/> animals on a ring of radius about 0.2 around the centre, each turned a different way as if grazing.</summary>
    private static List<PrimitiveSpec> Herd(uint hash, Rgb colour, float scale)
    {
        var count = MinHerd + (int)(hash / 5 % (MaxHerd - MinHerd + 1));
        var parts = new List<PrimitiveSpec>();
        for (var i = 0; i < count; i++)
        {
            var angle = MathF.Tau * i / count + (Unit(hash, i) - 0.5f) * 0.5f;
            var radius = 0.19f + Unit(hash, i + 20) * 0.04f;
            var yaw = Unit(hash, i + 40) * 360f;
            Animal(parts, MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, yaw, scale, colour.Scale(92 + (int)(Unit(hash, i + 60) * 16)));
        }
        return parts;
    }

    private static List<PrimitiveSpec> CropStalks(uint hash, Rgb crop)
    {
        var parts = new List<PrimitiveSpec>();
        var stem = new Rgb(95, 150, 65);
        for (var i = 0; i < 7; i++)
        {
            var (x, z) = i == 0 ? (0f, 0f) : (MathF.Cos(MathF.Tau * i / 6f + Unit(hash, i)) * 0.24f, MathF.Sin(MathF.Tau * i / 6f + Unit(hash, i)) * 0.24f);
            var height = 0.26f + Unit(hash, i + 10) * 0.1f;
            parts.Add(new PrimitiveSpec(PrimitiveShape.Cylinder, x, height / 2f, z, 0.04f, height, 0.04f, stem));
            parts.Add(new PrimitiveSpec(PrimitiveShape.Cone, x, height + 0.06f, z, 0.1f, 0.12f, 0.1f, crop));
        }
        return parts;
    }

    private static List<PrimitiveSpec> OreChunks(uint hash, Rgb ore)
    {
        var parts = new List<PrimitiveSpec>();
        var count = 3 + (int)(hash / 3 % 2);
        for (var i = 0; i < count; i++)
        {
            var angle = MathF.Tau * i / count + Unit(hash, i) * 0.6f;
            var radius = i == 0 ? 0f : 0.2f;
            var height = 0.12f + Unit(hash, i + 10) * 0.1f;
            var size = 0.16f + Unit(hash, i + 20) * 0.08f;
            parts.Add(new PrimitiveSpec(i % 2 == 0 ? PrimitiveShape.Box : PrimitiveShape.Cone, MathF.Cos(angle) * radius, height / 2f, MathF.Sin(angle) * radius,
                size, height, size, ore.Scale(85 + i * 10), Unit(hash, i + 30) * 90f));
        }
        return parts;
    }

    private static List<PrimitiveSpec> SeaShapes(uint hash, Rgb sea)
    {
        var parts = new List<PrimitiveSpec>();
        for (var i = 0; i < 4; i++)
        {
            var angle = MathF.Tau * i / 4f + Unit(hash, i);
            var radius = i == 0 ? 0f : 0.22f;
            parts.Add(new PrimitiveSpec(i % 2 == 0 ? PrimitiveShape.Sphere : PrimitiveShape.Cylinder, MathF.Cos(angle) * radius, 0.05f, MathF.Sin(angle) * radius, 0.16f, 0.1f, 0.16f, sea.Scale(90 + i * 8)));
        }
        return parts;
    }

    private static List<PrimitiveSpec> Trees(uint hash, int count, float ring, Rgb leaf, Rgb bark, bool jungle)
    {
        var parts = new List<PrimitiveSpec>();
        for (var i = 0; i < count; i++)
        {
            var angle = MathF.Tau * i / count + (Unit(hash, i) - 0.5f) * 0.4f;
            var radius = ring + (Unit(hash, i + 20) - 0.5f) * 0.1f;
            var scale = 0.85f + Unit(hash, i + 40) * 0.35f;
            var x = MathF.Cos(angle) * radius;
            var z = MathF.Sin(angle) * radius;
            var trunk = (jungle ? 0.22f : 0.12f) * scale;
            parts.Add(new PrimitiveSpec(PrimitiveShape.Cylinder, x, trunk / 2f, z, 0.07f * scale, trunk, 0.07f * scale, bark));
            var shade = leaf.Scale(85 + (int)(Unit(hash, i + 60) * 30));
            if (jungle) parts.Add(new PrimitiveSpec(PrimitiveShape.Sphere, x, trunk + 0.13f * scale, z, 0.3f * scale, 0.3f * scale, 0.3f * scale, shade));
            else parts.Add(new PrimitiveSpec(PrimitiveShape.Cone, x, trunk + 0.18f * scale, z, 0.3f * scale, 0.38f * scale, 0.3f * scale, shade));
        }
        return parts;
    }

    private static List<PrimitiveSpec> Marsh(uint hash)
    {
        var parts = new List<PrimitiveSpec>
        {
            new(PrimitiveShape.Cylinder, 0.1f, 0.01f, -0.05f, 0.55f, 0.02f, 0.55f, new Rgb(50, 90, 85)),
            new(PrimitiveShape.Cylinder, -0.35f, 0.01f, 0.3f, 0.35f, 0.02f, 0.35f, new Rgb(55, 100, 90)),
        };
        for (var i = 0; i < 9; i++)
        {
            var angle = MathF.Tau * i / 9f + Unit(hash, i) * 0.5f;
            var radius = 0.45f + Unit(hash, i + 20) * 0.2f;
            var height = 0.22f + Unit(hash, i + 40) * 0.12f;
            parts.Add(new PrimitiveSpec(PrimitiveShape.Cylinder, MathF.Cos(angle) * radius, height / 2f, MathF.Sin(angle) * radius, 0.03f, height, 0.03f, new Rgb(140, 150, 80).Scale(90 + i * 3)));
        }
        return parts;
    }
}
