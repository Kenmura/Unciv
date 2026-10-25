using Unciv.Cli;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;

namespace Unciv.Core.Tests.Cli;

// T168: the headless runner. Argument parsing, then a short real run (new -> play -> save/load -> hash/info) in a temp folder.
[Collection("RulesetCache")]
public sealed class CliTests : IDisposable
{
    private static readonly IReadOnlySet<string> Flags = new HashSet<string> { "zip", "hashes" };
    private readonly string dir = Directory.CreateTempSubdirectory("unciv-cli-").FullName;

    public void Dispose()
    {
        RulesetCache.Clear();
        Directory.Delete(dir, recursive: true);
    }

    private string Path(string name) => System.IO.Path.Combine(dir, name);

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = CliRunner.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ---- argument parsing

    [Fact]
    public void ParsesCommandOptionsAndFlags()
    {
        var args = new CliArgs(["play", "--in", "a.json", "--turns", "5", "--hashes"], Flags);
        Assert.Equal("play", args.Command);
        Assert.Equal("a.json", args.Require("in"));
        Assert.Equal(5, args.GetInt("turns", 0));
        Assert.True(args.Flag("hashes"));
        Assert.False(args.Flag("zip"));
        Assert.Null(args.Get("out"));
        Assert.Equal(7, args.GetInt("missing", 7));
        Assert.Equal(-3L, new CliArgs(["x", "--seed", "-3"], Flags).GetLong("seed", 0));
        Assert.Equal(9L, new CliArgs(["x"], Flags).GetLong("seed", 9));
        args.RejectUnknown();
    }

    [Theory]
    [InlineData("missing command")]
    [InlineData("new", "stray")]
    [InlineData("new", "--")]
    [InlineData("new", "--out")]
    [InlineData("new", "--out", "a", "--out", "b")]
    public void RejectsMalformedCommandLines(params string[] args)
    {
        var list = args[0] == "missing command" ? [] : args;
        Assert.Throws<UsageException>(() => new CliArgs(list, Flags));
    }

    [Fact]
    public void RejectsBadNumbersAndUnknownOptions()
    {
        Assert.Throws<UsageException>(() => new CliArgs(["x", "--n", "1.5"], Flags).GetInt("n", 0));
        Assert.Throws<UsageException>(() => new CliArgs(["x", "--n", "99999999999"], Flags).GetInt("n", 0));
        Assert.Throws<UsageException>(() => new CliArgs(["x", "--n", "9"], Flags).GetInt("n", 0, 0, 5));
        Assert.Throws<UsageException>(() => new CliArgs(["x", "--n", "abc"], Flags).GetLong("n", 0));
        Assert.Throws<UsageException>(() => new CliArgs(["x"], Flags).Require("out"));
        var args = new CliArgs(["x", "--typo", "1", "--known", "2"], Flags);
        args.Get("known");
        var e = Assert.Throws<UsageException>(args.RejectUnknown);
        Assert.Contains("--typo", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NumbersAreParsedWithTheInvariantCulture()
    {
        Assert.Equal(1234, new CliArgs(["x", "--n", "1234"], Flags).GetInt("n", 0));
        Assert.Throws<UsageException>(() => new CliArgs(["x", "--n", "1,234"], Flags).GetInt("n", 0));
    }

    [Fact]
    public void UsageErrorsExitWithCodeTwoAndHelpWithZero()
    {
        Assert.Equal(CliRunner.ExitOk, Run("--help").Code);
        Assert.Contains("usage: unciv-cli", Run("help").Out, StringComparison.Ordinal);
        Assert.Equal(CliRunner.ExitUsage, Run().Code);
        var bad = Run("frobnicate");
        Assert.Equal(CliRunner.ExitUsage, bad.Code);
        Assert.Contains("unknown command 'frobnicate'", bad.Err, StringComparison.Ordinal);
        Assert.Equal(CliRunner.ExitUsage, Run("new").Code); // --out missing
        Assert.Equal(CliRunner.ExitUsage, Run("play", "--in", Path("none.json")).Code); // --turns missing
    }

    [Theory]
    [InlineData("--nations", "Atlantis")]
    [InlineData("--nations", "Rome,Rome")]
    [InlineData("--nations", "City States")]
    [InlineData("--nations", ",")]
    [InlineData("--map-size", "Gigantic")]
    [InlineData("--map-type", "Cubic")]
    [InlineData("--shape", "Round")]
    [InlineData("--difficulty", "Impossible")]
    [InlineData("--speed", "Warp")]
    [InlineData("--city-states", "-1")]
    [InlineData("--bogus", "1")]
    public void NewRejectsBadChoices(string option, string value)
    {
        var result = Run("new", "--out", Path("g.json"), option, value);
        Assert.Equal(CliRunner.ExitUsage, result.Code);
        Assert.False(File.Exists(Path("g.json")));
    }

    // ---- end to end

    [Fact]
    public void NewPlaySaveLoadHashAndInfoRunEndToEnd()
    {
        var created = Run("new", "--out", Path("g.json"), "--seed", "9", "--map-seed", "5", "--city-states", "1", "--nations", "Babylon,Greece");
        Assert.Equal(CliRunner.ExitOk, created.Code);
        Assert.True(File.Exists(Path("g.json")));
        var startHash = Lines(created.Out).Single().Split("hash=")[1];

        Assert.Equal(startHash, Lines(Run("hash", "--in", Path("g.json")).Out).Single());
        Assert.Equal(CliRunner.ExitOk, Run("load", "--in", Path("g.json")).Code);

        var played = Run("play", "--in", Path("g.json"), "--turns", "3", "--hashes", "--out", Path("g3.json"));
        Assert.Equal(CliRunner.ExitOk, played.Code);
        var turnLines = Lines(played.Out).Where(it => it.StartsWith("turn ", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, turnLines.Count); // the start and each played turn
        Assert.Equal(4, turnLines.Select(it => it.Split(' ')[2]).Distinct().Count()); // the game advances
        Assert.Equal(startHash, turnLines[0].Split(' ')[2]);
        var endHash = turnLines[^1].Split(' ')[2];
        Assert.Equal(endHash, Lines(Run("hash", "--in", Path("g3.json")).Out).Single()); // the saved game is the played game

        // save --zip is lossless: the hash is of the state, not of the file encoding
        Assert.Equal(CliRunner.ExitOk, Run("save", "--in", Path("g3.json"), "--out", Path("z.json"), "--zip").Code);
        Assert.NotEqual(File.ReadAllText(Path("g3.json")), File.ReadAllText(Path("z.json")));
        Assert.Equal(endHash, Lines(Run("hash", "--in", Path("z.json")).Out).Single());

        // playing on from the save equals playing straight through
        var straight = Run("play", "--in", Path("g.json"), "--turns", "5", "--hashes");
        var resumed = Run("play", "--in", Path("z.json"), "--turns", "2", "--hashes");
        Assert.Equal(Lines(straight.Out).Last(), Lines(resumed.Out).Last());

        var info = Lines(Run("info", "--in", Path("g3.json")).Out);
        Assert.Contains("turn=3", info[0], StringComparison.Ordinal);
        Assert.Contains(info, it => it.StartsWith("civ Babylon Human alive", StringComparison.Ordinal));
        Assert.Contains(info, it => it.StartsWith("civ Greece AI alive", StringComparison.Ordinal));
    }

    [Fact]
    public void SameSeedGivesTheSameGameAndAnotherSeedDoesNot()
    {
        string Start(string name, string seed) => Lines(Run("new", "--out", Path(name), "--seed", seed, "--city-states", "0").Out).Single();
        var first = Start("a.json", "4");
        Assert.Equal(first, Start("b.json", "4"));
        Assert.Equal(File.ReadAllText(Path("a.json")), File.ReadAllText(Path("b.json")));
        Assert.NotEqual(first, Start("c.json", "5"));
    }

    [Fact]
    public void FailuresWhileRunningExitWithCodeOne()
    {
        Run("new", "--out", Path("g.json"), "--city-states", "0");
        var missing = Run("hash", "--in", Path("missing.json"));
        Assert.Equal(CliRunner.ExitFailure, missing.Code);
        Assert.Contains("does not exist", missing.Err, StringComparison.Ordinal);
        File.WriteAllText(Path("junk.json"), "not a game");
        Assert.Equal(CliRunner.ExitFailure, Run("load", "--in", Path("junk.json")).Code);
        Assert.NotEqual(CliRunner.ExitOk, Run("load", "--in", Path("g.json"), "--jsons", Path("nowhere")).Code);
    }

    [Fact]
    public void OptionValuesCannotLookLikeOptionsOrHelp()
    {
        Assert.Throws<UsageException>(() => new CliArgs(["new", "--out", "--zip"], Flags));
        var result = Run("new", "--out", "help");
        try
        {
            Assert.Equal(CliRunner.ExitOk, result.Code);
            Assert.DoesNotContain("usage:", result.Out, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete("help");
        }
    }

    [Fact]
    public void PlayStopsWhenTheGameHasEnded()
    {
        Run("new", "--out", Path("g.json"), "--nations", "Babylon", "--city-states", "0");
        var first = Run("play", "--in", Path("g.json"), "--turns", "3", "--hashes", "--out", Path("g.json"));
        Assert.Equal(CliRunner.ExitOk, first.Code);
        Assert.Contains("stopping", first.Out, StringComparison.Ordinal);
        Assert.True(File.Exists(Path("g.json")) && !File.Exists(Path("g.json.tmp")));
        var again = Run("play", "--in", Path("g.json"), "--turns", "3", "--hashes");
        Assert.Contains("stopping", again.Out, StringComparison.Ordinal);
        Assert.Single(Lines(again.Out), it => it.StartsWith("turn ", StringComparison.Ordinal));
    }

    [Fact]
    public void BadFoldersGiveClearErrors()
    {
        Run("new", "--out", Path("g.json"), "--city-states", "0");
        Assert.Contains("folder", Run("hash", "--in", dir).Err, StringComparison.Ordinal);
        Assert.Equal(CliRunner.ExitUsage, Run("hash", "--in", Path("g.json"), "--jsons", dir).Code);
    }

    [Fact]
    public void ProgramMainDelegatesToTheRunner()
    {
        var saved = Console.Out;
        try
        {
            Console.SetOut(TextWriter.Null);
            Assert.Equal(CliRunner.ExitOk, Program.Main(["help"]));
        }
        finally
        {
            Console.SetOut(saved);
        }
    }

    [Fact]
    public void PlayingZeroTurnsKeepsTheState()
    {
        var created = Run("new", "--out", Path("g.json"), "--city-states", "0");
        var hash = Lines(created.Out).Single().Split("hash=")[1];
        var played = Run("play", "--in", Path("g.json"), "--turns", "0", "--out", Path("h.json"));
        Assert.Equal(CliRunner.ExitOk, played.Code);
        Assert.EndsWith("hash=" + hash, Lines(played.Out).Single(), StringComparison.Ordinal);
    }

    // ---- T221: record / replay / submit / ruleset, mods and saved maps

    private static string HashOfLine(string line) => line.Split("hash=")[1].Trim();

    [Fact]
    public void RecordedLogReplaysToTheSameHashAndTamperingIsCaught()
    {
        var recorded = Run("record", "--out", Path("g.json"), "--log", Path("l.jsonl"), "--turns", "8", "--seed", "3", "--map-seed", "4", "--city-states", "0");
        Assert.Equal(CliRunner.ExitOk, recorded.Code);
        Assert.Contains("turns=8", recorded.Out, StringComparison.Ordinal);
        Assert.True(File.Exists(Path("g.json")));
        Assert.Equal(CliRunner.ExitOk, Run("load", "--in", Path("g.json")).Code);

        var replayed = Run("replay", "--log", Path("l.jsonl"), "--hashes");
        Assert.Equal(CliRunner.ExitOk, replayed.Code);
        Assert.True(Lines(replayed.Out).Count(it => it.StartsWith("checkpoint ", StringComparison.Ordinal)) >= 8);
        Assert.Contains("replay ok", Lines(replayed.Out)[^1], StringComparison.Ordinal);
        // the replay ends in the recorded state (the recorder prints the same full hash)
        Assert.Equal(HashOfLine(recorded.Out), HashOfLine(Lines(replayed.Out)[^1]));
        Assert.Equal(replayed.Out, Run("replay", "--log", Path("l.jsonl"), "--hashes").Out);

        var lines = File.ReadAllLines(Path("l.jsonl")).ToList();
        var index = lines.FindLastIndex(it => it.StartsWith("{\"checkpoint\"", StringComparison.Ordinal));
        var digit = lines[index].IndexOf("\"fullHash\":\"", StringComparison.Ordinal) + "\"fullHash\":\"".Length + 15; // the last of 16 hex digits
        lines[index] = string.Concat(lines[index].AsSpan(0, digit), lines[index][digit] == '0' ? "1" : "0", lines[index].AsSpan(digit + 1));
        File.WriteAllLines(Path("bad.jsonl"), lines);
        var bad = Run("replay", "--log", Path("bad.jsonl"));
        Assert.Equal(CliRunner.ExitFailure, bad.Code);
        Assert.Contains("DIVERGED", bad.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayFailuresAreReported()
    {
        Assert.Equal(CliRunner.ExitUsage, Run("replay").Code);
        Assert.Equal(CliRunner.ExitFailure, Run("replay", "--log", Path("none.jsonl")).Code);
        File.WriteAllText(Path("junk.jsonl"), "not a log\n");
        Assert.Equal(CliRunner.ExitFailure, Run("replay", "--log", Path("junk.jsonl")).Code);
        Assert.Equal(CliRunner.ExitUsage, Run("record", "--log", Path("l"), "--out", Path("g")).Code); // --turns missing
        Assert.Equal(CliRunner.ExitUsage, Run("record", "--log", Path("l"), "--turns", "1", "--map", Path("m")).Code); // a saved map cannot be replayed
    }

    [Fact]
    public void SubmitAppliesCommandsAsTheHumanAndStopsAtTheFirstRefusal()
    {
        Run("new", "--out", Path("g.json"), "--city-states", "0");
        File.WriteAllText(Path("ok.jsonl"), "{\"type\":\"end-turn\"}\n\n");
        var ok = Run("submit", "--in", Path("g.json"), "--commands", Path("ok.jsonl"), "--out", Path("after.json"));
        Assert.Equal(CliRunner.ExitOk, ok.Code);
        Assert.Contains("line 1 end-turn accepted", ok.Out, StringComparison.Ordinal);
        Assert.NotEqual(Lines(Run("hash", "--in", Path("g.json")).Out).Single(), Lines(Run("hash", "--in", Path("after.json")).Out).Single());

        File.WriteAllText(Path("bad.jsonl"), "{\"type\":\"no-such-command\",\"payload\":{}}\n{\"type\":\"end-turn\"}\n");
        var refused = Run("submit", "--in", Path("g.json"), "--commands", Path("bad.jsonl"), "--out", Path("never.json"));
        Assert.Equal(CliRunner.ExitFailure, refused.Code);
        Assert.Contains("line 1 no-such-command refused", refused.Out, StringComparison.Ordinal);
        Assert.False(File.Exists(Path("never.json")));

        File.WriteAllText(Path("junk.jsonl"), "[1]\n");
        Assert.Equal(CliRunner.ExitFailure, Run("submit", "--in", Path("g.json"), "--commands", Path("junk.jsonl")).Code);
        Assert.Equal(CliRunner.ExitFailure, Run("submit", "--in", Path("g.json"), "--commands", Path("none.jsonl")).Code);
        Assert.Equal(CliRunner.ExitUsage, Run("submit", "--in", Path("g.json")).Code);
    }

    [Fact]
    public void RulesetOptionSelectsTheBaseRuleset()
    {
        var vanilla = Run("new", "--out", Path("v.json"), "--city-states", "0");
        var gnk = Run("new", "--out", Path("k.json"), "--city-states", "0", "--ruleset", "Civ V - Gods & Kings");
        Assert.Equal(CliRunner.ExitOk, gnk.Code);
        Assert.Contains("Gods", File.ReadAllText(Path("k.json")), StringComparison.Ordinal);
        Assert.DoesNotContain("Gods", File.ReadAllText(Path("v.json")), StringComparison.Ordinal);
        Assert.NotEqual(HashOfLine(vanilla.Out), HashOfLine(gnk.Out));
        Assert.Equal(CliRunner.ExitOk, Run("play", "--in", Path("k.json"), "--turns", "2").Code);
        Assert.Equal(CliRunner.ExitUsage, Run("new", "--out", Path("x.json"), "--ruleset", "Nope").Code);
    }

    [Fact]
    public void ModsComeFromTheModsFolder()
    {
        var mods = Directory.CreateDirectory(Path("mods")).FullName;
        Directory.CreateDirectory(System.IO.Path.Combine(mods, "TestMod", "jsons"));
        File.WriteAllText(System.IO.Path.Combine(mods, "TestMod", "jsons", "Nations.json"),
            "[{\"name\":\"Atlantis\",\"leaderName\":\"Poseidon\",\"outerColor\":[0,0,0],\"innerColor\":[255,255,255],\"cities\":[\"Atlantis City\"]}]");

        Assert.Equal(CliRunner.ExitUsage, Run("new", "--out", Path("n.json"), "--mods", "TestMod").Code); // no --mods-dir
        Assert.Equal(CliRunner.ExitUsage, Run("new", "--out", Path("n.json"), "--mods-dir", Path("nowhere")).Code);
        Assert.Equal(CliRunner.ExitUsage, Run("new", "--out", Path("n.json"), "--nations", "Atlantis", "--mods-dir", mods).Code); // mod not enabled
        Assert.Equal(CliRunner.ExitUsage, Run("new", "--out", Path("n.json"), "--mods-dir", mods, "--mods", "TestMod,TestMod").Code);
        Assert.Equal(CliRunner.ExitUsage, Run("new", "--out", Path("n.json"), "--mods-dir", mods, "--mods", "Civ V - Vanilla").Code);

        var created = Run("new", "--out", Path("m.json"), "--city-states", "0", "--mods-dir", mods, "--mods", "TestMod", "--nations", "Atlantis,Rome");
        Assert.True(created.Code == CliRunner.ExitOk, created.Err);
        Assert.Contains("TestMod", File.ReadAllText(Path("m.json")), StringComparison.Ordinal);
        Assert.Equal(CliRunner.ExitOk, Run("play", "--in", Path("m.json"), "--turns", "2", "--mods-dir", mods).Code);

        var recorded = Run("record", "--log", Path("m.jsonl"), "--turns", "3", "--city-states", "0", "--mods-dir", mods, "--mods", "TestMod", "--nations", "Atlantis,Rome");
        Assert.Equal(CliRunner.ExitOk, recorded.Code);
        Assert.Equal(CliRunner.ExitOk, Run("replay", "--log", Path("m.jsonl"), "--mods-dir", mods).Code);
        // without the mod the ruleset differs from the one the log was played with
        Assert.Equal(CliRunner.ExitFailure, Run("replay", "--log", Path("m.jsonl")).Code);
    }

    [Fact]
    public void SavedMapStartsAGameOnThatMap()
    {
        Run("new", "--out", Path("g.json"), "--city-states", "0", "--seed", "2");
        var source = UncivFiles.GameInfoFromString(File.ReadAllText(Path("g.json")));
        foreach (var tile in source.TileMap.Values)
            foreach (var unit in tile.GetUnits().ToList()) unit.RemoveFromTile(); // like a map from the editor: terrain only
        source.TileMap.AssignContinents(TileMap.AssignContinentsMode.Reassign);
        FileConversions.WriteJson(new PathFileHandle(Path("m.map")), source.TileMap, false);

        var created = Run("new", "--out", Path("on-map.json"), "--map", Path("m.map"), "--city-states", "0", "--seed", "8");
        Assert.True(created.Code == CliRunner.ExitOk, created.Err);
        var game = UncivFiles.GameInfoFromString(File.ReadAllText(Path("on-map.json")));
        Assert.Equal(source.TileMap.Values.Count(), game.TileMap.Values.Count());
        Assert.Equal(CliRunner.ExitOk, Run("play", "--in", Path("on-map.json"), "--turns", "2").Code);

        var missing = Run("new", "--out", Path("x.json"), "--map", Path("none.map"));
        Assert.Equal(CliRunner.ExitFailure, missing.Code);
        Assert.False(File.Exists(Path("x.json")));
    }

    [Fact]
    public void PlayingInOneGoEqualsPlayingInPartsOverALongRun()
    {
        Run("new", "--out", Path("g.json"), "--seed", "6", "--map-seed", "2");
        var straight = Lines(Run("play", "--in", Path("g.json"), "--turns", "60", "--hashes").Out);
        var first = Run("play", "--in", Path("g.json"), "--turns", "25", "--out", Path("mid.json"));
        var second = Lines(Run("play", "--in", Path("mid.json"), "--turns", "35", "--hashes").Out);
        Assert.Equal(CliRunner.ExitOk, first.Code);
        Assert.Equal(straight[^1], second[^1]);
        Assert.Equal(straight[25], second[0]);
    }

    [Fact]
    public void PathFileHandleWrapsAHostPath()
    {
        var folder = new PathFileHandle(Path("sub"));
        Assert.False(folder.Exists);
        Assert.Equal(0, folder.Length);
        Assert.Equal(0, folder.LastModified);
        Assert.False(folder.Delete());
        folder.MakeDirs();
        Assert.True(folder.IsDirectory);
        Assert.False(folder.IsInternal);
        var file = folder.Child("a.txt");
        using (var stream = file.Write(append: false)) stream.WriteByte(65);
        using (var stream = file.Write(append: true)) stream.WriteByte(66);
        Assert.Equal("a.txt", file.Name);
        Assert.Equal("a", file.NameWithoutExtension);
        Assert.Equal(2, file.Length);
        Assert.True(file.LastModified > 0);
        using (var stream = file.Read()) Assert.Equal(65, stream.ReadByte());
        Assert.Equal(["a.txt"], folder.List().Select(it => it.Name));
        Assert.Empty(file.List());
        Assert.Equal("sub", file.Parent.Name);
        Assert.True(file.Delete());
        Assert.True(folder.Delete());
        Assert.False(folder.Exists);
    }
}
