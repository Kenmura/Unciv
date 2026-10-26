using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// C046: the developer console through the real game screen and engine, in a God-mode game. The backtick opens it; Tab completes (an ambiguous word lists the choices, a unique
/// one is finished); Enter on a good line sends one run-console-command, so the engine's gold changes and the command appears in the log; Up brings the line back from the
/// history; a bad line and a usage line only show their response (red and gold) and send nothing; a tile selected on the map is the tile the next line works on; Escape closes
/// the console (not the menu) and the history survives reopening.
/// </summary>
public sealed class ConsoleScenario : Scenario
{
    public override string Name => "console";

    public override bool Uses3D => true;

    public override bool GodMode => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the console easy to read over the map, and is the response colour (green ran, gold usage, red refused) clear?",
        "Does Tab complete the way a command line would?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var screen = new GameScreen();
        host.AddChild(screen);
        var local = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        screen.Start(local, AssetRegistry.ForRuleset(game.Ruleset));
        screen.TutorialsEnabled = false;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var city = ScenarioGame.FirstCity(game, ScenarioGame.HumanA);
        int goldBefore = 0;
        int logged = 0;

        void Type(string text)
        {
            screen.ConsolePopup.Input.Text = text;
            screen.ConsolePopup.Input.CaretColumn = text.Length;
        }

        bool Last(string line) => session.Log.Count == logged + 1 && session.Log[^1].Type == "run-console-command" && session.Log[^1].Payload.ToString().Contains(line, StringComparison.Ordinal);

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner and remember the gold and the command log", () =>
            {
                screen.Hud.HideBanner();
                goldBefore = human.Gold;
                logged = session.Log.Count;
            }),
            new PressKey(Key.Quoteleft),
            new WaitFrames(2),
            new AssertQuery("the backtick opened the console with an empty input and the keyboard focus", () =>
                screen.ConsolePopup.Visible && screen.ConsolePopup.Input.Text == "" && screen.ConsolePopup.Input.HasFocus() && screen.ConsolePopup.History.Count == 0),
            new Capture("open", "The console opened under the top bar, over the map"),

            new Act("type an ambiguous command start", () => Type("ci")),
            new PressKey(Key.Tab),
            new WaitFrames(1),
            new AssertQuery("Tab listed the choices and kept the text", () =>
                screen.ConsolePopup.Input.Text == "ci" && screen.ConsolePopup.ResponseText == "Matching completions: city, civ" && screen.ConsolePopup.ResponseTone == ConsoleTone.Hint),
            new Capture("choices", "Tab on an ambiguous word lists the matching completions"),
            new Act("type a unique start", () => Type("civ adds")),
            new PressKey(Key.Tab),
            new AssertQuery("Tab finished the word", () => screen.ConsolePopup.Input.Text == "civ addstat "),

            new Press("Keep open"),
            new Act("type a good line", () => Type("civ addstat gold 777")),
            new PressKey(Key.Enter),
            new WaitFrames(2),
            new AssertQuery("the engine's civ got the gold, one logged console command, the input is empty and the console stayed open", () =>
                human.Gold == goldBefore + 777 && Last("civ addstat gold 777") && screen.ConsolePopup.Visible && screen.ConsolePopup.Input.Text == ""
                && screen.ConsolePopup.ResponseTone == ConsoleTone.Ok && screen.ConsolePopup.ResponseText == "Done"),
            new Capture("ran", "After Enter: the response says Done, and the top bar shows the new gold"),

            new PressKey(Key.Up),
            new AssertQuery("Up brought the line back from the history", () => screen.ConsolePopup.Input.Text == "civ addstat gold 777" && screen.ConsolePopup.History.Count == 1),
            new Capture("history", "Up recalls the last line"),

            new Act("remember the command log", () => logged = session.Log.Count),
            new Act("type a line with a bad number", () => Type("civ addstat gold abc")),
            new PressKey(Key.Enter),
            new WaitFrames(2),
            new AssertQuery("the engine's reason is shown in red and nothing was sent ", () =>
                session.Log.Count == logged && screen.ConsolePopup.ResponseTone == ConsoleTone.Error && screen.ConsolePopup.ResponseText.Contains("not a valid number", StringComparison.Ordinal)
                && screen.ConsolePopup.Input.Text == "civ addstat gold abc"),
            new Capture("error", "A refused line shows the reason in red and stays in the input"),
            new Act("type a usage question", () => Type("civ addstat")),
            new PressKey(Key.Enter),
            new WaitFrames(2),
            new AssertQuery("the usage is shown in gold and nothing was sent", () =>
                session.Log.Count == logged && screen.ConsolePopup.ResponseTone == ConsoleTone.Hint && screen.ConsolePopup.ResponseText == "Format: civ addstat <stat> <amount> [civ]"),

            new Act("select the capital's tile on the map and ask about it", () =>
            {
                screen.Map.Select(city.GetCenterTile().Position);
                Type("tile checkfilter Land");
            }),
            new PressKey(Key.Enter),
            new WaitFrames(2),
            new AssertQuery("the selected tile was the one asked about", () =>
                session.Log.Count == logged && screen.ConsolePopup.ResponseTone == ConsoleTone.Hint && screen.ConsolePopup.ResponseText == "true"),

            new PressKey(Key.Escape),
            new WaitFrames(1),
            new AssertQuery("Escape closed the console, not the game menu", () => !screen.ConsolePopup.Visible && !screen.GameMenu.Visible),
            new PressKey(Key.Quoteleft),
            new WaitFrames(1),
            new AssertQuery("reopened: the history is still there and the input is empty", () =>
                screen.ConsolePopup.Visible && screen.ConsolePopup.History.SequenceEqual(new[] { "civ addstat gold 777" }) && screen.ConsolePopup.Input.Text == "" && screen.ConsolePopup.KeepOpen),
            new Press("Keep open"),
            new Act("type a second good line", () =>
            {
                logged = session.Log.Count;
                goldBefore = human.Gold;
                Type("civ addstat gold 1");
            }),
            new PressKey(Key.Enter),
            new WaitFrames(2),
            new AssertQuery("without Keep open the console closed after the command ran", () =>
                !screen.ConsolePopup.Visible && human.Gold == goldBefore + 1 && Last("civ addstat gold 1") && screen.ConsolePopup.History.Count == 2),
            new Capture("closed", "Keep open off: the console closed after the command"),
        ];
    }
}
