using Unciv.Commands;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Harness;

/// <summary>
/// The fixed game every client scenario starts from (C008): Vanilla ruleset, Tiny map, two human civs (Babylon moves first) and
/// one AI civ, each human with a city, all from fixed seeds. The Godot scenario runner and the headless replay both build their
/// session here, so "same seed + same commands" is a statement about exactly one factory. Engine-only; no Godot types.
/// </summary>
public static class ScenarioGame
{
    public const long GameSeed = 7L;
    public const int MapSeed = 3;
    public const long FixedClockMillis = 1000L;
    public const string HumanA = "Babylon";
    public const string HumanB = "Greece";
    public const string Ai = "Rome";

    /// <summary>Loads the Vanilla ruleset into <see cref="RulesetCache"/> (from the repository's jsons, no mods) unless it is already there.</summary>
    public static RulesetClass LoadVanilla()
    {
        if (RulesetCache.ContainsKey(BaseRuleset.Civ_V_Vanilla.FullName())) return RulesetCache.GetVanillaRuleset();
        var jsons = FindJsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        return RulesetCache.GetVanillaRuleset();
    }

    public static string FindJsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// <paramref name="solo"/> (C010): one human (Babylon) and the AI, no city founded: the game the client's QuickStart builds; otherwise the two-human game above.
    /// <paramref name="godMode"/> (C041): the game setup's God mode switch, so a scenario can use the debug commands (free purchases, DebugAddTechnology) to reach a state quickly. The switch is part of the setup, not a command, so the replay passes it too.
    /// </summary>
    public static GameSession Create(long gameSeed = GameSeed, int mapSeed = MapSeed, bool solo = false, bool godMode = false, bool skirmish = false)
    {
        solo |= skirmish;
        var ruleset = LoadVanilla();
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    GodMode = godMode,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = solo
                        ? [new Player(HumanA, PlayerType.Human), new Player(Ai)]
                        : [new Player(HumanA, PlayerType.Human), new Player(HumanB, PlayerType.Human), new Player(Ai)],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = mapSeed, MapSize = MapSize.Tiny }),
            gameSeed, FixedClockMillis);
        foreach (string name in solo ? Array.Empty<string>() : new[] { HumanA, HumanB })
        {
            var civ = game.GetCivilization(name);
            var settler = civ.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
            civ.AddCity(settler.CurrentTile.Position, settler);
        }
        if (skirmish) SetUpSkirmish(game);
        return new GameSession(game);
    }

    /// <summary>
    /// C012: the solo game plus an AI warrior on a tile next to the human warrior, at war with the human, and a human worker on another neighbouring tile: a
    /// battle, a worker and a settler to give orders to. Set up directly on the state (like the founded cities above), so a headless replay builds the same game.
    /// </summary>
    private static void SetUpSkirmish(GameInfo game)
    {
        var human = game.GetCivilization(HumanA);
        var ai = game.GetCivilization(Ai);
        var warrior = human.Units.GetCivUnits().OrderBy(it => it.Id).First(it => it.BaseUnit.Name == "Warrior");
        var free = warrior.CurrentTile.Neighbors
            .Where(it => it.IsLand && !it.IsImpassible() && !it.GetUnits().Any())
            .OrderBy(it => it.Position.x).ThenBy(it => it.Position.y).ToList();
        ai.Units.PlaceUnitNearTile(free[0].Position, "Warrior");
        human.Units.PlaceUnitNearTile(free[1].Position, "Worker");
        human.GetDiplomacyManagerOrMeet(ai).DeclareWar();
    }


    /// <summary>The 64-bit canonical hash of the whole game state (serialised form), as lowercase hex.</summary>
    public static string StateHash(GameSession session) =>
        StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(session.Game, forceZip: false))).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The first city of a civ, in the engine's own order.</summary>
    public static CityObject FirstCity(GameInfo game, string civName) => game.GetCivilization(civName).Cities[0];
}
