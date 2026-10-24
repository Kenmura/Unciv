using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Validation;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Validation;

/// <summary>
/// Ported from <c>tests/src/com/unciv/uniques/UniqueErrorTests.kt</c>. <c>TestGame</c> (T035) is replaced by <see cref="ValidatorTestRuleset"/>.
/// Not ported: <c>testEducatedEliteGreatPersonGifting</c>, which plays turns of a game (GameInfo, Civilization, T046 and later).
/// </summary>
[Collection("RulesetCache")]
public sealed class UniqueErrorTests : IDisposable
{
    private readonly ValidatorTestRuleset game = new();

    public void Dispose() => RulesetCache.Clear();

    private static RulesetErrorList ValidateUnique(string uniqueText) =>
        new UniqueValidator(RulesetCache.GetVanillaRuleset()).CheckUnique(new UniqueObject(uniqueText), null);

    [Fact]
    public void TestMultipleUniqueTypesSameText()
    {
        var textToUniqueType = new Dictionary<string, UniqueType>();
        var errors = new List<string>();
        foreach (var uniqueType in UniqueTypeExtensions.Entries)
        {
            if (textToUniqueType.TryGetValue(uniqueType.PlaceholderText(), out var existing))
                errors.Add($"UniqueTypes {uniqueType} and {existing} have the same text!");
            else textToUniqueType[uniqueType.PlaceholderText()] = uniqueType;
        }
        Assert.Empty(errors);
    }

    [Fact]
    public void TestCodependantTechs()
    {
        var ruleset = RulesetCache.GetVanillaRuleset();

        // Create a prerequisite loop
        var techWithPrerequisites = ruleset.Technologies.Values.First(it => it.Prerequisites.Count != 0);
        var prereq = ruleset.Technologies[techWithPrerequisites.Prerequisites.First()];
        prereq.Prerequisites.Add(techWithPrerequisites.Name);
        ruleset.ModOptions.IsBaseRuleset = true;

        // Check mod links and ensure we don't get a crash, instead we get errors
        var errors = ruleset.GetErrorList(false);
        Assert.True(errors.IsNotOK());
    }

    [Fact]
    public void TestPromotionCircularReferenceWithAcyclicAlternativeIsAllowed()
    {
        var root = game.CreateUnitPromotion();
        var firstBranch = game.CreateUnitPromotion();
        var secondBranch = game.CreateUnitPromotion();
        foreach (var promotion in new[] { root, firstBranch, secondBranch }) promotion.UnitTypes = ["Scout"];

        firstBranch.Prerequisites = [secondBranch.Name, root.Name];
        secondBranch.Prerequisites = [firstBranch.Name];
        game.Ruleset.ModOptions.IsBaseRuleset = true;

        var errors = game.Ruleset.GetErrorList(false);
        Assert.DoesNotContain(errors, it => it.Text.StartsWith("Circular Reference in Promotions", StringComparison.Ordinal));
    }

    [Fact]
    public void TestPromotionCircularReferenceWithoutAcyclicAlternativeIsWarned()
    {
        var firstBranch = game.CreateUnitPromotion();
        var secondBranch = game.CreateUnitPromotion();
        foreach (var promotion in new[] { firstBranch, secondBranch }) promotion.UnitTypes = ["Scout"];

        firstBranch.Prerequisites = [secondBranch.Name];
        secondBranch.Prerequisites = [firstBranch.Name];
        game.Ruleset.ModOptions.IsBaseRuleset = true;

        var errors = game.Ruleset.GetErrorList(false);
        Assert.Single(errors);
        Assert.Equal($"Circular Reference in Promotions: {firstBranch.Name}→{secondBranch.Name}→{firstBranch.Name}", errors.Single().Text);
    }

    [Fact]
    public void TestTimedGlobalUniqueAcceptsTriggerConditionsWhenOnUnit()
    {
        var ruleset = RulesetCache.GetVanillaRuleset();
        // Since the <for [3] turns> turns this unique into a triggerable, the <upon> trigger condition should be ok
        const string uniqueText = "[-5]% Strength <for [3] turns> <upon damaging a [Warrior] unit>";

        // Without a unit, this is an error
        var uniqueNoSourceObject = new UniqueObject(uniqueText);
        var errorListNoSourceObject = new UniqueValidator(ruleset).CheckUnique(uniqueNoSourceObject, null);
        Assert.Equal(RulesetErrorSeverity.Warning, errorListNoSourceObject.GetFinalSeverity());

        // When applied on a unit or promotion etc, this is fine
        var uniqueWithSourceObject = new UniqueObject(uniqueText, sourceObjectType: UniqueTarget.Promotion);
        var errorListCorrectUniqueContainer = new UniqueValidator(ruleset).CheckUnique(uniqueWithSourceObject, null);
        Assert.Equal(RulesetErrorSeverity.OK, errorListCorrectUniqueContainer.GetFinalSeverity());
    }

    [Fact]
    public void TestOneTimeGainStatAcceptsCivWideStats()
    {
        var errors = ValidateUnique("Gain [10] [Gold]");
        Assert.Equal(RulesetErrorSeverity.OK, errors.GetFinalSeverity());
    }

    [Fact]
    public void TestOneTimeGainStatRejectsNonCivWideStats() => AssertOnlyCivWideStatsError("Gain [10] [Food]", "Food");

    [Fact]
    public void TestOneTimeGainStatRangeRejectsNonCivWideStats() => AssertOnlyCivWideStatsError("Gain [5]-[10] [Production]", "Production");

    private static void AssertOnlyCivWideStatsError(string uniqueText, string statName)
    {
        var error = ValidateUnique(uniqueText)
            .Single(it => it.Text.Contains(UniqueValidator.WhichDoesNotFitParameterType, StringComparison.Ordinal));
        Assert.Equal(RulesetErrorSeverity.Error, error.ErrorSeverityToReport);
        Assert.Contains(statName, error.Text, StringComparison.Ordinal);
        Assert.Contains(UniqueParameterType.CivWideStatName.ParameterName(), error.Text, StringComparison.Ordinal);
    }
}
