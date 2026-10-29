using Godot;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C065: the restyled <see cref="ChoiceRow"/>: every option visible in one Inset track, exactly one chosen (shown in the accent tint), one click to change.
/// Same API as ChoiceRow, so a screen swaps one for the other.
/// </summary>
public partial class Segmented : PanelContainer
{
    private readonly ButtonGroup group = new();
    private readonly Dictionary<string, Button> buttons = [];
    private readonly HBoxContainer row = new();

    public Segmented(IEnumerable<string> options, string? selected)
    {
        ThemeTypeVariation = UiVariants.PanelSegmentTrack;
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        row.AddThemeConstantOverride("separation", 0);
        AddChild(row);
        foreach (string option in options)
        {
            var button = new Button { Text = option.TrText(), ToggleMode = true, ButtonGroup = group, ButtonPressed = option == selected, ThemeTypeVariation = UiVariants.ButtonSegment };
            button.Pressed += () => Chosen?.Invoke(option);
            buttons[option] = button;
            row.AddChild(button);
        }
    }

    /// <summary>Raised when the player presses an option (also the one already chosen).</summary>
    public event Action<string>? Chosen;

    public Button? ButtonFor(string option) => buttons.GetValueOrDefault(option);

    public string? Selected => buttons.FirstOrDefault(it => it.Value.ButtonPressed).Key;
}
