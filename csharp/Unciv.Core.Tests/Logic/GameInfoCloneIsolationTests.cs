using System.Collections;
using System.Reflection;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Logic;

// T219: GameInfo.Clone() must hand out a game that shares no mutable object with its source, or playing the clone ages the original.
// The test walks the whole object graph of a played game and of its clone by reflection and reports every object reachable from both.
[Collection("RulesetCache")]
public sealed class GameInfoCloneIsolationTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public GameInfoCloneIsolationTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.Get(BaseRuleset.Civ_V_GnK.FullName()).NotNull();
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

    private GameInfo PlayedGame(int turns)
    {
        var parameters = new GameParameters
        {
            BaseRuleset = ruleset.Name,
            UseCiv5Ai = true,
            NumberOfCityStates = 3,
            EspionageEnabled = true,
            VictoryTypes = ruleset.Victories.Keys.ToList(),
            MaxTurns = 300,
            Players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.AI), new Player("Rome", PlayerType.AI)],
        };
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(parameters, new MapParameters { Type = MapType.SmallContinents, Shape = MapShape.Rectangular, Seed = 5, MapSize = MapSize.Small }),
            11L, 1000L);
        while (game.Turns < turns)
        {
            game.SimulateUntilWin = true;
            game.SimulateMaxTurns = game.Turns + 1;
            game.NextTurn(2000L);
        }
        return game;
    }

    // Everything that may legitimately be reachable from both games: the rules (never mutated by play) and the type machinery.
    private static bool IsImmutable(Type type) =>
        type == typeof(string) || type.IsEnum || type.IsPrimitive || typeof(Delegate).IsAssignableFrom(type) || typeof(Type).IsAssignableFrom(type)
        || typeof(MemberInfo).IsAssignableFrom(type) || type.Name.Contains("Comparer", StringComparison.Ordinal) || type.Namespace is "System.Text" or "System.Globalization" or "System.Threading"
        || type.Name is "Version" or "CompatibilityVersion" or "TileHistoryState" || typeof(NotificationAction).IsAssignableFrom(type)
        || typeof(RulesetClass).IsAssignableFrom(type) || (type.Namespace?.StartsWith("Unciv.Models.Ruleset", StringComparison.Ordinal) ?? false);

    // Shared on purpose, each safe for the reason given.
    private static readonly HashSet<string> SharedByDesign =
    [
        "Tile.exploredBy",                                    // copy-on-write: Tile.Explore replaces the set, never adds to it
        "Tile.<CachedTerrainData>k__BackingField",            // memo of ruleset facts, keyed by terrain names
        "TileMap.<TileUniqueMapCache>k__BackingField",        // same memo
        "City.<ConquestExternals>k__BackingField",            // stateless service
        "GameInfo.<ClientState>k__BackingField",              // the client's environment (settings, automation hooks), not game state
    ];

    // Every reachable object with the path it was first found on and the object that holds it.
    private static Dictionary<object, (string Path, object? Parent)> Reachable(object root)
    {
        var seen = new Dictionary<object, (string Path, object? Parent)>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object, string)>();
        queue.Enqueue((root, "game"));
        seen[root] = ("game", null);
        object current = root;
        void Visit(object? child, string path)
        {
            // Empty arrays are the runtime's shared singletons (an empty List's backing store).
            if (child == null || IsImmutable(child.GetType()) || child is Array { Length: 0 } || child.GetType().IsValueType && child is not IEnumerable) return;
            if (seen.ContainsKey(child)) return;
            seen[child] = (path, current);
            queue.Enqueue((child, path));
        }
        while (queue.Count > 0)
        {
            var (obj, path) = queue.Dequeue();
            current = obj;
            var type = obj.GetType();
            if (obj is Array array)
            {
                if (!type.GetElementType()!.IsPrimitive) foreach (var item in array) Visit(item, path + "[]");
                continue;
            }
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (SharedByDesign.Contains(t.Name + "." + field.Name)) continue;
                    if (field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(string)) continue;
                    var value = field.GetValue(obj);
                    if (value != null && field.FieldType.IsValueType) VisitStruct(value, path + "." + field.Name, Visit);
                    else Visit(value, path + "." + field.Name);
                }
        }
        return seen;
    }

    private static void VisitStruct(object boxed, string path, Action<object?, string> visit)
    {
        foreach (var field in boxed.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(string)) continue;
            var value = field.GetValue(boxed);
            if (value != null && field.FieldType.IsValueType) VisitStruct(value, path + "." + field.Name, visit);
            else visit(value, path + "." + field.Name);
        }
    }

    [Fact]
    public void CloneOfAPlayedGameSharesNoMutableReferenceWithItsSource()
    {
        var game = PlayedGame(40);
        var clone = game.Clone();
        clone.SetTransients();
        var original = Reachable(game);
        var copied = Reachable(clone);
        // Only the topmost shared objects: whatever hangs below a shared object is shared with it.
        var shared = original.Where(it => copied.ContainsKey(it.Key) && (it.Value.Parent == null || !copied.ContainsKey(it.Value.Parent)))
            .Select(it => $"{it.Key.GetType().Name} at {it.Value.Path}")
            .Distinct().OrderBy(it => it, StringComparer.Ordinal).ToList();
        Assert.True(shared.Count == 0, "objects reachable from both the game and its clone:\n" + string.Join("\n", shared.Take(60)));
    }

    [Fact]
    public void PlayingTheCloneLeavesTheSourceUntouched()
    {
        var game = PlayedGame(30);
        var before = UncivFiles.GameInfoToString(game, forceZip: false);
        var clone = game.Clone();
        clone.SetTransients();
        for (var i = 0; i < 5; i++)
        {
            clone.SimulateUntilWin = true;
            clone.SimulateMaxTurns = clone.Turns + 1;
            clone.NextTurn(2000L);
        }
        Assert.Equal(before, UncivFiles.GameInfoToString(game, forceZip: false));
    }
}
