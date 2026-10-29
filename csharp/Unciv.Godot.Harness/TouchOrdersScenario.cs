using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Models;
using Unciv.Utils;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;

namespace Unciv.GodotHarness;

/// <summary>
/// C100: the interactions a desktop does with a mouse button, a hover or a key, done by synthetic touch on the 2400x1080 400 ppi phone profile of C096. A tap on a tile shows its tip above the finger
/// and the next press hides it; a long press on a button shows its tip and does not press it; a tap on a unit panel chip shows its tip; the Group select toggle stands in for Shift+click; a long
/// press with a unit selected is the right click that moves it; a finger dragged over the minimap moves the camera and not the map; a long press on a city tile locks and unlocks it.
/// Real finger events and Android's emulation order are not covered (see the ticket notes).
/// </summary>
public sealed class TouchOrdersScenario : Scenario
{
    private const float Dpi = 400f;

    public override string Name => "touch-orders";

    public override bool Uses3D => true;

    public override bool Skirmish => true;

    public override Vector2I Viewport => new(2400, 1080);

    public override float TouchDpi => Dpi;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the tile tip above the finger and readable, and does it leave when you touch elsewhere?",
        "Can you tell the Group select toggle is on, and is it big enough for a thumb?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        var queries = new GameQueries(localHost);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        screen.TutorialsEnabled = false;
        var map = screen.Map;
        var viewport = host.GetViewport();
        var tips = screen.Tooltips;
        MapUnit Find(string name) => human.Units.GetCivUnits().OrderBy(it => it.Id).First(it => it.BaseUnit.Name == name);
        var warrior = Find("Warrior");
        var settler = Find("Settler");
        HexCoord Where(MapUnit unit) => unit.CurrentTile.Position;
        Vector2 Pointer(HexCoord tile) => map.ScreenPositionOf(tile);
        Vector2 Centre(Control control) => control.GetGlobalRect().GetCenter();
        bool Free(HexCoord tile) => !game.Civilizations.SelectMany(it => it.Units.GetCivUnits()).Any(it => it.CurrentTile.Position == tile);
        bool Reachable(MapUnit unit, HexCoord tile)
        {
            try
            {
                return queries.Path(unit.Id, tile.x, tile.y).GetAwaiter().GetResult() is { Reachable: true, CanEnter: true };
            }
            catch (InvalidOperationException) // not a tile of the map
            {
                return false;
            }
        }
        // on the map, clear of the top bar, the unit panel (bottom left), the minimap (top right) and the action buttons
        bool Clear(HexCoord tile)
        {
            var at = Pointer(tile);
            return at.X > 450 && at.X < 1700 && at.Y > 230 && at.Y < 800 && !screen.Panel.GetGlobalRect().Grow(30f).HasPoint(at);
        }
        int logBefore = 0;
        HexCoord target = default;
        HexCoord startAt = default;
        HexCoord tapTile = default;
        HexCoord otherTile = default;
        bool toggledBefore = false;
        int cityLogBefore = 0;
        Vector3 focusBefore = default;
        Vector2 minimapAt = default;
        Unciv.Logic.City.City City() => human.Cities[0];
        string FoundCityLabel() => UnitOrdersModel.Clean(queries.Actions(settler.Id).GetAwaiter().GetResult().NotNull().Actions.First(it => it.Descriptor.ActionType == UnitActionType.FoundCity).Title);
        CityTileCell worked = new(0, 0, "", false, 0f, 0f, "", "");
        int workedBefore = 0;
        int clicks = 0;
        map.TileClicked += _ => clicks++;

        return
        [
            new WaitFrames(6),
            new Act("select the warrior", () =>
            {
                screen.Hud.HideBanner();
                screen.SelectUnitById(warrior.Id);
            }),
            new WaitFrames(3),
            new AssertQuery("with an own unit selected on a touch build the panel offers Group select, at least 48 dp tall", () =>
                screen.Panel.GroupToggle.Visible && screen.Panel.GroupToggle.GetGlobalRect().Size.Y + 0.5f >= UiTokens.DpToPx(UiTokens.TouchTargetDp, Dpi)),
            new Act("tap a tile that has nobody on it", () =>
            {
                var around = HexMath.GetHexCoordsAtDistance(Where(warrior), 2, 2, false).OrderBy(it => it.x).ThenBy(it => it.y).Where(it => Free(it) && Clear(it) && map.TileNodeAt(it) != null).ToList();
                tapTile = around[0];
                otherTile = around[1];
                InputSimulator.SimulateTap(viewport, Pointer(tapTile));
            }),
            new WaitUntil("the tile's tooltip is up above the finger", () => tips.IsShowing && tips.ShownText.Length > 0 && screen.HoveredTile == tapTile, 600),
            new WaitFrames(4),
            new AssertQuery("the tip does not cover the finger and is the engine's tile text", () =>
                !tips.PanelRect.HasPoint(Pointer(tapTile)) && screen.HoverTip.Length > 0 && tips.ShownText == screen.HoverTip),
            new Capture("tile-tip", "A tap on a tile shows its tooltip above the finger (a touch build has no hover); it goes at the next touch or after four seconds"),
            new Act("put a finger down on the map", () => InputSimulator.SimulateTouchDown(viewport, 0, Pointer(otherTile))),
            new AssertQuery("the next press hid the tip", () => !tips.IsShowing),
            new Act("lift", () => InputSimulator.SimulateTouchUp(viewport, 0, Pointer(otherTile))),
            new WaitUntil("the tap showed a tip again", () => tips.IsShowing, 600),
            new Act("let it time out", () => tips.Advance(TooltipHostSeconds())),
            new AssertQuery("the tip went by itself", () => !tips.IsShowing),
            new Act("long press the Group select toggle", () =>
            {
                toggledBefore = screen.Panel.GroupToggle.ButtonPressed;
                InputSimulator.SimulateTouchDown(viewport, 0, Centre(screen.Panel.GroupToggle));
            }),
            new WaitUntil("the held button showed its tooltip", () => tips.IsShowing && tips.TouchOwner == screen.Panel.GroupToggle, 600),
            new Act("lift", () => InputSimulator.SimulateTouchUp(viewport, 0, Centre(screen.Panel.GroupToggle))),
            new WaitFrames(3),
            new AssertQuery("a long press shows the tip without pressing the button, and the button works again", () =>
                screen.Panel.GroupToggle.ButtonPressed == toggledBefore && !screen.Panel.GroupToggle.Disabled && tips.ShownText.Contains("Shift+click", StringComparison.Ordinal)),
            new Act("tap a unit panel chip", () => InputSimulator.SimulateTap(viewport, Centre(screen.Panel.ChipsRow.GetChild<Control>(0)))),
            new WaitUntil("the chip's tip shows", () => tips.IsShowing && tips.TouchOwner == screen.Panel.ChipsRow.GetChild<Control>(0), 600),
            new Act("tap the same chip again", () => InputSimulator.SimulateTap(viewport, Centre(screen.Panel.ChipsRow.GetChild<Control>(0)))),
            new AssertQuery("a second tap on the same part toggles its tip off", () => !tips.IsShowing),
            new Act("tap Group select, then the settler's tile", () =>
            {
                InputSimulator.SimulateTap(viewport, Centre(screen.Panel.GroupToggle));
            }),
            new WaitFrames(3),
            new AssertQuery("the toggle is on and the settler's tile is on screen clear of the HUD", () => screen.Panel.GroupMode && Clear(Where(settler))),
            new Act("tap the settler's tile", () => InputSimulator.SimulateTap(viewport, Pointer(Where(settler)))),
            new WaitFrames(3),
            new AssertQuery("the tap added the settler to the group (what Shift+click does) and sent nothing", () =>
                screen.GroupUnitIds.SequenceEqual([warrior.Id, settler.Id]) && screen.SelectedUnitId == warrior.Id && session.Log.Count == 0),
            new Act("tap Group select off, select the warrior alone", () =>
            {
                InputSimulator.SimulateTap(viewport, Centre(screen.Panel.GroupToggle));
                screen.SelectUnitById(warrior.Id);
            }),
            new WaitFrames(3),
            new AssertQuery("the group is gone and the toggle is off", () => !screen.Panel.GroupMode && screen.GroupUnitIds.Count == 0),
            new Act("pick a free reachable tile and long press it", () =>
            {
                startAt = Where(warrior);
                logBefore = session.Log.Count;
                target = Enumerable.Range(2, 2).SelectMany(distance => HexMath.GetHexCoordsAtDistance(startAt, distance, distance, false).OrderBy(it => it.x).ThenBy(it => it.y))
                    .First(it => Free(it) && Clear(it) && Reachable(warrior, it));
                clicks = 0;
                InputSimulator.SimulateTouchDown(viewport, 0, Pointer(target));
            }),
            new WaitUntil("the long press ordered the unit to move (the right click it stands in for)", () => session.Log.Count == logBefore + 1, 900),
            new Act("lift", () => InputSimulator.SimulateTouchUp(viewport, 0, Pointer(target))),
            new WaitFrames(3),
            new AssertQuery("one move command, the unit left its tile, and the release was not also a tap", () =>
                session.Log.Count == logBefore + 1 && session.Log[^1].Type.Contains("move", StringComparison.OrdinalIgnoreCase) && Where(warrior) != startAt && clicks == 0),
            new Act("drag a finger across the minimap", () =>
            {
                focusBefore = map.Focus;
                var minimap = screen.Minimap;
                minimapAt = minimap.GlobalPosition + (minimap.ViewportRect() is { } view ? view.GetCenter() : minimap.Size / 2f);
                var to = minimapAt + new Vector2(10f, 0f);
                InputSimulator.SimulateTouchDown(viewport, 0, minimapAt);
                InputSimulator.SimulateTouchMove(viewport, 0, minimapAt, to);
                InputSimulator.SimulateTouchUp(viewport, 0, to);
            }),
            new WaitFrames(3),
            new AssertQuery("the minimap took the drag: the camera followed it and no tile was tapped", () => map.Focus.DistanceTo(focusBefore) > 0.01f && clicks == 0),
            new Act("found a city with the settler through its menu", () =>
            {
                screen.SelectUnitById(settler.Id);
                (screen.Panel.ButtonFor(FoundCityLabel()) ?? throw new InvalidOperationException("no Found city entry")).EmitSignal(BaseButton.SignalName.Pressed);
            }),
            new WaitUntil("the city exists", () => human.Cities.Count == 1, 300),
            new Act("open the city screen", () =>
            {
                screen.Refresh();
                screen.OpenCityScreen(localHost.Snapshot.Cities.First(it => it.Own?.Id == City().Id));
            }),
            new WaitFrames(3),
            new Act("long press a worked tile", () =>
            {
                worked = screen.City.Cells.First(it => it.State == "Worked");
                workedBefore = City().WorkedTiles.Count;
                cityLogBefore = session.Log.Count;
                InputSimulator.SimulateTouchDown(viewport, 0, Centre(screen.City.TileButton(worked.X, worked.Y)!));
            }),
            new WaitUntil("the long press locked the tile", () => City().LockedTiles.Contains(new HexCoord(worked.X, worked.Y)), 900),
            new Act("lift", () => InputSimulator.SimulateTouchUp(viewport, 0, Centre(screen.City.TileButton(worked.X, worked.Y)!))),
            new WaitFrames(4),
            new AssertQuery("it locked the tile and the release did not also free the citizen", () =>
                City().WorkedTiles.Count == workedBefore && City().LockedTiles.Count == 1 && session.Log.Count == cityLogBefore + 1 && screen.City.Cells.Any(it => it.X == worked.X && it.Y == worked.Y && it.Locked)),
            new Capture("tile-locked", "A long press on a worked tile of the city screen locked it (the padlock on the tile; its tip is up too): the touch stand-in for the right click, and the hint line says so"),
            new Act("long press it again", () => InputSimulator.SimulateTouchDown(viewport, 0, Centre(screen.City.TileButton(worked.X, worked.Y)!))),
            new WaitUntil("the second long press unlocked it", () => City().LockedTiles.Count == 0, 900),
            new Act("lift", () => InputSimulator.SimulateTouchUp(viewport, 0, Centre(screen.City.TileButton(worked.X, worked.Y)!))),
            new WaitFrames(4),
            new AssertQuery("it is unlocked and still worked", () => City().WorkedTiles.Count == workedBefore && !screen.City.Cells.Any(it => it.Locked)),
        ];

        static double TooltipHostSeconds() => Unciv.GodotClient.Ui.TooltipHost.TouchHideSeconds + 1.0;
    }
}
