using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C036: the multiplayer turn bar, a strip under the top bar: the headline ("Waiting for Bob"), the countdown to the host's deadline, and a line per human with a
/// connection dot (green connected, red offline) and Ended / Playing; plus short toasts for another human's visible actions. It draws a <see cref="TurnBarData"/>
/// and decides nothing. Toasts are counted in frames, not seconds, so a scenario's captures do not depend on the machine's speed.
/// </summary>
public partial class TurnBarView : Control
{
    private const int ToastFrames = 240;
    private const int MaxToasts = 3;

    private readonly PanelContainer panel = new() { Name = "TurnBarPanel", MouseFilter = MouseFilterEnum.Stop };
    private readonly Label headline = new() { Name = "Headline" };
    private readonly Label countdown = new() { Name = "Countdown" };
    private readonly VBoxContainer rows = new() { Name = "Players" };
    private readonly VBoxContainer toasts = new() { Name = "Toasts", MouseFilter = MouseFilterEnum.Ignore };
    private readonly List<(Label Label, int Left)> toastLabels = [];
    private UiTheme theme = null!; // JUSTIFY: C036 assigned by Init before the first Show
    private TurnBarData? data;

    public TurnBarView()
    {
        Name = "TurnBar";
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        var column = new VBoxContainer();
        var top = new HBoxContainer();
        top.AddChild(headline);
        top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(24, 0) });
        top.AddChild(countdown);
        column.AddChild(top);
        column.AddChild(rows);
        panel.AddChild(column);
        AddChild(panel);
        AddChild(toasts);
    }

    public string HeadlineText => headline.Text;

    /// <summary>The countdown text; empty when no timer runs.</summary>
    public string CountdownText => countdown.Text;

    public TurnBarData? Data => data;

    public IReadOnlyList<string> ToastTexts => toastLabels.Select(it => it.Label.Text).ToList();

    public Rect2 PanelRect => panel.GetGlobalRect();

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
    }

    /// <summary>Draws the bar; null hides it.</summary>
    public void Show(TurnBarData? bar)
    {
        data = bar;
        Visible = bar != null;
        if (bar == null) return;
        headline.Text = bar.Headline.TrText();
        countdown.Text = bar.Countdown ?? "";
        countdown.Visible = bar.Countdown != null;
        foreach (var child in rows.GetChildren())
        {
            rows.RemoveChild(child); // at once: a queued free would keep the old rows in the layout until the frame ends
            child.QueueFree();
        }
        foreach (var row in bar.Rows)
        {
            var line = new HBoxContainer { Name = "Player_" + row.CivId };
            var dotHolder = new CenterContainer { CustomMinimumSize = new Vector2(theme.Px(16), 0) };
            dotHolder.AddChild(new ColorRect
            {
                Name = row.Connected ? "Connected" : "Offline",
                CustomMinimumSize = new Vector2(theme.Px(10), theme.Px(10)),
                Color = ToColour(row.Connected ? PlaceholderPalette.HealthGood : PlaceholderPalette.HealthBad),
                TooltipText = (row.Connected ? "Connected" : "Disconnected").TrText(),
            });
            line.AddChild(dotHolder);
            line.AddChild(new Label { Name = "Name", Text = row.IsYou ? row.Name + " (you)" : row.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var state = new Label { Name = "State", Text = (row.Ended ? "Ended" : "Playing").TrText() };
            if (row.Ended) state.AddThemeColorOverride("font_color", ToColour(PlaceholderPalette.HealthGood));
            line.AddChild(state);
            rows.AddChild(line);
        }
        panel.CustomMinimumSize = new Vector2(theme.Px(260), 0);
    }

    /// <summary>Puts the bar centred just below <paramref name="top"/> pixels from the top of a screen <paramref name="width"/> wide.</summary>
    public void LayoutIn(float width, float top)
    {
        Size = new Vector2(width, 0);
        panel.Size = panel.GetCombinedMinimumSize();
        panel.Position = new Vector2((width - panel.Size.X) / 2f, top);
        toasts.Size = new Vector2(panel.Size.X, 0);
        toasts.Position = new Vector2(panel.Position.X, panel.Position.Y + panel.Size.Y + 8f);
    }

    public void Toast(string text)
    {
        var label = new Label { Text = text.TrText(), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        toasts.AddChild(label);
        toastLabels.Add((label, ToastFrames));
        while (toastLabels.Count > MaxToasts) Drop(0);
    }

    public override void _Process(double delta)
    {
        for (int i = toastLabels.Count - 1; i >= 0; i--)
        {
            toastLabels[i] = (toastLabels[i].Label, toastLabels[i].Left - 1);
            if (toastLabels[i].Left <= 0) Drop(i);
        }
    }

    private void Drop(int index)
    {
        toasts.RemoveChild(toastLabels[index].Label);
        toastLabels[index].Label.QueueFree();
        toastLabels.RemoveAt(index);
    }

    private static Color ToColour(Rgb rgb) => new(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);
}
