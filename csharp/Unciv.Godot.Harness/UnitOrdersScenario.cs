using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.Units;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.Models.Translations;
using Unciv.Queries.Units;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C012: the unit panel and orders on the real game screen, in the skirmish game (the solo game plus an adjacent enemy warrior at war with the human and a human
/// worker). Asserts, for every unit it selects, that the action menu lists exactly what the unit-actions query reports (same order, same enabled flag); that the
/// overlays equal the reachable and attackable-target queries; Fortify (state), the combat preview (equals the battle-preview query, sends nothing), Attack (both
/// healths equal what the engine does when the same attack is made headlessly on a replay of the log), Found city (a city exists on the tile), an improvement
/// order for the worker, and that Disband asks first and Cancel keeps the unit.
/// </summary>
public sealed class UnitOrdersScenario : Scenario
{
    public override string Name => "unit-orders";

    public override bool Uses3D => true;

    public override bool Skirmish => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the action menu discoverable: can you tell what the selected unit can do, and see why an entry is greyed out?",
        "Is the combat preview legible: both fighters, strengths and modifiers, the damage ranges and what happens to the defender?",
        "Are the blue reachable tiles and the red attack tiles distinguishable from the terrain and from each other?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session) => new Plan(host, session).Steps();

    private sealed class Plan
    {
        private readonly GameSession session;
        private readonly LocalHost localHost;
        private readonly GameQueries queries;
        private readonly GameScreen screen;
        private readonly Unciv.Logic.Civilization.Civilization human;
        private readonly Unciv.Logic.Civilization.Civilization ai;
        private readonly Unciv.Logic.Map.MapUnits.MapUnit warrior;
        private readonly Unciv.Logic.Map.MapUnits.MapUnit settler;
        private readonly Unciv.Logic.Map.MapUnits.MapUnit worker;
        private readonly Unciv.Logic.Map.MapUnits.MapUnit enemy;
        private readonly HexCoord enemyTile;
        private readonly HexCoord warriorTile;
        private readonly HexCoord settlerTile;
        private readonly HexCoord workerTile;
        private readonly int enemyHealthBefore;
        private readonly int warriorHealthBefore;
        private long seqBefore;
        private int expectedEnemyHealth = -1;
        private int expectedWarriorHealth = -1;
        private HexCoord? pathTarget;
        private string improvement = "";

        public Plan(Control host, GameSession session)
        {
            this.session = session;
            var game = session.Game;
            human = game.GetCivilization(ScenarioGame.HumanA);
            ai = game.GetCivilization(ScenarioGame.Ai);
            long now = 1000;
            localHost = new LocalHost(session, ScenarioGame.HumanA, () => now += 1000);
            queries = new GameQueries(localHost);
            screen = new GameScreen();
            host.AddChild(screen);
            screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
            warrior = human.Units.GetCivUnits().First(it => it.BaseUnit.Name == "Warrior");
            settler = human.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
            worker = human.Units.GetCivUnits().First(it => it.BaseUnit.Name == "Worker");
            enemy = ai.Units.GetCivUnits().First(it => it.BaseUnit.Name == "Warrior" && it.CurrentTile.AerialDistanceTo(warrior.CurrentTile) == 1);
            enemyTile = enemy.CurrentTile.Position;
            warriorTile = warrior.CurrentTile.Position;
            settlerTile = settler.CurrentTile.Position;
            workerTile = worker.CurrentTile.Position;
            enemyHealthBefore = enemy.Health;
            warriorHealthBefore = warrior.Health;
        }

        public List<ScenarioStep> Steps()
        {
            var steps = SelectWarriorSteps();
            steps.AddRange(FortifyAndAttackSteps());
            steps.AddRange(SettlerSteps());
            steps.AddRange(WorkerSteps());
            return steps;
        }

            private List<UnitActionInfo> QueryActions(int unitId) => [.. queries.Actions(unitId).GetAwaiter().GetResult().NotNull().Actions];

            // The menu is the query: the engine entries are exactly the query's actions, in its order, with its enabled flag.
            private bool MenuEqualsQuery(int unitId)
            {
                var expected = QueryActions(unitId);
                var shown = screen.Panel.Entries.Where(it => it.Kind == MenuEntryKind.Engine).ToList();
                return screen.SelectedUnitId == unitId && shown.Count == expected.Count
                    && shown.Zip(expected).All(pair => pair.First.Action.NotNull().Descriptor == pair.Second.Descriptor && pair.First.Enabled == pair.Second.IsEnabled
                        && pair.First.Label == UnitOrdersModel.Clean(pair.Second.Title));
            }

            private bool OverlaysEqualQueries(int unitId)
            {
                var reach = UnitOrdersModel.ReachableTiles(queries.Reachable(unitId).GetAwaiter().GetResult().NotNull()).Select(it => new HexCoord(it.X, it.Y)).ToHashSet();
                var attack = UnitOrdersModel.AttackTiles(queries.AttackTargets(unitId).GetAwaiter().GetResult().NotNull()).Select(it => new HexCoord(it.X, it.Y)).ToHashSet();
                return reach.SetEquals(screen.ReachableOverlay.Tiles) && attack.SetEquals(screen.AttackOverlay.Tiles);
            }

            // A click on a tile selects the next own unit there; the number of clicks that makes `target` the selection.
            private int ClicksTo(MapUnitRef target)
            {
                var here = human.Units.GetCivUnits().Where(it => it.CurrentTile.Position == target.Tile).OrderBy(it => it.Id).Select(it => it.Id).ToList();
                int at = screen.SelectedUnitId is { } current ? here.IndexOf(current) : -1;
                int goal = here.IndexOf(target.Id);
                return at < 0 ? goal + 1 : (goal - at + here.Count) % here.Count;
            }

            private void ChooseMenu(string label) =>
                (screen.Panel.ButtonFor(label) ?? throw new InvalidOperationException($"no menu entry {label}; shown: {string.Join(" | ", screen.Panel.Entries.Select(it => it.Label))}")).EmitSignal(BaseButton.SignalName.Pressed);

            private ScenarioStep[] Select(MapUnitRef target)
            {
                int clicks = Math.Max(1, ClicksTo(target));
                return [.. Enumerable.Range(0, clicks).Select(_ => (ScenarioStep)new Click(() => screen.Map.ScreenPositionOf(target.Tile)))];
            }

            private string FortifyLabel() => UnitOrdersModel.Clean(QueryActions(warrior.Id).First(it => it.Descriptor.ActionType == UnitActionType.Fortify).Title);
            private string FoundCityLabel() => UnitOrdersModel.Clean(QueryActions(settler.Id).First(it => it.Descriptor.ActionType == UnitActionType.FoundCity).Title);

        private List<ScenarioStep> SelectWarriorSteps()
        {
            var steps = new List<ScenarioStep>
            {
                new WaitFrames(3),
                new AssertQuery("the skirmish is set up: an enemy warrior next to ours, at war, and a worker", () =>
                    warrior.CurrentTile.AerialDistanceTo(enemy.CurrentTile) == 1 && human.IsAtWarWith(ai) && worker.Health > 0),
                new Capture("start", "Skirmish start: nothing selected, no panel"),
            };
            steps.AddRange(Select(new MapUnitRef(warrior.Id, warriorTile)));
            steps.AddRange(
            [
                new AssertQuery("the warrior is selected and its panel names it with its combat strength and moves", () =>
                    screen.SelectedUnitId == warrior.Id && screen.Panel.Visible && screen.Panel.TitleText == "Warrior"
                    && screen.Panel.StatsText.Contains("Combat 6", StringComparison.Ordinal) && screen.Panel.StatsText.Contains("Moves 2/2", StringComparison.Ordinal)),
                new AssertQuery("the warrior's menu equals the unit-actions query, with Disband last", () => MenuEqualsQuery(warrior.Id) && screen.Panel.Entries[^1].Kind == MenuEntryKind.Disband),
                new AssertQuery("the reachable overlay equals the reachable query and the attack overlay is the enemy's tile", () =>
                    OverlaysEqualQueries(warrior.Id) && screen.AttackOverlay.Tiles.SequenceEqual([enemyTile]) && screen.ReachableOverlay.Tiles.Count > 0),
                new Capture("warrior-selected", "Warrior selected: unit panel with its action menu, blue reachable tiles, red attack tile"),
                new Act("pick the farthest reachable tile", () =>
                {
                    var far = queries.Reachable(warrior.Id).GetAwaiter().GetResult().NotNull().Tiles
                        .Where(it => it.CanEnter && !(it.X == warriorTile.x && it.Y == warriorTile.y) && !(it.X == workerTile.x && it.Y == workerTile.y)).OrderByDescending(it => it.Cost).ThenBy(it => it.X).ThenBy(it => it.Y).First();
                    pathTarget = new HexCoord(far.X, far.Y);
                    seqBefore = session.LastSeq;
                }),
                new Click(() => screen.Map.ScreenPositionOf(pathTarget.NotNull())),
                new AssertQuery("clicking a reachable tile previews the path without sending a command", () => screen.PreviewTarget == pathTarget && screen.PathDotCount > 0 && session.LastSeq == seqBefore),
                new Capture("path-preview", "Path preview to a reachable tile: dots along the path, nothing moved"),
            ]);
            return steps;
        }

        private List<ScenarioStep> FortifyAndAttackSteps()
        {
            var steps = new List<ScenarioStep>();
            steps.AddRange(
            [
                // Fortify
                new Act("choose Fortify from the menu", () => ChooseMenu(FortifyLabel())),
                new WaitFrames(1),
                new AssertQuery("the warrior is fortified in the engine and the panel shows the order", () =>
                    warrior.IsFortified() && screen.Panel.StatsText.Contains("Order: Fortify", StringComparison.Ordinal)),
                new AssertQuery("the menu still equals the query after the order", () => MenuEqualsQuery(warrior.Id)),
                new Capture("fortified", "Warrior fortified: the panel shows the order"),

                // Attack: combat preview, then confirm
                new Act("replay the log on a fresh game and make the same attack headlessly", () =>
                {
                    var fresh = ScenarioGame.Create(skirmish: true);
                    foreach (var envelope in session.Log) fresh.Replay(envelope);
                    var result = fresh.Submit(ScenarioGame.HumanA, new AttackWithUnit { UnitId = warrior.Id, Target = enemyTile });
                    var freshEnemy = fresh.Game.GetCivilization(ScenarioGame.Ai).Units.GetCivUnits().FirstOrDefault(it => it.Id == enemy.Id);
                    var freshWarrior = fresh.Game.GetCivilization(ScenarioGame.HumanA).Units.GetCivUnits().FirstOrDefault(it => it.Id == warrior.Id);
                    expectedEnemyHealth = result.Accepted ? freshEnemy?.Health ?? 0 : -1;
                    expectedWarriorHealth = freshWarrior?.Health ?? 0;
                    seqBefore = session.LastSeq;
                }),
                new Click(() => screen.Map.ScreenPositionOf(enemyTile)),
                new AssertQuery("clicking the enemy shows the combat preview that equals the battle-preview query, and sends nothing", () =>
                {
                    var query = localHost.QueryAsync(new AttackPreviewQuery(enemyTile.x, enemyTile.y, warrior.Id)).GetAwaiter().GetResult().NotNull();
                    var text = UnitOrdersModel.Combat(query);
                    return screen.CombatPreview.Visible && screen.CombatPreview.TitleText == text.Title && screen.CombatPreview.BodyText == string.Join("\n", text.Lines)
                        && screen.CombatPreview.ConfirmEnabled && screen.CombatPreview.ConfirmText == "Attack" && session.LastSeq == seqBefore
                        && enemy.Health == enemyHealthBefore && query.DamageToDefenderMax > 0;
                }),
                new Capture("attack-preview", "Combat preview: fighters, strengths, damage ranges; Attack and Cancel"),
                new Press("Attack"),
                new WaitFrames(1),
                new AssertQuery("one attack command was logged and the preview closed", () => session.LastSeq == seqBefore + 1 && !screen.CombatPreview.Visible),
                new AssertQuery("the enemy lost health, and both healths equal the engine's own result for that attack", () =>
                    enemy.Health < enemyHealthBefore && enemy.Health == expectedEnemyHealth && warrior.Health == expectedWarriorHealth && warrior.Health <= warriorHealthBefore),
                new Capture("after-attack", "After the attack: the enemy is damaged"),
            ]);
            return steps;
        }

        private Unciv.Logic.Map.Tiles.Tile SiteTile() => settler.CurrentTile.Neighbors.First(it => it.IsLand);

        private List<ScenarioStep> SettlerSteps()
        {
            var steps = new List<ScenarioStep>();
            steps.AddRange(Select(new MapUnitRef(settler.Id, settlerTile)));
            steps.AddRange(
            [
                new AssertQuery("the settler is selected and its menu equals the query and offers Found city", () =>
                    screen.SelectedUnitId == settler.Id && MenuEqualsQuery(settler.Id)
                    && screen.Panel.Entries.Any(it => it.Action?.Descriptor.ActionType == UnitActionType.FoundCity && it.Enabled)),
                new Capture("settler-selected", "Settler selected: Found city in the menu"),
                new AssertQuery("C088: no site is shown before the pointer moves", () => screen.SiteOverlay.Tiles.Count == 0),
                new Act("C088: pointer onto a land tile next to the settler", () => InputSimulator.SimulateMouseMove(screen.GetViewport(), screen.Map.ScreenPositionOf(SiteTile().Position))),
                new WaitFrames(6),
                new AssertQuery("C088: the work radius is marked and the tooltip words the engine's city-site answer", () =>
                {
                    var site = queries.CitySite(SiteTile().Position.X, SiteTile().Position.Y).GetAwaiter().GetResult().NotNull();
                    return screen.HoveredTile == SiteTile().Position && site.Tiles.Count > 0 && screen.SiteOverlay.Tiles.Select(it => (it.X, it.Y)).ToHashSet().SetEquals(site.Tiles.Select(it => (it.X, it.Y)))
                        && screen.HoverTip == TooltipModel.ToBbcode(TooltipModel.CitySite(site), text => text.Tr()) && site.Yields.Count > 0;
                }),
                new Act("C088: let the tooltip delay elapse", () => screen.Tooltips.Advance(ScenarioRunner.UnreachableDelay)),
                new WaitFrames(2),
                new Capture("city-site", "Settler selected, pointer on a nearby tile: the tooltip lists the site's yields and resources, the gold hexes mark its work radius"),
                new Act("C088: pointer back onto the settler", () => InputSimulator.SimulateMouseMove(screen.GetViewport(), screen.Map.ScreenPositionOf(settlerTile))),
                new WaitFrames(4),
                new AssertQuery("C088: the radius goes away when the pointer moves on", () => screen.SiteOverlay.Tiles.Count > 0 == (screen.HoveredTile == settlerTile && screen.HoverTip.Contains("City site", StringComparison.Ordinal))),
                new Act("choose Found city", () => ChooseMenu(FoundCityLabel())),
                new WaitFrames(1),
                new AssertQuery("a city now stands on the settler's tile and the settler is gone", () =>
                    human.Cities.Any(it => it.Location == settlerTile) && human.Units.GetCivUnits().All(it => it.Id != settler.Id)),
                new AssertQuery("the selection and panel are cleared", () => screen.SelectedUnitId == null && !screen.Panel.Visible && screen.ReachableOverlay.Tiles.Count == 0),
                new Capture("city-founded", "Found city done: the city on the map, panel closed"),
            ]);
            return steps;
        }

        private List<ScenarioStep> WorkerSteps()
        {
            var steps = new List<ScenarioStep>();
            steps.AddRange(Select(new MapUnitRef(worker.Id, workerTile)));
            steps.AddRange(
            [
                new AssertQuery("the worker is selected, its menu equals the query and lists the improvements the tile query allows", () =>
                {
                    var options = queries.TileInfo(workerTile.x, workerTile.y, worker.Id).GetAwaiter().GetResult().NotNull().ImprovementOptions.NotNull();
                    var improve = screen.Panel.Entries.Where(it => it.Kind == MenuEntryKind.Improve).ToList();
                    return screen.SelectedUnitId == worker.Id && MenuEqualsQuery(worker.Id) && improve.Count == options.Count && improve.Count > 0
                        && improve.Zip(options).All(pair => pair.First.Argument == pair.Second.Name && pair.First.Enabled == (pair.Second.Problems.Count == 0));
                }),
                new AssertQuery("C068: every worker action is reachable and the grid has at most two rows", () =>
                    screen.Panel.Entries.All(it => screen.Panel.ButtonFor(it.Label) != null) && screen.Panel.GridRows <= 2 && screen.Panel.GridRect.End.Y <= screen.GetViewportRect().Size.Y),
                new Capture("worker-selected", "Worker selected: improvements in the menu"),
                new Act("choose the first improvement the worker can build", () =>
                {
                    var entry = screen.Panel.Entries.First(it => it.Kind == MenuEntryKind.Improve && it.Enabled);
                    improvement = entry.Argument.NotNull();
                    ChooseMenu(entry.Label);
                }),
                new WaitFrames(1),
                new AssertQuery("the engine has the worker building it", () => worker.CurrentTile.ImprovementInProgress == improvement),
                new Act("choose Disband", () => ChooseMenu(UnitOrdersModel.DisbandLabel)),
                new AssertQuery("Disband asks first", () => screen.Panel.IsAsking && human.Units.GetCivUnits().Any(it => it.Id == worker.Id)),
                new Capture("disband-confirm", "Disband asks for confirmation"),
                new Press("Cancel"),
                new AssertQuery("Cancel keeps the worker and shows the menu again", () => !screen.Panel.IsAsking && human.Units.GetCivUnits().Any(it => it.Id == worker.Id) && MenuEqualsQuery(worker.Id)),
                new Act("C068: a long menu (shown directly, not from the engine) keeps two rows and puts the rest behind More", () =>
                {
                    var menu = Enumerable.Range(1, 16).Select(i => new MenuEntry(MenuEntryKind.Improve, UnitOrdersModel.ImproveLabelPrefix + "Test " + i, i != 3, null, "Test " + i, i != 3 ? null : "Needs a road", "Improve:Test " + i,
                        AssetKey.Of(RulesetAssetKeys.ImprovementIcon, "Farm"))).ToList();
                    screen.Panel.Show(UnitOrdersModel.Panel(screen.Units[worker.Id].Unit, null), menu);
                }),
                new Press("More"),
                new AssertQuery("the popover lists the overflow and every entry keeps a button", () =>
                    screen.Panel.PopoverOpen && screen.Panel.GridRows == 2 && screen.Panel.MoreButton != null && screen.Panel.Entries.Count == 16 && screen.Panel.GridEntries.Count == 9
                    && screen.Panel.PopoverRect.End.Y <= screen.Panel.MoreButton.GetGlobalRect().Position.Y + 1f),
                new Capture("more-popover", "A long action list: two rows of icons and the rest in the More popover"),
            ]);
            return steps;
        }
    }

    private readonly record struct MapUnitRef(int Id, HexCoord Tile);
}
