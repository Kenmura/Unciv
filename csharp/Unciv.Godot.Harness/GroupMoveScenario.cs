using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;

namespace Unciv.GodotHarness;

/// <summary>
/// C091: group move and escort on the real game screen, in the skirmish game (warrior, settler, worker; an enemy warrior at war next to them). Shift-click puts the settler in
/// the warrior's group; the menu then offers Escort pair. The worker is then grouped with the pair and one click on a far tile (twice: preview, then go) commands the worker on its own
/// and the pair as a pair: three ordinary move commands, the pair ending on one tile. At the start of the next turn the pair takes its next step by itself (two more commands).
/// Every command in the log is a plain <c>move-unit</c>; the runner's replay check proves the engine needed nothing else.
/// </summary>
public sealed class GroupMoveScenario : Scenario
{
    public override string Name => "group-move";

    public override bool Uses3D => true;

    public override bool Skirmish => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can you tell which units are in the group (enlarged) and what the status line says about it?",
        "Does the Escort pair entry read as an option of the group and not as an action of one unit?",
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
        MapUnit Find(string name) => human.Units.GetCivUnits().OrderBy(it => it.Id).First(it => it.BaseUnit.Name == name);
        var warrior = Find("Warrior");
        var settler = Find("Settler");
        var worker = Find("Worker");
        HexCoord Where(MapUnit unit) => unit.CurrentTile.Position;
        int distanceBefore = 0;
        int startTurn = 0;
        HexCoord groupTarget = default;
        int logBefore = 0;
        HexCoord startAt = default;

        bool Free(HexCoord tile) => !game.Civilizations.SelectMany(it => it.Units.GetCivUnits()).Any(it => it.CurrentTile.Position == tile);
        bool CanPath(MapUnit unit, HexCoord tile, int minTurns = 1)
        {
            try
            {
                return queries.Path(unit.Id, tile.x, tile.y).GetAwaiter().GetResult() is { Reachable: true, CanEnter: true } path && path.Turns >= minTurns;
            }
            catch (InvalidOperationException) // not a tile of the map
            {
                return false;
            }
        }
        void ChooseMenu(string label) =>
            (screen.Panel.ButtonFor(label) ?? throw new InvalidOperationException($"no menu entry {label}")).EmitSignal(BaseButton.SignalName.Pressed);
        Vector2 Pointer(HexCoord tile) => screen.Map.ScreenPositionOf(tile);

        // a tile whose centre is on the map and not under the top bar, the unit panel, the toast or the side panels
        bool Clickable(HexCoord tile)
        {
            var at = Pointer(tile);
            return at.X > 20 && at.Y > 70 && at.X < 400 && at.Y < 330 && !screen.Panel.GetGlobalRect().Grow(20f).HasPoint(at);
        }

        return
        [
            new WaitFrames(3),
            new Act("select the warrior", () => screen.SelectUnitById(warrior.Id)),
            new WaitFrames(2),
            new ShiftClick(() => Pointer(Where(settler))),
            new WaitFrames(3),
            new AssertQuery("shift-click added the settler: both are in the group, the panel still shows the warrior, and the menu offers Escort pair", () =>
                screen.GroupUnitIds.SequenceEqual([warrior.Id, settler.Id]) && screen.SelectedUnitId == warrior.Id
                && screen.Panel.Entries.Any(it => it.Kind == MenuEntryKind.Group && it.Label == GroupMoveModel.EscortLabel)),
            new Capture("group-selected", "Warrior and settler in one group (shift-click): both enlarged, the panel offers Escort pair"),
            new Act("choose Escort pair", () => ChooseMenu(GroupMoveModel.EscortLabel)),
            new WaitFrames(3),
            new AssertQuery("the pair is recorded and the menu now offers Stop escort", () =>
                screen.Escorts.Any(it => it.MilitaryId == warrior.Id && it.CivilianId == settler.Id)
                && screen.Panel.Entries.Any(it => it.Kind == MenuEntryKind.Group && it.Label == GroupMoveModel.StopEscortLabel)),
            new Act("select the worker and shift-click the pair (settler, then warrior) into its group", () => screen.SelectUnitById(worker.Id)),
            new WaitFrames(2),
            new ShiftClick(() => Pointer(Where(warrior))),
            new WaitFrames(2),
            new ShiftClick(() => Pointer(Where(warrior))),
            new WaitFrames(3),
            new AssertQuery("the group is worker, settler and warrior; the escort pair stays a pair, and a three-unit group offers no new pair", () =>
                screen.GroupUnitIds.Count == 3 && screen.GroupUnitIds[0] == worker.Id && screen.Escorts.Count == 1 && !screen.Panel.Entries.Any(it => it.Kind == MenuEntryKind.Group)),
            new Act("pick the farthest explored tile that takes all three more than one turn", () =>
            {
                groupTarget = Enumerable.Range(3, 3).Reverse().SelectMany(distance => HexMath.GetHexCoordsAtDistance(Where(warrior), distance, distance, false).OrderBy(it => it.x).ThenBy(it => it.y))
                    .First(it => Free(it) && Clickable(it) && CanPath(warrior, it, 2) && CanPath(settler, it, 2) && CanPath(worker, it, 2));
                startAt = Where(warrior);
                logBefore = session.Log.Count;
            }),
            new Click(() => Pointer(groupTarget)),
            new AssertQuery("the first click previews the worker's path and sends nothing", () => screen.PreviewTarget == groupTarget && session.Log.Count == logBefore),
            new Click(() => Pointer(groupTarget)),
            new WaitFrames(3),
            new AssertQuery("the worker got its own move command and the pair two: only move-unit commands, and the pair is on one tile again", () =>
                session.Log.Count == logBefore + 3 && session.Log.Skip(logBefore).All(it => it.Type == "move-unit") && Where(warrior) == Where(settler)
                && Where(warrior) != startAt),
            new Capture("group-moved", "Worker grouped with the escort pair, one click on a far tile: the worker moved on its own, the pair together"),
            new Act("remember the distance and end the turn", () =>
            {
                distanceBefore = HexMath.GetDistance(Where(warrior), groupTarget);
                logBefore = session.Log.Count;
                startTurn = game.Turns;
                screen.TryHotkey("Space");
            }),
            new WaitUntil("the turn is processed", () => !screen.IsEndingTurn && !localHost.IsProcessingTurn && game.Turns > startTurn),
            new WaitUntil("the pair took its next step by itself and is still on one tile", () =>
                Where(warrior) == Where(settler) && HexMath.GetDistance(Where(warrior), groupTarget) < distanceBefore
                && session.Log.Skip(logBefore).Count(it => it.Type == "move-unit") == 2, 600),
            new WaitFrames(60), // the units finish walking and the turn banner has gone
            new Capture("escort-continued", "Next turn: the escort pair continued toward the target without a click"),
        ];
    }
}
