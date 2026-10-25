using System.Globalization;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Cli;

/// <summary>
/// The headless runner: <c>new</c> starts a game and writes a save, <c>play</c> advances a save by N turns, <c>save</c>/<c>load</c> convert and check saves,
/// <c>hash</c> and <c>info</c> inspect one. Every command is a pure function of its arguments and the files it reads: the game seed, map seed and
/// clock are inputs (no wall clock, no unseeded randomness), and all text is written with the invariant culture.
/// </summary>
public static class CliRunner
{
    public const int ExitOk = 0;
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;

    /// <summary>The clock reading a new game starts with (<c>currentTurnStartTime</c>); each played turn then advances it by <see cref="TurnMillis"/>.</summary>
    public const long StartMillis = 1_000_000L;
    public const long TurnMillis = 1000L;

    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "zip", "hashes" };

    public const string Usage = """
        usage: unciv-cli <command> [options]

        commands:
          new   --out FILE [--nations A,B,C] [--city-states N] [--map-size Tiny|Small|Medium|Large|Huge] [--map-type NAME]
                [--shape Rectangular|Hexagonal|"Flat Earth Hexagonal"] [--difficulty NAME] [--speed NAME]
                [--seed N] [--map-seed N] [--zip]
                Start a game (the first nation is the human, played by the AI automation) and save it.
          play  --in FILE --turns N [--out FILE] [--hashes] [--zip]
                Play N turns; --hashes prints the state hash after every turn, --out saves the result.
          save  --in FILE --out FILE [--zip]     Load a save and write it again (plain JSON, or --zip).
          load  --in FILE                        Load a save and print its summary line and state hash.
          hash  --in FILE                        Print the state hash (StateHasher over the plain JSON save).
          info  --in FILE                        Print the game's turn, map and civilizations.
        every command also takes [--jsons DIR]: the android/assets/jsons folder (default: searched upwards from the working directory).
        exit codes: 0 ok, 1 failure while running, 2 bad command line.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Count(it => it is "help" or "--help" or "-h") != 0)
            {
                output.WriteLine(Usage);
                return ExitOk;
            }
            var cli = new CliArgs(args, Flags);
            switch (cli.Command)
            {
                case "new": New(cli, output); break;
                case "play": Play(cli, output); break;
                case "save": Save(cli, output); break;
                case "load": Load(cli, output); break;
                case "hash": Hash(cli, output); break;
                case "info": Info(cli, output); break;
                default: throw new UsageException($"unknown command '{cli.Command}'");
            }
            return ExitOk;
        }
        catch (UsageException e)
        {
            error.WriteLine("error: " + e.Message);
            error.WriteLine(Usage);
            return ExitUsage;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException
                                      or System.Text.Json.JsonException or UncivShowableException or KeyNotFoundException or ArgumentException)
        {
            error.WriteLine("error: " + e.Message);
            return ExitFailure;
        }
    }

    /// <summary>The state hash: <see cref="StateHasher"/> over the plain (unzipped) JSON save, which holds the whole game state.</summary>
    public static string HashOf(GameInfo game) =>
        StateHasher.ComputeHexString(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static RulesetClass LoadRulesets(CliArgs cli)
    {
        var jsons = cli.Get("jsons") ?? FindJsons();
        RulesetCache.Clear();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        return RulesetCache.GetVanillaRuleset();
    }

    private static string FindJsons()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
                if (Directory.Exists(candidate)) return candidate;
            }
        throw new UsageException("android/assets/jsons not found; pass --jsons DIR");
    }

    private static T ChooseOf<T>(string option, string? text, string defaultName, IEnumerable<T> choices, Func<T, string> name)
    {
        var wanted = text ?? defaultName;
        var all = choices.ToList();
        return all.FirstOrDefault(it => name(it) == wanted)
            ?? throw new UsageException($"--{option} '{wanted}' is not one of: {string.Join(", ", all.Select(name))}");
    }

    private static void WriteSave(GameInfo game, string path, bool zip) =>
        File.WriteAllText(path, UncivFiles.GameInfoToString(game, forceZip: zip));

    private static GameInfo ReadSave(string path)
    {
        if (!File.Exists(path)) throw new IOException($"save file '{path}' does not exist");
        return UncivFiles.GameInfoFromString(File.ReadAllText(path));
    }

    private static GameInfo LoadGame(CliArgs cli)
    {
        var path = cli.Require("in");
        LoadRulesets(cli);
        return ReadSave(path);
    }

    private static void New(CliArgs cli, TextWriter output)
    {
        var outPath = cli.Require("out");
        var nations = (cli.Get("nations") ?? "Babylon,Greece,Rome").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var cityStates = cli.GetInt("city-states", 2, 0, 64);
        var seed = cli.GetLong("seed", 1);
        var mapSeed = cli.GetLong("map-seed", seed);
        var sizeName = cli.Get("map-size");
        var typeName = cli.Get("map-type");
        var shapeName = cli.Get("shape");
        var difficultyName = cli.Get("difficulty");
        var speedName = cli.Get("speed");
        var zip = cli.Flag("zip");
        var ruleset = LoadRulesets(cli);
        cli.RejectUnknown();

        if (nations.Length == 0) throw new UsageException("--nations needs at least one nation");
        if (nations.Distinct(StringComparer.Ordinal).Count() != nations.Length) throw new UsageException("--nations lists a nation twice");
        foreach (var nation in nations)
            if (!ruleset.Nations.TryGetValue(nation, out var found) || found.IsBarbarian || found.IsSpectator || found.IsCityState)
                throw new UsageException($"--nations: '{nation}' is not a major nation of the ruleset");

        var size = ChooseOf("map-size", sizeName, nameof(MapSize.Predefined.Tiny), Enum.GetNames<MapSize.Predefined>(), it => it);
        var type = ChooseOf("map-type", typeName, MapType.Pangaea, MapTypeNames, it => it);
        var shape = ChooseOf("shape", shapeName, MapShape.Rectangular, [MapShape.Rectangular, MapShape.Hexagonal, MapShape.FlatEarth], it => it);
        var difficulty = ChooseOf("difficulty", difficultyName, new GameParameters().Difficulty, ruleset.Difficulties.Keys, it => it);
        var speed = ChooseOf("speed", speedName, new GameParameters().Speed, ruleset.Speeds.Keys, it => it);

        var parameters = new GameParameters
        {
            BaseRuleset = ruleset.Name,
            Difficulty = difficulty,
            Speed = speed,
            NumberOfCityStates = cityStates,
            VictoryTypes = ruleset.Victories.Keys.ToList(),
            Players = nations.Select((it, i) => new Player(it, i == 0 ? PlayerType.Human : PlayerType.AI)).ToList(),
        };
        var mapParameters = new MapParameters { Type = type, Shape = shape, Seed = mapSeed, MapSize = new MapSize(size) };
        var game = GameStarter.StartNewGame(new GameSetupInfo(parameters, mapParameters), seed, StartMillis);
        WriteSave(game, outPath, zip);
        output.WriteLine($"{Summary(game)} hash={HashOf(game)}");
    }

    private static readonly string[] MapTypeNames =
    [
        MapType.Pangaea, MapType.SmallContinents, MapType.Perlin, MapType.Fractal, MapType.ContinentAndIslands, MapType.Archipelago,
        MapType.TwoContinents, MapType.ThreeContinents, MapType.InnerSea, MapType.Lakes, MapType.FourCorners, MapType.Boreal, MapType.Spiral,
    ];

    private static void Play(CliArgs cli, TextWriter output)
    {
        var turns = cli.GetInt("turns", -1, 0, 100_000);
        if (turns < 0) throw new UsageException("missing required option --turns");
        var outPath = cli.Get("out");
        var hashes = cli.Flag("hashes");
        var zip = cli.Flag("zip");
        var game = LoadGame(cli);
        cli.RejectUnknown();

        if (hashes) output.WriteLine($"turn {Inv(game.Turns)} {HashOf(game)}");
        for (var i = 0; i < turns; i++)
        {
            if (game.GetAliveMajorCivs().Count == 0)
            {
                output.WriteLine($"no major civilization is left on turn {Inv(game.Turns)}; stopping");
                break;
            }
            game.SimulateUntilWin = true;
            game.SimulateMaxTurns = game.Turns + 1;
            game.NextTurn(game.CurrentTurnStartTime + TurnMillis);
            if (hashes) output.WriteLine($"turn {Inv(game.Turns)} {HashOf(game)}");
        }
        if (outPath != null) WriteSave(game, outPath, zip);
        output.WriteLine($"{Summary(game)} hash={HashOf(game)}");
    }

    private static void Save(CliArgs cli, TextWriter output)
    {
        var outPath = cli.Require("out");
        var zip = cli.Flag("zip");
        var game = LoadGame(cli);
        cli.RejectUnknown();
        WriteSave(game, outPath, zip);
        output.WriteLine($"{Summary(game)} hash={HashOf(game)}");
    }

    private static void Load(CliArgs cli, TextWriter output)
    {
        var game = LoadGame(cli);
        cli.RejectUnknown();
        output.WriteLine($"{Summary(game)} hash={HashOf(game)}");
    }

    private static void Hash(CliArgs cli, TextWriter output)
    {
        var game = LoadGame(cli);
        cli.RejectUnknown();
        output.WriteLine(HashOf(game));
    }

    private static void Info(CliArgs cli, TextWriter output)
    {
        var game = LoadGame(cli);
        cli.RejectUnknown();
        output.WriteLine(Summary(game));
        output.WriteLine($"map: {game.TileMap.MapParameters.Type} {game.TileMap.MapParameters.Shape} {game.TileMap.MapParameters.MapSize.Name}, {Inv(game.TileMap.Values.Count())} tiles");
        foreach (var civ in game.Civilizations)
            output.WriteLine($"civ {civ.CivId} {civ.PlayerType} {(civ.IsAlive() ? "alive" : "dead")} cities={Inv(civ.Cities.Count)} gold={Inv(civ.Gold)}");
    }

    private static string Summary(GameInfo game) =>
        $"game {game.GameId} turn={Inv(game.Turns)} current={game.CurrentPlayer} civs={Inv(game.Civilizations.Count)} difficulty={game.Difficulty} speed={game.GameParameters.Speed}";
}
