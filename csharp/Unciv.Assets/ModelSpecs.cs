namespace Unciv.Assets;

public enum PrimitiveShape
{
    Box,
    Cylinder,
    Cone,
    Sphere,
}

/// <summary>One primitive of a placeholder model, in tile units: centre offset, size (width, height, depth; diameter for sphere/cylinder/cone from X/Z) and colour.</summary>
public readonly record struct PrimitiveSpec(PrimitiveShape Shape, float X, float Y, float Z, float SizeX, float SizeY, float SizeZ, Rgb Colour);

/// <summary>A placeholder model: primitives standing on y = 0, plus the initials shown on a billboard above it (empty for none).</summary>
public sealed record ModelSpec(IReadOnlyList<PrimitiveSpec> Primitives, string Initials, float BillboardHeight);

/// <summary>Builds the primitive description of unit blocks and props from a key alone. Same key, same model, on every machine.</summary>
public static class ModelSpecs
{
    private static readonly Dictionary<string, PrimitiveShape> UnitTypeShapes = new(StringComparer.Ordinal)
    {
        ["Melee"] = PrimitiveShape.Box,
        ["Sword"] = PrimitiveShape.Box,
        ["Gunpowder"] = PrimitiveShape.Box,
        ["Armor"] = PrimitiveShape.Box,
        ["Armored"] = PrimitiveShape.Box,
        ["WaterMelee"] = PrimitiveShape.Box,
        ["Melee Water"] = PrimitiveShape.Box,
        ["Ranged"] = PrimitiveShape.Cylinder,
        ["Archery"] = PrimitiveShape.Cylinder,
        ["Ranged Gunpowder"] = PrimitiveShape.Cylinder,
        ["WaterRanged"] = PrimitiveShape.Cylinder,
        ["Ranged Water"] = PrimitiveShape.Cylinder,
        ["Civilian"] = PrimitiveShape.Cylinder,
        ["WaterCivilian"] = PrimitiveShape.Cylinder,
        ["Civilian Water"] = PrimitiveShape.Cylinder,
        ["Mounted"] = PrimitiveShape.Cone,
        ["Scout"] = PrimitiveShape.Cone,
        ["Fighter"] = PrimitiveShape.Cone,
        ["Bomber"] = PrimitiveShape.Cone,
        ["AtomicBomber"] = PrimitiveShape.Cone,
        ["Atomic Bomber"] = PrimitiveShape.Cone,
        ["Missile"] = PrimitiveShape.Cone,
        ["Helicopter"] = PrimitiveShape.Cone,
        ["Siege"] = PrimitiveShape.Sphere,
        ["WaterSubmarine"] = PrimitiveShape.Sphere,
        ["Submarine"] = PrimitiveShape.Sphere,
        ["WaterAircraftCarrier"] = PrimitiveShape.Box,
        ["Aircraft Carrier"] = PrimitiveShape.Box,
    };

    /// <summary>Unit block: one coloured primitive (shape from <paramref name="unitType"/>, or the key itself for unitType keys; hash-chosen when unknown; colour from the key's hash) with an initials billboard above.</summary>
    public static ModelSpec Unit(AssetKey key, string? unitType = null)
    {
        var typeName = key.Category == RulesetAssetKeys.UnitType ? key.Name : unitType;
        var shape = typeName != null && UnitTypeShapes.TryGetValue(typeName, out var s)
            ? s
            : (PrimitiveShape)(StableHash.Of(key.ToString()) % 4);
        var colour = PlaceholderPalette.FromHash(key.ToString(), 140, 210);
        var primitive = shape switch
        {
            PrimitiveShape.Sphere => new PrimitiveSpec(shape, 0f, 0.25f, 0f, 0.5f, 0.5f, 0.5f, colour),
            PrimitiveShape.Cone => new PrimitiveSpec(shape, 0f, 0.3f, 0f, 0.5f, 0.6f, 0.5f, colour),
            PrimitiveShape.Cylinder => new PrimitiveSpec(shape, 0f, 0.3f, 0f, 0.45f, 0.6f, 0.45f, colour),
            _ => new PrimitiveSpec(shape, 0f, 0.25f, 0f, 0.5f, 0.5f, 0.5f, colour),
        };
        return new ModelSpec([primitive], GlyphFont.Initials(key.Name), 0.75f);
    }

    /// <summary>Resources, improvements and buildings from primitives. Horses are two small boxes and a sphere; others get one to three hash-chosen primitives.</summary>
    public static ModelSpec Prop(AssetKey key)
    {
        var colour = PlaceholderPalette.FromHash(key.ToString(), 120, 200);
        if (key is { Category: RulesetAssetKeys.Resource, Name: "Horses" })
        {
            var brown = PlaceholderPalette.HorseBrown;
            return new ModelSpec(
            [
                new PrimitiveSpec(PrimitiveShape.Box, -0.15f, 0.1f, 0f, 0.3f, 0.2f, 0.15f, brown),
                new PrimitiveSpec(PrimitiveShape.Box, 0.15f, 0.1f, 0.1f, 0.3f, 0.2f, 0.15f, brown.Scale(120)),
                new PrimitiveSpec(PrimitiveShape.Sphere, 0f, 0.3f, -0.1f, 0.2f, 0.2f, 0.2f, brown.Scale(80)),
            ], "", 0f);
        }
        var hash = StableHash.Of(key.ToString());
        var count = 1 + (int)(hash / 7 % 3);
        var primitives = new List<PrimitiveSpec>();
        for (var i = 0; i < count; i++)
        {
            var part = StableHash.Mix(hash, i, 17);
            var shape = (PrimitiveShape)(part % 4);
            var offset = count == 1 ? 0f : ((int)(part / 4 % 5) - 2) * 0.1f;
            var height = 0.2f + (part / 16 % 4) * 0.07f;
            primitives.Add(new PrimitiveSpec(shape, offset, height / 2f, i == 0 ? 0f : offset, 0.25f, height, 0.25f, colour.Scale(90 + i * 15)));
        }
        return new ModelSpec(primitives, "", 0f);
    }
}

/// <summary>Colours and metrics for the procedural UI theme (dark slate panels, gold accent); the Godot side maps these onto StyleBoxFlat.</summary>
public static class ThemeSpec
{
    public const int CornerRadius = 4;
    public const int BorderWidth = 2;
    public const int ContentMargin = 8;
    public const int FontSize = 16;
}
