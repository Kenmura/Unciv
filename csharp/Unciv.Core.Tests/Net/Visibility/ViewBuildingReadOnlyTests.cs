using System.Reflection;
using System.Text.RegularExpressions;
using Unciv.Commands.Events;
using Unciv.Commands.Log;
using Unciv.Core.Tests.Support;
using Unciv.Harness;
using Unciv.Logic;
using Unciv.Logic.Files;
using Unciv.Net.Visibility;
using Xunit.Abstractions;

namespace Unciv.Core.Tests.Net.Visibility;

// T296 (design/PERFORMANCE.md, Parallelism): T263 may build the per-recipient views on several threads, which is only safe while the builders never write
// shared game or ruleset state. The engine has lazy `??=` caches that the first call fills. This test pins the contract: after one warm-up round over every
// viewer, further view building changes no instance field reachable from the GameInfo, no static collection of the engine and not the FullHash. A failure
// names the first changed object, i.e. a lazy cache that must be warmed up (SetTransients) or locked before view building is parallelised.
[Collection("RulesetCache")]
public sealed class ViewBuildingReadOnlyTests(ITestOutputHelper output)
{
    private static readonly TurnStatusDto Status = new() { Ended = [], Phase = "playing" };

    public static TheoryData<string> Scenarios => new() { "default", "crowd", "trade", "espionage", "alerts", "rich", "policies", "victory", "decisions", "religion", "skirmish", "reloaded-default", "reloaded-crowd", "reloaded-espionage", "reloaded-trade", "reloaded-decisions" };

    // A game fresh from a save (the host after a restart): its transient caches are as cold as they get.
    private static GameInfo Load(string scenario) => scenario.StartsWith("reloaded-", StringComparison.Ordinal)
        ? UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(Create(scenario["reloaded-".Length..]).Game, forceZip: false))
        : Create(scenario).Game;

    private static Unciv.Commands.GameSession Create(string scenario) => scenario switch
    {
        "crowd" => ScenarioGame.Create(crowd: true),
        "trade" => ScenarioGame.Create(trade: true),
        "espionage" => ScenarioGame.Create(espionage: true),
        "alerts" => ScenarioGame.Create(alerts: true),
        "rich" => ScenarioGame.Create(rich: true),
        "policies" => ScenarioGame.Create(policies: true),
        "decisions" => ScenarioGame.Create(decisions: true),
        "religion" => ScenarioGame.Create(religion: ScenarioGame.ReligionFoundEnhance),
        "skirmish" => ScenarioGame.Create(skirmish: true),
        "victory" => ScenarioGame.Create(victory: true),
        _ => ScenarioGame.Create()
    };

    // One full host round for one recipient: capture, snapshot, delta with diff (which builds the events and a second snapshot).
    private static void BuildAllViews(GameInfo game)
    {
        foreach (var viewer in game.Civilizations)
        {
            var before = SnapshotBuilder.Build(game, viewer, 1, Status);
            var mark = ViewDeltaBuilder.Capture(game, viewer);
            ViewDeltaBuilder.BuildWithDiff(game, viewer, before, mark, Array.Empty<EngineEvent>(), 2, Status);
        }
    }

    private static List<string> Differences(List<string> before, List<string> after)
    {
        var result = new List<string>();
        if (before.Count != after.Count) result.Add($"entry count {before.Count} -> {after.Count}");
        for (var i = 0; i < System.Math.Min(before.Count, after.Count) && result.Count < 20; i++)
            if (before[i] != after[i]) result.Add($"{Clip(before[i])}\n      -> {Clip(after[i])}");
        return result;
    }

    private static HashSet<string> Normalised(List<string> entries) =>
        entries.Select(it => Regex.Replace(it, "[#@][0-9]+", "#")).ToHashSet(StringComparer.Ordinal);

    private static string Clip(string text) => text.Length <= 240 ? text : text[..240] + "...";

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void ViewBuilding_AfterWarmUp_WritesNoSharedState(string scenario)
    {
        var game = Load(scenario);
        var statics = typeof(GameInfo).Assembly;
        var hashBefore = ReplayVerifier.FullHash(game);

        var cold = ObjectGraphFingerprint.Of(game);
        var coldStatics = ObjectGraphFingerprint.OfStatics(statics);
        BuildAllViews(game); // warm-up: allowed to fill the lazy caches
        var warm = ObjectGraphFingerprint.Of(game);
        var warmStatics = ObjectGraphFingerprint.OfStatics(statics);
        // Information only (the audit table in design/PERFORMANCE.md): which object types the warm-up wrote, ignoring the visit ids that shift when objects appear.
        var written = Normalised(warm).Except(Normalised(cold)).Select(it => it[(it.IndexOf("<-", StringComparison.Ordinal) + 2)..it.IndexOf('{', StringComparison.Ordinal)]).GroupBy(it => it).OrderBy(it => it.Key, StringComparer.Ordinal);
        foreach (var group in written) output.WriteLine($"[{scenario}] warm-up wrote {group.Count()} x {group.Key}");
        foreach (var line in warmStatics.Except(coldStatics)) output.WriteLine($"[{scenario}] warm-up wrote static {line}");

        BuildAllViews(game);
        BuildAllViews(game);
        var hot = ObjectGraphFingerprint.Of(game);
        var hotStatics = ObjectGraphFingerprint.OfStatics(statics);

        Assert.True(warm.Count > 1000, $"the fingerprint saw only {warm.Count} objects, the walk is broken");
        Assert.Empty(Differences(warm, hot));
        Assert.Empty(Differences(warmStatics, hotStatics));
        Assert.Equal(hashBefore, ReplayVerifier.FullHash(game));
    }

    // The fingerprint only sees what a scenario exercises: a filter string first evaluated in a later game state would still write its cache entry from a
    // view building thread. Caches keyed by such strings must therefore be safe for concurrent writers, not merely warmed up (design/PERFORMANCE.md).
    [Theory]
    [InlineData(typeof(Unciv.Models.Ruleset.Tile.Terrain), "cachedMatchesFilterResult")]
    [InlineData(typeof(Unciv.Models.Ruleset.Building), "cachedMatchesFilterResult")]
    [InlineData(typeof(Unciv.Models.Ruleset.Unit.BaseUnit), "cachedMatchesFilterResult")]
    [InlineData(typeof(Unciv.Logic.City.CityConstructions), "containedBuildingFiltersCache")]
    public void LazilyKeyedFilterCaches_AreConcurrentDictionaries(Type owner, string fieldName)
    {
        var field = owner.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Equal(typeof(System.Collections.Concurrent.ConcurrentDictionary<string, bool>), field.FieldType);
    }

    [Fact]
    public void Fingerprint_SeesAnUnwrittenCacheField()
    {
        // The guard is only worth something if it notices a lazy write: a field filled behind the walker's back must change the fingerprint.
        var game = ScenarioGame.Create().Game;
        var before = ObjectGraphFingerprint.Of(game);
        var tile = game.TileMap.TileList[0];
        var field = typeof(Unciv.Logic.Map.Tiles.Tile).GetField("isAdjacentToCoastCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var original = field.GetValue(tile);
        field.SetValue(tile, original is true ? false : true);
        Assert.NotEmpty(Differences(before, ObjectGraphFingerprint.Of(game)));
        field.SetValue(tile, original);
        Assert.Empty(Differences(before, ObjectGraphFingerprint.Of(game)));
    }
}
