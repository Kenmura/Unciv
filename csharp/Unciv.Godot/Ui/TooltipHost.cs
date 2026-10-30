using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// One tooltip for the whole UI. <see cref="Attach"/> registers a control and a function producing its text (BBCode, so it can be rich);
/// after the pointer has rested on the control for <see cref="Delay"/> seconds the tooltip appears next to the pointer and follows it,
/// staying inside the window and below <see cref="TopInset"/> (C066: never over the HUD's top bar). Leaving the control, or pressing a mouse button, hides it. Time is fed by <see cref="Advance"/> (called each frame),
/// which lets a test fast-forward instead of waiting.
/// <para>C100, touch builds (<see cref="DisplayProfile.Touch"/>): there is no hover, so a tap on a part of the UI that has a tip (an <see cref="Attach"/>ed control or any control with a
/// Godot <c>TooltipText</c>, looked up through its parents like Godot's own tooltip) shows it above the finger, a long press (0.5 s) does so for a button (which is then not pressed),
/// the next press anywhere hides it (on the same part it toggles it off) and it goes away by itself after <see cref="TouchHideSeconds"/>. The map is shown a tip by its owner through
/// <see cref="Point"/>.</para>
/// </summary>
public partial class TooltipHost : Control
{
    private readonly UiTheme theme;
    /// <summary>The widest a tooltip gets, in unscaled pixels (design/ui-redesign.md 2.5); longer lines wrap.</summary>
    public const int MaxWidth = 340;

    private const int MinTextWidth = 120;
    private const float BoldSlack = 1.08f;

    private readonly PanelContainer panel = new() { Visible = false, ThemeTypeVariation = UiVariants.PanelTooltip };
    private readonly RichTextLabel text = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false };
    private Control? hovered;
    private Func<string>? textOf;
    private double rested;
    private string shownSource = "";
    private readonly Dictionary<Control, Func<string>> attached = [];
    private Vector2? anchor;
    private Control? touchOwner;
    private double shownFor;
    private TouchPress? pending;
    private BaseButton? heldButton;

    /// <summary>How long a touch tip stays when nothing dismisses it.</summary>
    public const double TouchHideSeconds = 4.0;

    private sealed record TouchPress(Control Owner, Func<string> Text, Vector2 At)
    {
        public double Held { get; set; }

        public bool Moved { get; set; }

        public bool Shown { get; set; }
    }

    public TooltipHost(UiTheme theme)
    {
        this.theme = theme;
        Theme = theme.Theme; // the game screen is not under a ScreenManager: without its own theme the tip came out as a bare grey box
        this.Fill(); MouseFilter = MouseFilterEnum.Ignore;
        // T323: the tip's text width follows the live scale (a shown tip is re-measured, a hidden one is reset to the minimum)
        theme.Fit(this, () => text.CustomMinimumSize = new Vector2(panel.Visible ? TextWidth() : theme.Px(MinTextWidth), 0));
        panel.MouseFilter = MouseFilterEnum.Ignore;
        text.MouseFilter = MouseFilterEnum.Ignore;
        panel.AddChild(text);
        AddChild(panel);
    }

    public double Delay { get; set; } = 0.5;

    public bool IsShowing => panel.Visible;

    public string ShownText => text.Text;

    /// <summary>The minimum width of the tip's text now (the harness checks that it follows the live scale).</summary>
    public float TextMinWidth => text.CustomMinimumSize.X;

    public Control? Hovered => hovered;

    public Rect2 PanelRect => panel.GetGlobalRect();

    /// <summary>The y (global) above which a tip may not be placed, or null for the top of the window: the owner gives the bottom edge of its top bar.</summary>
    public Func<float>? TopInset { get; set; }

    /// <summary>The control whose tip a touch has on show (null for the map or when none).</summary>
    public Control? TouchOwner => touchOwner;

    public void Attach(Control target, Func<string> bbcodeText)
    {
        attached[target] = bbcodeText;
        target.TreeExiting += () => attached.Remove(target);
        target.MouseEntered += () =>
        {
            if (!DisplayProfile.Current.Touch) Enter(target, bbcodeText);
        };
        target.MouseExited += () =>
        {
            if (hovered == target) Leave();
        };
        target.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true } && !DisplayProfile.Current.Touch) HideTip();
        };
    }

    public override void _Process(double delta) => Advance(delta);

    public override void _Input(InputEvent @event)
    {
        if (!DisplayProfile.Current.Touch) return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button when button.Device == InputEvent.DeviceIdEmulation:
                if (button.Pressed) TouchDown(button.Position);
                else TouchUp();
                break;
            case InputEventMouseMotion motion when pending != null && motion.Device == InputEvent.DeviceIdEmulation:
                if (pending.At.DistanceTo(motion.Position) > TouchSlop()) pending.Moved = true; // a drag (a scroll) is not a tap
                break;
        }
    }

    private float TouchSlop()
    {
        var profile = DisplayProfile.Current;
        return profile.UsesDensity ? UiTokens.DpToPx(UiTokens.TouchDragThresholdDp, profile.Dpi) : theme.Px(12);
    }

    /// <summary>The control with a tip that a press at the hovered control lands on (Godot's own rule: the control or the nearest parent with a text, stopping at a control that takes the mouse).</summary>
    private (Control Owner, Func<string> Text)? Resolve(Control? under)
    {
        for (var node = under; node != null; node = node.GetParent() as Control)
        {
            var current = node;
            if (attached.TryGetValue(current, out var bbcode) && bbcode().Length > 0) return (current, bbcode);
            if (current.TooltipText.Length > 0) return (current, () => current.TooltipText.Replace("[", "[lb]", StringComparison.Ordinal));
            if (current.MouseFilter == MouseFilterEnum.Stop) break;
        }
        return null;
    }

    private void TouchDown(Vector2 at)
    {
        ReleaseHeldButton(); // a release that never came must not leave the button disabled
        var found = Resolve(GetViewport().GuiGetHoveredControl());
        bool toggleOff = found != null && touchOwner == found.Value.Owner;
        if (hovered != null) Leave(); // the next press anywhere hides the tip
        pending = found == null || toggleOff ? null : new TouchPress(found.Value.Owner, found.Value.Text, at);
    }

    private void TouchUp()
    {
        if (pending is { Moved: false, Shown: false } tap && tap.Owner is not BaseButton) ShowTouch(tap);
        pending = null;
        if (heldButton is { } button)
        {
            heldButton = null;
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(button)) button.Disabled = false;
            }).CallDeferred(); // after the release has gone by: a long press showed the tip, it must not also press the button
        }
    }

    private void ReleaseHeldButton()
    {
        if (heldButton is { } button && GodotObject.IsInstanceValid(button)) button.Disabled = false;
        heldButton = null;
    }

    private void ShowTouch(TouchPress press)
    {
        press.Shown = true;
        Enter(press.Owner, press.Text);
        rested = Delay;
        anchor = press.At;
        touchOwner = press.Owner;
        shownFor = 0;
    }

    public void Advance(double seconds)
    {
        if (pending is { Owner: BaseButton held, Moved: false, Shown: false } press && GodotObject.IsInstanceValid(held))
        {
            press.Held += seconds;
            if (press.Held >= UiTokens.LongPressSeconds)
            {
                ShowTouch(press);
                if (!held.Disabled)
                {
                    held.Disabled = true;
                    heldButton = held;
                }
            }
        }
        if (touchOwner != null && (shownFor += seconds) > TouchHideSeconds) Leave();
        if (hovered == null) return;
        string current = textOf!();
        if (current.Length == 0)
        {
            // nothing to say (yet): stay hidden but keep the delay served, so the tip appears as soon as the text is there
            panel.Visible = false;
            rested = Math.Min(rested + seconds, Delay);
            return;
        }
        if (!panel.Visible)
        {
            rested += seconds;
            if (rested < Delay) return;
            panel.Visible = true;
            panel.Size = Vector2.Zero;
        }
        if (current != shownSource)
        {
            shownSource = current;
            text.Text = current;
            text.CustomMinimumSize = new Vector2(TextWidth(), 0);
            panel.Size = Vector2.Zero;
        }
        Follow();
    }

    /// <summary>Hides the tip and restarts the delay (the pointer moved to something else inside the same control, e.g. another map tile).</summary>
    public void Restart() => HideTip();

    /// <summary>Manual hover for a region that is not a control of its own (the map): the tip shows <paramref name="bbcodeText"/> until <see cref="Unpoint"/>; an empty text shows nothing.</summary>
    public void Point(Func<string> bbcodeText, Vector2? at = null)
    {
        if (hovered == this) textOf = bbcodeText;
        else Enter(this, bbcodeText);
        if (at is not { } place) return;
        // C100: a tip pointed at by a touch shows at once above the finger and goes by itself
        anchor = place;
        rested = Delay;
        shownFor = 0;
        touchOwner = this;
    }

    public void Unpoint()
    {
        if (hovered == this) Leave();
    }

    private void Enter(Control target, Func<string> bbcodeText)
    {
        hovered = target;
        textOf = bbcodeText;
        rested = 0;
        panel.Visible = false;
    }

    /// <summary>C100: raised when a tip that <see cref="Point"/> showed ends (touch: the next press or the timeout), so its owner drops what it keeps for it.</summary>
    public event Action? PointEnded;

    private void Leave()
    {
        bool pointed = hovered == this;
        anchor = null;
        touchOwner = null;
        hovered = null;
        textOf = null;
        HideTip();
        if (pointed) PointEnded?.Invoke();
    }

    private void HideTip()
    {
        rested = 0;
        shownSource = "";
        panel.Visible = false;
    }

    /// <summary>The width the tip's text is laid out at: that of its longest line, up to <see cref="MaxWidth"/> less the panel's padding, so a short tip is narrow and a long one wraps.</summary>
    private float TextWidth()
    {
        var font = text.GetThemeFont("normal_font");
        int fontSize = text.GetThemeFontSize("normal_font_size");
        float widest = 0f;
        foreach (string line in text.GetParsedText().Split('\n')) widest = Math.Max(widest, font.GetStringSize(line, HorizontalAlignment.Left, -1, fontSize).X);
        float padding = 2 * theme.Px(UiTokens.S3) + 2 * theme.Px(UiTokens.BorderWidth);
        float most = theme.Px(MaxWidth) - padding;
        // a little slack for bold runs, which measure wider than the regular face the parsed text is measured in
        return Math.Clamp(widest * BoldSlack + 2f, theme.Px(MinTextWidth), most);
    }

    /// <summary>
    /// Where a tip goes next to a finger: above it (the finger covers what is below), else below, else to its right or left, vertically centred on it; the first place that fits inside the
    /// window and under the top bar. When none does (a tall tip on a small window) the right side, clamped into the window.
    /// </summary>
    private Vector2 AroundFinger(Vector2 finger, Vector2 size, Vector2 window)
    {
        float gap = theme.Px(16) + theme.Px(UiTokens.TouchTargetDp) / 2f;
        float top = TopInset?.Invoke() ?? 0f;
        float x = Math.Clamp(finger.X - size.X / 2f, 0, Math.Max(0, window.X - size.X));
        float y = Math.Clamp(finger.Y - size.Y / 2f, top, Math.Max(top, window.Y - size.Y));
        var places = new[] { new Vector2(x, finger.Y - gap - size.Y), new Vector2(x, finger.Y + gap), new Vector2(finger.X + gap, y), new Vector2(finger.X - gap - size.X, y) };
        foreach (var place in places)
            if (place.X >= 0 && place.Y >= top && place.X + size.X <= window.X && place.Y + size.Y <= window.Y) return place;
        return new Vector2(Math.Clamp(finger.X + gap, 0, Math.Max(0, window.X - size.X)), y);
    }

    private void Follow()
    {
        var pointer = anchor ?? GetViewport().GetMousePosition();
        var size = panel.GetCombinedMinimumSize();
        var window = GetViewportRect().Size;
        float offset = theme.Px(16);
        var at = pointer + new Vector2(offset, offset);
        if (anchor != null) at = AroundFinger(pointer, size, window);
        if (anchor == null && at.X + size.X > window.X) at.X = Math.Max(0, pointer.X - offset - size.X);
        if (anchor == null && at.Y + size.Y > window.Y) at.Y = Math.Max(0, pointer.Y - offset - size.Y);
        // below the top bar whatever the pointer does: when flipping above the pointer would put the tip over the bar, sit just under it instead
        float top = (TopInset?.Invoke() ?? 0f) + theme.Px(UiTokens.S1);
        if (at.Y < top) at.Y = Math.Min(top, Math.Max(0, window.Y - size.Y));
        panel.Position = at;
    }
}
