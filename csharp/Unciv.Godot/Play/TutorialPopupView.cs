using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C026: the modal popup of one tutorial step (<see cref="TutorialQueue"/>): the tutorial's name, the step's text, a step counter when there are several, and an OK
/// button (Space also presses it). It raises <see cref="Dismissed"/>; the owner advances the queue and marks the tutorial seen after its last step.
/// </summary>
public partial class TutorialPopupView : Control
{
    private readonly ColorRect dim = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly PanelContainer panel = new() { Name = "TutorialPanel" };
    private readonly Label title = new() { Name = "TutorialTitle", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Label body = new() { Name = "TutorialBody", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label counter = new() { Name = "TutorialCounter", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button ok = new() { Name = "TutorialOk" };
    private UiTheme theme = null!; // JUSTIFY: C026 assigned by Init before the popup is shown

    public TutorialPopupView()
    {
        Name = "TutorialPopup";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        dim.Fill();
        AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.Fill();
        AddChild(centre);
        centre.AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(title);
        column.AddChild(body);
        column.AddChild(counter);
        column.AddChild(ok);
        ok.Pressed += () => Dismissed?.Invoke();
    }

    public TutorialPage? Current { get; private set; }

    public string TitleText => title.Text;

    public string BodyText => body.Text;

    public string CounterText => counter.Text;

    public Button OkButton => ok;

    /// <summary>The OK button was pressed (or Space).</summary>
    public event Action? Dismissed;

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
        var shade = uiTheme.Panel.BgColor;
        shade.A = 0.6f;
        dim.Color = shade;
        title.AddThemeFontSizeOverride("font_size", uiTheme.Px(22));
    }

    public void LayoutIn(Vector2 screen) => Size = screen;

    public void Open(TutorialPage page)
    {
        Current = page;
        title.Text = page.Title.Tr();
        body.Text = page.Text.TrText();
        body.CustomMinimumSize = new Vector2(theme.Px(420), 0f);
        counter.Visible = page.StepCount > 1;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        counter.Text = page.StepCount > 1 ? $"{(page.Step + 1).ToString(inv)} / {page.StepCount.ToString(inv)}" : "";
        ok.Text = "OK".Tr();
        Visible = true;
    }

    public void Close()
    {
        Current = null;
        Visible = false;
    }
}
