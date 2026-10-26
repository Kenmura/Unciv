namespace Unciv.Assets;

/// <summary>
/// C032 designed placeholder models for tile improvements and the city (key category <c>improvement</c>), built from primitives in tile units on y = 0 like <see cref="PropSpecs"/>.
/// Every model keeps inside radius 0.4 of its own centre so the client can stand it on the tile centre, or push it aside when a resource prop already sits there.
/// A pure function of the key; real art replaces any of it by key (<c>improvement/Farm</c>, ...) with no code change. Names without a design (mods) fall back to the generic hash-chosen prop.
/// </summary>
public static class ImprovementSpecs
{
    /// <summary>Radius a designed improvement model stays within.</summary>
    public const float Footprint = 0.4f;

    private static readonly Rgb Wood = new(125, 90, 55);
    private static readonly Rgb Stone = new(150, 150, 155);
    private static readonly Rgb Earth = new(120, 95, 60);
    private static readonly Rgb Straw = new(215, 190, 100);
    private static readonly Rgb Metal = new(105, 110, 120);

    /// <summary>Improvements that have a designed model. Roads, rails, removal orders and the repair order are not models.</summary>
    public static bool HasDesign(string name) => Build(name, StableHash.Of(AssetKey.Of(RulesetAssetKeys.Improvement, name).ToString())) != null;

    /// <summary>The designed model for an <c>improvement/...</c> key, or null.</summary>
    public static ModelSpec? TryBuild(AssetKey key)
    {
        if (key.Category != RulesetAssetKeys.Improvement) return null;
        if (CitySpecs.TierOf(key.Name) is { } tier) return CitySpecs.Build(tier);
        var parts = Build(key.Name, StableHash.Of(key.ToString()));
        return parts == null ? null : new ModelSpec(parts, "", 0f);
    }

    private static PrimitiveSpec Box(float x, float y, float z, float w, float h, float d, Rgb c, float yaw = 0f) => new(PrimitiveShape.Box, x, y, z, w, h, d, c, yaw);

    private static PrimitiveSpec Cyl(float x, float y, float z, float w, float h, float d, Rgb c) => new(PrimitiveShape.Cylinder, x, y, z, w, h, d, c);

    private static PrimitiveSpec Cone(float x, float y, float z, float w, float h, float d, Rgb c) => new(PrimitiveShape.Cone, x, y, z, w, h, d, c);

    private static PrimitiveSpec Ball(float x, float y, float z, float s, Rgb c) => new(PrimitiveShape.Sphere, x, y, z, s, s, s, c);

    private static float Unit(uint hash, int salt) => (StableHash.Mix(hash, salt, 5) & 0xFFFF) / 65535f;

    private static List<PrimitiveSpec>? Build(string name, uint hash) => name switch
    {
        "Farm" => Furrows(new Rgb(150, 120, 65), new Rgb(120, 175, 70), 3),
        "Terrace farm" => Terraces(),
        "Plantation" => Rows(hash, new Rgb(60, 140, 60)),
        "Pasture" => Fence(new Rgb(115, 175, 80)),
        "Camp" => Tent(new Rgb(185, 150, 100)),
        "Barbarian encampment" => [.. Tent(new Rgb(150, 50, 45)), Box(0.2f, 0.28f, 0.2f, 0.02f, 0.5f, 0.02f, Wood), Box(0.26f, 0.45f, 0.2f, 0.12f, 0.08f, 0.01f, new Rgb(200, 40, 40))],
        "Mine" => MineShaft(),
        "Quarry" => [Box(0f, 0.04f, 0f, 0.6f, 0.08f, 0.5f, Earth), Box(-0.08f, 0.12f, 0.02f, 0.4f, 0.1f, 0.34f, Stone), Box(0.04f, 0.2f, 0.02f, 0.22f, 0.1f, 0.2f, Stone.Scale(115)), Box(0.2f, 0.06f, -0.2f, 0.12f, 0.08f, 0.1f, Stone.Scale(90), 20f)],
        "Lumber mill" => [Box(-0.1f, 0.12f, 0.05f, 0.34f, 0.24f, 0.3f, Wood), Box(-0.1f, 0.28f, 0.05f, 0.38f, 0.05f, 0.34f, Wood.Scale(70)), Box(0.17f, 0.04f, -0.05f, 0.08f, 0.08f, 0.4f, Wood.Scale(120)), Box(0.17f, 0.04f, 0.05f, 0.08f, 0.08f, 0.4f, Wood.Scale(105)), Box(0.17f, 0.11f, 0f, 0.08f, 0.08f, 0.4f, Wood.Scale(112))],
        "Trading post" => [Box(0f, 0.1f, 0f, 0.36f, 0.2f, 0.3f, Wood), Cone(0f, 0.3f, 0f, 0.5f, 0.2f, 0.44f, Straw), Box(0.2f, 0.06f, 0.2f, 0.1f, 0.12f, 0.1f, Wood.Scale(80))],
        "Oil well" => [Box(0f, 0.02f, 0f, 0.4f, 0.04f, 0.4f, Metal.Scale(70)), Cone(0f, 0.28f, 0f, 0.26f, 0.5f, 0.26f, Metal), Cyl(0f, 0.06f, 0f, 0.05f, 0.1f, 0.05f, new Rgb(30, 30, 35))],
        "Offshore Platform" => [Box(0f, 0.28f, 0f, 0.5f, 0.06f, 0.5f, Metal), Cyl(-0.2f, 0.13f, -0.2f, 0.06f, 0.26f, 0.06f, Metal.Scale(80)), Cyl(0.2f, 0.13f, -0.2f, 0.06f, 0.26f, 0.06f, Metal.Scale(80)), Cyl(-0.2f, 0.13f, 0.2f, 0.06f, 0.26f, 0.06f, Metal.Scale(80)), Cyl(0.2f, 0.13f, 0.2f, 0.06f, 0.26f, 0.06f, Metal.Scale(80)), Cone(0f, 0.5f, 0f, 0.16f, 0.36f, 0.16f, Metal.Scale(120))],
        "Fishing Boats" => [Box(0f, 0.05f, 0f, 0.46f, 0.1f, 0.18f, Wood), Box(0.26f, 0.08f, 0f, 0.1f, 0.06f, 0.1f, Wood.Scale(90), 45f), Box(0f, 0.3f, 0f, 0.025f, 0.4f, 0.025f, Wood.Scale(70)), Box(0.05f, 0.34f, 0f, 0.2f, 0.28f, 0.01f, new Rgb(235, 230, 215))],
        "Fort" => Stronghold(false),
        "Citadel" => Stronghold(true),
        "Academy" => [Box(0f, 0.03f, 0f, 0.56f, 0.06f, 0.4f, Stone), Box(0f, 0.18f, 0f, 0.4f, 0.24f, 0.28f, new Rgb(215, 215, 225)), Cone(0f, 0.38f, 0f, 0.5f, 0.16f, 0.36f, new Rgb(70, 100, 170)), Cyl(-0.18f, 0.14f, 0.17f, 0.05f, 0.22f, 0.05f, Stone.Scale(115)), Cyl(0.18f, 0.14f, 0.17f, 0.05f, 0.22f, 0.05f, Stone.Scale(115))],
        "Manufactory" => [Box(-0.05f, 0.12f, 0.02f, 0.5f, 0.24f, 0.32f, new Rgb(150, 110, 100)), Cyl(0.18f, 0.3f, -0.08f, 0.09f, 0.4f, 0.09f, new Rgb(95, 90, 90)), Box(-0.1f, 0.26f, 0.02f, 0.3f, 0.04f, 0.36f, Metal.Scale(80))],
        "Customs house" => [Box(0f, 0.1f, 0f, 0.5f, 0.2f, 0.34f, new Rgb(200, 170, 110)), Box(0f, 0.22f, 0f, 0.56f, 0.04f, 0.4f, Wood.Scale(80)), Cone(0f, 0.34f, 0f, 0.3f, 0.16f, 0.3f, new Rgb(190, 60, 50)), Box(0.28f, 0.08f, 0.05f, 0.12f, 0.16f, 0.12f, Wood)],
        "Holy site" => [Box(0f, 0.03f, 0f, 0.5f, 0.06f, 0.5f, Stone), Cyl(-0.14f, 0.18f, -0.14f, 0.06f, 0.24f, 0.06f, new Rgb(235, 235, 240)), Cyl(0.14f, 0.18f, -0.14f, 0.06f, 0.24f, 0.06f, new Rgb(235, 235, 240)), Cyl(-0.14f, 0.18f, 0.14f, 0.06f, 0.24f, 0.06f, new Rgb(235, 235, 240)), Cyl(0.14f, 0.18f, 0.14f, 0.06f, 0.24f, 0.06f, new Rgb(235, 235, 240)), Cone(0f, 0.38f, 0f, 0.46f, 0.14f, 0.46f, new Rgb(240, 220, 150))],
        "Landmark" => [Box(0f, 0.03f, 0f, 0.3f, 0.06f, 0.3f, Stone), Cone(0f, 0.3f, 0f, 0.14f, 0.44f, 0.14f, new Rgb(235, 215, 150))],
        "Moai" => [Box(0f, 0.03f, 0f, 0.28f, 0.06f, 0.2f, Stone.Scale(80)), Box(0f, 0.25f, 0f, 0.14f, 0.38f, 0.1f, Stone.Scale(105)), Box(0f, 0.38f, 0.02f, 0.2f, 0.12f, 0.12f, Stone.Scale(110))],
        "Ancient ruins" => Ruins(hash, 4),
        "City ruins" => Ruins(hash, 6),
        _ => null,
    };

    private static List<PrimitiveSpec> Furrows(Rgb soil, Rgb crop, int rows)
    {
        var parts = new List<PrimitiveSpec> { Box(0f, 0.01f, 0f, 0.62f, 0.02f, 0.5f, soil) };
        for (var i = 0; i < rows; i++)
        {
            var z = (i - (rows - 1) / 2f) * 0.15f;
            parts.Add(Box(0f, 0.045f, z, 0.56f, 0.05f, 0.07f, crop.Scale(90 + i * 8)));
        }
        return parts;
    }

    private static List<PrimitiveSpec> Terraces() =>
    [
        Box(0f, 0.03f, 0f, 0.62f, 0.06f, 0.5f, new Rgb(130, 105, 65)),
        Box(0f, 0.09f, 0f, 0.48f, 0.06f, 0.38f, new Rgb(120, 170, 70)),
        Box(0f, 0.15f, 0f, 0.34f, 0.06f, 0.26f, new Rgb(135, 185, 80)),
        Box(0f, 0.21f, 0f, 0.2f, 0.06f, 0.14f, new Rgb(150, 195, 90)),
    ];

    private static List<PrimitiveSpec> Rows(uint hash, Rgb leaf)
    {
        var parts = new List<PrimitiveSpec> { Box(0f, 0.01f, 0f, 0.6f, 0.02f, 0.5f, Earth) };
        for (var row = 0; row < 2; row++)
        {
            for (var i = 0; i < 4; i++)
            {
                var size = 0.15f + Unit(hash, row * 4 + i) * 0.05f;
                parts.Add(Ball(-0.21f + i * 0.14f, 0.02f + size / 2f, row == 0 ? -0.1f : 0.1f, size, leaf.Scale(85 + (row * 4 + i) * 4)));
            }
        }
        return parts;
    }

    private static List<PrimitiveSpec> Fence(Rgb grass)
    {
        var parts = new List<PrimitiveSpec> { Box(0f, 0.01f, 0f, 0.62f, 0.02f, 0.52f, grass) };
        foreach (var (x, z, w, d) in new[] { (0f, -0.25f, 0.62f, 0.03f), (0f, 0.25f, 0.62f, 0.03f), (-0.3f, 0f, 0.03f, 0.5f), (0.3f, 0f, 0.03f, 0.5f) })
            parts.Add(Box(x, 0.1f, z, w, 0.035f, d, Wood));
        foreach (var (x, z) in new[] { (-0.3f, -0.25f), (0.3f, -0.25f), (-0.3f, 0.25f), (0.3f, 0.25f), (0f, -0.25f), (0f, 0.25f) })
            parts.Add(Box(x, 0.08f, z, 0.04f, 0.16f, 0.04f, Wood.Scale(80)));
        return parts;
    }

    private static List<PrimitiveSpec> Tent(Rgb cloth) =>
    [
        Cone(0f, 0.15f, 0f, 0.42f, 0.3f, 0.42f, cloth),
        Box(0.02f, 0.09f, 0.2f, 0.1f, 0.14f, 0.02f, cloth.Scale(55)),
        Cyl(-0.26f, 0.04f, 0.1f, 0.08f, 0.08f, 0.08f, new Rgb(60, 60, 60)),
        Box(-0.26f, 0.1f, 0.1f, 0.02f, 0.1f, 0.02f, Wood),
    ];

    private static List<PrimitiveSpec> MineShaft() =>
    [
        Cone(0f, 0.17f, -0.04f, 0.6f, 0.34f, 0.5f, Earth.Scale(85)),
        Box(0f, 0.09f, 0.2f, 0.2f, 0.18f, 0.06f, new Rgb(35, 30, 30)),
        Box(-0.1f, 0.1f, 0.2f, 0.04f, 0.2f, 0.07f, Wood),
        Box(0.1f, 0.1f, 0.2f, 0.04f, 0.2f, 0.07f, Wood),
        Box(0f, 0.21f, 0.2f, 0.28f, 0.04f, 0.07f, Wood),
        Box(0.22f, 0.05f, 0.18f, 0.16f, 0.08f, 0.1f, Metal),
    ];

    private static List<PrimitiveSpec> Stronghold(bool tall)
    {
        var wall = Stone.Scale(tall ? 100 : 90);
        var height = tall ? 0.24f : 0.16f;
        var parts = new List<PrimitiveSpec> { Box(0f, height / 2f, 0f, 0.5f, height, 0.5f, wall), Box(0f, height + 0.02f, 0f, 0.38f, 0.04f, 0.38f, wall.Scale(75)) };
        foreach (var (x, z) in new[] { (-0.24f, -0.24f), (0.24f, -0.24f), (-0.24f, 0.24f), (0.24f, 0.24f) })
        {
            parts.Add(Cyl(x, height / 2f + 0.06f, z, 0.14f, height + 0.12f, 0.14f, wall.Scale(110)));
            parts.Add(Cone(x, height + 0.17f, z, 0.17f, 0.12f, 0.17f, tall ? new Rgb(170, 50, 45) : Wood));
        }
        return parts;
    }

    private static List<PrimitiveSpec> Ruins(uint hash, int pieces)
    {
        var parts = new List<PrimitiveSpec> { Box(0f, 0.015f, 0f, 0.56f, 0.03f, 0.5f, Stone.Scale(85)) };
        for (var i = 0; i < pieces; i++)
        {
            var angle = MathF.Tau * i / pieces + Unit(hash, i) * 0.5f;
            var radius = 0.1f + Unit(hash, i + 10) * 0.12f;
            var height = 0.1f + Unit(hash, i + 20) * 0.2f;
            parts.Add(i % 2 == 0
                ? Cyl(MathF.Cos(angle) * radius, 0.03f + height / 2f, MathF.Sin(angle) * radius, 0.08f, height, 0.08f, Stone.Scale(100 + i * 4))
                : Box(MathF.Cos(angle) * radius, 0.03f + height / 4f, MathF.Sin(angle) * radius, 0.14f, height / 2f, 0.1f, Stone.Scale(90 + i * 4), Unit(hash, i + 30) * 90f));
        }
        return parts;
    }
}
