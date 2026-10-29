using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>A small icon followed by text, e.g. a stat icon and its number.</summary>
public partial class IconLabel : HBoxContainer
{
    private readonly Label label = new();

    public IconLabel(UiTheme theme, AssetKey icon, string text)
    {
        var picture = new TextureRect
        {
            Texture = theme.Icon(icon),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            CustomMinimumSize = new Vector2(theme.Px(UiTheme.IconSize), theme.Px(UiTheme.IconSize)),
        };
        AddChild(picture);
        AddChild(label);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Text = text;
        IconKey = icon;
    }

    public AssetKey IconKey { get; }

    public string Text
    {
        get => label.Text;
        set => label.Text = value;
    }
}
