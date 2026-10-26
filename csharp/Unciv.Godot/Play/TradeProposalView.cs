using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.Models.Translations;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C039: the modal popup for one incoming trade or peace proposal (<see cref="TradeProposalModel"/>): who proposes, what they give and ask, and the answers
/// Accept, Decline and "How about something else", plus "Decide later". It raises <see cref="Answered"/> with the pressed button (the owner submits its command) or
/// <see cref="Deferred"/>.
/// </summary>
public partial class TradeProposalView : Control
{
    private readonly ColorRect dim = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly PanelContainer panel = new() { Name = "ProposalPanel" };
    private readonly Label title = new() { Name = "ProposalTitle", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Label body = new() { Name = "ProposalBody", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer buttons = new() { Name = "ProposalButtons" };
    private readonly Dictionary<string, Button> byId = [];
    private UiTheme theme = null!; // JUSTIFY: C039 assigned by Init before the popup is shown

    public TradeProposalView()
    {
        Name = "ProposalPopup";
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
        column.AddChild(buttons);
    }

    public TradeProposal? Current { get; private set; }

    public string TitleText => title.Text;

    public string BodyText => body.Text;

    /// <summary>The ids of the buttons shown, top to bottom ("DecideLater" included).</summary>
    public IReadOnlyList<string> ButtonIds => buttons.GetChildren().Select(it => it.Name.ToString()).ToList();

    public Button? ButtonById(string id) => byId.GetValueOrDefault(id);

    public event Action<TradeProposal, AlertButton>? Answered;

    public event Action<TradeProposal>? Deferred;

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

    public void Open(TradeProposal popup)
    {
        Current = popup;
        title.Text = popup.Title.TrText();
        body.Text = popup.Body.TrText();
        body.CustomMinimumSize = new Vector2(theme.Px(360), 0f);
        foreach (var child in buttons.GetChildren())
        {
            buttons.RemoveChild(child);
            child.QueueFree();
        }
        byId.Clear();
        foreach (var answer in popup.Buttons)
        {
            var button = new Button { Name = answer.Id, Text = answer.Label.TrText() };
            var chosen = answer;
            button.Pressed += () => Answered?.Invoke(popup, chosen);
            buttons.AddChild(button);
            byId[answer.Id] = button;
        }
        var later = new Button { Name = "DecideLater", Text = "Decide later".Tr() };
        later.Pressed += () => Deferred?.Invoke(popup);
        buttons.AddChild(later);
        byId["DecideLater"] = later;
        Visible = true;
    }

    public void Close()
    {
        Current = null;
        Visible = false;
    }
}
