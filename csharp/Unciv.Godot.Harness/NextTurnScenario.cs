using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Assets;
using Unciv.Net.Visibility;

namespace Unciv.GodotHarness;

/// <summary>
/// C040: End turn on a crowded map (the human and ten AI civs) is processed off the UI thread. The overlay is captured with fixed progress (so the golden does not depend on timing), then
/// Next turn is pressed for real: the scenario asserts the window kept drawing (no frame took long, and several frames passed while the civs played), the overlay is gone afterwards and
/// the turn advanced. The headless replay of the command log must reach the same state hash as the run.
/// </summary>
public sealed class NextTurnScenario : Scenario
{
    /// <summary>A frame this far apart from the previous one means the UI thread was blocked by the turn.</summary>
    private const double MaxFrameMs = 750;

    public override string Name => "next-turn";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override bool Crowd => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear the game is busy (not frozen), and which civ is being played?",
        "Does the dimmed overlay make it obvious nothing underneath can be clicked?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        long now = 1000;
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => now += 1000);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        screen.Hud.HoldBanner = true;
        int startTurn = game.Turns;

        return
        [
            new WaitFrames(4),
            new AssertQuery("the game has the human and the crowd of AI civs", () => game.Civilizations.Count(it => it.IsAI() && it.IsMajorCiv()) >= ScenarioGame.CrowdAiCount),
            new Act("show the overlay at a fixed point of the turn", () => screen.TurnOverlay.Show(NextTurnProgressModel.Of(new TurnProgress(2, ScenarioGame.CrowdAiCount, "Rome"), id => id))),
            new WaitFrames(2),
            new AssertQuery("the overlay names the civ and its position", () => screen.TurnOverlay.Visible && screen.TurnOverlay.DetailText == "Rome - 3 of 10"),
            new Capture("overlay", "Next turn in progress: a dimmed screen with a bar, 'Rome - 3 of 10'"),
            new Act("hide the overlay again", () => screen.TurnOverlay.Close()),
            new Act("press Next turn (not settled: the processing is what is tested)", () => screen.TryHotkey("Space")),
            new AssertQuery("pressing Next turn started processing and raised the overlay", () => screen.IsEndingTurn && screen.TurnOverlay.Visible && localHost.IsProcessingTurn),
            new WaitUntil("the turn is processed", () => !screen.IsEndingTurn && game.Turns > startTurn),
            new AssertQuery("the window kept drawing while the civs played (no long frame)", () => ScenarioRunner.LongestFrameMs < MaxFrameMs),
            new AssertQuery("the turn took several frames, so the work really ran beside the UI", () => ScenarioRunner.FramesWaited >= 3),
            new AssertQuery("the overlay is gone and the game moved on", () => !screen.TurnOverlay.Visible && !localHost.IsProcessingTurn && game.Turns == startTurn + 1 && screen.Turn == game.Turns),
            new AssertQuery("the status line reports the civs played", () => screen.StatusText.Contains("civ(s) played", StringComparison.Ordinal)),
            new WaitFrames(2),
            new Capture("after", "Back in control on the next turn"),
        ];
    }
}
