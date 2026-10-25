using System.Text;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Determinism;

// T160: replays the games the real Kotlin code played (csharp/migration/golden/KotlinGoldenExporter.kt, run by export.sh) and compares a
// canonical digest of the resource-free map and of the starting civ/unit state. kotlin-golden.txt is the Kotlin output, verbatim.
// Those two games cannot compare resources/Oasis: Kotlin shuffles with an unseeded Random (StrategicBonusResourcePlacementLogic, oasis
// placement) and the C# port seeds those shuffles; see the T160 notes.
// T216: the per-turn tests sidestep that. The exporter generated a map once and saved it (golden-map-<seed>.map, written by Kotlin), then
// played 30 turns from that file with the real Kotlin GameInfo.nextTurn, writing a digest of the whole map (resources and ownership
// included) plus civs/cities/units per turn. The C# game starts from the same file with the same gameId and must match every turn.
[Collection("RulesetCache")]
public sealed class KotlinGoldenTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public KotlinGoldenTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private static string Fnv(string text)
    {
        unchecked
        {
            var h = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(text))
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            return h.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // Must stay byte-identical to mapDigest/civDigest in KotlinGoldenExporter.kt.
    private static string MapDigest(GameInfo game)
    {
        var sb = new StringBuilder();
        foreach (var t in game.TileMap.TileList.OrderBy(it => it.Position.X).ThenBy(it => it.Position.Y))
            sb.Append($"T {t.Position.X},{t.Position.Y} {t.BaseTerrain}|{string.Join('+', t.TerrainFeatures.Where(it => it != "Oasis").Order(StringComparer.Ordinal))}|{t.Improvement ?? ""}|{t.NaturalWonder ?? ""}|{t.RoadStatus}\n");
        return sb.ToString();
    }

    private static string CivDigest(GameInfo game)
    {
        var sb = new StringBuilder();
        sb.Append($"turn {game.Turns}\n");
        foreach (var civ in game.Civilizations)
        {
            sb.Append($"C {civ.CivName} gold={civ.Gold} cities={civ.Cities.Count} units={civ.Units.GetCivUnitsSize()} techs={civ.Tech.TechsResearched.Count}\n");
            foreach (var c in civ.Cities.OrderBy(it => it.Name, StringComparer.Ordinal))
                sb.Append($"  city {c.Name} {c.Location.X},{c.Location.Y} pop={c.Population.Population} tiles={c.Tiles.Count}\n");
            foreach (var u in civ.Units.GetCivUnits().Select(it => $"  unit {it.Name} {it.GetTile().Position.X},{it.GetTile().Position.Y}").Order(StringComparer.Ordinal))
                sb.Append(u).Append('\n');
        }
        return sb.ToString();
    }

    // Must stay byte-identical to fullDigest in KotlinGoldenExporter.kt.
    private static string FullDigest(GameInfo game)
    {
        var sb = new StringBuilder();
        foreach (var t in game.TileMap.TileList.OrderBy(it => it.Position.X).ThenBy(it => it.Position.Y))
            sb.Append($"T {t.Position.X},{t.Position.Y} {t.BaseTerrain}|{string.Join('+', t.TerrainFeatures.Order(StringComparer.Ordinal))}|{t.Resource ?? ""}|{t.Improvement ?? ""}|{t.RoadStatus}|{t.GetCity()?.Civ.CivName ?? ""}\n");
        return sb.ToString() + CivDigest(game);
    }

    private static string DebugRanks(GameInfo game)
    {
        var sb = new StringBuilder();
        foreach (var civ in game.Civilizations)
            foreach (var u in civ.Units.GetCivUnits())
                sb.Append($"#DBG U {civ.CivName} {u.Name} {u.GetTile().Position.X},{u.GetTile().Position.Y} id={u.Id} mov={u.CurrentMovement.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)} hp={u.Health} action={u.Action ?? "null"}\n");
        foreach (var civ in game.Civilizations)
            foreach (var c in civ.Cities)
            {
                sb.Append($"#DBG {c.Name} techs=[{string.Join(", ", civ.Tech.TechsResearched.Order(StringComparer.Ordinal))}]\n");
                foreach (var t in c.Expansion.GetChoosableTiles().ToList())
                    sb.Append($"#DBG  {t.Position.X},{t.Position.Y} rank={c.Expansion.RankTileForExpansion(t)} stats={t.Stats.GetTileStats(c, civ)} see={(civ.CanSeeResource(t.TileResource) ? "true" : "false")}\n");
            }
        return sb.ToString();
    }

    private sealed class DiskFile(string path) : IUncivFileHandle
    {
        public string Name => Path.GetFileName(path);
        public string NameWithoutExtension => Path.GetFileNameWithoutExtension(path);
        public bool IsInternal => false;
        public bool Exists => File.Exists(path);
        public bool IsDirectory => false;
        public long Length => new FileInfo(path).Length;
        public long LastModified => 0;
        public IUncivFileHandle Parent => throw new NotSupportedException();
        public IUncivFileHandle Child(string name) => throw new NotSupportedException();
        public IReadOnlyList<IUncivFileHandle> List() => [];
        public void MakeDirs() => throw new NotSupportedException();
        public bool Delete() => throw new NotSupportedException();
        public Stream Read() => File.OpenRead(path);
        public Stream Write(bool append) => throw new NotSupportedException();
    }

    private sealed record SavedGolden(int MapSeed, string GameId, string MapFile, List<(int Turn, string Digest)> Turns);

    private static List<SavedGolden> LoadSavedGolden()
    {
        var result = new List<SavedGolden>();
        var path = Path.Combine(AppContext.BaseDirectory, "Determinism", "kotlin-golden.txt");
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split(' ');
            string Field(string name) => parts.Single(it => it.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..];
            if (parts[0] == "saved")
                result.Add(new SavedGolden(int.Parse(Field("seed"), System.Globalization.CultureInfo.InvariantCulture), Field("gameId"), Field("file"), []));
            else if (parts[0] == "turn")
                result.Single(it => it.MapSeed == int.Parse(Field("seed"), System.Globalization.CultureInfo.InvariantCulture))
                    .Turns.Add((int.Parse(Field("n"), System.Globalization.CultureInfo.InvariantCulture), Field("digest")));
        }
        return result;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void PerTurnStateFromSavedMapMatchesKotlin(int mapSeed)
    {
        var golden = LoadSavedGolden().Single(it => it.MapSeed == mapSeed);
        var mapPath = Path.Combine(AppContext.BaseDirectory, "Determinism", golden.MapFile);
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    UseCiv5Ai = false,   // T235: the golden values come from the Kotlin AI
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = new[] { "Babylon", "Greece", "Rome" }.Select((it, i) => new Player(it, i == 0 ? PlayerType.Human : PlayerType.AI)).ToList(),
                },
                new MapParameters { Name = "golden" + mapSeed, Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = mapSeed, MapSize = MapSize.Tiny })
            { MapFile = golden.MapFile },
            1L, 1000L, name => new DiskFile(mapPath), golden.GameId);
        game.GameId = "golden-" + mapSeed; // the exporter pins the id after the start, see KotlinGoldenExporter.exportSavedMapTurns
        Assert.NotEmpty(golden.Turns);
        var firstDivergence = FirstDivergentTurn.GetValueOrDefault(mapSeed, int.MaxValue);
        foreach (var (turn, digest) in golden.Turns)
        {
            if (turn > 0) game.NextTurn(2000L);
            Assert.Equal(turn, game.Turns);
            DumpIfAsked(mapSeed, "turn " + turn, FullDigest(game) + DebugRanks(game));
            var matches = digest == Fnv(FullDigest(game));
            if (turn < firstDivergence)
                Assert.True(matches, $"seed {mapSeed} turn {turn} diverges from Kotlin");
            else if (turn == firstDivergence)
                Assert.False(matches, $"seed {mapSeed} turn {turn} now matches Kotlin: raise FirstDivergentTurn (or drop the entry)");
        }
    }

    // T231: the first turn whose state differs from Kotlin's, per map seed (absent = all 30 golden turns match). T216 found barbarian "wander"
    // diverging at seed 3 turn 19 / seed 11 turn 16 because T208 re-sorted unit lists into tile order; unit lists are now kept in creation
    // (MapUnit.Id) order on both load and refresh (deliberate deviation, see UnitManager.SortById), which matches a live Kotlin game.
    // Seed 3 turn 23 still differs: Kotlin itself is not reproducible there (unseeded randomness, re-running export.sh gives different digests
    // for seed 3 turns 23-24 and seed 11 turn 24), so later turns are deliberately not asserted.
    private static readonly Dictionary<int, int> FirstDivergentTurn = new() { [3] = 23 };

    private sealed record Golden(int MapSeed, string GameId, string Map, string Start);

    private static List<Golden> LoadGolden()
    {
        var result = new List<Golden>();
        var path = Path.Combine(AppContext.BaseDirectory, "Determinism", "kotlin-golden.txt");
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split(' ');
            if (parts[0] == "game")
            {
                string Field(string name) => parts.Single(it => it.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..];
                result.Add(new Golden(int.Parse(Field("seed"), System.Globalization.CultureInfo.InvariantCulture), Field("gameId"), Field("map"), Field("start")));
            }
        }
        return result;
    }

    private GameInfo NewGame(int mapSeed, string gameId) =>
        GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = new[] { "Babylon", "Greece", "Rome" }.Select((it, i) => new Player(it, i == 0 ? PlayerType.Human : PlayerType.AI)).ToList(),
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = mapSeed, MapSize = MapSize.Tiny }),
            1L, 1000L, gameId: gameId);

    public static TheoryData<int> Seeds => new() { 3, 11 };

    [Theory]
    [MemberData(nameof(Seeds))]
    public void MapAndStartingStateMatchKotlin(int mapSeed)
    {
        var golden = LoadGolden().Single(it => it.MapSeed == mapSeed);
        var game = NewGame(mapSeed, golden.GameId);
        DumpIfAsked(mapSeed, "start", MapDigest(game) + CivDigest(game));
        Assert.Equal(golden.Map, Fnv(MapDigest(game)));
        Assert.Equal(golden.Start, Fnv(CivDigest(game)));
    }

    // Debug aid: GOLDEN_DUMP=<file> writes the full text so it can be diffed against the exporter's dump.
    private static void DumpIfAsked(int mapSeed, string label, string text)
    {
        var path = Environment.GetEnvironmentVariable("GOLDEN_DUMP");
        if (!string.IsNullOrEmpty(path)) File.AppendAllText(path, $"## seed {mapSeed} {label}\n{text}");
    }
}
