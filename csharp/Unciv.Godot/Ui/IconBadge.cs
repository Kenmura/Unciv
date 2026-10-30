using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C063: an icon on a filled circle, e.g. a nation emblem in its colours. The pack's glyphs are light silhouettes, so the badge colour is what makes them readable on any
/// background; a missing key shows the registry's generated placeholder in the same place.
/// </summary>
public partial class IconBadge : Control
{
    private readonly UiTheme theme;
    private readonly TextureRect picture = new();
    private readonly Color fill;
    private readonly Color ring;

    /// <param name="theme">The UI theme (scale and icon source).</param>
    /// <param name="icon">The icon key.</param>
    /// <param name="size">Edge length in unscaled pixels.</param>
    /// <param name="fill">Badge colour.</param>
    /// <param name="glyph">Tint of the icon on it (white keeps the file's own colours).</param>
    public IconBadge(UiTheme theme, AssetKey icon, Color fill, Color glyph, int size = 32)
    {
        this.theme = theme;
        this.fill = fill;
        ring = ProceduralAssets.ToColor(UiTokens.BorderStrong);
        IconKey = icon;
        Size = size;
        picture.Texture = theme.Icon(icon);
        picture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        picture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        picture.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        picture.Modulate = glyph;
        picture.MouseFilter = MouseFilterEnum.Ignore;
        picture.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(picture);
        // T316: the size and the inset are in theme pixels: follow a window resize or a scale change
        theme.Fit(this, () =>
        {
            picture.OffsetLeft = picture.OffsetTop = Inset;
            picture.OffsetRight = picture.OffsetBottom = -Inset;
            CustomMinimumSize = new Vector2(theme.Px(size), theme.Px(size));
            QueueRedraw();
        });
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public AssetKey IconKey { get; }

    public new int Size { get; }

    public Color Fill => fill;

    private float Inset => theme.Px(Size) * 0.18f;

    public override void _Draw()
    {
        var centre = GetRect().Size / 2f;
        float radius = Math.Min(centre.X, centre.Y);
        DrawCircle(centre, radius, fill);
        DrawArc(centre, radius - 0.5f, 0, Mathf.Tau, 48, ring, theme.Px(UiTokens.BorderWidth));
    }
}
