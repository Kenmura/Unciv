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
    private readonly VBoxContainer body = new() { Name = "ProposalBody" };
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

    public string BodyText => Current?.Body ?? "";

    /// <summary>C073: the offer lines as drawn, in order: what they give, then what they ask (each a <see cref="IconLabel"/>).</summary>
    public IReadOnlyList<IconLabel> ItemLines => body.GetChildren().OfType<VBoxContainer>().SelectMany(it => it.GetChildren().OfType<IconLabel>()).ToList();

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
        foreach (var child in body.GetChildren())
        {
            body.RemoveChild(child); // at once: a queued free would leave the old lines in the layout until the frame ends
            child.QueueFree();
        }
        AddSection("They give", popup.GiveItems);
        AddSection("They ask", popup.AskItems);
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

    private void AddSection(string heading, IReadOnlyList<TradeItem> items)
    {
        var section = new VBoxContainer { Name = "Section_" + heading.Replace(' ', '_') };
        section.AddThemeConstantOverride("separation", Ui.Px(UiTokens.S1));
        section.AddChild(new SectionHeader(heading));
        if (items.Count == 0) section.AddChild(new Label { Text = "Nothing".Tr(), ThemeTypeVariation = UiVariants.LabelSecondary });
        foreach (var item in items) section.AddChild(new IconLabel(Ui, item.Icon, item.Text.TrText(), trim: true));
        body.AddChild(section);
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
