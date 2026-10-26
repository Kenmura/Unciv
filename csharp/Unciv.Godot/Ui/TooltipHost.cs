using Godot;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// One tooltip for the whole UI. <see cref="Attach"/> registers a control and a function producing its text (BBCode, so it can be rich);
/// after the pointer has rested on the control for <see cref="Delay"/> seconds the tooltip appears next to the pointer and follows it,
/// staying inside the window. Leaving the control, or pressing a mouse button, hides it. Time is fed by <see cref="Advance"/> (called each frame),
/// which lets a test fast-forward instead of waiting.
/// </summary>
public partial class TooltipHost : Control
{
    private readonly UiTheme theme;
    private readonly PanelContainer panel = new() { Visible = false };
    private readonly RichTextLabel text = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false };
    private Control? hovered;
    private Func<string>? textOf;
    private double rested;
    private string shownSource = "";

    public TooltipHost(UiTheme theme)
    {
        this.theme = theme;
        this.Fill(); MouseFilter = MouseFilterEnum.Ignore;
        text.CustomMinimumSize = new Vector2(theme.Px(220), 0);
        panel.MouseFilter = MouseFilterEnum.Ignore;
        text.MouseFilter = MouseFilterEnum.Ignore;
        panel.AddChild(text);
        AddChild(panel);
    }

    public double Delay { get; set; } = 0.5;

    public bool IsShowing => panel.Visible;

    public string ShownText => text.Text;

    public Control? Hovered => hovered;

    public Rect2 PanelRect => panel.GetGlobalRect();

    public void Attach(Control target, Func<string> bbcodeText)
    {
        target.MouseEntered += () => Enter(target, bbcodeText);
        target.MouseExited += () =>
        {
            if (hovered == target) Leave();
        };
        target.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true }) HideTip();
        };
    }

    public override void _Process(double delta) => Advance(delta);

    public void Advance(double seconds)
    {
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
            panel.Size = Vector2.Zero;
        }
        Follow();
    }

    /// <summary>Hides the tip and restarts the delay (the pointer moved to something else inside the same control, e.g. another map tile).</summary>
    public void Restart() => HideTip();

    /// <summary>Manual hover for a region that is not a control of its own (the map): the tip shows <paramref name="bbcodeText"/> until <see cref="Unpoint"/>; an empty text shows nothing.</summary>
    public void Point(Func<string> bbcodeText)
    {
        if (hovered == this) textOf = bbcodeText;
        else Enter(this, bbcodeText);
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

    private void Leave()
    {
        hovered = null;
        textOf = null;
        HideTip();
    }

    private void HideTip()
    {
        rested = 0;
        shownSource = "";
        panel.Visible = false;
    }

    private void Follow()
    {
        var pointer = GetViewport().GetMousePosition();
        var size = panel.GetCombinedMinimumSize();
        var window = GetViewportRect().Size;
        float offset = theme.Px(16);
        var at = pointer + new Vector2(offset, offset);
        if (at.X + size.X > window.X) at.X = Math.Max(0, pointer.X - offset - size.X);
        if (at.Y + size.Y > window.Y) at.Y = Math.Max(0, pointer.Y - offset - size.Y);
        panel.Position = at;
    }
}
