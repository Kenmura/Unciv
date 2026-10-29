using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C066: a <see cref="PanelContainer"/> whose top edge carries the brass <see cref="UiTokens.Accent"/> hairline (design/ui-redesign.md 2.4). A <c>StyleBoxFlat</c> has one border colour,
/// so the theme variation (<see cref="UiVariants.PanelModal"/>, <see cref="UiVariants.PanelHeader"/>) reserves the line's thickness and this control paints the accent over it,
/// between the rounded corners, after the stylebox. <see cref="Modal"/> and <see cref="ScreenFrame"/> are its only users.
/// </summary>
public partial class AccentPanel : PanelContainer
{
    private UiTheme? theme;
    private int cornerRadius = UiTokens.RadiusPanel;

    /// <summary>The colour of the drawn hairline (read by the harness to check it is the accent, not the border colour).</summary>
    public Color LineColour { get; } = ProceduralAssets.ToColor(UiTokens.Accent);

    /// <summary>The rectangle of the hairline in local coordinates, or an empty rectangle before <see cref="Bind"/>.</summary>
    public Rect2 LineRect => theme == null ? default : new Rect2(theme.Px(cornerRadius), 0, Math.Max(0, Size.X - 2 * theme.Px(cornerRadius)), UiTokens.ScaledAtLeastOne(UiTokens.HairlineWidth, theme.Scale));

    /// <summary>Takes the scale from <paramref name="uiTheme"/>; <paramref name="radius"/> is the unscaled corner radius the line stops short of (0 for a square header).</summary>
    public void Bind(UiTheme uiTheme, int radius)
    {
        theme = uiTheme;
        cornerRadius = radius;
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        // after the base class has drawn the stylebox; _Draw would run before it
        if (what == NotificationDraw && theme != null) DrawRect(LineRect, LineColour);
    }
}
