using System.Text;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Determinism;

// T217: mod-ruleset variant of KotlinGoldenTests (Testland nation from golden-mod/, games played by csharp/migration/golden/KotlinModGoldenExporter.kt via export-mod.sh) and compares a
// canonical digest of the resource-free map and of the starting civ/unit state. kotlin-golden-mod.txt is the Kotlin output, verbatim.
// Not compared: resources, Oasis and anything after turn 0. Kotlin shuffles with an unseeded Random (StrategicBonusResourcePlacementLogic, oasis placement), so a
// Kotlin game cannot be replayed (the C# port seeds those shuffles); see the T160 notes.
[Collection("RulesetCache")]
public sealed class KotlinModGoldenTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public KotlinModGoldenTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(AppContext.BaseDirectory, "Determinism")));
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

    private sealed record Golden(int MapSeed, string GameId, string Map, string Start);

    private static List<Golden> LoadGolden()
    {
        var result = new List<Golden>();
        var path = Path.Combine(AppContext.BaseDirectory, "Determinism", "kotlin-golden-mod.txt");
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split(' ');
            if (parts[0] == "modgame")
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
                    UseCiv5Ai = false,   // T235: the golden values come from the Kotlin AI
                    BaseRuleset = ruleset.Name,
                    Mods = ["golden-mod"],
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = new[] { "Babylon", "Testland", "Rome" }.Select((it, i) => new Player(it, i == 0 ? PlayerType.Human : PlayerType.AI)).ToList(),
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = mapSeed, MapSize = MapSize.Tiny }),
            1L, 1000L, gameId: gameId);

    public static TheoryData<int> Seeds => new() { 3, 11 };

    [Theory]
    [MemberData(nameof(Seeds))]
    public void ModMapAndStartingStateMatchKotlin(int mapSeed)
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
