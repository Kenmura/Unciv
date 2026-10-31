namespace Unciv.Assets;

/// <summary>T377: the look rules of a territory border that do not depend on the engine: band width in screen pixels (so it follows the zoom and the UI scale) and the casing colour.</summary>
public static class BorderStyle
{
    /// <summary>The band is this fraction of the tile's screen width wide before it is clamped.</summary>
    public const float WidthFraction = 0.03f;

    /// <summary>The band never gets thinner than this many UI-scale pixels, so it never vanishes at the farthest zoom.</summary>
    public const float MinScaledPixels = 1.5f;

    /// <summary>The band never gets thicker than this many UI-scale pixels, so it never swallows a tile at the closest zoom.</summary>
    public const float MaxScaledPixels = 4f;

    /// <summary>The casing (the thin outer edge of the band) takes this share of the band.</summary>
    public const float CasingShare = 0.3f;

    /// <summary>Opacity of the border of a remembered (fogged) tile.</summary>
    public const float RememberedOpacity = 0.65f;

    /// <summary>The band's width in whole screen pixels, at least 2: <paramref name="tileScreenPixels"/> is the tile's corner-to-corner width on screen, <paramref name="uiScale"/> the effective UI scale (<see cref="UiTokens.AutoScale"/> times the user's).</summary>
    public static int WidthPixels(float tileScreenPixels, float uiScale)
    {
        var scale = uiScale > 0f && float.IsFinite(uiScale) ? uiScale : 1f;
        var raw = float.IsFinite(tileScreenPixels) ? WidthFraction * tileScreenPixels : 0f;
        var width = Math.Clamp(raw, MinScaledPixels * scale, MaxScaledPixels * scale);
        return Math.Max(2, (int)MathF.Round(width, MidpointRounding.AwayFromZero));
    }

    /// <summary>The casing colour: the nation's inner colour when it contrasts with the outer one by at least 3:1, else black or white, whichever contrasts more.</summary>
    public static Rgb CasingFor(Rgb outer, Rgb inner)
    {
        if (UiTokens.Contrast(outer, inner) >= 3.0) return inner;
        var black = new Rgb(0, 0, 0);
        var white = new Rgb(255, 255, 255);
        return UiTokens.Contrast(outer, black) >= UiTokens.Contrast(outer, white) ? black : white;
    }
}
