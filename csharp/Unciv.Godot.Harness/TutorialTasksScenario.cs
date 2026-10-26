using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.Units;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.Harness;
using Unciv.Models;

namespace Unciv.GodotHarness;

/// <summary>
/// C054: the floating tutorial task card on the real game screen, from the Gods &amp; Kings Events with Floating presentation. The first available task shows under the
/// top bar ("Move a unit!"); a click folds it to a "?" button and the button opens it again; doing what it asks (here: reporting the move, founding the city, opening the
/// city screen) moves on to the next task the engine finds available; with Show tutorials off the card is gone. The settings file lives in the harness temp directory.
/// </summary>
public sealed class TutorialTasksScenario : Scenario
{
    public override string Name => "tutorial-tasks";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override bool TutorialTasks => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the task card readable and clear of the top bar and the HUD buttons at 640x480?",
        "Is it obvious that a click folds the card away (the \"?\" button)?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-tutorial-tasks-scenario-" + System.Environment.ProcessId, "settings.json");
        if (File.Exists(path)) File.Delete(path);
        var service = new ClientSettingsService(new ClientSettingsStore(path));
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.TutorialsEnabled = false; // the popups are C026's scenario; off before the first look at the game
        screen.TaskCardEnabled = true;
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        screen.BindSettings(service);
        var card = screen.TaskCard;
        var refusals = new List<string>();

        void Do(GameCommand command)
        {
            var result = localHost.SubmitAsync(command).GetAwaiter().GetResult();
            if (!result.Accepted) refusals.Add($"{command.GetType().Name}: {result.Message}");
        }

        return
        [
            new WaitFrames(4),
            new Act("dismiss the start banner", screen.Hud.HideBanner),
            new AssertQuery("the first task, Move unit, is shown", () => card.Visible && card.EventName == "Tutorial Task: [Move unit]" && card.LineTexts.Any(it => it.StartsWith("Move a unit", StringComparison.Ordinal))),
            new Capture("move-unit", "Game start: the floating task card under the top bar asks to move a unit"),
            new Click(() => card.CardRect.GetCenter()),
            new WaitFrames(2),
            new AssertQuery("a click folds the card to the ? button", () => card.Collapsed && card.CollapsedButton.Visible && card.LineTexts.Count == 0),
            new Capture("collapsed", "The same card folded to its small ? button"),
            new Click(() => card.CollapsedButton.GetGlobalRect().GetCenter()),
            new WaitFrames(2),
            new AssertQuery("the button opens it again", () => !card.Collapsed && card.LineTexts.Count > 0),
            new Act("move the warrior (the client reports it)", () =>
            {
                var warrior = human.Units.GetCivUnits().First(it => it.Name == "Warrior");
                var settler = human.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
                var target = warrior.CurrentTile.Neighbors.First(it => it.IsLand && !it.IsImpassible() && it != settler.CurrentTile);
                Do(new MoveUnit { UnitId = warrior.Id, To = target.Position });
                Do(new Unciv.Commands.Turn.CompleteTutorialTask { Task = TutorialTaskModel.MoveUnitTask });
                screen.Refresh();
            }),
            new WaitFrames(3),
            new AssertQuery("every setup command was accepted", () => refusals.Count == 0),
            new AssertQuery("Move unit is done: the card asks to found a city", () => card.EventName == "Tutorial Task: [Found city]"),
            new Act("found the city", () =>
            {
                var settler = human.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
                Do(new ExecuteUnitAction { UnitId = settler.Id, Descriptor = UnitActionDescriptor.Describe(settler).First(it => it.Descriptor.ActionType == UnitActionType.FoundCity).Descriptor });
                screen.Refresh();
            }),
            new WaitFrames(4),
            new AssertQuery("the founded city completed Found city: Enter city screen follows", () => refusals.Count == 0 && human.Cities.Count == 1 && card.EventName == "Tutorial Task: [Enter city screen]"),
            new Act("open the city screen", () => screen.OpenCityScreen(localHost.Snapshot.Cities.First(it => it.Own != null))),
            new WaitFrames(6),
            new AssertQuery("entering the city completed that task and Found city unlocked Pick technology", () => game.ClientState.TutorialTasksCompleted(human.CivId).Contains("Enter city screen") && card.EventName == "Tutorial Task: [Pick technology]"),
            new Act("close the city screen", () => screen.City.Close()),
            new WaitFrames(3),
            new Capture("pick-technology", "After founding the city and opening it: the card now asks to pick a technology"),
            new Act("switch Show tutorials off", () => service.Update(s => s with { ShowTutorials = false })),
            new Act("refresh the HUD", screen.Refresh),
            new WaitFrames(3),
            new AssertQuery("with tutorials off the card is gone", () => !card.Visible),
        ];
    }
}
