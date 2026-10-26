using System.Globalization;
using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Log;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;

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
          record --out FILE --log FILE --turns N [the options of new]
                Like new, but the human civ is played by the scripted human (the command layer, no auto-play) for N human turns;
                writes the command log to --log and the final game to --out.
          replay --log FILE [--hashes]
                Replay a command log from its header and compare the state hash at every checkpoint (exit 1 on a divergence);
                --hashes prints each checkpoint.
          submit --in FILE --commands FILE [--actor CIV] [--out FILE]
                Submit the commands of FILE (one json line {"type":"end-turn","payload":{}} each) as the human civ, stopping at the first refusal.
          save  --in FILE --out FILE [--zip]     Load a save and write it again (plain JSON, or --zip).
          load  --in FILE                        Load a save and print its summary line and state hash.
          hash  --in FILE                        Print the state hash (StateHasher over the plain JSON save).
          info  --in FILE                        Print the game's turn, map and civilizations.
        new/record also take [--ruleset NAME] [--mods A,B] [--map FILE] (a saved map; not with record); every command takes [--mods-dir DIR] (the folder of mods, needed by --mods
        and by saves/logs that use mods) and [--jsons DIR]: the android/assets/jsons folder (default: data/jsons next to the executable in a release build, else searched upwards from the working directory).
        exit codes: 0 ok, 1 failure while running, 2 bad command line.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length != 0 && args[0] is "help" or "--help" or "-h")
            {
                output.WriteLine(Usage);
                return ExitOk;
            }
            var cli = new CliArgs(args, Flags);
            var code = ExitOk;
            switch (cli.Command)
            {
                case "new": New(cli, output); break;
                case "record": Record(cli, output); break;
                case "replay": code = Replay(cli, output); break;
                case "submit": code = Submit(cli, output); break;
                case "play": Play(cli, output); break;
                case "save": Save(cli, output); break;
                case "load": Load(cli, output); break;
                case "hash": Hash(cli, output); break;
                case "info": Info(cli, output); break;
                default: throw new UsageException($"unknown command '{cli.Command}'");
            }
            return code;
        }
        catch (UsageException e)
        {
            error.WriteLine("error: " + e.Message);
            error.WriteLine(Usage);
            return ExitUsage;
        }
        catch (Exception e)
        {
            // JUSTIFY: T168 a CLI must turn any failure of the engine on a hostile save into exit code 1 and a message, not a stack trace
            if (e is OutOfMemoryException or StackOverflowException) throw;
            error.WriteLine("error: " + e.Message);
            return ExitFailure;
        }
    }

    /// <summary>The state hash: <see cref="StateHasher"/> over the plain (unzipped) JSON save, which holds the whole game state.</summary>
    public static string HashOf(GameInfo game) =>
        StateHasher.ComputeHexString(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Loads the base rulesets, and the mods of <c>--mods-dir</c> when given, into <see cref="RulesetCache"/>.</summary>
    private static void LoadRulesets(CliArgs cli)
    {
        var jsons = cli.Get("jsons") ?? FindJsons();
        var modsDir = cli.Get("mods-dir");
        if (!File.Exists(Path.Combine(jsons, "ModCategories.json"))) throw new UsageException($"'{jsons}' is not a rulesets folder (no ModCategories.json); pass --jsons DIR");
        if (modsDir != null && !Directory.Exists(modsDir)) throw new UsageException($"--mods-dir '{modsDir}' is not a folder");
        RulesetCache.Clear();
        var errors = RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, modsDir ?? Path.Combine(jsons, "no mods here")), noMods: modsDir == null);
        if (errors.Count != 0) throw new IOException("the rulesets have errors: " + string.Join("; ", errors));
    }

    private static string FindJsons()
    {
        // A release build ships the rulesets in data/jsons next to the executable (tools/release.py), so it runs from any folder.
        var bundled = Path.Combine(AppContext.BaseDirectory, "data", "jsons");
        if (Directory.Exists(bundled)) return bundled;
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

    private static void WriteSave(GameInfo game, string path, bool zip) => WriteText(path, UncivFiles.GameInfoToString(game, forceZip: zip));

    private static void WriteText(string path, string text)
    {
        // Write beside the target and move over it, so a failure while writing (also with --out equal to --in) keeps the old file.
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    private static GameInfo ReadSave(string path)
    {
        if (Directory.Exists(path)) throw new IOException($"'{path}' is a folder, not a save file");
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
        var zip = cli.Flag("zip");
        var (setup, seed) = BuildSetup(cli, allowMap: true);
        var game = GameStarter.StartNewGame(setup, seed, StartMillis, it => new PathFileHandle(it));
        WriteSave(game, outPath, zip);
        output.WriteLine($"{Summary(game)} hash={HashOf(game)}");
    }

    /// <summary>The setup of a new game from the command line (also loads the rulesets); the first nation is the human.</summary>
    private static (GameSetupInfo Setup, long Seed) BuildSetup(CliArgs cli, bool allowMap)
    {
        var nations = (cli.Get("nations") ?? "Babylon,Greece,Rome").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var cityStates = cli.GetInt("city-states", 2, 0, 64);
        var seed = cli.GetLong("seed", 1);
        var mapSeed = cli.GetLong("map-seed", seed);
        var sizeName = cli.Get("map-size");
        var typeName = cli.Get("map-type");
        var shapeName = cli.Get("shape");
        var difficultyName = cli.Get("difficulty");
        var speedName = cli.Get("speed");
        var baseName = cli.Get("ruleset") ?? BaseRuleset.Civ_V_Vanilla.FullName();
        var modNames = (cli.Get("mods") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mapFile = allowMap ? cli.Get("map") : null;
        LoadRulesets(cli);
        cli.RejectUnknown();

        if (!RulesetCache.ContainsKey(baseName) || !RulesetCache.Get(baseName)!.ModOptions.IsBaseRuleset)
            throw new UsageException($"--ruleset '{baseName}' is not one of: {string.Join(", ", RulesetCache.GetSortedBaseRulesets())}");
        foreach (var mod in modNames)
            if (!RulesetCache.ContainsKey(mod) || RulesetCache.Get(mod)!.ModOptions.IsBaseRuleset)
                throw new UsageException($"--mods: '{mod}' is not a loaded mod (pass --mods-dir DIR)");
        if (modNames.Distinct(StringComparer.Ordinal).Count() != modNames.Length) throw new UsageException("--mods lists a mod twice");
        var parameters = new GameParameters { BaseRuleset = baseName, Mods = new LinkedHashSet<string>(modNames) };
        var ruleset = RulesetCache.GetComplexRuleset(parameters);

        if (nations.Length == 0) throw new UsageException("--nations needs at least one nation");
        if (nations.Distinct(StringComparer.Ordinal).Count() != nations.Length) throw new UsageException("--nations lists a nation twice");
        foreach (var nation in nations)
            if (!ruleset.Nations.TryGetValue(nation, out var found) || found.IsBarbarian || found.IsSpectator || found.IsCityState)
                throw new UsageException($"--nations: '{nation}' is not a major nation of the ruleset");

        var size = ChooseOf("map-size", sizeName, nameof(MapSize.Predefined.Tiny), Enum.GetNames<MapSize.Predefined>(), it => it);
        var type = ChooseOf("map-type", typeName, MapType.Pangaea, MapTypeNames, it => it);
        var shape = ChooseOf("shape", shapeName, MapShape.Rectangular, [MapShape.Rectangular, MapShape.Hexagonal, MapShape.FlatEarth], it => it);
        parameters.Difficulty = ChooseOf("difficulty", difficultyName, new GameParameters().Difficulty, ruleset.Difficulties.Keys, it => it);
        parameters.Speed = ChooseOf("speed", speedName, new GameParameters().Speed, ruleset.Speeds.Keys, it => it);
        parameters.NumberOfCityStates = cityStates;
        parameters.VictoryTypes = ruleset.Victories.Keys.ToList();
        parameters.Players = nations.Select((it, i) => new Player(it, i == 0 ? PlayerType.Human : PlayerType.AI)).ToList();
        var mapParameters = new MapParameters { Type = type, Shape = shape, Seed = mapSeed, MapSize = new MapSize(size), BaseRuleset = baseName, Mods = new LinkedHashSet<string>(modNames) };
        var setup = new GameSetupInfo(parameters, mapParameters);
        if (mapFile != null)
        {
            if (!File.Exists(mapFile)) throw new IOException($"map file '{mapFile}' does not exist");
            mapParameters.Name = Path.GetFileNameWithoutExtension(mapFile);
            setup.MapFile = mapFile;
        }
        return (setup, seed);
    }

    private static void Record(CliArgs cli, TextWriter output)
    {
        var turns = cli.GetInt("turns", -1, 0, 100_000);
        if (turns < 0) throw new UsageException("missing required option --turns");
        var logPath = cli.Require("log");
        var outPath = cli.Get("out");
        var zip = cli.Flag("zip");
        var (setup, seed) = BuildSetup(cli, allowMap: false);
        var header = CommandLogHeader.Create(setup, seed, StartMillis);
        var game = GameStarter.StartNewGame(setup, seed, StartMillis);
        var human = game.Civilizations.First(it => it.PlayerType == PlayerType.Human).CivId;
        var session = new GameSession(game) { Clock = ScriptedHuman.DeterministicClock(StartMillis + TurnMillis) };
        var text = new StringWriter(CultureInfo.InvariantCulture);
        var played = new ScriptedHuman(session, human, new CommandLogWriter(text, header)).PlayTurns(turns);
        WriteText(logPath, text.ToString());
        if (outPath != null) WriteSave(game, outPath, zip);
        output.WriteLine($"recorded human={human} turns={Inv(played)} commands={session.LastSeq.ToString(CultureInfo.InvariantCulture)} {Summary(game)} hash={ReplayVerifier.FullHash(game).ToString("x16", CultureInfo.InvariantCulture)}");
    }

    private static int Replay(CliArgs cli, TextWriter output)
    {
        var logPath = cli.Require("log");
        var hashes = cli.Flag("hashes");
        LoadRulesets(cli);
        cli.RejectUnknown();
        if (!File.Exists(logPath)) throw new IOException($"command log '{logPath}' does not exist");
        var log = CommandLogReader.Read(File.ReadAllText(logPath));
        if (hashes)
            foreach (var checkpoint in log.Lines.OfType<CommandLogLine.Checkpoint>())
                output.WriteLine($"checkpoint seq={checkpoint.Seq.ToString(CultureInfo.InvariantCulture)} turn={Inv(checkpoint.Turn)} {checkpoint.FullHash.ToString("x16", CultureInfo.InvariantCulture)}");
        var result = ReplayVerifier.Verify(log);
        var final = result.FinalHash.ToString("x16", CultureInfo.InvariantCulture);
        if (!result.Matches)
        {
            output.WriteLine($"replay DIVERGED at seq={result.DivergedAtSeq?.ToString(CultureInfo.InvariantCulture)} after {Inv(result.CheckpointsVerified)} verified checkpoints, hash={final}");
            return ExitFailure;
        }
        output.WriteLine($"replay ok checkpoints={Inv(result.CheckpointsVerified)} commands={result.LastSeq.ToString(CultureInfo.InvariantCulture)} hash={final}");
        return ExitOk;
    }

    private static int Submit(CliArgs cli, TextWriter output)
    {
        var commandsPath = cli.Require("commands");
        var actorOption = cli.Get("actor");
        var outPath = cli.Get("out");
        var zip = cli.Flag("zip");
        var game = LoadGame(cli);
        cli.RejectUnknown();
        if (!File.Exists(commandsPath)) throw new IOException($"commands file '{commandsPath}' does not exist");
        var actor = actorOption ?? game.Civilizations.FirstOrDefault(it => it.PlayerType == PlayerType.Human)?.CivId
            ?? throw new UsageException("the game has no human civ; pass --actor CIV");
        var session = new GameSession(game) { Clock = ScriptedHuman.DeterministicClock(game.CurrentTurnStartTime + TurnMillis) };
        var number = 0;
        foreach (var text in File.ReadLines(commandsPath))
        {
            number++;
            if (text.Trim().Length == 0) continue;
            CommandEnvelope envelope;
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                var type = root.GetProperty("type").GetString() ?? throw new FormatException("type is null");
                var payload = root.TryGetProperty("payload", out var found) ? found.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
                envelope = new CommandEnvelope(0, type, actor, payload);
            }
            catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
            {
                throw new IOException($"line {Inv(number)} of '{commandsPath}' is not a command: {e.Message}");
            }
            var result = session.Submit(envelope);
            if (!result.Accepted)
            {
                output.WriteLine($"line {Inv(number)} {envelope.Type} refused: {result.Validation.Code} {result.Validation.Message}");
                return ExitFailure;
            }
            output.WriteLine($"line {Inv(number)} {envelope.Type} accepted");
        }
        if (outPath != null) WriteSave(game, outPath, zip);
        output.WriteLine($"{Summary(game)} hash={HashOf(game)}");
        return ExitOk;
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
            if (game.Civilizations.Any(it => it.IsAlive() && it.IsMajorCiv() && it.VictoryManager.HasWon()))
            {
                output.WriteLine($"the game is won on turn {Inv(game.Turns)}; stopping");
                break;
            }
            var before = game.Turns;
            game.SimulateUntilWin = true;
            game.SimulateMaxTurns = before + 1;
            game.NextTurn(game.CurrentTurnStartTime + TurnMillis);
            if (game.Turns == before)
            {
                // NextTurn stops short when a civ wins or nobody is left to play: the turn counter does not move.
                output.WriteLine($"the game ended on turn {Inv(game.Turns)}; stopping");
                break;
            }
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
