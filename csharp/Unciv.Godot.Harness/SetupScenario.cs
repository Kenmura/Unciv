using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Setup;
using Unciv.GodotClient.Ui;
using Unciv.Harness;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C015: from the main menu through the new-game screen by clicking: base ruleset, players and nations, difficulty, era, victory types, map options, seed and advanced flags;
/// a bad seed is refused with a message and starts nothing; then Start. The started game's parameters equal the choices, and its state hash equals a headless
/// <see cref="GameStarter"/> run from an independently written setup with the same seed and clock. The clock is fixed so the run is reproducible.
/// </summary>
public sealed class SetupScenario : Scenario
{
    private const long Clock = 5000;
    private const string BadSeedMessage = "The seed must be a whole number from 0 to 9223372036854775807 (leave it empty for a random map).";

    public override string Name => "setup";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the main menu clear about what is available (Resume disabled)?",
        "Can you tell which option of each row is chosen, and which player slot is selected?",
        "Are the nation colours readable and the taken nations visibly unavailable?",
        "Is the error for a bad seed visible and understandable?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var registry = AssetRegistry.ForRuleset(session.Game.Ruleset);
        var theme = new UiTheme(registry);
        var manager = new ScreenManager(theme);
        host.AddChild(manager);
        var menu = new MainMenu(); manager.Push(menu);

        NewGameScreen? screen = null;
        LocalHost? started = null;
        GameScreen? gameScreen = null;
        int startCalls = 0;
        menu.NewGameRequested += () =>
        {
            screen = new NewGameScreen(theme, NewGameForm.FromRulesetCache(), form =>
            {
                startCalls++;
                return LocalHost.Start(form, () => Clock);
            });
            screen.Started += hosted =>
            {
                started = hosted;
                gameScreen = new GameScreen { Name = "GameScreen" };
                var playRoot = new Control { Name = "Play", MouseFilter = Control.MouseFilterEnum.Ignore };
                playRoot.AddChild(gameScreen);
                manager.Reset(playRoot);
                gameScreen.Start(hosted, AssetRegistry.ForRuleset(hosted.Ruleset));
            };
            manager.Push(screen);
        };

        NewGameScreen Screen() => screen.NotNull();
        Vector2 Centre(Func<Control?> find) => find()?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        // a page is taller than the 640x480 window: scroll the control into view (the next steps wait a frame for the layout)
        Act Show(Func<Control?> find) => new("scroll into view", () => { if (find() is { } control) Screen().Reveal(control); });
        Click ClickChoice(string field, string option) => new(() => Centre(() => Screen().ChoiceButton(field, option)));
        Click ClickNation(string nation) => new(() => Centre(() => Screen().NationButton(nation)));
        Click ClickSlotNation(int slot) => new(() => Centre(() => Screen().SlotNationButton(slot)));

        // The pages are rebuilt after every change, and a new button has no layout until a frame has passed: wait after each input.
        ScenarioStep[] script =
        [
            new WaitFrames(3),
            new AssertQuery("the menu is the only screen; Resume last is disabled", () => manager.Depth == 1 && !menu.CanResume),
            new Capture("menu", "Main menu: New game, Resume last (disabled), Load game, Multiplayer, Host game, Options, Quit"),
            new Press("New game"),
            new WaitFrames(2),
            new AssertQuery("the setup screen opened with the defaults: four players, one human", () => manager.Depth == 2 && Screen().Form.Slots.Count == 4 && Screen().Form.Humans == 1),
            new Capture("defaults", "New game screen, Players tab, with the defaults"),
            .. LayoutChecks.Widths(host, "setup screen", () => Screen(), "1280x720: the Players tab with the nation grid and the detail card using the extra width", "1920x1080: the same screen at full HD"),
            .. GermanSteps(host, manager, () => Screen()),

            new Press("Rules"),
            new Click(() => Centre(() => Screen().ChoiceButton("Base ruleset", "Civ V - Vanilla"))),
            new AssertQuery("the base ruleset is Vanilla", () => Screen().Form.BaseRuleset == "Civ V - Vanilla"),
            new Capture("rules", "Rules tab: Vanilla chosen as the base ruleset, no mods installed"),

            new Press("Players"),
            new Press("Fewer players"),
            new Press("Fewer players"),
            new AssertQuery("two players", () => Screen().Form.Slots.Count == 2),
            Show(() => Screen().SlotNationButton(0)), ClickSlotNation(0),
            Show(() => Screen().NationButton("Babylon")), ClickNation("Babylon"),
            Show(() => Screen().SlotNationButton(1)), ClickSlotNation(1),
            Show(() => Screen().NationButton("Rome")), ClickNation("Rome"),
            new AssertQuery("Babylon and Rome chosen", () => Screen().Form.Slots.Select(it => it.Nation).SequenceEqual(["Babylon", "Rome"])),
            new AssertQuery("Babylon is taken in the grid while slot 2 is selected", () => Screen().NationButton("Babylon")?.Disabled == true && Screen().NationButton("Rome")?.Disabled == false),
            Show(() => Screen().SlotHumanButton(1)), new Click(() => Centre(() => Screen().SlotHumanButton(1))),
            new AssertQuery("choosing slot 2 as human moves the human", () => Screen().Form.Slots.Select(it => it.IsHuman).SequenceEqual([false, true])),
            Show(() => Screen().SlotHumanButton(0)), new Click(() => Centre(() => Screen().SlotHumanButton(0))),
            new AssertQuery("and back to slot 1", () => Screen().Form.Slots.Select(it => it.IsHuman).SequenceEqual([true, false])),
            new Act("no city-states", () => { while (Screen().Form.CityStates > 0) Screen().Form.SetCityStates(Screen().Form.CityStates - 1); Screen().Refresh(); }),
            new AssertQuery("the footer summarises the choices and the detail pane describes Rome", () => Screen().Summary.StartsWith("2 players, 0 city-states", StringComparison.Ordinal) && Screen().FindChild("NationDetail", true, false) != null),
            new Capture("players", "Players tab: Babylon (human) and Rome (AI), no city-states, the detail card showing Rome"),

            new Press("Game"),
            Show(() => Screen().ChoiceButton("Difficulty", "King")), ClickChoice("Difficulty", "King"),
            Show(() => Screen().ChoiceButton("Era", "Classical era")), ClickChoice("Era", "Classical era"),
            new Act("scroll to Domination", () => Reveal(Screen(), "Domination")),
            new Press("Domination"),
            new AssertQuery("King, Classical era, no Domination victory", () => Screen().Form.Difficulty == "King" && Screen().Form.Era == "Classical era" && !Screen().Form.Victories.Contains("Domination")),
            new Capture("game", "Game tab: King, Classical era, Domination switched off"),

            new Press("Map"),
            Show(() => Screen().ChoiceButton("Type", "Pangaea")), ClickChoice("Type", "Pangaea"),
            Show(() => Screen().ChoiceButton("Shape", "Hexagonal")), ClickChoice("Shape", "Hexagonal"),
            Show(() => Screen().ChoiceButton("Size", "Tiny")), ClickChoice("Size", "Tiny"),
            Show(() => Screen().ChoiceButton("Resources", "Abundant")), ClickChoice("Resources", "Abundant"),
            new Act("scroll to World wrap", () => Reveal(Screen(), "World wrap")),
            new Press("World wrap"),
            new Act("type the seed 3", () => TypeSeed(Screen(), "3")),
            new Act("scroll to the seed", () => Screen().Reveal(Screen().SeedField)),
            new AssertQuery("map choices reached the form", () => Screen().Form is { MapType: "Pangaea", Shape: "Hexagonal", Size: "Tiny", Resources: "Abundant", WorldWrap: false, SeedText: "3" }),
            new Capture("map", "Map tab: Pangaea, Hexagonal, Tiny, Abundant, no world wrap, seed 3"),

            new Press("Advanced"),
            new Press("No barbarians"),
            new Press("Nuclear weapons"),
            new AssertQuery("advanced flags reached the form", () => Screen().Form.NoBarbarians && !Screen().Form.NuclearWeapons),
            new Capture("advanced", "Advanced tab: No barbarians on, Nuclear weapons off"),

            new Act("type a bad seed", () => TypeSeed(Screen(), "abc")),
            new AssertQuery("the problem is shown at once", () => Screen().Message == BadSeedMessage),
            new Press("Start game"),
            new WaitFrames(4),
            new AssertQuery("nothing started, the error stays and Start works again", () => started == null && startCalls == 1 && Screen().Message == BadSeedMessage && !Screen().StartButton.Disabled),
            new Press("Map"),
            new Act("scroll to the seed", () => Screen().Reveal(Screen().SeedField)),
            new Capture("bad-seed", "Map tab with the bad seed: the reason is shown under the tabs"),
            new Act("fix the seed", () => TypeSeed(Screen(), "3")),
            new AssertQuery("no problems left", () => Screen().Message == ""),

            new Press("Start game"),
            new WaitFrames(8),
            new AssertQuery("the game started and replaced the setup screen", () => started != null && gameScreen != null && manager.Depth == 1 && startCalls == 2),
            new AssertQuery("parameters equal the choices", () => ChoicesReached(started.NotNull().Session.Game)),
            new AssertQuery("the player is Babylon on turn 0 and the map is Tiny hexagonal", () => started.NotNull().ViewerCivId == "Babylon" && gameScreen.NotNull().Turn == 0 && started.NotNull().Session.Game.TileMap.MapParameters is { Shape: "Hexagonal", WorldWrap: false } && started.NotNull().Session.Game.TileMap.MapParameters.MapSize.Name == "Tiny"),
            new AssertQuery("the civ list is Babylon and Rome (no barbarians)", () => started.NotNull().Session.Game.Civilizations.Select(it => it.CivName).Order(StringComparer.Ordinal).SequenceEqual(["Babylon", "Rome"])),
            new AssertQuery("state hash equals a headless GameStarter run of the same setup, seed and clock", () => ScenarioGame.StateHash(started.NotNull().Session) == ScenarioGame.StateHash(new GameSession(GameStarter.StartNewGame(ExpectedSetup(), 3, Clock)))),
            new Capture("started", "The new game, started from the setup screen"),
        ];
        return script.SelectMany(step => step is Press or Click or Act ? new[] { step, new WaitFrames(2) } : [step]).ToList();
    }

    private static void Reveal(NewGameScreen screen, string buttonText)
    {
        static Button? Find(Node node, string text) =>
            node is Button { Visible: true } b && b.Text == text ? b : node.GetChildren().Select(it => Find(it, text)).FirstOrDefault(it => it != null);
        if (Find(screen, buttonText) is { } button) screen.Reveal(button);
    }

    private static void TypeSeed(NewGameScreen screen, string text)
    {
        screen.SeedField.Text = text;
        screen.SeedField.EmitSignal(LineEdit.SignalName.TextChanged, text);
    }

    /// <summary>C079: the setup screen in German: a fresh screen (text is built with the screen), then back to English with a fresh screen for the rest of the script.</summary>
    private static ScenarioStep[] GermanSteps(Control host, ScreenManager manager, Func<NewGameScreen> screen) =>
    [
        new Act("close the setup screen", () => manager.Pop()),
        LocaleSteps.Use("German"),
        new Press("New game"),
        new WaitFrames(3),
        new AssertQuery("German: the setup screen is open again", () => manager.Depth == 2),
        LayoutChecks.Assert("German: no setup text is cut off, leaves the window or overlaps another", host, () => screen()),
        new Capture("de", "German at 640x480: the tabs, the player rows and the buttons fit without clipping or overlap"),
        new Act("close the setup screen", () => manager.Pop()),
        LocaleSteps.Use("English"),
        new Press("New game"),
        new WaitFrames(3),
    ];

    // The same choices written out by hand, independent of the form
    private static GameSetupInfo ExpectedSetup() => new(
        new GameParameters
        {
            BaseRuleset = "Civ V - Vanilla",
            Difficulty = "King",
            StartingEra = "Classical era",
            NumberOfCityStates = 0,
            NoBarbarians = true,
            NuclearWeaponsEnabled = false,
            EspionageEnabled = true,
            VictoryTypes = ["Scientific", "Cultural", "Diplomatic", "Time"],
            Players = [new Player("Babylon", PlayerType.Human), new Player("Rome")],
        },
        new MapParameters { BaseRuleset = "Civ V - Vanilla", Type = MapType.Pangaea, Shape = MapShape.Hexagonal, MapSize = MapSize.Tiny, MapResources = "Abundant", WorldWrap = false, Seed = 3 });

    private static bool ChoicesReached(GameInfo game)
    {
        var p = game.GameParameters;
        var m = game.TileMap.MapParameters;
        return p.BaseRuleset == "Civ V - Vanilla" && p.Difficulty == "King" && p.StartingEra == "Classical era" && p.NumberOfCityStates == 0
            && p.NoBarbarians && !p.NuclearWeaponsEnabled && p.VictoryTypes.SequenceEqual(["Scientific", "Cultural", "Diplomatic", "Time"])
            && p.Players.Select(it => (it.ChosenCiv, it.PlayerType)).SequenceEqual([("Babylon", PlayerType.Human), ("Rome", PlayerType.AI)])
            && m.Type == MapType.Pangaea && m.MapResources == "Abundant" && m.Seed == 3;
    }
}
