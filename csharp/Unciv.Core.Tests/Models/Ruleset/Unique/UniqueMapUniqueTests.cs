using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// <c>UniqueMap</c> has no Kotlin test class, so iteration orders and lookups are compared with the real compiled Kotlin class
/// (<c>Golden/UniqueMap.golden.tsv</c>, Unciv 4.22.13 jar): a map filled from the first 1200 lines of the Unique corpus (plus 20 duplicates),
/// queried under <c>GameContext.IgnoreConditionals</c> so no conditional is evaluated. Rows: kind, key, then the unique texts in
/// Kotlin's order (joined with U+001F) or the boolean results. <c>getAllUniques()</c> follows <c>java.util.HashMap</c> bucket order of the
/// placeholder texts, <c>getTriggeredUniques</c> follows <c>EnumMap</c> ordinal order.
/// </summary>
public class UniqueMapUniqueTests
{
    private const char Us = '\u001f';

    private static string Texts(IEnumerable<UniqueObject> uniques) => string.Join(Us, uniques.Select(it => it.Text));

    private static string[][] LoadGolden() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Models", "Ruleset", "Unique", "Golden", "UniqueMap.golden.tsv"))
            .Select(it => it.Split('\t')).ToArray();

    // The corpus is the first column of the Unique golden file, in the order the Kotlin harness read it
    private static (UniqueMap Map, List<UniqueObject> All) Build()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Models", "Ruleset", "Unique", "Golden", "Unique.golden.tsv"))
            .Select(it => it.Split('\t')[0]).ToList();
        var map = new UniqueMap();
        var all = new List<UniqueObject>();
        foreach (string text in lines.Take(1200))
        {
            var unique = new UniqueObject(text);
            all.Add(unique);
            map.AddUnique(unique);
        }
        for (int i = 0; i < 20; i++)
        {
            var unique = new UniqueObject(lines[i * 7]);
            all.Add(unique);
            map.AddUnique(unique);
        }
        return (map, all);
    }

    [Fact]
    public void QueriesAndIterationOrdersMatchKotlin()
    {
        var (map, all) = Build();
        var golden = LoadGolden();
        var ignore = GameContext.IgnoreConditionalsState;
        int checkedRows = 0;

        foreach (var row in golden)
        {
            string kind = row[0], key = row[1], expected = row[2];
            switch (kind)
            {
                case "ALL":
                    Assert.True(expected == Texts(map.GetAllUniques()), "getAllUniques");
                    var viaForEach = new List<UniqueObject>();
                    map.ForEachUnique(viaForEach.Add);
                    Assert.True(expected == Texts(viaForEach), "forEachUnique");
                    break;
                case "TYPE":
                    var type = Enum.Parse<UniqueType>(key);
                    Assert.True(expected == Texts(map.GetUniques(type)), $"getUniques {key}");
                    var viaForEachType = new List<UniqueObject>();
                    map.ForEachUnique(type, viaForEachType.Add);
                    Assert.True(expected == Texts(viaForEachType), $"forEachUnique {key}");
                    break;
                case "MATCH":
                    var matchType = Enum.Parse<UniqueType>(key);
                    Assert.True(expected == Texts(map.GetMatchingUniques(matchType, ignore)), $"getMatchingUniques {key}");
                    var matched = new List<UniqueObject>();
                    map.ForEachMatchingUnique(matchType, ignore, matched.Add);
                    // forEachMatchingUnique skips timed uniques exactly like getMatchingUniques
                    Assert.True(expected == Texts(matched), $"forEachMatchingUnique {key}");
                    break;
                case "HAS":
                    var hasType = Enum.Parse<UniqueType>(key);
                    var parts = expected.Split(',');
                    Assert.True(bool.Parse(parts[0]) == map.HasUnique(hasType, ignore), $"hasUnique {key}");
                    Assert.True(bool.Parse(parts[1]) == map.HasMatchingUnique(hasType, ignore), $"hasMatchingUnique {key}");
                    break;
                case "TAG":
                    Assert.True(expected == Texts(map.GetTagUniques(key)), $"getTagUniques {key}");
                    Assert.True(expected == Texts(map.GetMatchingTagUniques(key, ignore).Distinct()) || map.GetTagUniques(key).Any(it => it.IsTimedTriggerable),
                        $"getMatchingTagUniques {key}");
                    break;
                case "TAGHAS":
                    var tagParts = expected.Split(',');
                    Assert.True(bool.Parse(tagParts[0]) == map.HasTagUnique(key), $"hasTagUnique {key}");
                    Assert.True(bool.Parse(tagParts[1]) == map.HasUnique(key, ignore), $"hasUnique(tag) {key}");
                    Assert.True(bool.Parse(tagParts[2]) == map.HasMatchingTagUnique(key, ignore), $"hasMatchingTagUnique {key}");
                    break;
                case "TRIG":
                    var trigger = Enum.Parse<UniqueType>(key);
                    Assert.True(expected == Texts(map.GetTriggeredUniques(trigger, ignore)), $"getTriggeredUniques {key}");
                    Assert.True(expected == Texts(map.GetTriggeredUniques(trigger, ignore, _ => true)), $"getTriggeredUniques with filter {key}");
                    break;
                default:
                    continue;
            }
            checkedRows++;
        }
        Assert.True(checkedRows > 1900, $"only {checkedRows} rows checked");

        // The mutations at the end of the golden file
        map.RemoveUnique(all[0]);
        map.RemoveUnique(all[^3]);
        Assert.True(golden.Single(it => it[0] == "AFTERREMOVE")[2] == Texts(map.GetAllUniques()), "after removeUnique");
        var emptyRow = golden.Single(it => it[0] == "EMPTY")[2].Split(',');
        Assert.Equal(bool.Parse(emptyRow[0]), map.IsEmpty());
        Assert.Equal(bool.Parse(emptyRow[1]), UniqueMap.Empty.IsEmpty());
        map.Clear();
        Assert.True(map.IsEmpty());
        Assert.Empty(map.GetAllUniques());
    }

    [Fact]
    public void ForEachUniqueWithFilterOnlyVisitsAcceptedUniques()
    {
        var map = new UniqueMap(new[] { new UniqueObject("[+1 Gold] from every [Shrine]"), new UniqueObject("Free Great Person"), new UniqueObject("[+2 Food] from every [Farm]") });
        var seen = new List<string>();
        map.ForEachUnique(it => it.Text.StartsWith('['), it => seen.Add(it.Text));
        Assert.Equal(2, seen.Count);
        Assert.DoesNotContain("Free Great Person", seen);
    }

    [Fact]
    public void TimedUniquesAreNeverMatched()
    {
        var timed = new UniqueObject("[+25]% Strength <when attacking> <for [3] turns>");
        var plain = new UniqueObject("[+25]% Strength");
        var map = new UniqueMap(new[] { timed, plain });
        // the context ignores conditionals, so only the timed filter can exclude `timed`
        var ignore = GameContext.IgnoreConditionalsState;
        Assert.Equal(new[] { plain }, map.GetMatchingUniques(UniqueType.Strength, ignore).ToArray());
        Assert.Equal(new[] { plain }, map.GetMatchingTagUniques(plain.PlaceholderText, ignore).ToArray());
        var visited = new List<UniqueObject>();
        map.ForEachMatchingUnique(UniqueType.Strength, ignore, visited.Add);
        Assert.Equal(new[] { plain }, visited.ToArray());
        visited.Clear();
        map.ForEachMatchingTagUnique(plain.PlaceholderText, ignore, UniqueMap.NoUniqueFilter, visited.Add);
        Assert.Equal(new[] { plain }, visited.ToArray());
        // hasUnique excludes timed uniques too, hasMatchingUnique does not (an upstream asymmetry)
        var onlyTimed = new UniqueMap(new[] { timed });
        Assert.False(onlyTimed.HasUnique(UniqueType.Strength, ignore));
        Assert.False(onlyTimed.HasUnique(timed.PlaceholderText, ignore));
        Assert.True(onlyTimed.HasMatchingUnique(UniqueType.Strength, ignore));
        Assert.True(onlyTimed.HasMatchingTagUnique(timed.PlaceholderText, ignore));
    }

    [Fact]
    public void FilterIsAppliedBeforeConditionals()
    {
        var unique = new UniqueObject("[+25]% Strength <when attacking>");
        var map = new UniqueMap(new[] { unique });
        var conditionalsEvaluated = false;
        // A rejecting filter must keep forEachMatchingUnique from ever touching the (not yet ported) conditionals
        map.ForEachMatchingUnique(UniqueType.Strength, GameContext.EmptyState, it => { conditionalsEvaluated = true; return false; }, _ => { });
        Assert.True(conditionalsEvaluated);
    }

    [Fact]
    public void RemoveUniqueRemovesOnlyThatInstance()
    {
        var a = new UniqueObject("[+25]% Strength");
        var b = new UniqueObject("[+25]% Strength");
        var map = new UniqueMap(new[] { a, b });
        map.RemoveUnique(a);
        Assert.Same(b, Assert.Single(map.GetUniques(UniqueType.Strength)));
        map.RemoveUnique(new UniqueObject("[+99]% Strength")); // not in the map: no effect
        Assert.Single(map.GetUniques(UniqueType.Strength));
        map.RemoveUnique(new UniqueObject("unknown thing")); // unknown tag: no effect
    }

    [Fact]
    public void UnknownTypeUniquesAreOnlyFoundByTag()
    {
        var unknown = new UniqueObject("some mod specific [thing]");
        var map = new UniqueMap();
        map.AddUnique(unknown);
        Assert.True(map.HasTagUnique("some mod specific []"));
        Assert.Same(unknown, Assert.Single(map.GetTagUniques("some mod specific []")));
        Assert.All(map.GetAllUniques(), it => Assert.Null(it.Type));
        Assert.False(map.IsEmpty());
    }

    private sealed class RecordingMap : UniqueMap
    {
        public List<UniqueObject> Added { get; } = new();
        public override void AddUnique(UniqueObject unique)
        {
            Added.Add(unique);
            base.AddUnique(unique);
        }
    }

    [Fact]
    public void AddUniqueIsTheOverridableEntryPointOfAddUniques()
    {
        var map = new RecordingMap();
        map.AddUniques(new[] { new UniqueObject("a [b]"), new UniqueObject("c") });
        Assert.Equal(2, map.Added.Count);
        Assert.Equal(2, map.GetAllUniques().Count());
    }
}
