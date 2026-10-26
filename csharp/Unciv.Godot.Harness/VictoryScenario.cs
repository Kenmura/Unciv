using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// C024: a forced one-turn conquest through the real game screen and the real engine. Rome has no units left, so Babylon is the last major civ alive; nothing is shown
/// while the game runs. Ending the turn makes the engine record a Domination victory, and the victory screen opens with the engine's wording, the ranking (Babylon first,
/// Rome defeated and last) and the score history. "One more turn...!" sends the command, the game continues and ending the turn works again.
/// </summary>
public sealed class VictoryScenario : Scenario
{
    public override string Name => "victory";

    public override bool Uses3D => true;

    public override bool Victory => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does the screen say clearly that you won, and how?",
        "Is the ranking easy to read, with your own line recognisable?",
        "Can you tell the lines of the graph apart and which civ each belongs to?",
        "Is the tone of the victory paragraph right?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var recording = new CityAScenario.RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => 1000L));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));
        int turn = 0;
        int logged = 0;

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new AssertQuery("while the game runs nothing is shown and nobody has won", () => !screen.GameOver.Visible && game.VictoryData == null),
            new Act("remember the turn", () => turn = game.Turns),
            new Press("Next turn"),
            new WaitFrames(4),
            new AssertQuery("the engine recorded Babylon's Domination victory", () => game.VictoryData is { WinningCiv: "Babylon", VictoryType: "Domination" }),
            new AssertQuery("the victory screen is open with the engine's words and the player's buttons", () =>
                screen.GameOver.Visible && screen.GameOver.TitleText.Contains("Domination", StringComparison.Ordinal)
                && screen.GameOver.BodyText == game.Ruleset.Victories["Domination"].VictoryString
                && screen.GameOver.ButtonIds.SequenceEqual(["OneMoreTurn", "MainMenu"])),
            new AssertQuery("the ranking lists Babylon first and Rome, defeated, last", () =>
                screen.GameOver.RankingTexts.Count == 2 && screen.GameOver.RankingTexts[0].StartsWith("1. Babylon", StringComparison.Ordinal)
                && screen.GameOver.RankingTexts[1].StartsWith("Rome (defeated)", StringComparison.Ordinal)),
            new AssertQuery("the history graph draws a line for each civ", () => screen.GameOver.GraphSize.Lines == 2 && screen.GameOver.GraphSize.Points >= 2),
            new Capture("victory", "The victory screen after a one-turn conquest: title, the victory paragraph, the ranking and the score history"),
            new Act("remember the command log", () => logged = session.Log.Count),
            new Click(() => screen.GameOver.ButtonById("OneMoreTurn")?.GetGlobalRect().GetCenter() ?? Vector2.Zero),
            new WaitFrames(3),
            new AssertQuery("one-more-turn was sent, the engine continues the game and the screen is gone", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "one-more-turn" && game.OneMoreTurnMode && !screen.GameOver.Visible),
            new Press("Next turn"),
            new WaitFrames(4),
            new AssertQuery("ending the turn works again: the game moved on and the victory screen stayed away", () => game.Turns > turn && !screen.GameOver.Visible),
            new Capture("continued", "After One more turn the world is playable again"),
        ];
    }
}
