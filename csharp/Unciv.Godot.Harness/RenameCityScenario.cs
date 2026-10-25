using Godot;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// The trivial scenario that proves the runner (C008): a button in a test scene submits a RenameCity command, the label shows the
/// engine's answer. Press by text, activate by key, assert from the query, capture three states.
/// </summary>
public sealed class RenameCityScenario : Scenario
{
    private static readonly string[] Names = ["Ur", "Uruk"];

    public override string Name => "rename-city";

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the city name readable at 640x480?",
        "Does the label change each time the button is activated (mouse and Enter key)?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var city = ScenarioGame.FirstCity(session.Game, ScenarioGame.HumanA);
        var label = new Label { Position = new Vector2(40, 40), Text = Show(city.Name) };
        label.AddThemeFontSizeOverride("font_size", 32);
        int renames = 0;
        var button = new Button { Text = "Rename city", Position = new Vector2(40, 140), CustomMinimumSize = new Vector2(240, 60) };
        button.Pressed += () =>
        {
            var result = session.Submit(ScenarioGame.HumanA, new RenameCity { CityId = city.Id, Name = Names[renames % Names.Length] });
            if (result.Accepted) renames++;
            label.Text = Show(city.Name);
        };
        host.AddChild(label);
        host.AddChild(button);
        string originalName = city.Name;

        return
        [
            new WaitFrames(2),
            new AssertQuery("city starts with its founding name", () => city.Name == originalName),
            new Capture("start", "Before any command: the city's founding name"),
            new Press("Rename city"),
            new AssertQuery("first press renamed the city to Ur", () => city.Name == "Ur" && session.LastSeq == 1),
            new Capture("renamed", "After clicking the button: Ur"),
            new PressKey(Key.Enter),
            new AssertQuery("Enter on the focused button renamed it to Uruk", () => city.Name == "Uruk" && session.LastSeq == 2),
            new Capture("renamed-again", "After pressing Enter: Uruk"),
        ];
    }

    private static string Show(string name) => "City: " + name;
}
