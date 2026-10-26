using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Saves;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;
using Unciv.Logic;
using Unciv.Models.Metadata;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;

namespace Unciv.GodotClient;

/// <summary>
/// Minimal entry point until the menus exist (C015): a seeded Tiny Pangaea game with Babylon (you) against Rome (AI), built by <see cref="GameStarter"/>
/// from the Vanilla ruleset, shown in a <see cref="GameScreen"/>. The same shape as the harness's solo scenario game, so its scenarios replay it headlessly.
/// </summary>
public partial class QuickStart : Node
{
    public const long GameSeed = 7L;
    public const int MapSeed = 3;
    public const string Human = "Babylon";
    public const string Ai = "Rome";

    public override void _Ready()
    {
        var ruleset = RulesetLocator.LoadVanilla();
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = [new Player(Human, PlayerType.Human), new Player(Ai)],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = MapSeed, MapSize = MapSize.Tiny }),
            GameSeed, WallClockMillis());
        var host = new LocalHost(new GameSession(game), Human, WallClockMillis);
        var screen = new GameScreen { Name = "GameScreen" };
        screen.Presenter.Enabled = true;
        AddChild(screen);
        screen.Start(host, AssetRegistry.ForRuleset(game.Ruleset));

        // C016: save/load. Saves live in the per-user saves folder; a loaded game is a new LocalHost handed to the same screen. Settings come from the C043 service (autosave interval).
        var settings = new ClientSettingsService(new ClientSettingsStore(UserPaths.Settings));
        var saves = new SaveController(new SaveStore(UserPaths.Saves), settings, new UiTheme(AssetRegistry.ForRuleset(game.Ruleset)),
            text => LocalHost.FromSave(text, WallClockMillis), loaded => screen.Start(loaded, AssetRegistry.ForRuleset(loaded.Ruleset)));
        AddChild(saves);
        saves.Attach(host);
    }

    private static long WallClockMillis() => (long)(Time.GetUnixTimeFromSystem() * 1000.0);
}
