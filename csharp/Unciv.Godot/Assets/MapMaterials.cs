using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Assets;

/// <summary>Colour work for the map view (C009): feature blending, fog dimming, highlight and backdrop. Lives in Assets/ because it writes colours.</summary>
public static class MapMaterials
{
    /// <summary>Share of the overlay (feature) colour mixed into the base terrain colour.</summary>
    public const float OverlayShare = 0.5f;

    /// <summary>
    /// A copy of <paramref name="terrain"/> with <paramref name="overlay"/>'s colour blended in (a feature on a base terrain) and, when
    /// <paramref name="visible"/> is false, the fog dimming multiplied in. The registry's cached material is never modified.
    /// </summary>
    public static Material Tinted(Material terrain, Material? overlay, bool visible)
    {
        if (terrain is not StandardMaterial3D standard) return terrain;
        var copy = (StandardMaterial3D)standard.Duplicate();
        if (overlay is StandardMaterial3D feature) copy.AlbedoColor = standard.AlbedoColor.Lerp(feature.AlbedoColor, OverlayShare);
        if (!visible) copy.AlbedoColor *= ProceduralAssets.ToColor(PlaceholderPalette.FogDim);
        return copy;
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

    /// <summary>Black backdrop with flat white-ish ambient light, so unexplored space is black and tiles keep their palette colours.</summary>
    public static Godot.Environment Backdrop() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = ProceduralAssets.ToColor(PlaceholderPalette.Unexplored),
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = ProceduralAssets.ToColor(PlaceholderPalette.Light),
        AmbientLightEnergy = 0.55f,
    };

    /// <summary>Gives a unit block's neutral <c>Banner</c> the civ's colour (C010). The model is freshly built per call, so its material is owned by the caller.</summary>
    public static void ColourBanner(Node3D unitModel, float r, float g, float b)
    {
        if (unitModel.GetNodeOrNull<MeshInstance3D>("Banner") is { } banner)
            banner.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(r, g, b), Roughness = 1f };
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
