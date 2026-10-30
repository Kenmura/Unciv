using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// T322: holds one child and gives it at most <c>maxWidth</c> logical pixels (times the live scale), centred in the room it has: a form on a 4K or ultrawide window keeps its label next to its control instead of
/// spreading across the whole window. The height is what the child needs, so a <see cref="ScrollContainer"/> around it still scrolls.
/// </summary>
public partial class WidthCap : Container
{
    private readonly UiTheme theme;
    private readonly int maxWidth;

    public WidthCap(UiTheme theme, int maxWidth = UiTokens.MaxFormWidth)
    {
        this.theme = theme;
        this.maxWidth = maxWidth;
        Name = "WidthCap";
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        theme.Fit(this, QueueSort);
    }

    /// <summary>The widest the child is laid out, in screen pixels at the live scale.</summary>
    public int MaxPixels => theme.Px(maxWidth);

    public override Vector2 _GetMinimumSize()
    {
        var size = Vector2.Zero;
        foreach (var child in GetChildren())
        {
            if (child is Control { Visible: true } control) size = size.Max(control.GetCombinedMinimumSize());
        }
        return new Vector2(0, size.Y);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationSortChildren) return;
        float width = Math.Min(Size.X, MaxPixels);
        foreach (var child in GetChildren())
        {
            if (child is not Control { Visible: true } control) continue;
            float w = Math.Max(width, control.GetCombinedMinimumSize().X);
            FitChildInRect(control, new Rect2((Size.X - w) / 2f, 0, w, Size.Y));
        }
    }
}
