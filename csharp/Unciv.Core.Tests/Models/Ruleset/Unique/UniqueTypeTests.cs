using System.Text;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// <c>UniqueType</c> has no Kotlin test class, so these compare every entry with golden values dumped by reflection from
/// the real compiled Kotlin enum (<c>Golden/UniqueType.golden.tsv</c>, Unciv 4.22.13 jar; <c>UniqueType.values()</c> on the JVM).
/// Columns: ordinal, name, text, placeholderText, targetTypes (sorted by UniqueTarget ordinal),
/// flags (set iteration order), docDescription (<c>&lt;null&gt;</c> when null), parameterTypeMap
/// (positions split by <c>;</c>, alternatives by <c>/</c>), canAcceptUniqueTarget for each UniqueTarget in ordinal order as 0/1.
/// Text columns escape <c>\</c>, tab, LF and CR.
/// </summary>
public class UniqueTypeTests
{
    private sealed record GoldenRow(
        int Ordinal,
        string Name,
        string Text,
        string PlaceholderText,
        string Targets,
        string Flags,
        string? DocDescription,
        string ParameterTypeMap,
        string CanAccept);

    private static readonly IReadOnlyList<GoldenRow> Golden = LoadGolden();

    private static IReadOnlyList<GoldenRow> LoadGolden()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Models", "Ruleset", "Unique", "Golden", "UniqueType.golden.tsv");
        var rows = new List<GoldenRow>();
        foreach (var line in File.ReadAllLines(path))
        {
            var c = line.Split('\t');
            Assert.Equal(9, c.Length);
            rows.Add(new GoldenRow(
                int.Parse(c[0], System.Globalization.CultureInfo.InvariantCulture),
                c[1],
                Unescape(c[2]),
                Unescape(c[3]),
                c[4],
                c[5],
                c[6] == "<null>" ? null : Unescape(c[6]),
                c[7],
                c[8]));
        }
        return rows;
    }

    private static string Unescape(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\')
            {
                sb.Append(s[i]);
                continue;
            }
            i++;
            sb.Append(s[i] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                _ => throw new InvalidDataException($"bad escape in golden file: {s}"),
            });
        }
        return sb.ToString();
    }

    private static void AssertNoMismatches(Func<UniqueType, GoldenRow, string?> check)
    {
        var problems = new List<string>();
        foreach (var row in Golden)
        {
            var problem = check(UniqueTypeExtensions.Entries[row.Ordinal], row);
            if (problem != null) problems.Add($"{row.Name}: {problem}");
        }
        Assert.True(problems.Count == 0, $"{problems.Count} mismatches, first: {string.Join(" | ", problems.Take(5))}");
    }

    [Fact]
    public void Entries_CountOrdinalsAndNames_MatchKotlin()
    {
        Assert.Equal(638, Golden.Count);
        // Kotlin's entries come first and keep their ordinals; the Brave New World gates (B001) and hooks (B003) are appended after them
        Assert.Equal(Golden.Count + (int)UniqueType.CatchEnemySpiesModifier - (int)UniqueType.EnablesInternationalTradeRoutes + 1, UniqueTypeExtensions.Entries.Count);
        AssertNoMismatches((type, row) =>
            (int)type == row.Ordinal && type.ToString() == row.Name ? null : $"got {(int)type} {type}, expected {row.Ordinal}");
    }

    [Fact]
    public void TextAndPlaceholderText_MatchKotlin() =>
        AssertNoMismatches((type, row) =>
            type.Text() == row.Text && type.PlaceholderText() == row.PlaceholderText
                ? null
                : $"got '{type.Text()}' / '{type.PlaceholderText()}', expected '{row.Text}' / '{row.PlaceholderText}'");

    [Fact]
    public void TargetTypes_MatchKotlin() =>
        AssertNoMismatches((type, row) =>
        {
            // Kotlin's HashSet iteration order is arbitrary, so the golden file is sorted by ordinal: compare as sets.
            var actual = string.Join(',', type.TargetTypes().OrderBy(t => (int)t));
            return actual == row.Targets ? null : $"got {actual}, expected {row.Targets}";
        });

    [Fact]
    public void Flags_MatchKotlinIncludingOrder() =>
        AssertNoMismatches((type, row) =>
        {
            var actual = string.Join(',', type.Flags());
            return actual == row.Flags ? null : $"got {actual}, expected {row.Flags}";
        });

    [Fact]
    public void DocDescription_MatchesKotlin() =>
        AssertNoMismatches((type, row) =>
            type.DocDescription() == row.DocDescription ? null : $"got '{type.DocDescription()}', expected '{row.DocDescription}'");

    [Fact]
    public void ParameterTypeMap_MatchesKotlin() =>
        AssertNoMismatches((type, row) =>
        {
            var actual = string.Join(';', type.ParameterTypeMap().Select(position => string.Join('/', position)));
            return actual == row.ParameterTypeMap ? null : $"got {actual}, expected {row.ParameterTypeMap}";
        });

    [Fact]
    public void CanAcceptUniqueTarget_MatchesKotlinForEveryTarget() =>
        AssertNoMismatches((type, row) =>
        {
            var actual = new string(Enum.GetValues<UniqueTarget>().Select(t => type.CanAcceptUniqueTarget(t) ? '1' : '0').ToArray());
            return actual == row.CanAccept ? null : $"got {actual}, expected {row.CanAccept}";
        });

    [Fact]
    public void CanAcceptUniqueTarget_Examples()
    {
        // Global uniques are acceptable wherever a Building (or Nation, Tech, ...) is the source...
        Assert.True(UniqueType.Stats.CanAcceptUniqueTarget(UniqueTarget.Building));
        Assert.True(UniqueType.Stats.CanAcceptUniqueTarget(UniqueTarget.Wonder));
        // ...but not the other way round: Stats is not declared for units.
        Assert.False(UniqueType.Stats.CanAcceptUniqueTarget(UniqueTarget.Unit));
    }

    [Fact]
    public void UniqueTypeMap_FindsEveryEntryByPlaceholderText()
    {
        var map = UniqueTypeExtensions.UniqueTypeMap;
        Assert.Equal(UniqueTypeExtensions.Entries.Count, map.Count);
        foreach (var type in UniqueTypeExtensions.Entries)
            Assert.Equal(type, map[type.PlaceholderText()]);

        Assert.False(map.ContainsKey("not a unique"));
        Assert.Equal(UniqueType.Stats, map["[]"]);
    }
}
