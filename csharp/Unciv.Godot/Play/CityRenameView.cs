using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.Models.Translations;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C048: the "Rename city" popup over the city screen: a text field that starts as the current name, the reason a name is refused as you type
/// (<see cref="CityRenameModel"/>), "OK" (Enter in the field does the same) and "Cancel" (Escape). It builds the
/// <c>RenameCity</c> command and hands it to <see cref="submit"/>; it never changes a city itself.
/// </summary>
public partial class CityRenameView : Modal
{
    private readonly Action<GameCommand> submit;
    private readonly LineEdit nameField = new() { Name = "RenameField" };
    private readonly Label problem = new() { Name = "RenameProblem", ThemeTypeVariation = UiVariants.LabelSmall, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button confirm = new() { Name = "RenameConfirm", Text = "OK".Tr() };
    private readonly Button cancel = new() { Name = "RenameCancel", Text = "Cancel".Tr() };
    private string cityId = "";
    private string current = "";

    public CityRenameView(Action<GameCommand> submitCommand, UiTheme theme)
    {
        submit = submitCommand;
        Name = "RenamePopup";
        Init(theme);
        SetHeader("Rename city");
        Body.AddChild(nameField);
        problem.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(UiTokens.DangerText));
        Body.AddChild(problem);
        SetActions([(confirm, ChoiceRole.Primary), (cancel, ChoiceRole.Secondary)]);
        nameField.TextChanged += _ => Update();
        nameField.TextSubmitted += _ => Confirm();
        confirm.Pressed += Confirm;
        cancel.Pressed += Cancel;
    }

    public LineEdit Field => nameField;

    public string ProblemText => problem.Text;

    public bool CanConfirm => !confirm.Disabled;

    /// <summary>Shows the popup for a city; the nameField starts as its current name, selected, with the keyboard focus.</summary>
    public void Open(string id, string name)
    {
        cityId = id;
        current = name;
        nameField.CustomMinimumSize = new Vector2(Fit(360), 0);
        nameField.Text = name;
        Update();
        Visible = true;
        nameField.GrabFocus();
        nameField.SelectAll();
    }

    /// <summary>What the OK button (or Enter) does: sends the command when the name is acceptable, else only shows why not.</summary>
    public void Confirm()
    {
        var command = CityRenameModel.Command(cityId, current, nameField.Text);
        if (command == null)
        {
            Update();
            return;
        }
        Visible = false;
        submit(command);
    }

    /// <summary>What the Cancel button and Escape do: closes without sending anything.</summary>
    public void Cancel() => Visible = false;

    protected override bool OnCancel()
    {
        Cancel();
        return true;
    }

    private void Update()
    {
        string? why = CityRenameModel.Problem(nameField.Text, current);
        problem.Text = (why ?? "").TrText();
        confirm.Disabled = why != null;
    }
}
