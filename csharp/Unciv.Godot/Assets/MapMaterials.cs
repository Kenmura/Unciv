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
}
