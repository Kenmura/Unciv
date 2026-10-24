using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Validation;
using Unciv.Models.Translations;

namespace Unciv.Core.Tests.Models.Ruleset.Validation;

/// <summary>
/// Ported from <c>tests/src/com/unciv/uniques/RulesetValidatorTests.kt</c>. <c>TestGame</c> (T035) is replaced by <see cref="ValidatorTestRuleset"/>,
/// which provides what these tests use from it.
/// </summary>
[Collection("RulesetCache")]
public sealed class RulesetValidatorTests : IDisposable
{
    private readonly ValidatorTestRuleset game = new();

    public void Dispose() => RulesetCache.Clear();

    private Personality AddPersonality(string name, params string[] uniques)
    {
        var personality = new Personality { Name = name };
        personality.Uniques.AddRange(uniques);
        game.Ruleset.Personalities[name] = personality;
        return personality;
    }

    private bool HasRecursiveResourceUniqueError(string modifierText) =>
        game.Ruleset.GetErrorList().Any(it =>
            it.ErrorSeverityToReport == RulesetErrorSeverity.Error
            && it.Text.Contains(modifierText, StringComparison.Ordinal)
            && it.Text.Contains("recursive evaluation loop", StringComparison.Ordinal));

    private bool HasSelfReplacementError(string objectName) =>
        game.Ruleset.GetErrorList().Any(it =>
            it.ErrorSeverityToReport == RulesetErrorSeverity.Error
            && it.Text == $"{objectName} replaces itself!");

    [Fact]
    public void RulesetValidatorRejectsBuildingThatReplacesItself()
    {
        var building = game.CreateBuilding();
        building.UniqueTo = "Test nation";
        building.Replaces = building.Name;

        Assert.True(HasSelfReplacementError(building.Name));
    }

    [Fact]
    public void RulesetValidatorAcceptsBuildingThatReplacesAnotherBuilding()
    {
        var replacedBuilding = game.CreateBuilding();
        var replacementBuilding = game.CreateBuilding();
        replacementBuilding.Replaces = replacedBuilding.Name;
        replacementBuilding.UniqueTo = "Test nation";

        Assert.False(HasSelfReplacementError(replacementBuilding.Name));
    }

    [Fact]
    public void RulesetValidatorRejectsUnitThatReplacesItself()
    {
        var unit = game.CreateBaseUnit();
        unit.UniqueTo = "Test nation";
        unit.Replaces = unit.Name;

        Assert.True(HasSelfReplacementError(unit.Name));
    }

    [Fact]
    public void RulesetValidatorAcceptsUnitThatReplacesAnotherUnit()
    {
        var replacedUnit = game.CreateBaseUnit();
        var replacementUnit = game.CreateBaseUnit();
        replacementUnit.Replaces = replacedUnit.Name;
        replacementUnit.UniqueTo = "Test nation";

        Assert.False(HasSelfReplacementError(replacementUnit.Name));
    }

    [Fact]
    public void RulesetValidatorRejectsTileImprovementThatReplacesItself()
    {
        var improvement = game.CreateTileImprovement();
        improvement.UniqueTo = "Test nation";
        improvement.Replaces = improvement.Name;

        Assert.True(HasSelfReplacementError(improvement.Name));
    }

    [Fact]
    public void RulesetValidatorAcceptsTileImprovementThatReplacesAnotherImprovement()
    {
        var replacedImprovement = game.CreateTileImprovement();
        var replacementImprovement = game.CreateTileImprovement();
        replacementImprovement.Replaces = replacedImprovement.Name;
        replacementImprovement.UniqueTo = "Test nation";

        Assert.False(HasSelfReplacementError(replacementImprovement.Name));
    }

    private Building AddParkBuilding(string originRuleset)
    {
        var park = new Building { Name = "Park", OriginRuleset = originRuleset };
        park.Uniques.Add("Unbuildable");
        game.Ruleset.Buildings["Park"] = park;
        return park;
    }

    private static bool HasCollisionWarning(RulesetErrorList errors, string otherSource) =>
        errors.Any(it =>
            it.ErrorSeverityToReport == RulesetErrorSeverity.OK
            && it.Text.Contains("\"Park\"", StringComparison.Ordinal)
            && it.Text.Contains("Building", StringComparison.Ordinal)
            && it.Text.Contains(otherSource, StringComparison.Ordinal));

    [Fact]
    public void RulesetValidatorWarnsAboutSpyNameCollisionWithRulesetObject()
    {
        AddParkBuilding("Test mod");
        game.Ruleset.Nations.Values.First().SpyNames = ["Park"];

        Assert.True(HasCollisionWarning(game.Ruleset.GetErrorList(), "Nation.spyNames"));
    }

    [Fact]
    public void RulesetValidatorWarnsAboutNonRulesetObjectNameCollisions()
    {
        game.Ruleset.Name = "Test mod";
        AddParkBuilding("Test mod");
        game.Ruleset.Religions.Add("Park");

        Assert.True(HasCollisionWarning(game.Ruleset.GetErrorList(), "Religion"));
    }

    [Fact]
    public void RulesetValidatorWarnsWhenModNameCollisionHasEmptyOrigin()
    {
        game.Ruleset.Name = "";
        AddParkBuilding(BaseRuleset.Civ_V_GnK.FullName());
        game.Ruleset.Religions.Add("Park");

        Assert.True(HasCollisionWarning(game.Ruleset.GetErrorList(), "Religion"));
    }

    [Fact]
    public void RulesetValidatorWarnsAboutLeaderNameCollisions()
    {
        AddParkBuilding("Test mod");
        game.Ruleset.Nations.Values.First().LeaderName = "Park";

        Assert.True(HasCollisionWarning(game.Ruleset.GetErrorList(), "Nation.leaderName"));
    }

    [Fact]
    public void RulesetValidatorWarnsAboutCityNameCollisions()
    {
        AddParkBuilding("Test mod");
        game.Ruleset.Nations.Values.First().Cities = ["Park"];

        Assert.True(HasCollisionWarning(game.Ruleset.GetErrorList(), "Nation.cities"));
    }

    [Fact]
    public void RulesetValidatorAcceptsPersonalityWithoutUniques()
    {
        AddPersonality("Reserved");

        var errors = game.Ruleset.GetErrorList();

        Assert.DoesNotContain(errors, it => it.Text.Contains("Personality Uniques are not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void RulesetValidatorAcceptsSupportedPersonalityUniques()
    {
        string unique = UniqueType.WillNotBuild.Text().FillPlaceholders("Melee");
        AddPersonality("Builder Avoider", unique);

        var errors = game.Ruleset.GetErrorList();

        Assert.DoesNotContain(errors, it => it.Text.Contains("Personality Uniques are not supported", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, it => it.Text.Contains(unique, StringComparison.Ordinal));
    }

    [Fact]
    public void RulesetValidatorWarnsAboutNonPersonalityUniquesOnPersonalities()
    {
        AddPersonality("Unsupported Unique Holder", UniqueType.Unbuildable.Text());

        var errors = game.Ruleset.GetErrorList();

        Assert.Contains(errors, it =>
            it.ErrorSeverityToReport == RulesetErrorSeverity.Warning
            && it.Text.Contains(UniqueType.Unbuildable.Text(), StringComparison.Ordinal)
            && it.Text.Contains("not allowed on its target type", StringComparison.Ordinal));
    }

    [Fact]
    public void RulesetValidatorRejectsResourceUniqueForEveryCitywideResourceCountable()
    {
        var citywideResource = game.CreateResource(UniqueType.CityResource.Text());
        var providedResource = game.CreateResource();

        game.CreateBuilding($"Provides [1] [{providedResource.Name}] <for every [{citywideResource.Name}]>");

        Assert.True(HasRecursiveResourceUniqueError($"for every [{citywideResource.Name}]"));
    }

    [Fact]
    public void RulesetValidatorRejectsResourceUniqueWhenAboveCitywideResourceConditional()
    {
        var citywideResource = game.CreateResource(UniqueType.CityResource.Text());
        var providedResource = game.CreateResource();

        game.CreateBuilding($"Provides [1] [{providedResource.Name}] <when above [1] [{citywideResource.Name}]>");

        Assert.True(HasRecursiveResourceUniqueError($"when above [1] [{citywideResource.Name}]"));
    }

    [Fact]
    public void RulesetValidatorAcceptsResourceUniqueForEveryNormalResourceCountable()
    {
        var normalResource = game.CreateResource();
        var providedResource = game.CreateResource();

        game.CreateBuilding($"Provides [1] [{providedResource.Name}] <for every [{normalResource.Name}]>");

        Assert.False(HasRecursiveResourceUniqueError($"for every [{normalResource.Name}]"));
    }

    [Fact]
    public void RulesetValidatorAcceptsResourceUniqueForEveryNonResourceCountable()
    {
        var providedResource = game.CreateResource();

        game.CreateBuilding($"Provides [1] [{providedResource.Name}] <for every [Cities]>");

        Assert.False(HasRecursiveResourceUniqueError("for every [Cities]"));
    }
}
