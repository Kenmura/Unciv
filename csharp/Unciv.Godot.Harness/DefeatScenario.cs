using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// C024: the viewer's civ is destroyed (no units, no city) while Rome plays on. The defeat screen opens at once with the engine's default defeat paragraph and offers to
/// keep watching or to leave. Keep watching sends nothing, closes the screen and it does not come back when the world refreshes.
/// </summary>
public sealed class DefeatScenario : Scenario
{
    public override string Name => "defeat";

    public override bool Uses3D => true;

    public override bool Defeat => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does the screen say you were defeated without implying the game is over?",
        "Is it clear you can keep watching?",
        "Is the tone of the defeat paragraph right?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var recording = new CityAScenario.RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => 1000L));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));
        int logged = 0;

        return
        [
            new WaitFrames(4),
            new AssertQuery("the game is not over but Babylon is destroyed", () => game.VictoryData == null && game.GetCivilization(ScenarioGame.HumanA).IsDefeated()),
            new AssertQuery("the defeat screen is open with the default defeat paragraph and keep-watching and main-menu buttons", () =>
                screen.GameOver.Visible && screen.GameOver.TitleText == "Defeated" && screen.GameOver.BodyText == new Unciv.Models.Ruleset.Victory().DefeatString
                && screen.GameOver.ButtonIds.SequenceEqual(["MainMenu", "KeepWatching"])),
            new Capture("defeat", "The defeat screen: the game goes on without you; keep watching or go to the main menu"),
            new Act("remember the command log", () => logged = session.Log.Count),
            new Click(() => screen.GameOver.ButtonById("KeepWatching")?.GetGlobalRect().GetCenter() ?? Vector2.Zero),
            new WaitFrames(3),
            new AssertQuery("keep watching sent nothing and closed the screen", () => session.Log.Count == logged && !screen.GameOver.Visible),
            new Act("refresh the world", () => screen.Refresh()),
            new WaitFrames(4),
            new AssertQuery("the screen does not come back for a defeat the player already acknowledged", () => !screen.GameOver.Visible),
            new Capture("watching", "Keep watching: the world is visible again"),
        ];
    }
}
