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
        MouseFilter = MouseFilterEnum.Ignore;
        label.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(label);
        Accent = accent;
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

    /// <summary>Gold pill with dark text (available picks) instead of the red pill with light text (alerts).</summary>
    public bool Accent
    {
        get => ThemeTypeVariation == UiVariants.PanelBadgeAccent;
        set
        {
            ThemeTypeVariation = value ? UiVariants.PanelBadgeAccent : UiVariants.PanelBadge;
            label.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(value ? UiTokens.OnAccent : UiTokens.TextPrimary));
        }
    }

    public string Text => label.Text;
}
