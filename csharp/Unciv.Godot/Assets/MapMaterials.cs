using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Assets;

/// <summary>Colour work for the map view (C009): feature blending, fog dimming, highlight and backdrop. Lives in Assets/ because it writes colours.</summary>
public static class MapMaterials
{
    /// <summary>Share of the overlay (feature) colour mixed into the base terrain colour.</summary>
    public const float OverlayShare = 0.5f;

    /// <summary>
    /// The colour of a tile (sRGB-encoded numbers): the terrain material's albedo with <paramref name="overlay"/>'s blended in (a feature on a base terrain)
    /// and, when <paramref name="visible"/> is false, the fog dimming multiplied in. The registry's cached materials are never modified.
    /// </summary>
    public static Tint TileColour(Material terrain, Material? overlay, bool visible)
    {
        if (terrain is not StandardMaterial3D standard) return new Tint(1f, 1f, 1f);
        var colour = new Tint(standard.AlbedoColor.R, standard.AlbedoColor.G, standard.AlbedoColor.B);
        if (overlay is StandardMaterial3D feature) colour = colour.Lerp(new Tint(feature.AlbedoColor.R, feature.AlbedoColor.G, feature.AlbedoColor.B), OverlayShare);
        return visible ? colour : colour * Tint.Of(PlaceholderPalette.FogDim);
    }

    /// <summary>Material of the merged terrain chunks (C031): the colours are baked into the vertex colours, a shared grain texture goes on top.</summary>
    public static StandardMaterial3D TerrainChunks() => new()
    {
        AlbedoColor = new Color(1f, 1f, 1f),
        AlbedoTexture = ProceduralAssets.ToTexture(ProceduralImages.Noise(AssetKey.Of("mesh", "terrain-chunk"))),
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        VertexColorUseAsAlbedo = true,
        Roughness = 1f,
    };

    /// <summary>Baked vertex colour: linear tile colour times the mesh's relief shade.</summary>
    public static Color VertexColour(Tint linear, float shade) => new(linear.R * shade, linear.G * shade, linear.B * shade);

    /// <summary>Multiplies the fog dimming into every mesh of a prop model (C003) so props on explored-but-unseen tiles darken with their tile; materials are copied, never edited in place.</summary>
    public static void Dim(Node3D model)
    {
        var fog = ProceduralAssets.ToColor(PlaceholderPalette.FogDim);
        foreach (var child in model.GetChildren())
        {
            if (child is MeshInstance3D { MaterialOverride: StandardMaterial3D material } mesh)
            {
                var copy = (StandardMaterial3D)material.Duplicate();
                copy.AlbedoColor *= fog;
                mesh.MaterialOverride = copy;
            }
            else if (child is Node3D nested) Dim(nested);
        }
    }

    /// <summary>Material of the instanced grass tufts (C002): vertex colours (root dark, tip light) times the per-instance colour; double sided because a blade is one triangle.</summary>
    public static StandardMaterial3D Vegetation() => new()
    {
        AlbedoColor = new Color(1f, 1f, 1f),
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        Roughness = 1f,
    };

    /// <summary>Per-instance colour of a tuft on <paramref name="baseTerrain"/>: the terrain's palette colour lightened a little, dimmed when the tile is not visible.</summary>
    public static Color TuftColour(string baseTerrain, bool visible)
    {
        var colour = ProceduralAssets.ToColor(PlaceholderPalette.ForTerrain(baseTerrain)).Lerp(new Color(1f, 1f, 1f), 0.25f);
        return visible ? colour : colour * ProceduralAssets.ToColor(PlaceholderPalette.FogDim);
    }

    /// <summary>Translucent unshaded accent colour for the selected tile.</summary>
    public static StandardMaterial3D Highlight()
    {
        var colour = ProceduralAssets.ToColor(PlaceholderPalette.AccentHover);
        colour.A = 0.55f;
        return new StandardMaterial3D
        {
            AlbedoColor = colour,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = false,
        };
    }

    /// <summary>Translucent unshaded film of one colour: the reachable-tile and attack-range overlays (C012).</summary>
    public static StandardMaterial3D Overlay(Rgb tint)
    {
        var colour = ProceduralAssets.ToColor(tint);
        colour.A = 0.4f;
        return new StandardMaterial3D { AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
    }

    /// <summary>Black backdrop with flat white-ish ambient light, so unexplored space is black and tiles keep their palette colours.</summary>
    public static Godot.Environment Backdrop() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = ProceduralAssets.ToColor(PlaceholderPalette.Unexplored),
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = ProceduralAssets.ToColor(PlaceholderPalette.Light),
        AmbientLightEnergy = 0.55f,
    };

    /// <summary>
    /// The albedo a civ's colour gives a <c>TeamColor</c> material (C080): the colour itself on a flat material; on a textured one the colour is blended halfway to white so the multiplied texture
    /// (skin, metal, cloth) still reads and the figure still takes on the civ's hue.
    /// </summary>
    public static Color TeamTint(Color civ, bool textured) => textured ? Colors.White.Lerp(civ, 0.5f) : civ;

    /// <summary>
    /// Gives a unit model the civ's colour (C010, C080): every surface whose material is named <see cref="UnitModelContract.TeamColorMaterial"/> gets its own tinted copy (the loaded scene shares its
    /// materials between instances); a model without such a material falls back to its neutral <c>Banner</c> node, as the generated blocks have.
    /// </summary>
    public static void ColourTeam(Node3D unitModel, float r, float g, float b)
    {
        var civ = new Color(r, g, b);
        var found = false;
        foreach (var mesh in unitModel.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            for (var i = 0; i < (mesh.Mesh?.GetSurfaceCount() ?? 0); i++)
            {
                if (mesh.GetActiveMaterial(i) is not BaseMaterial3D { ResourceName: UnitModelContract.TeamColorMaterial } material) continue;
                var tinted = (BaseMaterial3D)material.Duplicate();
                tinted.AlbedoColor = TeamTint(civ, tinted.AlbedoTexture != null);
                mesh.SetSurfaceOverrideMaterial(i, tinted);
                found = true;
            }
        }
        if (!found && unitModel.GetNodeOrNull<MeshInstance3D>("Banner") is { } banner)
            banner.MaterialOverride = new StandardMaterial3D { AlbedoColor = civ, Roughness = 1f };
    }

    /// <summary>The colour a unit model wears: its <c>TeamColor</c> surfaces' albedo, else its <c>Banner</c>'s; null when it has neither (for tests).</summary>
    public static Color? TeamColourOf(Node3D unitModel)
    {
        foreach (var mesh in unitModel.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
            for (var i = 0; i < (mesh.Mesh?.GetSurfaceCount() ?? 0); i++)
                if (mesh.GetActiveMaterial(i) is BaseMaterial3D { ResourceName: UnitModelContract.TeamColorMaterial } material) return material.AlbedoColor;
        return unitModel.GetNodeOrNull<MeshInstance3D>("Banner")?.MaterialOverride is StandardMaterial3D banner ? banner.AlbedoColor : null;
    }

    /// <summary>Small unshaded sphere of the accent colour: one dot of the path preview (C010).</summary>
    public static MeshInstance3D PathDot()
    {
        var colour = ProceduralAssets.ToColor(PlaceholderPalette.Accent);
        return new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
