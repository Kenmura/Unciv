using System.Globalization;
using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C010: the vertical slice end to end through the real screen. Select the warrior by clicking its tile, preview and confirm a move by clicking the destination twice,
/// press Next turn five times (AI civs play between), right-click a second move, and finish five turns on. Engine state is asserted before every capture; the run ends with the
/// runner's check that the headless replay of the command log reaches the same state hash. Plays the solo game (one human, one AI, no city founded).
/// </summary>
public sealed class V1VerticalScenario : Scenario
{
    private const int TurnsToPlay = 5;

    public override string Name => "v1-vertical";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does it feel playable: can you tell your units from the terrain, and is the selected unit obvious?",
        "Is the path of dots clear, and does a second click moving the unit feel natural?",
        "After Next turn, is it clear what happened (turn number, status line)? Are AI moves visible when an AI unit is in sight?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        long now = 1000;
        int startTurn = game.Turns; // the engine counts from 0: five Next turns end on startTurn + 5
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => now += 1000);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        var viewport = host.GetViewport();

        var warrior = human.Units.GetCivUnits().First(it => it.BaseUnit.Name == "Warrior");
        var startTile = warrior.CurrentTile.Position;
        var sharing = human.Units.GetCivUnits().Where(it => it.CurrentTile.Position == startTile).OrderBy(it => it.Id).ToList();
        int clicksToSelect = sharing.IndexOf(warrior) + 1;
        float movementBefore = warrior.CurrentMovement;
        long seqBefore = 0;
        var ais = game.Civilizations.Where(it => it.IsMajorCiv() && !it.IsHuman()).ToList();
        var aiBefore = ais.ToDictionary(it => it.CivId, Fingerprint);
        var queries = new GameQueries(localHost);
        var reachable = queries.Reachable(warrior.Id).GetAwaiter().GetResult().NotNull().Tiles
            .Where(it => it.CanEnter && !(it.X == startTile.x && it.Y == startTile.y))
            .OrderByDescending(it => it.Cost).ThenBy(it => it.X).ThenBy(it => it.Y).ToList();
        var destination = new HexCoord(reachable[0].X, reachable[0].Y);
        HexCoord? secondDestination = null;

        var steps = new List<ScenarioStep>
        {
            new WaitFrames(3),
            new AssertQuery("the game starts on its first turn with the human's units drawn", () => screen.Turn == startTurn && screen.Units.Values.Count(it => it.IsOwn) == sharing.Count + OtherOwnUnits(human, startTile)),
            new AssertQuery("the quick-start game has a warrior to play with and a reachable destination", () => reachable.Count > 0 && movementBefore > 0f),
            new Capture("start", "Turn 1: the human's units as coloured blocks on the map; nothing selected"),
        };
        for (int i = 0; i < clicksToSelect; i++) steps.Add(new Click(() => screen.Map.ScreenPositionOf(new HexCoord(startTile.x, startTile.y))));
        steps.AddRange(
        [
            new AssertQuery("clicking the tile selected the warrior", () => screen.SelectedUnitId == warrior.Id),
            new AssertQuery("the status line names the unit and its movement", () => screen.StatusText.StartsWith("Warrior", StringComparison.Ordinal) && screen.StatusText.Contains("moves", StringComparison.Ordinal)),
            new AssertQuery("the tile is highlighted", () => screen.Map.IsHighlightVisible && screen.Map.Selected == new HexCoord(startTile.x, startTile.y)),
            new Capture("selected", "Warrior selected: highlighted tile, bigger block, movement in the status line"),

            new Act("remember the command count", () => seqBefore = session.LastSeq),
            new Click(() => screen.Map.ScreenPositionOf(destination)),
            new AssertQuery("the first click on the destination only previews: path dots drawn, unit not moved, no command sent", () =>
                screen.PreviewTarget == destination && screen.PathDotCount > 0 && warrior.CurrentTile.Position == startTile && session.LastSeq == seqBefore),
            new Capture("path", "First click on the destination: a path of dots, nothing has moved yet"),
            new Click(() => screen.Map.ScreenPositionOf(destination)),
            new AssertQuery("the second click moved the unit to the destination", () => warrior.CurrentTile.Position == destination),
            new AssertQuery("the move spent movement", () => warrior.CurrentMovement < movementBefore),
            new AssertQuery("exactly one command was submitted, and the unit is still selected with the dots gone", () =>
                session.LastSeq == seqBefore + 1 && screen.SelectedUnitId == warrior.Id && screen.PathDotCount == 0),
            new AssertQuery("the drawn block moved with it", () => screen.Units[warrior.Id].Unit.X == destination.x && screen.Units[warrior.Id].Unit.Y == destination.y),
            new Capture("moved", "After the second click: the warrior stands on the destination, movement spent"),
        ]);

        for (int turn = 2; turn <= 1 + TurnsToPlay; turn++)
        {
            int expected = startTurn + turn - 1;
            steps.Add(new Press("Next turn"));
            steps.Add(new WaitFrames(1));
            steps.Add(new AssertQuery($"turn {turn}: the turn counter advanced and the status line reports it", () =>
                game.Turns == expected && screen.Turn == expected && screen.StatusText.StartsWith($"Turn {expected}", StringComparison.Ordinal)));
            steps.Add(new AssertQuery($"turn {turn}: the warrior has full movement again", () => warrior.CurrentMovement >= movementBefore));
            if (turn == 2)
            {
                steps.Add(new Act("pick a second destination", () =>
                {
                    var options = queries.Reachable(warrior.Id).GetAwaiter().GetResult().NotNull().Tiles.Where(it => it.CanEnter && !(it.X == warrior.CurrentTile.Position.x && it.Y == warrior.CurrentTile.Position.y)
                            && !screen.Panel.GetGlobalRect().Grow(8f).HasPoint(screen.Map.ScreenPositionOf(new HexCoord(it.X, it.Y)))) // C012: the unit panel covers part of the map
                        .OrderBy(it => it.X).ThenBy(it => it.Y).ToList();
                    secondDestination = new HexCoord(options[0].X, options[0].Y);
                    seqBefore = session.LastSeq;
                }));
                steps.Add(new AssertQuery("the unit stayed selected across the turn", () => screen.SelectedUnitId == warrior.Id));
                steps.Add(new Act("right-click the second destination", () =>
                    InputSimulator.SimulateMouseClick(viewport, screen.Map.ScreenPositionOf(secondDestination.NotNull()), MouseButton.Right)));
                steps.Add(new AssertQuery("a right click moved straight there with one command", () =>
                    warrior.CurrentTile.Position == secondDestination && session.LastSeq == seqBefore + 1));
            }
            steps.Add(new Capture($"turn{turn}", $"Turn {turn} after Next turn" + (turn == 2 ? " and a right-click move" : "")));
        }

        steps.AddRange(
        [
            new AssertQuery("five Next turns brought the game five turns on", () => game.Turns == startTurn + TurnsToPlay),
            new AssertQuery("every AI civ acted: its units and cities differ from turn 1", () => ais.Count > 0 && ais.All(it => Fingerprint(it) != aiBefore[it.CivId])),
            new AssertQuery("the server commands of the AI civs are in the log", () => session.Log.Count(it => it.Type == "run-ai-turn") >= TurnsToPlay * ais.Count && session.Log.Count(it => it.Type == "begin-turn") == TurnsToPlay),
            new Click(() => screen.Map.ScreenPositionOf(new HexCoord(warrior.CurrentTile.Position.x, warrior.CurrentTile.Position.y))),
            new AssertQuery("the unit is still selectable after five turns", () => screen.SelectedUnitId == warrior.Id),
        ]);
        return steps;
    }

    private static int OtherOwnUnits(Unciv.Logic.Civilization.Civilization human, HexCoord startTile) =>
        human.Units.GetCivUnits().Count(it => it.CurrentTile.Position != startTile);

    private static string Fingerprint(Unciv.Logic.Civilization.Civilization civ) =>
        string.Join(";", civ.Units.GetCivUnits().OrderBy(it => it.Id).Select(it => $"{it.Id}:{it.Name}@{it.CurrentTile.Position.x},{it.CurrentTile.Position.y}"))
        + "|" + string.Join(";", civ.Cities.Select(it => it.Name + "@" + it.Location.x.ToString(CultureInfo.InvariantCulture)));
}
