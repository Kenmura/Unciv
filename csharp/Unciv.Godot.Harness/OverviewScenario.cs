using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Queries.Empire;
using Unciv.Queries.Victory;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C023: the empire overview through the real game screen and the real engine, in the rich two-human game (Babylon with its capital and a puppet city; Greece, the other human, and Rome are not met). The numbers on the
/// stats page are the engine's overview answer; the cities and units tables list exactly the viewer's own cities and units; the resource and ranking tables equal their queries;
/// espionage is off in this game and the page says so. The jump buttons close the overview and open the city screen (puppet city) / select the unit.
/// </summary>
public sealed class OverviewScenario : Scenario
{
    public override string Name => "overview";

    public override bool Uses3D => true;

    public override bool Rich => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Are the tabs easy to find and read: stats, cities, units, resources, ranking, religion, espionage?",
        "Do the tables line up and is it clear which row is the capital or a puppet city?",
        "Does 'Go to' take me where I expect (camera and selection)?",
        "Is a tab the game does not use (espionage) clearly explained rather than empty?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var recording = new CityAScenario.RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => 1000L));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));
        int logged = 0;

        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        Vector2 Tab(string tab) => Centre(screen.Overview.TabButton(tab));
        T Ask<T>(Unciv.Queries.IQuery<T> query) where T : class => recording.QueryAsync(query).GetAwaiter().GetResult().NotNull();
        // The engine's own list, independent of the screen and of the snapshot
        List<string> EngineCities() => human.Cities.OrderByDescending(it => it.IsCapital()).ThenBy(it => it.Name, StringComparer.Ordinal).Select(it => it.Name).ToList();
        List<string> EngineUnits() => human.Units.GetCivUnits().OrderBy(it => it.Name, StringComparer.Ordinal).ThenBy(it => it.Id).Select(it => it.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        string puppet = human.Cities.Single(it => it.IsPuppet).Name;
        int unitId = human.Units.GetCivUnits().OrderBy(it => it.Id).First().Id;

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new Click(() => Centre(screen.OverviewButton)),
            new WaitFrames(3),
            new AssertQuery("the overview opens on the stats tab with the engine's numbers", () =>
            {
                var overview = Ask(new EmpireOverviewQuery());
                var entries = OverviewModel.StatsCards(overview).SelectMany(it => it.Entries).ToList();
                var cards = screen.Overview.FindChild("StatsCards", true, false) as GridContainer;
                return screen.IsOverviewVisible && screen.Overview.SelectedTab == OverviewModel.Stats && overview.Gold == human.Gold && overview.Cities == human.Cities.Count
                    && entries.Any(it => it.Label == "Gold" && it.Value == human.Gold.ToString(System.Globalization.CultureInfo.InvariantCulture)) && screen.Overview.Notice(OverviewModel.Stats) == null
                    && screen.Overview.Tables(OverviewModel.Stats).Count == 1 && screen.Overview.Frame.TitleText == "Overview" && screen.Overview.Frame.CloseButton.Visible
                    && cards is { Columns: 2 } && cards.GetChildCount() == 4 && cards.GetChildren().OfType<Control>().All(it => it.Size.X > 0 && it.GetGlobalRect().End.X <= screen.GetViewportRect().Size.X);
            }),
            new Capture("stats", "Stats: the empire's per-turn numbers, score breakdown and known civilizations"),

            new Click(() => Tab(OverviewModel.Cities)),
            new WaitFrames(2),
            new AssertQuery("the cities table lists exactly the viewer's cities, capital first, with the puppet marked", () =>
                screen.Overview.SelectedTab == OverviewModel.Cities && screen.Overview.Tables(OverviewModel.Cities)[0].Rows.Select(it => it.Key).SequenceEqual(EngineCities())
                && screen.Overview.Tables(OverviewModel.Cities)[0].Rows.Count == 2
                && screen.Overview.Tables(OverviewModel.Cities)[0].Rows.Single(it => it.Key == puppet).Cells[5] == "Puppet"),
            new AssertQuery("the table is typed: numbers are right-aligned, the table fits the screen and each row has its own Go to button", () =>
            {
                var data = screen.Overview.DataTables(OverviewModel.Cities)[0];
                return data.Cell(0, 1).HorizontalAlignment == HorizontalAlignment.Right && data.Cell(0, 0).HorizontalAlignment == HorizontalAlignment.Left
                    && data.GetGlobalRect().Position.X >= 0 && data.GetGlobalRect().End.X <= screen.GetViewportRect().Size.X
                    && EngineCities().All(it => screen.Overview.JumpButton(OverviewModel.Cities, it) is { } jump && jump.IsVisibleInTree());
            }),
            new Capture("cities", "Cities: a sortable table of the capital and a puppet city with population, health, current build and notes, and a Go to button at the end of each row"),
            new Click(() => Centre(screen.Overview.DataTables(OverviewModel.Cities)[0].HeaderButton(0))),
            new WaitFrames(2),
            new AssertQuery("clicking the City header sorts ascending by name (a triangle shows) and a second click reverses it; the Go to buttons stay with their rows", () =>
            {
                var data = screen.Overview.DataTables(OverviewModel.Cities)[0];
                var names = EngineCities().Order(StringComparer.Ordinal).ToList();
                bool ascending = data.SortColumn == 0 && data.SortAscending && Enumerable.Range(0, data.RowCount).Select(i => data.CellText(i, 0)).SequenceEqual(names)
                    && data.HeaderButton(0).Text.EndsWith('▲');
                data.SortBy(0, false);
                bool descending = Enumerable.Range(0, data.RowCount).Select(i => data.CellText(i, 0)).SequenceEqual(names.AsEnumerable().Reverse())
                    && data.HeaderButton(0).Text.EndsWith('▼');
                return ascending && descending && screen.Overview.JumpButton(OverviewModel.Cities, puppet)?.GetParent().GetIndex() == data.Cell(names.Count - 1 - names.IndexOf(puppet), 0).GetParent().GetIndex() + 6;
            }),

            new Click(() => Tab(OverviewModel.Units)),
            new WaitFrames(2),
            new AssertQuery("the units table lists exactly the viewer's units and none of the other civs'", () =>
                screen.Overview.Tables(OverviewModel.Units)[0].Rows.Select(it => it.Key).SequenceEqual(EngineUnits())
                && screen.Overview.Tables(OverviewModel.Units)[0].Count == human.Units.GetCivUnitsSize()),
            new Capture("units", "Units: own units with position, health, moves and order"),

            new Click(() => Tab(OverviewModel.Resources)),
            new WaitFrames(2),
            new AssertQuery("the resources table equals the engine's luxury and strategic list", () =>
                screen.Overview.Tables(OverviewModel.Resources)[0].Rows.Select(it => (it.Cells[0], it.Cells[2]))
                    .SequenceEqual(Ask(new EmpireTopBarQuery()).Resources.Select(it => (it.Name, it.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture))))),

            new Click(() => Tab(OverviewModel.Ranking)),
            new WaitFrames(2),
            new AssertQuery("the ranking tables are the engine's columns, in its order, unmet civs unnamed", () =>
            {
                var columns = recording.QueryAsync(new RankingsQuery()).GetAwaiter().GetResult();
                if (columns == null) return screen.Overview.Notice(OverviewModel.Ranking) != null;
                var shown = screen.Overview.Tables(OverviewModel.Ranking);
                return shown.Count == columns.Count && shown.Count > 0 && columns.Select((column, index) =>
                    shown[index].Rows.Select(it => it.Key).SequenceEqual(column.Entries.Select(it => it.Civ.CivName ?? ""))).All(it => it)
                    && shown.All(it => it.Rows.All(row => row.Cells[1] != ScenarioGame.Ai || human.Knows(game.GetCivilization(ScenarioGame.Ai))));
            }),
            new Capture("ranking", "Ranking: one table per category, best first"),

            new Click(() => Tab(OverviewModel.Religion)),
            new WaitFrames(2),
            new AssertQuery("the religion page is the engine's religion answer", () =>
                OverviewModel.ReligionLines(Ask(new ReligionQuery())).Count > 0 && screen.Overview.Notice(OverviewModel.Religion) == null),
            new Click(() => Tab(OverviewModel.Espionage)),
            new WaitFrames(2),
            new AssertQuery("espionage is off in this game: the engine refuses the query and the page says so", () =>
                recording.QueryAsync(new SpyDestinationsQuery()).GetAwaiter().GetResult() == null && screen.Overview.Notice(OverviewModel.Espionage) == "Espionage is not enabled in this game."),

            new Act("remember the command log", () => logged = session.Log.Count),
            new Click(() => Tab(OverviewModel.Cities)),
            new WaitFrames(2),
            new Click(() => Centre(screen.Overview.JumpButton(OverviewModel.Cities, puppet))),
            new WaitFrames(3),
            new AssertQuery("'Go to' on the puppet city closes the overview and opens that city's screen; nothing was sent", () =>
                !screen.IsOverviewVisible && screen.IsCityPanelVisible && screen.OpenCity?.Name == puppet && session.Log.Count == logged),
            new Act("close the city and open the overview on the units tab", () =>
            {
                screen.OpenCityScreen(null);
                screen.OpenOverviewScreen(OverviewModel.Units);
            }),
            new WaitFrames(3),
            new Click(() => Centre(screen.Overview.JumpButton(OverviewModel.Units, unitId.ToString(System.Globalization.CultureInfo.InvariantCulture)))),
            new WaitFrames(3),
            new AssertQuery("'Go to' on a unit closes the overview and selects that unit", () =>
                !screen.IsOverviewVisible && screen.SelectedUnitId == unitId && session.Log.Count == logged),
            new Act("open the overview again", () => screen.OpenOverviewScreen(OverviewModel.Stats)),
            new WaitFrames(3),
            new Press("Close overview"),
            new AssertQuery("the close button closes the screen", () => !screen.IsOverviewVisible),
        ];
    }
}
