using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Validation;
using Unciv.Models.Translations;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Validation;

/// <summary>
/// T036: the pieces of the validation package that the Kotlin fixtures do not cover directly. Kotlin has no test for these, so the expectations are
/// derived by hand from the Kotlin source (TextSimilarity.kt, Suppression.kt, RulesetErrorList.kt, ModCompatibility.kt, BaseRulesetValidator.kt, RulesetValidator.kt).
/// </summary>
[Collection("RulesetCache")]
public sealed class RulesetValidatorUnitTests : IDisposable
{
    private readonly ValidatorTestRuleset game = new();

    public void Dispose() => RulesetCache.Clear();

    // region TextSimilarity

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "abc", 0)]
    [InlineData("Abc", "abc", 1)] // equal ignoring case: +1
    [InlineData("abc", "abd", 1)] // no match for either: skip both
    [InlineData("abcd", "acd", 1)] // 'c' found one ahead in the first string only
    [InlineData("ab", "abxyzw", 2)] // tail: (0 + 4) / 2
    [InlineData("on [] tiles", "in [] tiles", 14)] // the weak case for errors at the start: 7 + 7
    [InlineData("seilt [] no", "seilt [] ni", 1)] // and the same reversed
    public void GetTextDistance(string a, string b, int expected) =>
        Assert.Equal(expected, TextSimilarity.GetTextDistance(a, b));

    [Fact]
    public void RelativeTextDistanceUsesTheBetterOfForwardAndReversed()
    {
        Assert.Equal(2.0 / 22, TextSimilarity.GetRelativeTextDistance("on [] tiles", "in [] tiles"), 12);
        Assert.Equal(0.0, TextSimilarity.GetRelativeTextDistance("same", "same"));
        Assert.Equal(1.0 / 3, TextSimilarity.GetRelativeTextDistance("abc", "abd"), 12);
    }

    // endregion
    // region Suppression and RulesetErrorList

    [Theory]
    [InlineData("*is supposed to automatically upgrade*", true)]
    [InlineData("a sufficiently selective text", true)]
    [InlineData("*too short*", false)] // 11 chars + 2 stars is below 14
    [InlineData("*unbalanced wildcard but long", false)]
    [InlineData("contains <angle> brackets and is long", false)]
    [InlineData("contains {braces} and is long enough", false)]
    [InlineData("unique \"X\" is deprecated as of 4.0, replace with \"Y\"", false)]
    [InlineData("unique \"X\" not found in Unciv's unique types, and is not used as a filtering unique", false)]
    public void IsValidFilter(string filter, bool expected) => Assert.Equal(expected, Suppression.IsValidFilter(filter));

    private static string SuppressionUnique(string filter) => UniqueType.SuppressWarnings.PlaceholderText().FillPlaceholders(filter);

    private static RulesetClass RulesetWithGlobalSuppression(string filter)
    {
        var ruleset = new RulesetClass();
        ruleset.ModOptions.Uniques = [SuppressionUnique(filter)];
        return ruleset;
    }

    [Fact]
    public void ErrorListKeepsTheHighestSeverityOfDuplicates()
    {
        var list = new RulesetErrorList();
        Assert.True(list.Add("same text", RulesetErrorSeverity.Warning));
        Assert.False(list.Add("same text", RulesetErrorSeverity.OK));
        Assert.False(list.Add("same text", RulesetErrorSeverity.Warning));
        Assert.True(list.Add("same text", RulesetErrorSeverity.Error));
        Assert.Single(list);
        Assert.Equal(RulesetErrorSeverity.Error, list[0].ErrorSeverityToReport);
        Assert.True(list.IsError());
    }

    [Fact]
    public void ErrorListAddAllChecksDuplicatesButNotSuppression()
    {
        var ruleset = RulesetWithGlobalSuppression("*suppressed warning*");
        var list = new RulesetErrorList(ruleset);
        list.Add("a suppressed warning text", RulesetErrorSeverity.Warning, sourceObject: null); // dropped
        Assert.Empty(list);

        var source = new RulesetErrorList();
        source.Add("a suppressed warning text", RulesetErrorSeverity.Warning);
        source.Add("other", RulesetErrorSeverity.OK);
        Assert.True(list.AddAll(source));
        Assert.Equal(["a suppressed warning text", "other"], list.Select(it => it.Text));
        Assert.False(list.AddAll(source));
    }

    [Fact]
    public void ErrorsCanNotBeSuppressedButWarningsCanByGlobalObjectAndModifierFilters()
    {
        var ruleset = RulesetWithGlobalSuppression("*global filter text*");
        var list = new RulesetErrorList(ruleset);
        Assert.False(list.Add("some Global Filter Text here", RulesetErrorSeverity.WarningOptionsOnly, sourceObject: null)); // case-insensitive
        Assert.True(list.Add("some Global Filter Text here", RulesetErrorSeverity.Error, sourceObject: null));

        var building = new Building { Name = "Hut" };
        building.Uniques.Add(SuppressionUnique("object scoped"));
        Assert.False(list.Add("an object scoped message", RulesetErrorSeverity.Warning, building)); // no wildcards needed on an object
        Assert.True(list.Add("an object scoped message", RulesetErrorSeverity.Warning, sourceObject: null));

        var unique = new UniqueObject("[+1 Gold] <" + SuppressionUnique("modifier scoped") + ">");
        Assert.False(list.Add("a modifier scoped message", RulesetErrorSeverity.Warning, sourceObject: null, unique));
        Assert.False(list.Add("exact", RulesetErrorSeverity.OK, building, new UniqueObject("[+1 Gold] <" + SuppressionUnique("exact") + ">")));
    }

    [Fact]
    public void ErrorListSeverityAndText()
    {
        var list = new RulesetErrorList();
        Assert.Equal(RulesetErrorSeverity.OK, list.GetFinalSeverity());
        Assert.False(list.IsNotOK());
        list.Add("hint", RulesetErrorSeverity.WarningOptionsOnly);
        list.Add("a <b> [c]", RulesetErrorSeverity.ErrorOptionsOnly);
        list.Add("warn", RulesetErrorSeverity.Warning);
        Assert.Equal(RulesetErrorSeverity.ErrorOptionsOnly, list.GetFinalSeverity());
        Assert.True(list.IsNotOK());
        Assert.False(list.IsError());
        Assert.True(list.IsWarnUser());
        // sorted descending (stable), the filtered-out WarningOptionsOnly is shown only when unfiltered, and brackets are replaced by look-alikes
        Assert.Equal("ErrorOptionsOnly: a 〈b〉 ⟦c⟧\nWarning: warn", list.GetErrorText());
        Assert.Equal("ErrorOptionsOnly: a 〈b〉 ⟦c⟧\nWarning: warn\nWarningOptionsOnly: hint", list.GetErrorText(unfiltered: true));
        Assert.Equal("Warning: warn", list.GetErrorText(it => it.Text == "warn"));
    }

    [Fact]
    public void OfBuildsAnEmptyListWhenSuppressed()
    {
        Assert.Single(RulesetErrorList.Of("x"));
        var ruleset = RulesetWithGlobalSuppression("*some suppressed*");
        Assert.Empty(RulesetErrorList.Of("some suppressed text", RulesetErrorSeverity.Warning, ruleset));
    }

    [Fact]
    public void SeverityMappingAndPresentation()
    {
        Assert.Equal(RulesetErrorSeverity.WarningOptionsOnly, UniqueParameterErrorSeverity.PossibleFilteringUnique.GetRulesetErrorSeverity());
        Assert.Equal(RulesetErrorSeverity.Warning, UniqueParameterErrorSeverity.RulesetSpecific.GetRulesetErrorSeverity());
        Assert.Equal(RulesetErrorSeverity.Error, UniqueParameterErrorSeverity.RulesetInvariant.GetRulesetErrorSeverity());
        Assert.Equal("OtherIcons/Checkmark", RulesetErrorSeverity.OK.IconName());
        Assert.Equal("OtherIcons/ExclamationMark", RulesetErrorSeverity.ErrorOptionsOnly.IconName());
        Assert.Equal("OtherIcons/Stop", RulesetErrorSeverity.Error.IconName());
        Assert.Equal(new EngineColor(1f, 0f, 0f, 1f), RulesetErrorSeverity.Error.Color());
    }

    // endregion
    // region ModCompatibility

    private static RulesetClass Mod(string name, params string[] uniques)
    {
        var ruleset = new RulesetClass { Name = name };
        ruleset.ModOptions.Uniques = [.. uniques];
        return ruleset;
    }

    [Fact]
    public void ModNameFilter()
    {
        Assert.True(ModCompatibility.ModNameFilter("Some Mod", "Some Mod"));
        Assert.True(ModCompatibility.ModNameFilter("Some Mod", "*some*"));
        Assert.False(ModCompatibility.ModNameFilter("Some Mod", "some"));
        Assert.False(ModCompatibility.ModNameFilter("Some Mod", "**")); // too short for a pattern
    }

    [Fact]
    public void MeetsBaseAndAllRequirements()
    {
        var baseRuleset = Mod("Base");
        baseRuleset.ModOptions.IsBaseRuleset = true;
        var other = Mod("Other");
        var needsBase = Mod("NeedsBase", "Mod requires [Base]");
        var needsOther = Mod("NeedsOther", "Mod requires [Other]");
        var incompatible = Mod("Incompatible", "Mod is incompatible with [Other]");
        RulesetCache.Put("Base", baseRuleset);
        RulesetCache.Put("Other", other);

        Assert.True(ModCompatibility.MeetsBaseRequirements(needsBase, baseRuleset));
        Assert.True(ModCompatibility.MeetsBaseRequirements(needsOther, baseRuleset)); // Other is a cached extension mod
        Assert.False(ModCompatibility.MeetsBaseRequirements(Mod("Needs", "Mod requires [Absent]"), baseRuleset));
        Assert.False(ModCompatibility.MeetsBaseRequirements(Mod("X", "Mod is incompatible with [Base]"), baseRuleset));
        Assert.True(ModCompatibility.MeetsBaseRequirements(incompatible, baseRuleset)); // not incompatible with the base

        Assert.True(ModCompatibility.MeetsAllRequirements(needsOther, baseRuleset, [other, needsOther])); // itself is ignored
        Assert.False(ModCompatibility.MeetsAllRequirements(needsOther, baseRuleset, [needsOther]));
        Assert.False(ModCompatibility.MeetsAllRequirements(incompatible, baseRuleset, [other]));
        Assert.False(ModCompatibility.MeetsAllRequirements(other, baseRuleset, [incompatible])); // symmetric
    }

    [Fact]
    public void ExtensionModAndAudioVisualAndConstantsOnly()
    {
        Assert.True(ModCompatibility.IsExtensionMod(Mod("Mod")));
        Assert.False(ModCompatibility.IsExtensionMod(Mod("")));
        var baseRuleset = Mod("Base");
        baseRuleset.ModOptions.IsBaseRuleset = true;
        Assert.False(ModCompatibility.IsExtensionMod(baseRuleset));

        Assert.True(ModCompatibility.IsAudioVisualMod(Mod("Mod", UniqueType.ModIsAudioVisual.Text())));
        Assert.False(ModCompatibility.IsAudioVisualMod(Mod("Mod", UniqueType.ModIsNotAudioVisual.Text())));
        Assert.False(ModCompatibility.IsAudioVisualMod(Mod("Mod"))); // no folder: not guessed

        var withSounds = Mod("Mod");
        withSounds.FolderLocation = new TestFolder("Mod", new() { ["sounds"] = new Dictionary<string, object> { ["a.ogg"] = "" } });
        Assert.True(ModCompatibility.IsAudioVisualMod(withSounds));
        var withAtlas = Mod("Mod");
        withAtlas.FolderLocation = new TestFolder("Mod", new() { ["Images.atlas"] = "" });
        Assert.True(ModCompatibility.IsAudioVisualMod(withAtlas));
        Assert.False(ModCompatibility.IsConstantsOnly(withAtlas));

        var constantsOnly = Mod("Mod");
        constantsOnly.FolderLocation = new TestFolder("Mod", new() { ["jsons"] = new Dictionary<string, object> { ["ModOptions.json"] = "{}" } });
        Assert.True(ModCompatibility.IsConstantsOnly(constantsOnly));
        Assert.False(ModCompatibility.IsAudioVisualMod(constantsOnly));
        Assert.False(ModCompatibility.IsConstantsOnly(Mod("Mod")));
    }

    // endregion
    // region RulesetValidator checks

    private RulesetErrorList Errors() => game.Ruleset.GetErrorList();

    [Fact]
    public void BuiltInRulesetsValidateWithoutAnyMessage()
    {
        foreach (var baseRuleset in Enum.GetValues<BaseRuleset>().Where(it => it != BaseRuleset.Civ_V_BNW))
            Assert.Empty(RulesetCache.Get(baseRuleset.FullName())!.GetErrorList(tryFixUnknownUniques: true).Select(it => $"{it.ErrorSeverityToReport}: {it.Text}"));
    }

    [Fact]
    public void ReferencesToMissingObjectsAreErrorsOnlyInABaseRuleset()
    {
        var building = game.CreateBuilding();
        building.RequiredTech = "Missing Tech";
        building.UniqueTo = "Nowhere";
        Assert.Contains(Errors(), it => it.Text == $"{building.Name} requires tech Missing Tech which does not exist!" && it.ErrorSeverityToReport == RulesetErrorSeverity.Error);

        // An extension mod is validated without its base, so RulesetSpecific checks do not run
        game.Ruleset.ModOptions.IsBaseRuleset = false;
        Assert.DoesNotContain(Errors(), it => it.Text.Contains("Missing Tech", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildingWithoutCostOrTechIsAWarning()
    {
        var building = game.CreateBuilding();
        Assert.Contains(Errors(), it => it.ErrorSeverityToReport == RulesetErrorSeverity.Warning
            && it.Text == $"{building.Name} is buildable and therefore should either have an explicit cost or reference an existing tech!");
        building.Cost = 100;
        Assert.DoesNotContain(Errors(), it => it.Text.StartsWith(building.Name + " is buildable", StringComparison.Ordinal));
    }

    [Fact]
    public void UniqueToMisspellingIsAHintAtOkSeverity()
    {
        var nation = game.Ruleset.Nations.Values.First(it => it.Name.Length > 8);
        var building = game.CreateBuilding();
        building.Cost = 10;
        building.UniqueTo = nation.Name + "x";
        var hint = Errors().Single(it => it.Text.StartsWith(building.Name + " has uniqueTo", StringComparison.Ordinal));
        Assert.Equal(RulesetErrorSeverity.OK, hint.ErrorSeverityToReport);
        Assert.Contains($"may be a misspelling of \"{nation.Name}\"", hint.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ModConstantsOutOfRangeAreReportedWithTheKotlinMessage()
    {
        game.Ruleset.ModOptions.Constants.CityWorkRange = 0;
        game.Ruleset.ModOptions.Constants.BaseCityBombardRange = 1001;
        var errors = Errors();
        // the stray '}' after the property name is part of the Kotlin message
        Assert.Contains(errors, it => it.Text == "ModConstant 'cityWorkRange}' does not meet criteria 'Minimum 1, Maximum 100'.");
        Assert.Contains(errors, it => it.Text == "ModConstant 'baseCityBombardRange}' does not meet criteria 'Minimum 0, Maximum 1000'.");
        Assert.DoesNotContain(errors, it => it.Text.Contains("cityExpandRange", StringComparison.Ordinal));
    }

    [Fact]
    public void CircularEventTriggersAreWarnedInJavaHashSetOrder()
    {
        var eventA = new Event { Name = "A" };
        var choiceA = new EventChoice { Text = "go" };
        choiceA.Uniques.Add("Triggers a [B] event");
        eventA.Choices = [choiceA];
        var eventB = new Event { Name = "B" };
        var choiceB = new EventChoice { Text = "back" };
        choiceB.Uniques.Add("Triggers a [A] event");
        eventB.Choices = [choiceB];
        game.Ruleset.Events["A"] = eventA;
        game.Ruleset.Events["B"] = eventB;

        var texts = Errors().Where(it => it.Text.StartsWith("Circular trigger in Events", StringComparison.Ordinal)).Select(it => it.Text).ToList();
        // starting at A the history is {A}, then {A, B}; starting at B it is {B}, then the java.util.HashSet orders {A, B} by hash bucket
        Assert.Equal(
        [
            "Circular trigger in Events: A→B→A - will cause an infinite loop for AI!",
            "Circular trigger in Events: A→B→B - will cause an infinite loop for AI!",
        ], texts);
    }

    [Fact]
    public void EventChoiceUnreachableForAiBreaksTheCycle()
    {
        var eventA = new Event { Name = "A" };
        var choiceA = new EventChoice { Text = "go" };
        choiceA.Uniques.AddRange(["Triggers a [B] event", "Unavailable <for [AI player] Civilizations>"]);
        eventA.Choices = [choiceA];
        var eventB = new Event { Name = "B" };
        var choiceB = new EventChoice { Text = "back" };
        choiceB.Uniques.Add("Triggers a [A] event");
        eventB.Choices = [choiceB];
        game.Ruleset.Events["A"] = eventA;
        game.Ruleset.Events["B"] = eventB;

        Assert.DoesNotContain(Errors(), it => it.Text.StartsWith("Circular trigger in Events", StringComparison.Ordinal));
    }

    [Fact]
    public void NationColorsWithoutContrastGetSuggestedColors()
    {
        var nation = game.Ruleset.Nations.Values.First(it => !it.IsCityState && !it.IsBarbarian && !it.IsSpectator);
        nation.InnerColor = [10, 10, 10];
        nation.OuterColor = [12, 12, 12];
        nation.SetTransients();
        var error = Errors().Single(it => it.Text.Contains("colors do not contrast enough", StringComparison.Ordinal));
        Assert.Equal(RulesetErrorSeverity.WarningOptionsOnly, error.ErrorSeverityToReport);
        Assert.StartsWith($"{nation.Name}'s colors do not contrast enough - it is unreadable!\nSuggested colors: \n\t\t\"outerColor\": [", error.Text, StringComparison.Ordinal);
        Assert.Contains("\n\t\t\"innerColor\": [", error.Text, StringComparison.Ordinal);

        nation.InnerColor = [10, 10];
        nation.SetTransients();
        Assert.Contains(Errors(), it => it.Text == $"{nation.Name}'s innerColor is not an array of three integers in the 0..255 range");
    }

    [Fact]
    public void PromotionsAtTheSamePositionAreErrors()
    {
        var first = game.CreateUnitPromotion();
        var second = game.CreateUnitPromotion();
        foreach (var promotion in new[] { first, second })
        {
            promotion.UnitTypes = ["Scout"];
            promotion.Row = 9;
            promotion.Column = 9;
        }
        Assert.Contains(Errors(), it => it.Text == $"Promotions {first.Name} and {second.Name} have the same position: 9/9");
    }

    [Fact]
    public void FreeBuildingGrantedAndRemovableIsWarnedAndSuppressible()
    {
        var granted = game.CreateBuilding("Gain a free [Barracks] [in this city]");
        var remover = game.CreateBuilding("Remove [Barracks] [in this city]");
        granted.Cost = remover.Cost = 10;
        var warning = Errors().Single(it => it.Text.StartsWith("Building \"Barracks\" is both granted free", StringComparison.Ordinal));
        Assert.Equal(RulesetErrorSeverity.WarningOptionsOnly, warning.ErrorSeverityToReport);
        Assert.Contains($"Grants: \"Gain a free [Barracks] [in this city]\" on Buildings \"{granted.Name}\",", warning.Text, StringComparison.Ordinal);
        Assert.Contains($"Possible removals: \"Remove [Barracks] [in this city]\" on Buildings \"{remover.Name}\"\n", warning.Text, StringComparison.Ordinal);

        // the building named in the message hosts the suppression unique, which the message itself spells out
        var suppressed = new ValidatorTestRuleset().Ruleset;
        suppressed.Buildings["Barracks"].Uniques.Add(SuppressionUnique("is both granted free and could possibly be removed by triggers"));
        var other = new Building { Name = "Other", OriginRuleset = suppressed.Name, Cost = 10 };
        other.Uniques.AddRange(["Gain a free [Barracks] [in this city]", "Remove [Barracks] [in this city]"]);
        suppressed.Buildings["Other"] = other;
        Assert.DoesNotContain(suppressed.GetErrorList(), it => it.Text.StartsWith("Building \"Barracks\" is both granted free", StringComparison.Ordinal));
    }

    [Fact]
    public void FileAndFolderNamesAreChecked()
    {
        game.Ruleset.FolderLocation = new TestFolder("Mod", new()
        {
            ["Units.json"] = "[]",
            ["Atlas.json"] = "[]",
            ["jsons_"] = new Dictionary<string, object>(),
            ["game2.png"] = "",
            ["Images"] = new Dictionary<string, object> { ["stray.png"] = "", ["TileSetz"] = new Dictionary<string, object>() },
            ["jsons"] = new Dictionary<string, object> { ["Unitz.json"] = "[]", ["Units.json"] = "[]" },
        });
        var errors = Errors();
        Assert.Contains(errors, it => it.Text == "File Units.json is located in the root folder - it should be moved to a 'jsons' folder" && it.ErrorSeverityToReport == RulesetErrorSeverity.Error);
        Assert.DoesNotContain(errors, it => it.Text.StartsWith("File Atlas.json", StringComparison.Ordinal));
        Assert.Contains(errors, it => it.Text == "Folder \"jsons_\" is probably a misspelling of jsons" && it.ErrorSeverityToReport == RulesetErrorSeverity.OK);
        Assert.Contains(errors, it => it.Text == "File \"Images/stray.png\" is misplaced - Images folders should not contain any files directly - only subfolders");
        Assert.Contains(errors, it => it.Text == "Folder \"Images/TileSetz\" is probably a misspelling of TileSets");
        Assert.Contains(errors, it => it.Text == "File Unitz.json is in the jsons folder but is not a recognized ruleset file\nPossible misspelling of: Units.json");
        Assert.Contains(errors, it => it.Text.StartsWith("Your images are being generated into multiple atlas files", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckCombinedModLinksValidatesTheCombinedRuleset()
    {
        var (ruleset, errors) = RulesetCache.CheckCombinedModLinks(new LinkedHashSet<string>(), BaseRuleset.Civ_V_GnK.FullName());
        Assert.NotNull(ruleset);
        Assert.True(ruleset.ModOptions.IsBaseRuleset);
        Assert.Empty(errors);
    }

    // endregion
}
