using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;

namespace Unciv.Core.Tests.Models.Ruleset.Validation;

/// <summary>
/// Ported from <c>tests/src/com/unciv/uniques/AllObjectsTests.kt</c>. Kotlin's <c>runTestParcours</c> over the base rulesets becomes a loop that reports the offending names.
/// </summary>
[Collection("RulesetCache")]
public sealed class AllObjectsTests : IDisposable
{
    public AllObjectsTests()
    {
        RulesetCache.Clear();
        string jsons = ValidatorTestRuleset.JsonsPath();
        Assert.Empty(RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true));
    }

    public void Dispose() => RulesetCache.Clear();

    private static List<T> FindDuplicateReferences<T>(IEnumerable<T> entries) where T : class
    {
        var seen = new List<T>();
        var duplicates = new List<T>();
        foreach (var entry in entries)
        {
            if (seen.Any(it => ReferenceEquals(it, entry))) duplicates.Add(entry);
            else seen.Add(entry);
        }
        return duplicates;
    }

    [Fact]
    public void AllRulesetObjectsShouldNotReturnDuplicateReferences()
    {
        foreach (var baseRuleset in Enum.GetValues<BaseRuleset>())
            Assert.Empty(FindDuplicateReferences(RulesetCache.Get(baseRuleset.FullName())!.AllRulesetObjects()).Select(it => $"{baseRuleset.FullName()}: {it.Name}"));
    }

    [Fact]
    public void AllICivilopediaTextShouldNotReturnDuplicateReferences()
    {
        foreach (var baseRuleset in Enum.GetValues<BaseRuleset>())
            Assert.Empty(FindDuplicateReferences(RulesetCache.Get(baseRuleset.FullName())!.AllICivilopediaText()).Select(it => $"{baseRuleset.FullName()}: {it.MakeLink()}"));
    }

    [Fact]
    public void AllUniquesShouldNotReturnDuplicateReferences()
    {
        foreach (var baseRuleset in Enum.GetValues<BaseRuleset>())
            Assert.Empty(FindDuplicateReferences(RulesetCache.Get(baseRuleset.FullName())!.AllUniques()).Select(it => $"{baseRuleset.FullName()}: {it.Text}"));
    }
}
