using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.Models.Translations;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C039: the modal popup for one incoming trade or peace proposal (<see cref="TradeProposalModel"/>): who proposes, what they give and ask, and the answers
/// Accept, Decline and "How about something else", plus "Decide later". It raises <see cref="Answered"/> with the pressed button (the owner submits its command) or
/// <see cref="Deferred"/>. C066: a <see cref="Modal"/> (the first answer, Accept, is primary; "Decide later" is ghost and what Escape does).
/// </summary>
public partial class TradeProposalView : Modal
{
    private readonly Label body = new() { Name = "ProposalBody", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Dictionary<string, Button> byId = [];
    private readonly List<string> ids = [];

    public TradeProposalView()
    {
        Name = "ProposalPopup";
        TitleLabel.Name = "ProposalTitle";
        Body.AddChild(body);
    }

    public TradeProposal? Current { get; private set; }

    public string TitleText => TitleLabel.Text;

    public string BodyText => body.Text;

    /// <summary>The ids of the answers in the order the popup lists them ("DecideLater" last), not the order they are laid out in.</summary>
    public IReadOnlyList<string> ButtonIds => ids;

    public Button? ButtonById(string id) => byId.GetValueOrDefault(id);

    public event Action<TradeProposal, AlertButton>? Answered;

    public event Action<TradeProposal>? Deferred;

    public void Open(TradeProposal popup)
    {
        Current = popup;
        SetHeader(popup.Title);
        body.CustomMinimumSize = new Vector2(Fit(440), 0f);
        body.Text = popup.Body.TrText();
        byId.Clear();
        ids.Clear();
        var actions = new List<(Button, ChoiceRole)>();
        foreach (var answer in popup.Buttons)
        {
            var button = new Button { Name = answer.Id, Text = answer.Label.TrText() };
            var chosen = answer;
            button.Pressed += () => Answered?.Invoke(popup, chosen);
            actions.Add((button, actions.Count == 0 ? ChoiceRole.Primary : ChoiceRole.Secondary));
            byId[answer.Id] = button;
            ids.Add(answer.Id);
        }
        var later = new Button { Name = "DecideLater", Text = "Decide later".Tr() };
        later.Pressed += () => Deferred?.Invoke(popup);
        actions.Add((later, ChoiceRole.Ghost));
        byId["DecideLater"] = later;
        ids.Add("DecideLater");
        SetActions(actions);
        Visible = true;
    }

    public void Close()
    {
        Current = null;
        Visible = false;
    }

    /// <summary>Escape puts the proposal off, like "Decide later".</summary>
    protected override bool OnCancel()
    {
        if (Current is not { } popup) return false;
        Deferred?.Invoke(popup);
        return true;
    }
}
