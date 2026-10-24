using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Validation;
using Unciv.Models.Translations;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Validation;

/// <summary>
/// The validator-dependent tests of <c>tests/src/com/unciv/uniques/DeprecatedUniquesTest.kt</c> (cases 3, 5, 6, 7; case 4 does not apply because no
/// UniqueType is half-deprecated), plus the validator-filtered replacement texts of the ten multi-alternative entries.
/// <para>
/// <c>Golden/DeprecatedUniquesValidated.golden.tsv</c> (entry name, sign variant, unique text, replacement text) was produced by the real Kotlin
/// <c>Unique.getReplacementText(vanillaRuleset)</c> (<c>UniqueValidator(ruleset).checkUnique(...).isEmpty()</c> filter) in a throwaway test in <c>tests/</c>
/// (run with <c>./gradlew :tests:test --offline</c>, not committed) for every <c>DeprecatedUniqueType</c> whose replacement has several alternatives.
/// The unique text is the entry text with its i-th <c>[parameter]</c> replaced by <c>[10*(i+1)]</c>, <c>[-10*(i+1)]</c> or <c>[+10*(i+1)]</c>.
/// </para>
/// </summary>
[Collection("RulesetCache")]
public sealed class DeprecatedUniquesValidatorTests : IDisposable
{
    private readonly ValidatorTestRuleset game = new();

    public void Dispose() => RulesetCache.Clear();

    [Fact]
    public void FullyDeprecatedUniqueReportsErrorInValidator()
    {
        var unique = new UniqueObject(DeprecatedUniqueTypes.Get(DeprecatedUniqueType.TripleGoldFromEncampmentsAndCities).Text);

        var errors = new UniqueValidator(RulesetCache.GetVanillaRuleset()).CheckUnique(unique, null);

        Assert.Contains(errors, it => it.ErrorSeverityToReport == RulesetErrorSeverity.ErrorOptionsOnly);
        var error = errors.Single();
        Assert.Equal("The unique \"Receive triple Gold from Barbarian encampments and pillaging Cities\" is deprecated As of 4.18.15, " +
            "replace with \"Receive [+200]% Gold from Barbarian encampments and pillaging Cities\"", error.Text);
    }

    [Fact]
    public void AutoUpdaterReplacesFullyDeprecatedUnique()
    {
        string deprecatedText = DeprecatedUniqueTypes.Get(DeprecatedUniqueType.TripleGoldFromEncampmentsAndCities).Text;
        game.CreateBuilding(deprecatedText);

        var replacements = UniqueAutoUpdater.GetDeprecatedReplaceableUniques(game.Ruleset, game.Ruleset);

        Assert.True(replacements.TryGetValue(deprecatedText, out string? replacementText));
        var replacement = new UniqueObject(replacementText);
        Assert.Null(replacement.GetDeprecationAnnotation());
        Assert.Null(DeprecatedUniqueTypes.Lookup(replacement.PlaceholderText));
    }

    [Fact]
    public void AutoUpdaterFollowsChainThroughDeprecatedUniqueType()
    {
        var chainStart = DeprecatedUniqueTypes.Get(DeprecatedUniqueType.DecreasedBuildingMaintenanceDeprecated);
        const string concreteText = "-[50]% maintenance cost for buildings [in all cities]";

        // Verify our test assumption: the replacement's placeholder matches BuildingMaintenanceOld
        Assert.Equal(DeprecatedUniqueTypes.Get(DeprecatedUniqueType.BuildingMaintenanceOld).PlaceholderText, chainStart.ReplaceWith.GetPlaceholderText());

        game.CreateBuilding(concreteText);

        var replacements = UniqueAutoUpdater.GetDeprecatedReplaceableUniques(game.Ruleset, game.Ruleset);

        Assert.True(replacements.TryGetValue(concreteText, out string? replacementText));
        var finalReplacement = new UniqueObject(replacementText);
        Assert.Null(finalReplacement.GetDeprecationAnnotation());
        Assert.Null(DeprecatedUniqueTypes.Lookup(finalReplacement.PlaceholderText));
        Assert.Equal(UniqueType.BuildingMaintenance, finalReplacement.Type);
    }

    [Fact]
    public void AutoUpdaterHandlesMixOfHalfAndFullyDeprecatedUniques()
    {
        // Fully deprecated (in DeprecatedUniqueType)
        game.CreateBuilding(DeprecatedUniqueTypes.Get(DeprecatedUniqueType.TripleGoldFromEncampmentsAndCities).Text);
        // FoodConsumptionBySpecialists: "[relativeAmount]% Food consumption by specialists [cityFilter]" (half-deprecated in UniqueType in older versions)
        game.CreateBuilding("[+50]% Food consumption by specialists [in all cities]");

        var replacements = UniqueAutoUpdater.GetDeprecatedReplaceableUniques(game.Ruleset, game.Ruleset);

        Assert.True(replacements.ContainsKey(DeprecatedUniqueTypes.Get(DeprecatedUniqueType.TripleGoldFromEncampmentsAndCities).Text));
        Assert.True(replacements.ContainsKey("[+50]% Food consumption by specialists [in all cities]"));

        // All replacements must be non-deprecated
        foreach (var (original, replacement) in replacements)
        {
            var replacementUnique = new UniqueObject(replacement);
            Assert.True(replacementUnique.GetDeprecationAnnotation() == null, $"Replacement for '{original}' is still deprecated: '{replacement}'");
        }
    }

    [Fact]
    public void AutoUpdaterReplacesInJsonTextOnlyQuotedAndConditionalOccurrences()
    {
        var folder = new TestFolder("mod", new Dictionary<string, object>
        {
            ["jsons"] = new Dictionary<string, object>
            {
                ["Buildings.json"] = """[ { "name": "A", "uniques": ["Receive triple Gold from Barbarian encampments and pillaging Cities", "[+1 Gold] <Receive triple Gold from Barbarian encampments and pillaging Cities>"] } ]""",
                ["Notes.json"] = "Receive triple Gold from Barbarian encampments and pillaging Cities",
            },
        });
        var mod = new Unciv.Models.Ruleset.Ruleset { Name = "mod", FolderLocation = folder };
        var replaceable = new Unciv.Utils.JavaHashMap<string, string>();
        replaceable["Receive triple Gold from Barbarian encampments and pillaging Cities"] = "Receive [+200]% Gold from Barbarian encampments and pillaging Cities";

        var result = UniqueAutoUpdater.AutoupdateUniques(mod, replaceable);

        // Only the files that are known ruleset files and exist are processed
        var (fileName, newText) = Assert.Single(result);
        Assert.Equal("Buildings.json", fileName);
        Assert.Equal(
            """[ { "name": "A", "uniques": ["Receive [+200]% Gold from Barbarian encampments and pillaging Cities", "[+1 Gold] <Receive [+200]% Gold from Barbarian encampments and pillaging Cities>"] } ]""",
            newText);
    }

    [Fact]
    public void MultiAlternativeReplacementsMatchKotlin()
    {
        var ruleset = RulesetCache.GetVanillaRuleset();
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Models", "Ruleset", "Unique", "Golden", "DeprecatedUniquesValidated.golden.tsv"));
        Assert.Equal(30, lines.Length);
        foreach (string line in lines)
        {
            string[] cols = line.Split('\t');
            Assert.Equal(4, cols.Length);
            var unique = new UniqueObject(cols[2]);
            Assert.True(unique.DeprecatedType != null, $"{cols[0]}: {cols[2]} does not resolve to a deprecated type");
            Assert.Equal(cols[3], unique.GetReplacementText(ruleset));
        }
    }
}
