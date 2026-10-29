namespace Unciv.Assets;

public enum PrimitiveShape
{
    Box,
    Cylinder,
    Cone,
    Sphere,
}

/// <summary>One primitive of a placeholder model, in tile units: centre offset, size (width, height, depth; diameter for sphere/cylinder/cone from X/Z), colour and rotation about the vertical axis in degrees (C003; positions are already in the model frame, Yaw only turns the primitive itself).</summary>
public readonly record struct PrimitiveSpec(PrimitiveShape Shape, float X, float Y, float Z, float SizeX, float SizeY, float SizeZ, Rgb Colour, float Yaw = 0f);

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
        List<PrimitiveSpec> primitives = [primitive, .. Accessories(typeName, colour)];
        return new ModelSpec(primitives, GlyphFont.Initials(key.Name), 0.75f);
    }

    private static PrimitiveSpec Part(PrimitiveShape shape, float x, float y, float z, float sx, float sy, float sz, Rgb colour) => new(shape, x, y, z, sx, sy, sz, colour);

    /// <summary>
    /// C004: what makes each unit type read differently from a distance, added after the body (the body stays <c>Primitives[0]</c>): infantry carry a head and shield, archers a bow, riders sit on their mount,
    /// siege has a barrel, armour a turret, ships a hull with mast or guns, submarines a tower, aircraft wings, civilians a head. Everything stands on y = 0 and stays under the billboard.
    /// </summary>
    private static IEnumerable<PrimitiveSpec> Accessories(string? typeName, Rgb body)
    {
        var skin = PlaceholderPalette.UnitSkin;
        var metal = PlaceholderPalette.UnitMetal;
        var wood = PlaceholderPalette.UnitWood;
        switch (typeName)
        {
            case "Melee" or "Sword":
                yield return Part(PrimitiveShape.Sphere, 0f, 0.58f, 0f, 0.2f, 0.2f, 0.2f, skin);
                yield return Part(PrimitiveShape.Box, 0.3f, 0.28f, 0f, 0.06f, 0.36f, 0.26f, metal);
                break;
            case "Gunpowder" or "Ranged Gunpowder":
                yield return Part(PrimitiveShape.Sphere, 0f, typeName == "Gunpowder" ? 0.58f : 0.66f, 0f, typeName == "Gunpowder" ? 0.2f : 0.18f, typeName == "Gunpowder" ? 0.2f : 0.18f, typeName == "Gunpowder" ? 0.2f : 0.18f, skin);
                yield return Part(PrimitiveShape.Box, 0.28f, 0.4f, 0f, 0.06f, 0.06f, 0.5f, metal);
                break;
            case "Ranged" or "Archery":
                yield return Part(PrimitiveShape.Sphere, 0f, 0.66f, 0f, 0.18f, 0.18f, 0.18f, skin);
                yield return Part(PrimitiveShape.Box, 0.3f, 0.35f, 0f, 0.04f, 0.5f, 0.04f, wood);
                break;
            case "Mounted" or "Scout":
                yield return Part(PrimitiveShape.Sphere, 0f, 0.66f, 0f, 0.18f, 0.18f, 0.18f, skin);
                yield return Part(PrimitiveShape.Box, 0f, 0.08f, 0.2f, 0.12f, 0.16f, 0.12f, wood);
                break;
            case "Armor" or "Armored":
                yield return Part(PrimitiveShape.Cylinder, 0f, 0.58f, 0f, 0.3f, 0.16f, 0.3f, metal);
                yield return Part(PrimitiveShape.Box, 0f, 0.58f, 0.3f, 0.06f, 0.06f, 0.4f, metal);
                break;
            case "Siege":
                yield return Part(PrimitiveShape.Box, 0f, 0.3f, 0.25f, 0.12f, 0.12f, 0.5f, wood);
                yield return Part(PrimitiveShape.Box, 0f, 0.04f, 0f, 0.7f, 0.08f, 0.2f, wood);
                break;
            case "Civilian":
                yield return Part(PrimitiveShape.Sphere, 0f, 0.66f, 0f, 0.18f, 0.18f, 0.18f, skin);
                yield return Part(PrimitiveShape.Box, 0.28f, 0.2f, 0f, 0.12f, 0.2f, 0.12f, wood);
                break;
            case "WaterMelee" or "Melee Water" or "WaterRanged" or "Ranged Water" or "WaterCivilian" or "Civilian Water":
                yield return Part(PrimitiveShape.Box, 0f, 0.1f, 0f, 0.4f, 0.2f, 0.8f, body.Scale(60));
                yield return Part(PrimitiveShape.Box, 0f, 0.45f, 0f, 0.05f, 0.5f, 0.05f, wood);
                if (typeName is "WaterMelee" or "Melee Water") yield return Part(PrimitiveShape.Box, 0f, 0.45f, 0.02f, 0.02f, 0.3f, 0.4f, PlaceholderPalette.Light);
                else if (typeName is "WaterCivilian" or "Civilian Water") yield return Part(PrimitiveShape.Box, 0f, 0.32f, -0.25f, 0.2f, 0.12f, 0.2f, wood);
                else yield return Part(PrimitiveShape.Box, 0f, 0.28f, 0.3f, 0.08f, 0.08f, 0.3f, metal);
                break;
            case "WaterSubmarine" or "Submarine":
                yield return Part(PrimitiveShape.Cylinder, 0f, 0.5f, 0f, 0.14f, 0.2f, 0.14f, metal);
                break;
            case "WaterAircraftCarrier" or "Aircraft Carrier":
                yield return Part(PrimitiveShape.Box, 0.18f, 0.55f, 0f, 0.14f, 0.3f, 0.2f, metal);
                break;
            case "Fighter":
                yield return Part(PrimitiveShape.Box, 0f, 0.3f, 0f, 0.8f, 0.04f, 0.2f, metal);
                break;
            case "Bomber" or "AtomicBomber" or "Atomic Bomber":
                yield return Part(PrimitiveShape.Box, 0f, 0.3f, 0f, 0.9f, 0.04f, 0.3f, metal);
                yield return Part(PrimitiveShape.Box, 0f, 0.5f, -0.2f, 0.04f, 0.2f, 0.12f, metal);
                if (typeName != "Bomber") yield return Part(PrimitiveShape.Sphere, 0f, 0.1f, 0f, 0.16f, 0.16f, 0.16f, PlaceholderPalette.HealthBad);
                break;
            case "Helicopter":
                yield return Part(PrimitiveShape.Box, 0f, 0.62f, 0f, 0.8f, 0.03f, 0.06f, metal);
                yield return Part(PrimitiveShape.Box, 0f, 0.3f, -0.3f, 0.05f, 0.05f, 0.3f, metal);
                break;
            case "Missile":
                yield return Part(PrimitiveShape.Box, 0f, 0.08f, 0f, 0.4f, 0.04f, 0.04f, metal);
                break;
        }
    }

    /// <summary>Resources, features, improvements and buildings from primitives. Designed props (<see cref="PropSpecs"/>: herds, crops, ores, forest, jungle, marsh) come first; anything else gets one to three hash-chosen primitives.</summary>
    public static ModelSpec Prop(AssetKey key)
    {
        if (PropSpecs.TryBuild(key) is { } designed) return designed;
        var colour = PlaceholderPalette.FromHash(key.ToString(), 120, 200);
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
