using Godot;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.Empire;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Logic.City;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using Unciv.Net.Visibility;
using Unciv.Queries;
using Unciv.Assets;

namespace Unciv.GodotHarness;

/// <summary>
/// C017: city screen A through the real game screen and the real engine. Babylon's capital opens in the city screen; a click on a worked tile frees its citizen, a click on an
/// available tile puts the citizen there (the city's stats change, asserted from <c>CityStats</c>), a right click locks and unlocks it, a Market (bought with God mode) gives a
/// Merchant slot the + and - buttons fill and empty, the focus is chosen, and "Reset citizens" lets the engine reassign. Finally a city of another civ is opened: it shows only its
/// snapshot entry and the screen never asks a city query for it (the recording session proves it).
/// </summary>
public sealed class CityAScenario : Scenario
{
    public override string Name => "city-a";

    public override bool Uses3D => true;

    public override bool GodMode => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear at a glance which tiles are worked, which are free to work, and which are locked?",
        "Do the yield labels on the tiles stay legible, and is the stat block readable (hover a stat for its breakdown)?",
        "Is the information density right for the city screen: header, stats, specialists, focus, tile grid?",
        "Does the foreign city view show only what a stranger could know?",
    ];

    private static readonly CityDto Foreign = new CityDto
    {
        Name = "Thebes", Owner = ScenarioGame.HumanB, X = 1, Y = 1, Population = 7, IsCapital = true, IsPuppet = false, IsBeingRazed = false, Health = 25, MaxHealth = 40,
        VisibleBuildings = ["Pyramids"],
    };

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        long now = 1000;
        var recording = new RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => now += 1000));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));
        var city = ScenarioGame.FirstCity(game, ScenarioGame.HumanA);
        var refusals = new List<string>();

        void Do(GameCommand command)
        {
            var result = recording.SubmitAsync(command).GetAwaiter().GetResult();
            if (!result.Accepted) refusals.Add($"{command.GetType().Name}: {result.Message}");
        }

        CityDto Dto() => recording.Snapshot.Cities.First(it => it.Own?.Id == city.Id);
        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        CityTileCell First(string state) => screen.City.Cells.First(it => it.State == state);
        Vector2 Tile(CityTileCell cell) => Centre(screen.City.TileButton(cell.X, cell.Y));
        Unciv.Queries.Cities.CityPanelResult Panel() => screen.City.Panel.NotNull();
        float Food() => city.CityStats.CurrentCityStats[Stat.Food];

        var worked = new CityTileCell(0, 0, "", false, 0f, 0f, "", "");
        var available = worked;
        int workedBefore = 0;
        float foodBefore = 0f;
        float goldBefore = 0f;
        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner, buy a Market with God mode, and open the capital's city screen", () =>
            {
                screen.Hud.HideBanner();
                Do(new DebugAddTechnology { Tech = "Bronze Working" });
                Do(new DebugAddTechnology { Tech = "Currency" });
                Do(new QueueConstruction { CityId = city.Id, Name = "Market" });
                Do(new PurchaseConstruction { CityId = city.Id, Name = "Market" });
                screen.Refresh();
                screen.OpenCityScreen(Dto());
            }),
            new WaitFrames(2),
            new AssertQuery("every setup command was accepted", () => refusals.Count == 0),
            new AssertQuery("the hook names the opened city and the screen shows the engine's numbers for it", () =>
                screen.OpenCity?.Name == city.Name && screen.IsCityPanelVisible && !screen.City.IsForeign && screen.City.Panel?.CityId == city.Id
                && screen.City.TitleText.StartsWith(city.Name, StringComparison.Ordinal) && screen.City.InfoText.Contains("Food", StringComparison.Ordinal)),
            new AssertQuery("the grid classifies the work range: one centre, the worked tiles equal the engine's, and free tiles to choose from", () =>
                screen.City.Cells.Count(it => it.State == "Center") == 1
                && screen.City.Cells.Count(it => it.State == "Worked") == city.WorkedTiles.Count
                && screen.City.Cells.Any(it => it.State == "Available")),
            new AssertQuery("the Market's Merchant slot is offered, empty", () => screen.City.SpecialistButton("Merchant", add: true) is { Disabled: true } && Panel().MaxSpecialists.Any(it => it.Name == "Merchant")),
            new Act("remember the engine's numbers", () =>
            {
                worked = First("Worked");
                available = screen.City.Cells.Where(it => it.State == "Available").OrderByDescending(it => it.Label.Length).First();
                workedBefore = city.WorkedTiles.Count;
                foodBefore = Food();
                goldBefore = city.CityStats.CurrentCityStats[Stat.Gold];
            }),
            new Capture("open", "The capital's city screen: header, stats, focus, and the tile grid with the worked tiles lit"),

            new Click(() => Tile(worked)),
            new AssertQuery("a click on a worked tile freed its citizen", () =>
                refusals.Count == 0 && city.WorkedTiles.Count == workedBefore - 1 && Panel().Growth.FreePopulation == 1 && screen.City.Cells.Any(it => it.X == worked.X && it.Y == worked.Y && it.State == "Available")),
            new Click(() => Tile(available)),
            new AssertQuery("a click on an available tile put the citizen there, and the city's stats are the engine's new ones", () =>
                refusals.Count == 0 && city.WorkedTiles.Count == workedBefore && city.WorkedTiles.Contains(new Unciv.Logic.Map.HexCoord(available.X, available.Y))
                && screen.City.Cells.Any(it => it.X == available.X && it.Y == available.Y && it.State == "Worked")
                && Panel().TotalStats.Any(it => it.Stat == "Food" && Math.Abs(it.Value - Food()) < 0.001f)),
            new Capture("tile-moved", "The citizen moved to another tile; the stats follow"),
            new Click(() => Tile(First("Center"))),
            new AssertQuery("a click on the city centre is explained, not sent", () => screen.City.MessageText.Contains("always worked", StringComparison.Ordinal) && refusals.Count == 0),
            new Act("right click the new tile to lock it", () =>
                InputSimulator.SimulateMouseClick(host.GetViewport(), Tile(available), MouseButton.Right)),
            new AssertQuery("a right click locked the worked tile", () =>
                city.LockedTiles.Contains(new Unciv.Logic.Map.HexCoord(available.X, available.Y)) && screen.City.Cells.Any(it => it.X == available.X && it.Y == available.Y && it.Locked)),
            new Capture("tile-locked", "The tile is locked: it keeps its golden outline"),
            new Act("right click the tile again to unlock it", () =>
                InputSimulator.SimulateMouseClick(host.GetViewport(), Tile(available), MouseButton.Right)),
            new AssertQuery("a second right click unlocked it", () => city.LockedTiles.Count == 0 && !screen.City.Cells.Any(it => it.Locked)),

            new Click(() => Tile(First("Worked"))),
            new AssertQuery("with a free citizen the Merchant + button is enabled", () => Panel().Growth.FreePopulation == 1 && screen.City.SpecialistButton("Merchant", add: true) is { Disabled: false }),
            new Act("remember gold", () => goldBefore = city.CityStats.CurrentCityStats[Stat.Gold]),
            new Click(() => Centre(screen.City.SpecialistButton("Merchant", add: true))),
            new AssertQuery("the + button made the free citizen a Merchant, and gold rose in the engine's stats", () =>
                refusals.Count == 0 && city.Population.GetNewSpecialists()["Merchant"] == 1 && city.CityStats.CurrentCityStats[Stat.Gold] > goldBefore
                && screen.City.SideTexts.Contains("Merchant 1/1") && screen.City.SpecialistButton("Merchant", add: false) is { Disabled: false } && screen.City.SpecialistButton("Merchant", add: true) is { Disabled: true }),
            new Capture("specialist", "A Merchant at work: the specialist row reads 1/1 and the stats include it"),
            new Click(() => Centre(screen.City.SpecialistButton("Merchant", add: false))),
            new AssertQuery("the - button turned the Merchant back into a citizen", () => city.Population.GetNewSpecialists()["Merchant"] == 0 && Panel().Growth.FreePopulation == 1),

            new Act("choose the Production focus", () => screen.City.ChooseFocus(CityFocus.ProductionFocus)),
            new AssertQuery("the engine has the focus and the dropdown shows it", () =>
                city.GetCityFocus() == CityFocus.ProductionFocus && Panel().CityFocus == "ProductionFocus" && refusals.Count == 0),
            new Press("Reset citizens"),
            new AssertQuery("Reset citizens let the engine reassign: nothing is locked, no citizen idles", () => city.LockedTiles.Count == 0 && Panel().Growth.FreePopulation == 0),
            new Capture("focused", "After choosing the Production focus and resetting citizens"),

            new Act("open a city of another civ", () =>
            {
                recording.Queries.Clear();
                screen.OpenCityScreen(Foreign);
            }),
            new WaitFrames(2),
            new AssertQuery("the foreign city shows only its snapshot entry: owner, size, defence, wonders", () =>
                screen.City.IsForeign && screen.City.Panel == null && screen.City.TitleText == "Thebes"
                && screen.City.SideTexts.Contains("Wonders: Pyramids") && screen.City.SideTexts.Contains("Defence: 25/40") && screen.City.Cells.Count == 0 && !screen.City.HasFocusChoice),
            new AssertQuery("no city query was asked about it", () => recording.Queries.Count == 0),
            new AssertQuery("a foreign city cannot be renamed: no Rename button, and the popup does not open (C048)", () =>
            {
                screen.City.OpenRename();
                return !screen.City.RenameButton.Visible && !screen.City.RenamePopup.Visible;
            }),
            new Capture("foreign", "A city of another civ: only what a stranger can see"),
            new Press("Close city"),
            new AssertQuery("the close button closes the screen and the hook", () => screen.OpenCity == null && !screen.IsCityPanelVisible),
            new AssertQuery("the whole scenario stayed on the command path: nothing was refused", () => refusals.Count == 0),
        ];
    }

    /// <summary>Forwards to a real session and remembers every query type asked since the last <see cref="Queries"/> clear.</summary>
    internal sealed class RecordingSession(IGameSession inner) : IGameSession
    {
        public List<string> Queries { get; } = [];

        public string ViewerCivId => inner.ViewerCivId;

        public Unciv.Models.Ruleset.Ruleset Ruleset => inner.Ruleset;

        public ClientSnapshot Snapshot => inner.Snapshot;

        public event Action<ClientSnapshot>? SnapshotChanged
        {
            add => inner.SnapshotChanged += value;
            remove => inner.SnapshotChanged -= value;
        }

        public event Action<EventBatch>? EventsReceived
        {
            add => inner.EventsReceived += value;
            remove => inner.EventsReceived -= value;
        }

        public event Action<string>? Desynchronized
        {
            add => inner.Desynchronized += value;
            remove => inner.Desynchronized -= value;
        }

        public Task ResyncAsync() => inner.ResyncAsync();

        public Task<TResult?> QueryAsync<TResult>(IQuery<TResult> query) where TResult : class
        {
            Queries.Add(query.GetType().Name);
            return inner.QueryAsync(query);
        }

        public Task<SubmitResult> SubmitAsync(GameCommand command) => inner.SubmitAsync(command);

        public Task<TurnOutcome> EndTurnAsync() => inner.EndTurnAsync();
        public Task<TurnOutcome> AutoPlayTurnAsync() => inner.AutoPlayTurnAsync();

        public Task<string?> ExportSaveAsync() => inner.ExportSaveAsync();
    }
}
