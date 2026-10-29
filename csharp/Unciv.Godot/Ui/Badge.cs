using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>C065: a count pill (red for alerts, gold for available picks), "99+" beyond 99, hidden at zero. Does not take mouse input.</summary>
public partial class Badge : PanelContainer
{
    private readonly Label label = new() { HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = UiVariants.LabelSmall };
    private int count;

    public Badge(int count, bool accent = false)
    {
        ThemeTypeVariation = accent ? UiVariants.PanelBadgeAccent : UiVariants.PanelBadge;
        MouseFilter = MouseFilterEnum.Ignore;
        label.MouseFilter = MouseFilterEnum.Ignore;
        // the pill is red or gold: the text on it is the dark OnAccent colour on gold, light on red
        label.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(accent ? UiTokens.OnAccent : UiTokens.TextPrimary));
        AddChild(label);
        Count = count;
    }

    public int Count
    {
        get => count;
        set
        {
            count = value;
            label.Text = UiText.BadgeText(value);
            Visible = value > 0;
        }
    }

    public string Text => label.Text;
}
