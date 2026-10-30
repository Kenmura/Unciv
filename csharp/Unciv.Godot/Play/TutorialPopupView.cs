using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C026: the modal popup of one tutorial step (<see cref="TutorialQueue"/>): the tutorial's name, the step's text, a step counter when there are several, and an OK
/// button (Space also presses it). It raises <see cref="Dismissed"/>; the owner advances the queue and marks the tutorial seen after its last step.
/// C066: a <see cref="Modal"/>; Escape dismisses the step like OK.
/// </summary>
public partial class TutorialPopupView : Modal
{
    private readonly Label body = new() { Name = "TutorialBody", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label counter = new() { Name = "TutorialCounter", ThemeTypeVariation = UiVariants.LabelMuted };
    private readonly Button ok = new() { Name = "TutorialOk" };
    private readonly Button never = new() { Name = "TutorialNever" };

    public TutorialPopupView()
    {
        Name = "TutorialPopup";
        TitleLabel.Name = "TutorialTitle";
        Body.AddChild(body);
        Body.AddChild(counter);
        SetActions([(never, ChoiceRole.Ghost), (ok, ChoiceRole.Primary)]);
        never.Pressed += () => NeverShowRequested?.Invoke();
        ok.Pressed += () => Dismissed?.Invoke();
    }

    public TutorialPage? Current { get; private set; }

    public string TitleText => TitleLabel.Text;

    public string BodyText => body.Text;

    public string CounterText => counter.Text;

    public Button OkButton => ok;

    public Button NeverButton => never;

    /// <summary>The player chose "Never show tutorials": the owner turns the persisted setting off and closes the popup.</summary>
    public event Action? NeverShowRequested;

    /// <summary>The OK button was pressed (or Space, or Escape).</summary>
    public event Action? Dismissed;

    public void Open(TutorialPage page)
    {
        Current = page;
        SetHeader(page.Title);
        body.CustomMinimumSize = new Vector2(Fit(480), 0f);
        body.Text = page.Text.TrText();
        counter.Visible = page.StepCount > 1;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        counter.Text = page.StepCount > 1 ? $"{(page.Step + 1).ToString(inv)} / {page.StepCount.ToString(inv)}" : "";
        ok.Text = "OK".Tr();
        never.Text = "Never show tutorials".Tr();
        Visible = true;
    }

    public void Close()
    {
        Current = null;
        Visible = false;
    }

    protected override bool OnCancel()
    {
        if (Current == null) return false;
        Dismissed?.Invoke();
        return true;
    }
}
