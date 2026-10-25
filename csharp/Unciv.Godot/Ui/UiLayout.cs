using Godot;

namespace Unciv.GodotClient.Ui;

internal static class UiLayout
{
    /// <summary>Stretch to the parent's full rectangle (anchors 0..1, no offsets); works before the control has a parent.</summary>
    public static void Fill(this Control control)
    {
        control.AnchorLeft = 0;
        control.AnchorTop = 0;
        control.AnchorRight = 1;
        control.AnchorBottom = 1;
        control.OffsetLeft = 0;
        control.OffsetTop = 0;
        control.OffsetRight = 0;
        control.OffsetBottom = 0;
    }
}
