using Unciv.Cli;
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
}
