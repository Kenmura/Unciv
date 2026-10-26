using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C040: the overlay shown while End turn is processed off the UI thread: a dimmed full-screen blocker (it takes the mouse, so nothing underneath can be clicked) with a
/// heading, the civ being played ("Rome - 3 of 8") and a progress bar (<see cref="NextTurnProgressModel"/>). The window keeps drawing because the work is on another thread.
/// </summary>
public partial class NextTurnProgressView : Control
{
    private readonly ColorRect dim = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label heading = new() { Name = "ProgressHeading", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Label detail = new() { Name = "ProgressDetail", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ProgressBar bar = new() { Name = "ProgressBar", MinValue = 0, MaxValue = 1, Step = 0.001, ShowPercentage = false };

    public NextTurnProgressView()
    {
        Name = "NextTurnProgress";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        dim.Fill();
        AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.Fill();
        AddChild(centre);
        var panel = new PanelContainer { Name = "ProgressPanel" };
        centre.AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(heading);
        column.AddChild(detail);
        column.AddChild(bar);
    }

    public string HeadingText => heading.Text;

    public string DetailText => detail.Text;

    public double Fraction => bar.Value;

    public void Init(UiTheme uiTheme)
    {
        Theme = uiTheme.Theme;
        var shade = uiTheme.Panel.BgColor;
        shade.A = 0.5f;
        dim.Color = shade;
        heading.AddThemeFontSizeOverride("font_size", uiTheme.Px(20));
        bar.CustomMinimumSize = new Vector2(uiTheme.Px(320), uiTheme.Px(14));
    }

    public void LayoutIn(Vector2 screen) => Size = screen;

    /// <summary>Shows the overlay (if hidden) with this progress.</summary>
    public void Show(NextTurnProgressViewData view)
    {
        heading.Text = view.Heading.TrText();
        detail.Text = view.Detail.TrText();
        bar.Value = view.Fraction;
        Visible = true;
    }

    public void Close() => Visible = false;
}
