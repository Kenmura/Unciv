using Godot;

namespace Unciv.GodotClient.Ui;

internal static class UiLayout
{
    /// <summary>T316: a minimum size of <paramref name="width"/> x <paramref name="height"/> unscaled pixels that follows the live scale (see <see cref="UiTheme.Fit"/>).</summary>
    public static T FitMin<T>(this T control, UiTheme theme, int width, int height = 0) where T : Control
    {
        theme.Fit(control, () => control.CustomMinimumSize = new Vector2(theme.Px(width), theme.Px(height)));
        return control;
    }

    /// <summary>T316: a container constant (separation, margin) of <paramref name="unscaled"/> pixels that follows the live scale.</summary>
    public static T FitConstant<T>(this T control, UiTheme theme, string name, int unscaled) where T : Control
    {
        theme.Fit(control, () => control.AddThemeConstantOverride(name, theme.Px(unscaled)));
        return control;
    }

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
