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
/// (<see cref="CityRenameModel"/>), "OK" (Enter in the field does the same) and "Cancel" (Escape, handled by the city screen). It builds the
/// <c>RenameCity</c> command and hands it to <see cref="submit"/>; it never changes a city itself.
/// </summary>
public partial class CityRenameView : Control
{
    private readonly Action<GameCommand> submit;
    private readonly LineEdit nameField = new() { Name = "RenameField", CustomMinimumSize = new Vector2(240, 0) };
    private readonly Label problem = new() { Name = "RenameProblem" };
    private readonly Button confirm = new() { Name = "RenameConfirm", Text = "OK".Tr() };
    private readonly Button cancel = new() { Name = "RenameCancel", Text = "Cancel".Tr() };
    private string cityId = "";
    private string current = "";

    public CityRenameView(Action<GameCommand> submitCommand, UiTheme theme)
    {
        submit = submitCommand;
        Name = "RenamePopup";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        this.Fill();
        var dim = new ColorRect { Color = theme.Panel.BgColor with { A = 0.7f }, MouseFilter = MouseFilterEnum.Ignore };
        dim.Fill();
        AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.Fill();
        AddChild(centre);
        var panel = new PanelContainer();
        centre.AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(new Label { Text = "Rename city".Tr(), HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(nameField);
        problem.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(PlaceholderPalette.ErrorText));
        column.AddChild(problem);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddChild(confirm);
        row.AddChild(cancel);
        column.AddChild(row);
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

    private void Update()
    {
        string? why = CityRenameModel.Problem(nameField.Text, current);
        problem.Text = (why ?? "").TrText();
        confirm.Disabled = why != null;
    }
}
