using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.Models.Translations;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C013: the modal popup for one pending alert (<see cref="AlertPopupModel"/>): title, text and one button per answer the engine accepts, plus "Decide later"
/// when the alert may stay pending. It raises <see cref="Answered"/> with the pressed button (the owner submits its command) or <see cref="Deferred"/>.
/// C066: a <see cref="Modal"/>: the first answer is the primary button, the others secondary, "Decide later" ghost; Escape means "Decide later" when the alert may be put off
/// and does nothing when it must be answered.
/// </summary>
public partial class AlertPopupView : Modal
{
    private readonly Label body = new() { Name = "AlertBody", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Dictionary<string, Button> byId = [];
    private readonly List<string> ids = [];

    public AlertPopupView()
    {
        Name = "AlertPopup";
        TitleLabel.Name = "AlertTitle";
        Body.AddChild(body);
    }

    public AlertPopup? Current { get; private set; }

    public string TitleText => TitleLabel.Text;

    public string BodyText => body.Text;

    /// <summary>The ids of the answers in the order the popup lists them ("DecideLater" last), not the order they are laid out in.</summary>
    public IReadOnlyList<string> ButtonIds => ids;

    public Button? ButtonById(string id) => byId.GetValueOrDefault(id);

    public event Action<AlertPopup, AlertButton>? Answered;

    public event Action<AlertPopup>? Deferred;

    public void Open(AlertPopup popup)
    {
        Current = popup;
        SetHeader(popup.Title, icon: AssetKey.Of(RulesetAssetKeys.UiIcon, "ExclamationMark"));
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
        if (popup.CanDefer)
        {
            var later = new Button { Name = "DecideLater", Text = "Decide later".Tr() };
            later.Pressed += () => Deferred?.Invoke(popup);
            actions.Add((later, ChoiceRole.Ghost));
            byId["DecideLater"] = later;
            ids.Add("DecideLater");
        }
        SetActions(actions);
        Visible = true;
    }

    public void Close()
    {
        Current = null;
        Visible = false;
    }

    /// <summary>Escape puts the alert off when it may be put off; an alert that must be answered ignores it (and the game screen below does not see the key either).</summary>
    protected override bool OnCancel()
    {
        if (Current is not { CanDefer: true } popup) return Current != null;
        Deferred?.Invoke(popup);
        return true;
    }
}
