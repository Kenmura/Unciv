using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// Ported from <c>tests/src/com/unciv/uniques/DeprecatedUniquesTest.kt</c>: tests 1 and 2 (the table itself). Tests 3, 5, 6, 7 need the validator
/// and are in <c>Validation/DeprecatedUniquesValidatorTests</c> (T036); test 2's half-deprecated hop does not apply
/// because no <see cref="UniqueType"/> entry is deprecated.
/// <para>
/// <c>Golden/DeprecatedUniques.golden.tsv</c> (columns: entry name, sign variant, unique text, replacement text) was produced by the real
/// Kotlin <c>Unique.getReplacementText(vanillaRuleset)</c> via a throwaway test in <c>tests/</c> (not committed) for every entry
/// whose replacement has a single alternative. Each unique text is the entry text with its i-th parameter replaced by
/// <c>[10*(i+1)]</c>, <c>[-10*(i+1)]</c> or <c>[+10*(i+1)]</c>.
/// </para>
/// </summary>
public class DeprecatedUniquesTests
{
    private const string OrDelimiter = "\" OR \"";

    [Fact]
    public void TableHasEveryKotlinEntryInOrder()
    {
        Assert.Equal(210, DeprecatedUniqueTypes.Entries.Count);
        for (int i = 0; i < DeprecatedUniqueTypes.Entries.Count; i++)
            Assert.Equal((int)DeprecatedUniqueTypes.Entries[i].Type, i);
        Assert.Equal("Receive triple Gold from Barbarian encampments and pillaging Cities",
            DeprecatedUniqueTypes.Get(DeprecatedUniqueType.TripleGoldFromEncampmentsAndCities).Text);
    }

    [Fact]
    public void NoPlaceholderTextConflictBetweenDeprecatedAndUniqueType()
    {
        var conflicts = DeprecatedUniqueTypes.Entries
            .Where(entry => UniqueTypeExtensions.UniqueTypeMap.ContainsKey(entry.PlaceholderText))
            .Select(entry => $"{entry.Type} conflicts with {UniqueTypeExtensions.UniqueTypeMap[entry.PlaceholderText]}")
            .ToList();
        Assert.True(conflicts.Count == 0, string.Join("\n", conflicts));
    }

    [Fact]
    public void AllDeprecatedUniqueTypeReplacementChainsTerminate()
    {
        foreach (var entry in DeprecatedUniqueTypes.Entries)
        {
            string currentPlaceholder = entry.ReplaceWith.GetPlaceholderText();
            int hops = 0;
            while (DeprecatedUniqueTypes.Lookup(currentPlaceholder) is { } next)
            {
                currentPlaceholder = next.ReplaceWith.GetPlaceholderText();
                hops++;
                Assert.True(hops <= 20, $"Replacement chain for {entry.Type} has not terminated after 20 hops");
            }
        }
    }

    [Fact]
    public void ChainDecreasedBuildingMaintenanceGoesThroughBuildingMaintenanceOld()
    {
        var start = DeprecatedUniqueTypes.Get(DeprecatedUniqueType.DecreasedBuildingMaintenanceDeprecated);
        Assert.Equal(DeprecatedUniqueTypes.Get(DeprecatedUniqueType.BuildingMaintenanceOld).PlaceholderText, start.ReplaceWith.GetPlaceholderText());
        var old = DeprecatedUniqueTypes.Get(DeprecatedUniqueType.BuildingMaintenanceOld);
        Assert.Equal(UniqueType.BuildingMaintenance, UniqueTypeExtensions.UniqueTypeMap[old.ReplaceWith.GetPlaceholderText()]);
    }

    [Fact]
    public void UniqueResolvesTheDeprecatedTypeOnlyWhenThereIsNoRealType()
    {
        var deprecated = new UniqueObject("Receive triple Gold from Barbarian encampments and pillaging Cities");
        Assert.Null(deprecated.Type);
        Assert.Same(DeprecatedUniqueTypes.Get(DeprecatedUniqueType.TripleGoldFromEncampmentsAndCities), deprecated.DeprecatedType);
        Assert.Same(deprecated.DeprecatedType, deprecated.GetDeprecationAnnotation());

        var current = new UniqueObject(UniqueType.Stats.Text());
        Assert.NotNull(current.Type);
        Assert.Null(current.DeprecatedType);
        Assert.Null(current.GetDeprecationAnnotation());
        Assert.Equal("", current.GetReplacementText(_ => true));
    }

    [Fact]
    public void HasFlagReadsTheFlagsOfTheDeprecatedType()
    {
        var hidden = new UniqueObject("Hidden when religion is disabled");
        Assert.True(hidden.HasFlag(UniqueFlag.HiddenToUsers));
        Assert.False(hidden.HasFlag(UniqueFlag.NoConditionals));
        var plain = new UniqueObject("Receive triple Gold from Barbarian encampments and pillaging Cities");
        Assert.False(plain.HasFlag(UniqueFlag.HiddenToUsers));
    }

    [Fact]
    public void OnlyTheFourHiddenEntriesCarryFlags()
    {
        var flagged = DeprecatedUniqueTypes.Entries.Where(entry => entry.Flags.Count > 0).Select(entry => entry.Type).ToList();
        Assert.Equal(
        [
            DeprecatedUniqueType.HiddenWithoutVictoryType, DeprecatedUniqueType.HiddenWithoutReligion,
            DeprecatedUniqueType.HiddenWithoutEspionage, DeprecatedUniqueType.NaturalWonderConvertNeighborsExcept,
        ], flagged);
    }

    [Fact]
    public void ReplacementTextMatchesKotlinForEverySingleAlternativeEntry()
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Models", "Ruleset", "Unique", "Golden", "DeprecatedUniques.golden.tsv"));
        Assert.Equal(600, lines.Length);
        foreach (string line in lines)
        {
            string[] cols = line.Split('\t');
            Assert.Equal(4, cols.Length);
            var unique = new UniqueObject(cols[2]);
            Assert.True(unique.DeprecatedType != null, $"{cols[0]}: {cols[2]} does not resolve to a deprecated type");
            Assert.Equal(cols[3], unique.GetReplacementText(_ => true));
        }
    }

    [Fact]
    public void SeveralAlternativesAreFilteredByTheValidatorPredicate()
    {
        // Hand-derived from Unique.getReplacementText with a stand-in predicate (the real validator is covered by DeprecatedUniquesValidatorTests): alternatives are separated by " OR ".
        var unique = new UniqueObject("+[5]% Strength vs [Melee]");
        Assert.Contains(OrDelimiter, unique.DeprecatedType!.ReplaceWith);
        Assert.Equal("[+5]% Strength <vs cities>", unique.GetReplacementText(it => it.Text.Contains("cities")));
        Assert.Equal("[+amount]% Strength <vs [combatantFilter] units>\", \"[+amount]% Strength <vs cities>", unique.GetReplacementText(_ => false));
    }
}
