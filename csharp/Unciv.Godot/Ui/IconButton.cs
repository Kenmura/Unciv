using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C065: a button with an icon and optional text. <see cref="Label"/> is the untranslated name that is the button's accessible name and the harness handle
/// (<c>Press("Label")</c> finds an icon-only button by it); the tooltip reads "Label (Key)". A <see cref="Badge"/> can sit on its top right corner.
/// </summary>
public partial class IconButton : Button
{
    /// <summary>The metadata key (C061) under which the label is stored, for the harness to find a button that shows no text.</summary>
    public const string LabelMeta = "ui_label";

    private readonly UiTheme theme;
    private Badge? badge;

    public IconButton(UiTheme theme, AssetKey? icon, string label, string? text = null, string? hotkey = null, string variation = UiVariants.ButtonIcon)
    {
        this.theme = theme;
        Label = label;
        Hotkey = hotkey;
        ThemeTypeVariation = variation;
        SetMeta(LabelMeta, label);
        if (icon is { } key)
        {
            Icon = theme.Icon(key);
            AddThemeConstantOverride("icon_max_width", theme.Px(UiTheme.IconSize));
        }
        if (text != null) Text = text.TrText();
        TooltipText = UiText.TooltipFor(label.TrText(), hotkey);
    }

    /// <summary>The untranslated accessible name.</summary>
    public string Label { get; }

    /// <summary>The key shown in the tooltip, or null.</summary>
    public string? Hotkey { get; }

    public Badge? CountBadge => badge;

    /// <summary>Shows <paramref name="count"/> on the corner (hidden at zero); <paramref name="accent"/> picks the gold pill for "available" instead of the red one for alerts.</summary>
    public void SetBadge(int count, bool accent = false)
    {
        if (badge == null)
        {
            badge = new Badge(count, accent);
            badge.SetAnchorsPreset(LayoutPreset.TopRight);
            badge.GrowHorizontal = GrowDirection.Begin;
            badge.OffsetRight = theme.Px(UiTokens.S1);
            badge.OffsetTop = -theme.Px(UiTokens.S1);
            AddChild(badge);
        }
        else
        {
            badge.Count = count;
        }
    }
}
