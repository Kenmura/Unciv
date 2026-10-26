using System.Globalization;
using Unciv.Commands;
using Unciv.Commands.Log;
using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Net.Identity;
using Unciv.Net.Visibility;
using Unciv.Utils;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Utils;

// T230: the host secret mixed into every state-based seed. No secret = Kotlin behaviour; same secret = same rolls and a working replay;
// a different secret = different rolls; the secret never reaches a save, a snapshot or an exported log header.
[Collection("RulesetCache")]
public sealed class HostSecretRandomTests
{
    private const long Secret = 0x1234_5678_9ABC_DEF0L;
    private readonly TestGame game = new();
    private readonly Civilization attackerCiv;
    private readonly MapUnitCombatant attacker;

    public HostSecretRandomTests()
    {
        game.MakeHexagonalMap(4);
        attackerCiv = game.AddCiv(isPlayer: true);
        var other = game.AddCiv();
        attacker = new MapUnitCombatant(game.AddUnit("Warrior", attackerCiv, game.GetTile(1, 1)));
        game.AddUnit("Warrior", other, game.GetTile(0, 1));
    }

    private static List<int> Draws(GameRandom rng) => Enumerable.Range(0, 8).Select(_ => rng.NextInt(1000000)).ToList();

    [Fact]
    public void SaltedSeedIsIdentityWithoutSecretAndDeterministicWithOne()
    {
        Assert.Equal(12345L, Randomness.SaltedSeed(12345L, 0));
        Assert.Equal(Randomness.SaltedSeed(12345L, Secret), Randomness.SaltedSeed(12345L, Secret));
        Assert.NotEqual(12345L, Randomness.SaltedSeed(12345L, Secret));
        Assert.NotEqual(Randomness.SaltedSeed(12345L, Secret), Randomness.SaltedSeed(12345L, Secret + 1));
        Assert.NotEqual(Randomness.SaltedSeed(12345L, Secret), Randomness.SaltedSeed(12346L, Secret));
        // never a degenerate seed, whatever the inputs
        foreach (var seed in new[] { 0L, -1L, long.MinValue, long.MaxValue }) Assert.NotNull(Randomness.Salted(seed, Secret));
        Assert.Equal(Draws(new GameRandom(77)), Draws(Randomness.Salted(77, 0)));
    }

    [Fact]
    public void GameContextRandomIsUnchangedWithoutSecretAndSecretDependentWithOne()
    {
        var context = new GameContext(gameInfo: game.GameInfo);
        var plain = Draws(context.StateBasedRandom("T230"));
        Assert.Equal(Draws(Randomness.StateBasedRandom("T230", 31, context.GetHashCode())), plain); // Kotlin parity

        game.GameInfo.RandomSalt = Secret;
        var salted = Draws(context.StateBasedRandom("T230"));
        Assert.NotEqual(plain, salted);
        Assert.Equal(salted, Draws(context.StateBasedRandom("T230")));
        game.GameInfo.RandomSalt = Secret + 1;
        Assert.NotEqual(salted, Draws(context.StateBasedRandom("T230")));
    }

    [Fact]
    public void CombatRollDependsOnTheSecretButNotOnWhoAsks()
    {
        float plain = BattleDamage.GetRandomness(attacker);
        Assert.Equal(game.GameInfo.SeededRandom((long)game.GameInfo.Turns * attacker.GetTile().Position.ToVector2().GetHashCode()).NextFloat(), plain);
        game.GameInfo.RandomSalt = Secret;
        float salted = BattleDamage.GetRandomness(attacker);
        Assert.NotEqual(plain, salted);
        Assert.Equal(salted, BattleDamage.GetRandomness(attacker));
        game.GameInfo.RandomSalt = Secret + 1;
        Assert.NotEqual(salted, BattleDamage.GetRandomness(attacker));
    }

    [Fact]
    public void SeededRandomMatchesTheDirectSeedWithoutSecret()
    {
        Assert.Equal(Draws(new GameRandom(-550)), Draws(game.GameInfo.SeededRandom(-550)));
        game.GameInfo.RandomSalt = Secret;
        Assert.NotEqual(Draws(new GameRandom(-550)), Draws(game.GameInfo.SeededRandom(-550)));
    }

    [Fact]
    public void ConditionalChanceRollDependsOnTheSecret()
    {
        var unique = new UniqueObject("[+1 Gold] <with [50]% chance>", UniqueTarget.Building, "Library");
        var state = new GameContext(civInfo: attackerCiv);
        // the roll only decides ConditionalChance; 50% is true for some secrets and false for others, so scan a few and require both outcomes
        var results = new HashSet<bool>();
        for (long salt = 1; salt <= 40; salt++)
        {
            game.GameInfo.RandomSalt = salt;
            var conditional = new UniqueObject("with [50]% chance");
            results.Add(Conditionals.ConditionalApplies(unique, conditional, state));
        }
        Assert.Equal(2, results.Count);
        game.GameInfo.RandomSalt = 5;
        var first = Conditionals.ConditionalApplies(unique, new UniqueObject("with [50]% chance"), state);
        Assert.Equal(first, Conditionals.ConditionalApplies(unique, new UniqueObject("with [50]% chance"), state));
    }

    [Fact]
    public void TheSecretIsNeitherSerializedNorSnapshottedAndCloneKeepsIt()
    {
        var civ = attackerCiv;
        civ.Cache.UpdateState();
        game.GameInfo.CurrentPlayerCiv = civ;
        game.GameInfo.RandomSalt = Secret;
        var marker = Secret.ToString(CultureInfo.InvariantCulture);
        var saved = UncivFiles.GameInfoToString(game.GameInfo, forceZip: false);
        Assert.DoesNotContain(marker, saved, StringComparison.Ordinal);
        Assert.DoesNotContain("andomSalt", saved, StringComparison.Ordinal);
        Assert.Equal(ReplayVerifier.FullHash(game.GameInfo), ReplayVerifier.FullHash(HostSecret.ApplyTo(game.GameInfo.Clone(), 0)));

        var snapshot = SnapshotBuilder.ToCanonicalJson(SnapshotBuilder.Build(game.GameInfo, civ, 1, new TurnStatusDto { Ended = [], Phase = "playing" }));
        Assert.DoesNotContain(marker, snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("andomSalt", snapshot, StringComparison.Ordinal);
        Assert.Equal(Secret, game.GameInfo.Clone().RandomSalt);

        // no view member exposes it either
        var viewMembers = typeof(Unciv.View.GameView).GetProperties().Select(it => it.Name);
        Assert.DoesNotContain(viewMembers, name => name.Contains("Salt", StringComparison.Ordinal) || name.Contains("Secret", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratedSecretsAreNonZeroAndSkipAZeroDraw()
    {
        Assert.NotEqual(0L, HostSecret.Generate());
        Assert.NotEqual(HostSecret.Generate(), HostSecret.Generate());
        var draws = new ZeroThenOne();
        Assert.Equal(1L, HostSecret.Generate(draws));
        Assert.Equal(2, draws.Calls);
    }

    private sealed class ZeroThenOne : ISecureRandom
    {
        public int Calls;
        public void Fill(Span<byte> buffer)
        {
            buffer.Clear();
            if (++Calls > 1) buffer[0] = 1;
        }
    }

    // Audit (T230): every seeded generator in the engine goes through the host secret. A new `new GameRandom(` in game logic would let a client predict
    // its rolls, so it has to be a deliberate, listed exception (map generation and the game starter run before the secret exists; holidays use the calendar).
    // T230 review: also catches target-typed `GameRandom x = new(seed)` / `=> new(seed)` (CityConquestFunctions slipped through the first version of the scan)
    // and a direct Randomness.StateBasedRandom(...) / Salted(.., 0) bypass.
    private static readonly System.Text.RegularExpressions.Regex UnsaltedRandom = new(
        @"new\s+GameRandom\s*\(|GameRandom\??\s+\w+\s*=\s*new\s*\(|GameRandom\??\s+\w+\s*=>\s*new\s*\(|Randomness\.StateBasedRandom\(",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    [Fact]
    public void NoGameLogicBuildsAnUnsaltedGameRandom()
    {
        var root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "Unciv.Core", "Logic"))) root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("Unciv.Core not found");
        string[] allowed = ["Utils/GameRandom.cs", "Utils/Randomness.cs", "Logic/GameStarter.cs", "Logic/HolidayDates.cs", "Logic/Simulation/Simulation.cs", "Logic/Map/MapGenerator/MapGenerationRandomness.cs", "Models/Ruleset/Unique/GameContext.cs"];
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "Unciv.Core"), "*.cs", SearchOption.AllDirectories)
            .Where(it => !it.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(it => (Path: Path.GetRelativePath(Path.Combine(root, "Unciv.Core"), it).Replace('\\', '/'), Text: File.ReadAllText(it)))
            .Where(it => it.Text.Split('\n').Any(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && UnsaltedRandom.IsMatch(line)))
            .Select(it => it.Path)
            .Where(it => !allowed.Contains(it))
            .ToList();
        Assert.Empty(offenders);
    }

    // ---- replay -------------------------------------------------------------------------------------------------------------------

    private const string Human = "Babylon";

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private static (CommandLog Log, ulong FinalHash) PlayHosted(long hostSecret, int turns)
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        try
        {
            var ruleset = RulesetCache.GetVanillaRuleset();
            var setup = new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = new[] { Human, "Greece", "Rome" }.Select((it, i) => new Player(it, i == 0 ? PlayerType.Human : PlayerType.AI)).ToList(),
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny });
            var header = CommandLogHeader.Create(setup, 1L, 1000L, hostSecret: hostSecret);
            var started = HostSecret.ApplyTo(GameStarter.StartNewGame(setup, 1L, 1000L), hostSecret);
            var session = new GameSession(started) { Clock = ScriptedHuman.DeterministicClock(2000L) };
            var text = new StringWriter(CultureInfo.InvariantCulture);
            var player = new ScriptedHuman(session, Human, new CommandLogWriter(text, header));
            Assert.Equal(turns, player.PlayTurns(turns));
            var log = CommandLogReader.Read(text.ToString());
            return (log, ReplayVerifier.FullHash(session.Game));
        }
        finally { RulesetCache.Clear(); }
    }

    [Fact]
    public void ReplayWithTheHeaderSecretMatchesAndAnotherSecretPlaysAnotherGame()
    {
        const int turns = 40;
        var (hosted, hash) = PlayHosted(Secret, turns);
        Assert.Equal(Secret, hosted.Header.HostSecret);
        RulesetCache.Clear();
        LoadRulesets();
        try
        {
            var result = ReplayVerifier.Verify(hosted);
            Assert.True(result.Matches, $"diverged at {result.DivergedAtSeq}");
            Assert.Equal(hash, result.FinalHash);
            Assert.Equal(Secret, result.Game.RandomSalt);

            // a snapshot (which carries no secret) plus the tail gets the secret from the header
            var tailResult = ReplayVerifier.VerifyTail(UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(ReplayVerifier.StartGame(hosted.Header), forceZip: false)), 0, hosted);
            Assert.True(tailResult.Matches);
            Assert.Equal(Secret, tailResult.Game.RandomSalt);
        }
        finally { RulesetCache.Clear(); }

        var (_, otherHash) = PlayHosted(Secret + 1, turns);
        var (_, plainHash) = PlayHosted(0, turns);
        Assert.NotEqual(hash, otherHash);
        Assert.NotEqual(hash, plainHash);
        Assert.NotEqual(otherHash, plainHash);
        // the same secret twice is the same game
        Assert.Equal(hash, PlayHosted(Secret, turns).FinalHash);
    }

    [Fact]
    public void ReplayWithoutTheSecretDivergesAndTheHeaderRoundTripsIt()
    {
        var (hosted, _) = PlayHosted(Secret, 40);
        var text = UncivJsonLine(hosted.Header);
        Assert.Contains("\"hostSecret\":", text, StringComparison.Ordinal);
        Assert.Equal(Secret, CommandLogReader.Read(text + "\n").Header.HostSecret);

        var stripped = hosted.Header.WithoutSecret();
        Assert.Equal(0, stripped.HostSecret);
        Assert.DoesNotContain("hostSecret", UncivJsonLine(stripped), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret.ToString(CultureInfo.InvariantCulture), UncivJsonLine(stripped), StringComparison.Ordinal);

        LoadRulesets();
        try
        {
            var result = ReplayVerifier.Verify(new CommandLog(stripped, hosted.Lines));
            Assert.False(result.Matches);
        }
        catch (CommandDesyncException) { /* a command that no longer validates is divergence too */ }
        finally { RulesetCache.Clear(); }
    }

    private static string UncivJsonLine(CommandLogHeader header) => Unciv.Json.UncivJson.Serialize(header);

    private static void LoadRulesets()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
    }
}
