using Godot;
using Unciv.Commands;

namespace Unciv.GodotHarness;

/// <summary>
/// C061: the harness itself. The scenario runs in a 1280x720 window (<see cref="Viewport"/>) and presses an icon-only button (no text, only
/// <c>ui_label</c> metadata) by its label. No command is submitted, so the log stays empty.
/// </summary>
public sealed class ViewportScenario : Scenario
{
    public override string Name => "viewport";

    public override Vector2I Viewport => new(1280, 720);

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        int presses = 0;
        var label = new Label { Text = "Waiting", Position = new Vector2(40, 40) };
        var button = new Button { Position = new Vector2(1000, 600), CustomMinimumSize = new Vector2(120, 60) };
        button.SetMeta(ScenarioRunner.LabelMeta, "Open map");
        button.Pressed += () => { presses++; label.Text = "Pressed by label"; };
        host.AddChild(label);
        host.AddChild(button);

        return
        [
            new WaitFrames(2),
            new AssertQuery("the viewport is 1280x720", () => host.GetViewport().GetVisibleRect().Size == new Vector2(1280, 720)),
            new Press("Open map"),
            new AssertQuery("the text-less button was pressed once through its ui_label", () => presses == 1),
            new Capture("pressed", "A 1280x720 capture: the label reads \"Pressed by label\" and an empty button sits bottom right (harness test, not a game screen)"),
        ];
    }
}
