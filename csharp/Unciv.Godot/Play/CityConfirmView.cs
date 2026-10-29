using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Localization;
using Unciv.Models.Translations;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>C069: the confirmation before a destructive city command (Raze): the question, a Danger button that sends the command and Cancel (also Escape).</summary>
public partial class CityConfirmView : Modal
{
    private readonly Action<GameCommand> submit;
    private readonly Label question = new() { Name = "ConfirmText", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button confirm = new() { Name = "ConfirmDanger" };
    private readonly Button cancel = new() { Name = "ConfirmCancel", Text = "Cancel".Tr() };
    private GameCommand? pending;

    public CityConfirmView(Action<GameCommand> submitCommand, UiTheme theme)
    {
        submit = submitCommand;
        Name = "ConfirmPopup";
        Init(theme);
        Body.AddChild(question);
        SetActions([(confirm, ChoiceRole.Danger), (cancel, ChoiceRole.Secondary)]);
        confirm.Pressed += Confirm;
        cancel.Pressed += Cancel;
    }

    public string QuestionText => question.Text;

    public Button ConfirmButton => confirm;

    public void Open(CityStateAction action)
    {
        pending = action.Command;
        SetHeader(action.Label);
        question.CustomMinimumSize = new Vector2(Fit(Modal.MinWidth), 0);
        question.Text = (action.Confirm ?? "").TrText();
        confirm.Text = action.Label.TrText();
        Visible = true;
        cancel.GrabFocus(); // the safe choice has the focus
    }

    public void Confirm()
    {
        var command = pending;
        Cancel();
        if (command != null) submit(command);
    }

    public void Cancel()
    {
        pending = null;
        Visible = false;
    }

    protected override bool OnCancel()
    {
        Cancel();
        return true;
    }
}
