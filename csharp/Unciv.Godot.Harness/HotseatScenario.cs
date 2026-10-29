using Godot;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.Empire;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Ui;
using Unciv.Harness;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C029: hotseat with two humans (Babylon, then Greece, then Rome plays). Babylon ends the turn: the map and every panel are hidden in the same frame and an opaque pass-the-device cover names Greece
/// (nothing of Babylon's game stays visible, and Greece's turn has not begun). Pressing Ready hands the game over: Babylon's session is retired (it refuses every command), the screen shows the game as
/// Greece (its own units only) and Greece's turn has begun. Greece ends the turn and the device goes back to Babylon the same way.
/// </summary>
public sealed class HotseatScenario : Scenario
{
    public override string Name => "hotseat";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "While the cover is up, is there any way to see the previous player's map, units or numbers?",
        "Is it clear who the device goes to next, and that nothing happens until they press Ready?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        long now = 1000;
        var babylonHost = new LocalHost(session, ScenarioGame.HumanA, () => now += 1000);
        var screen = new GameScreen();
        host.AddChild(screen);
        var cover = new HandOverView();
        host.AddChild(cover);
        var registry = AssetRegistry.ForRuleset(game.Ruleset);
        cover.Init(new UiTheme(registry));
        var hotseat = new HotseatController(screen, cover, _ => registry, id => id);
        host.AddChild(hotseat);
        hotseat.Present(babylonHost);
        var refusals = new List<string>();

        string Viewer() => screen.Session?.ViewerCivId ?? "";
        bool Started(string civ) => game.IsTurnStarted(game.GetCivilization(civ));
        void Answer(string civ)
        {
            var city = game.GetCivilization(civ).Cities[0];
            foreach (var command in new GameCommand[] { new QueueConstruction { CityId = city.Id, Name = "Warrior" }, new SetResearchQueue { Techs = ["Pottery"] } })
            {
                var result = screen.Session.NotNull().SubmitAsync(command).GetAwaiter().GetResult();
                if (!result.Accepted) refusals.Add($"{command.GetType().Name}: {result.Message}");
            }
        }
        bool EverythingHidden() =>
            screen.IsConcealed && !screen.Visible && !screen.Map.IsVisibleInTree() && !screen.Overlays.IsVisibleInTree() && !screen.Hud.IsVisibleInTree() && !screen.Minimap.IsVisibleInTree()
            && !screen.Notifications.IsVisibleInTree() && !screen.Panel.IsVisibleInTree() && screen.Units.Values.All(it => !it.IsVisibleInTree());
        bool OnlyOwnUnitsOf(string civ) => screen.Units.Count > 0 && screen.Units.Values.Where(it => it.IsOwn).All(it => it.Unit.Owner == civ) && screen.Units.Values.Any(it => it.IsOwn);

        return
        [
            new WaitFrames(4),
            new AssertQuery("Babylon is the viewer and its turn runs", () => Viewer() == ScenarioGame.HumanA && screen.Session.NotNull().Snapshot.You == ScenarioGame.HumanA && !cover.Visible && screen.Visible && OnlyOwnUnitsOf(ScenarioGame.HumanA)),
            new Act("Babylon answers the decisions that block End turn", () => Answer(ScenarioGame.HumanA)),
            new Capture("babylon", "Babylon's turn: its own map and units, no cover"),
            new Act("Babylon presses Next turn", () => screen.TryHotkey("Space")),
            new WaitUntil("the cover is up", () => cover.Visible),
            new WaitFrames(2),
            new AssertQuery("the cover names Greece", () => cover.CivId == ScenarioGame.HumanB && cover.HeadingText.Contains("Greece", StringComparison.Ordinal) && !cover.HeadingText.Contains("Babylon", StringComparison.Ordinal)),
            new AssertQuery("the previous player's view is entirely hidden and takes no input", () => EverythingHidden() && screen.ProcessMode == Node.ProcessModeEnum.Disabled && hotseat.IsCovering),
            new AssertQuery("Greece's turn has not begun while the device changes hands", () => !Started(ScenarioGame.HumanB) && !babylonHost.IsRetired && refusals.Count == 0),
            new AssertQuery("the game cannot be saved while the device changes hands (a save does not record whose turn is next)", () =>
                !babylonHost.CanExportSave && babylonHost.ExportSaveAsync().GetAwaiter().GetResult() == null),
            new AssertQuery("the popup is the shared modal frame: centred, accent hairline, titled, buttons inside with the primary right-most", () => ModalChecks.Framed(cover)),
            new Capture("cover", "Pass the device to Greece: an opaque cover, no map, no panels, a Ready button"),
            new PressKey(Key.Escape),
            new AssertQuery("Escape is swallowed by the cover: it stays up and the game menu does not open behind it", () => cover.Visible && cover.CivId == ScenarioGame.HumanB && !screen.GameMenu.Visible),
            new Act("Greece presses Ready", () => cover.ReadyButton.EmitSignal(BaseButton.SignalName.Pressed)),
            new WaitUntil("the game is handed to Greece", () => !cover.Visible && !screen.IsConcealed && Viewer() == ScenarioGame.HumanB),
            new WaitFrames(3),
            new AssertQuery("Babylon's session is retired and refuses commands and queries", () =>
                babylonHost.IsRetired
                && !babylonHost.SubmitAsync(new SetResearchQueue { Techs = ["Pottery"] }).GetAwaiter().GetResult().Accepted
                && !babylonHost.EndTurnAsync().GetAwaiter().GetResult().Accepted),
            new AssertQuery("the screen shows the game as Greece: its turn began, only its own units are its own", () =>
                screen.Visible && screen.Session.NotNull().Snapshot.You == ScenarioGame.HumanB && Started(ScenarioGame.HumanB) && OnlyOwnUnitsOf(ScenarioGame.HumanB) && screen.Map.IsVisibleInTree() && screen.ProcessMode != Node.ProcessModeEnum.Disabled),
            new AssertQuery("saved again, the game resumes as Greece with its turn running", () =>
            {
                var text = screen.Session.NotNull().ExportSaveAsync().GetAwaiter().GetResult();
                if (text == null) return false;
                var resumed = LocalHost.FromSave(text, () => now += 1000);
                return resumed.ViewerCivId == ScenarioGame.HumanB && !resumed.TurnNeedsBegin;
            }),
            new Capture("greece", "Greece's turn: the game as Greece (its own units), Babylon's view is gone"),
            new Act("Greece answers its decisions and presses Next turn", () =>
            {
                Answer(ScenarioGame.HumanB);
                screen.TryHotkey("Space");
            }),
            new WaitUntil("the cover is up again", () => cover.Visible),
            new AssertQuery("the device goes back to Babylon, hidden again", () => cover.CivId == ScenarioGame.HumanA && EverythingHidden() && !Started(ScenarioGame.HumanA) && refusals.Count == 0),
            new Act("Babylon presses Ready", () => cover.ReadyButton.EmitSignal(BaseButton.SignalName.Pressed)),
            new WaitUntil("Babylon plays again", () => !cover.Visible && !screen.IsConcealed && Viewer() == ScenarioGame.HumanA),
            new WaitFrames(2),
            new AssertQuery("it is Babylon's turn again, with a fresh session", () => Started(ScenarioGame.HumanA) && screen.Session.NotNull().Snapshot.You == ScenarioGame.HumanA && !ReferenceEquals(screen.Session, babylonHost) && OnlyOwnUnitsOf(ScenarioGame.HumanA)),
        ];
    }
}
