using Godot;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>A wrapping row of toggle buttons of which exactly one is chosen (a segmented choice: every option visible, one click to change).</summary>
public partial class ChoiceRow : HFlowContainer
{
    private readonly ButtonGroup group = new();
    private readonly Dictionary<string, Button> buttons = [];

    public ChoiceRow(IEnumerable<string> options, string? selected)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foreach (string option in options)
        {
            var button = new Button { Text = option.TrText(), ToggleMode = true, ButtonGroup = group, ButtonPressed = option == selected };
            button.Pressed += () => Chosen?.Invoke(option);
            buttons[option] = button;
            AddChild(button);
        }
    }

    /// <summary>Raised when the player presses an option (also the one already chosen).</summary>
    public event Action<string>? Chosen;

    public Button? ButtonFor(string option) => buttons.GetValueOrDefault(option);

    public string? Selected => buttons.FirstOrDefault(it => it.Value.ButtonPressed).Key;
}
