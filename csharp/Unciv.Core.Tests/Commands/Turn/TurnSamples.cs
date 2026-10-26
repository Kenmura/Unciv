using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Logic.Civilization;

namespace Unciv.Core.Tests.Commands.Turn;

// T187: a valid sample of each turn command plus the state it needs, for GameCommandTests (a fresh started game each; Babylon and Greece are humans, Rome an AI).
internal static class TurnSamples
{
    public static readonly string[] Types =
    [
        "end-turn", "force-end-turn", "run-ai-turn", "begin-turn", "move-automated-units", "set-player-settings", "ack-alert",
        "respond-to-alert", "dismiss-policy-picker", "dismiss-move-spies", "resign", "one-more-turn",
        "start-auto-play", "stop-auto-play", "auto-play-turn", "complete-tutorial-task", "choose-tutorial-task-choice",
    ];

    // A new game asks for a production and a research target; end-turn needs both settled.
    private static void Settle(Civilization babylon)
    {
        babylon.Cities[0].CityConstructions.AddToQueue("Warrior");
        babylon.Tech.TechsToResearch.Add("Pottery");
    }

    private static AutoPlayState StartedAutoPlay()
    {
        var state = new AutoPlayState();
        state.StartMultiturnAutoPlay("Babylon", 3, true, false);
        return state;
    }

    public static (GameCommand Command, string Actor) Sample(string type, GameInfo game)
    {
        var babylon = game.GetCivilization("Babylon");
        switch (type)
        {
            case "end-turn":
                Settle(babylon);
                return (new EndTurn { Now = 5000 }, "Babylon");
            case "force-end-turn":
                return (new ForceEndTurn { CivId = "Babylon", Now = 5000 }, CommandContext.ServerActor);
            case "run-ai-turn":
                game.EndPlayerTurn(5000); // Greece is the second human, Rome the AI after it
                game.BeginPlayerTurn(5500);
                game.EndPlayerTurn(5600);
                return (new RunAiTurn { CivId = "Rome" }, CommandContext.ServerActor);
            case "begin-turn":
                game.EndPlayerTurn(5000);
                while (game.TurnPhase is TurnPhase.AutoPlayers) game.RunAutoPlayers();
                var next = ((TurnPhase.AwaitingInput)game.TurnPhase).Civ; // the next human of the started game
                return (new BeginTurn { CivId = next.CivId, Now = 6000 }, CommandContext.ServerActor);
            case "move-automated-units":
                babylon.Units.GetCivUnits().First(it => it.CurrentMovement > 0).Action = "Explore";
                return (new MoveAutomatedUnits(), "Babylon");
            case "set-player-settings":
                return (new SetPlayerSettings { AutoBuildingRoads = false }, "Babylon");
            case "ack-alert":
                babylon.PopupAlerts.Add(new PopupAlert(AlertType.WonderBuilt, "Pyramids"));
                return (new AckAlert { Index = babylon.PopupAlerts.Count - 1, AlertType = AlertType.WonderBuilt, Value = "Pyramids" }, "Babylon");
            case "respond-to-alert":
                var city = babylon.Cities[0];
                babylon.PopupAlerts.Add(new PopupAlert(AlertType.CityConquered, city.Id));
                return (new RespondToAlert { Index = babylon.PopupAlerts.Count - 1, AlertType = AlertType.CityConquered, Value = city.Id, Choice = "puppet" }, "Babylon");
            case "dismiss-policy-picker":
                babylon.Policies.ShouldOpenPolicyPicker = true;
                return (new DismissPolicyPicker(), "Babylon");
            case "dismiss-move-spies":
                game.GameParameters.EspionageEnabled = true;
                babylon.EspionageManager.AddSpy();
                return (new DismissMoveSpies(), "Babylon");
            case "resign":
                return (new Resign { Now = 5000 }, "Babylon");
            case "start-auto-play":
                return (new StartAutoPlay { Turns = 3 }, "Babylon");
            case "stop-auto-play":
                game.ClientState.AutoPlay = StartedAutoPlay();
                return (new StopAutoPlay(), "Babylon");
            case "complete-tutorial-task":
                return (new CompleteTutorialTask { Task = "Conquer a city" }, "Babylon");
            case "choose-tutorial-task-choice":
                // the ruleset may be shared with other tests: a uniquely named task whose choice is only available to this sample
                var task = new Unciv.Models.Ruleset.Event { Name = "Sample tutorial task", Presentation = Unciv.Models.Ruleset.Event.PresentationType.Floating };
                task.Uniques.Add("Only available <if tutorials are enabled>");
                var choice = new Unciv.Models.Ruleset.EventChoice { Name = "ok", Text = "OK" };
                choice.Uniques.Add("Mark tutorial [Sample tutorial task] complete");
                task.Choices.Add(choice);
                game.Ruleset.Events[task.Name] = task;
                return (new ChooseTutorialTaskChoice { Event = task.Name, Choice = 0 }, "Babylon");
            case "auto-play-turn":
                game.ClientState.AutoPlay = StartedAutoPlay();
                return (new AutoPlayTurn { Now = 5000 }, "Babylon");
            case "one-more-turn":
                game.VictoryData = new VictoryData { WinningCiv = "Greece", VictoryType = "Scientific", VictoryTurn = 1 };
                return (new OneMoreTurn(), "Babylon");
            default:
                throw new InvalidOperationException($"No sample for turn command \"{type}\"");
        }
    }
}
