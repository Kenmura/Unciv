using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C065: "[icon][title / subtitle][trailing value]" as one toggle button. Hover and the selected state lift to Surface3 and the selected row gets a 3 px accent bar on its left.
/// A row with a <see cref="DisabledReason"/> is disabled, greyed, and shows the reason in place of its subtitle. The harness finds it by its title (<c>ui_label</c>).
/// </summary>
public partial class ListRow : Button
{
    private readonly UiTheme theme;
    private readonly HBoxContainer content = new();
    private readonly Label titleLabel = new() { ThemeTypeVariation = UiVariants.LabelBodyStrong, VerticalAlignment = VerticalAlignment.Center };
    private readonly Label subtitleLabel = new() { ThemeTypeVariation = UiVariants.LabelSmall, Visible = false };
    private readonly Label trailingLabel = new() { ThemeTypeVariation = UiVariants.LabelNumber, VerticalAlignment = VerticalAlignment.Center, Visible = false };

    public ListRow(UiTheme theme, string title, string? subtitle = null, string? trailing = null, AssetKey? icon = null, string? disabledReason = null, ButtonGroup? group = null)
    {
        this.theme = theme;
        ThemeTypeVariation = UiVariants.ButtonRow;
        ToggleMode = true;
        ButtonGroup = group;
        Title = title;
        DisabledReason = disabledReason;
        SetMeta(IconButton.LabelMeta, title);
        content.MouseFilter = MouseFilterEnum.Ignore;
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        content.OffsetLeft = theme.Px(UiTokens.S3);
        content.OffsetRight = -theme.Px(UiTokens.S3);
        content.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        if (icon is { } key)
        {
            content.AddChild(new TextureRect
            {
                Texture = theme.Icon(key),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = TextureFilterEnum.LinearWithMipmaps,
                CustomMinimumSize = new Vector2(theme.Px(UiTheme.IconSize), theme.Px(UiTheme.IconSize)),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        var texts = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        texts.AddThemeConstantOverride("separation", 0);
        titleLabel.Text = title.TrText();
        titleLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        titleLabel.MouseFilter = MouseFilterEnum.Ignore;
        subtitleLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        subtitleLabel.MouseFilter = MouseFilterEnum.Ignore;
        texts.AddChild(titleLabel);
        texts.AddChild(subtitleLabel);
        content.AddChild(texts);
        trailingLabel.MouseFilter = MouseFilterEnum.Ignore;
        content.AddChild(trailingLabel);
        AddChild(content);
        Subtitle = subtitle;
        Trailing = trailing;
        if (disabledReason != null)
        {
            Disabled = true;
            subtitleLabel.Text = disabledReason.TrText();
            subtitleLabel.Visible = true;
            var muted = ProceduralAssets.ToColor(UiTokens.TextDisabled);
            titleLabel.AddThemeColorOverride("font_color", muted);
            trailingLabel.AddThemeColorOverride("font_color", muted);
            subtitleLabel.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(UiTokens.Warning));
        }
        else
        {
            subtitleLabel.Text = subtitle?.TrText() ?? "";
            subtitleLabel.Visible = !string.IsNullOrEmpty(subtitle);
        }
        trailingLabel.Visible = !string.IsNullOrEmpty(trailing);
        trailingLabel.Text = trailing ?? "";
        Toggled += _ => QueueRedraw();
    }

    /// <summary>The untranslated title (also the harness handle).</summary>
    public string Title { get; }

    public string? Subtitle { get; }

    public string? Trailing { get; }

    /// <summary>Why the row cannot be chosen, or null when it can. The reason replaces the subtitle.</summary>
    public string? DisabledReason { get; }

    /// <summary>The second line as shown: the disabled reason when there is one, else the subtitle; empty when hidden.</summary>
    public string ShownSubtitle => subtitleLabel.Visible ? subtitleLabel.Text : "";

    public string ShownTrailing => trailingLabel.Visible ? trailingLabel.Text : "";

    /// <summary>C075: translates the title again after a language change.</summary>
    public void Retranslate() => titleLabel.Text = Title.TrText();

    public override void _Ready() => Refit();

    public override void _Notification(int what)
    {
        if (what == NotificationThemeChanged) Refit();
    }

    public override void _Draw()
    {
        if (ButtonPressed) DrawRect(new Rect2(0, 0, theme.Px(3), Size.Y), ProceduralAssets.ToColor(UiTokens.Accent));
    }

    /// <summary>The row is as tall as its text needs (the one-line height of a control at least), because a button does not size itself from its children.</summary>
    private void Refit()
    {
        if (!IsInsideTree()) return;
        float height = content.GetCombinedMinimumSize().Y + 2 * theme.Px(UiTokens.S1);
        CustomMinimumSize = new Vector2(0, Math.Max(theme.Px(UiTokens.RowHeight), height));
    }
}
