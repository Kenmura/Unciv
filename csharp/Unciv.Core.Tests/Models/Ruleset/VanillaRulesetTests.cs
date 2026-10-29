using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Validation;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Models.Ruleset;

/// <summary>
/// T037: golden test that loads the shipped base rulesets (Vanilla and Gods &amp; Kings; the repository bundles no mods, they are downloaded) and validates them.
/// Kotlin's LoadGameTest.loadRulesets only calls <c>RulesetCache.loadRulesets(noMods = true)</c>, which is the first step here.
/// The expected counts are the number of top-level entries in the asset JSON files (checked independently of the loader with a lenient JSON parse where the
/// files parse strictly: Buildings, CityStateTypes, Policies, Quests, Specialists, Speeds, UnitTypes, Units, VictoryTypes (Vanilla); Beliefs, Events,
/// Personalities, Religions (G&amp;K)), and the remaining ones are pinned from the loader output and checked against the unique ids of the JSON files.
/// </summary>
[Collection("RulesetCache")]
public sealed class VanillaRulesetTests : IDisposable
{
    public VanillaRulesetTests() => RulesetCache.Clear();

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private static RulesetClass LoadBase(BaseRuleset baseRuleset)
    {
        var errors = RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(JsonsPath(), Path.Combine(JsonsPath(), "no mods here")), noMods: true);
        Assert.Empty(errors);
        return RulesetCache.Get(baseRuleset.FullName())!;
    }

    [Fact]
    public void LoadRulesetsNoModsLoadsExactlyTheBaseRulesets()
    {
        LoadBase(BaseRuleset.Civ_V_Vanilla);
        Assert.Equal(["Civ V - Vanilla", "Civ V - Gods & Kings"], RulesetCache.Keys.Order(StringComparer.Ordinal).Reverse());
        Assert.Equal(2, RulesetCache.Count);
    }

    [Fact]
    public void VanillaObjectCounts()
    {
        var r = LoadBase(BaseRuleset.Civ_V_Vanilla);
        Assert.Equal(100, r.Buildings.Count);
        Assert.Equal(100, r.Units.Count);
        Assert.Equal(28, r.UnitTypes.Count);
        Assert.Equal(5, r.CityStateTypes.Count);
        Assert.Equal(5, r.Victories.Count);
        Assert.Equal(4, r.Specialists.Count);
        Assert.Equal(4, r.Speeds.Count);
        Assert.Equal(17, r.Quests.Count);
        Assert.Equal(74, r.Technologies.Count);
        Assert.Equal(62, r.Nations.Count);
        Assert.Equal(9, r.Eras.Count);
        Assert.Empty(r.Religions);
        Assert.Empty(r.Beliefs);
    }

    [Fact]
    public void GodsAndKingsObjectCounts()
    {
        var r = LoadBase(BaseRuleset.Civ_V_GnK);
        Assert.Equal(56, r.Beliefs.Count);
        Assert.Equal(14, r.Events.Count);
        Assert.Equal(42, r.Personalities.Count);
        Assert.Equal(11, r.Religions.Count);
        Assert.Equal(28, r.UnitTypes.Count);
        Assert.Equal(5, r.CityStateTypes.Count);
        Assert.Equal(5, r.Victories.Count);
        Assert.Equal(4, r.Specialists.Count);
        Assert.Equal(4, r.Speeds.Count);
        Assert.Equal(17, r.Quests.Count);
    }

    [Fact]
    public void BnwGatesAreDeclaredForModOptionsAndOffInTheShippedRulesets()
    {
        UniqueType[] gates = [UniqueType.EnablesInternationalTradeRoutes, UniqueType.EnablesGreatWorksAndTourism, UniqueType.EnablesArchaeology,
            UniqueType.EnablesIdeologies, UniqueType.EnablesWorldCongress, UniqueType.BnwHappinessRules];
        Assert.All(gates, it => Assert.True(it.CanAcceptUniqueTarget(UniqueTarget.ModOptions)));
        Assert.All(gates, it => Assert.Contains(UniqueFlag.NoConditionals, it.Flags()));
        foreach (var baseRuleset in Enum.GetValues<BaseRuleset>().Where(it => it != BaseRuleset.Civ_V_BNW))
        {
            var modOptions = (IHasUniques)LoadBase(baseRuleset).ModOptions;
            Assert.All(gates, it => Assert.False(modOptions.HasUnique(it), baseRuleset.FullName() + " " + it));
        }
        Assert.Equal(6, UniqueTypeExtensions.Entries.Count(it => it.TargetTypes().Contains(UniqueTarget.ModOptions) && (int)it >= (int)UniqueType.EnablesInternationalTradeRoutes));
    }

    [Fact]
    public void ObjectNamesAreUniqueKeysAndOriginIsSet()
    {
        foreach (var baseRuleset in Enum.GetValues<BaseRuleset>().Where(it => it != BaseRuleset.Civ_V_BNW))
        {
            var r = LoadBase(baseRuleset);
            Assert.NotEmpty(r.Buildings);
            foreach (var building in r.Buildings) Assert.Equal(building.Key, building.Value.Name);
            foreach (var unit in r.Units) Assert.Equal(unit.Key, unit.Value.Name);
            foreach (var tech in r.Technologies) Assert.Equal(tech.Key, tech.Value.Name);
            Assert.All(r.Units.Values, it => Assert.Equal(r.Name, it.OriginRuleset));
            Assert.All(r.Technologies.Values, it => Assert.Equal(r.Name, it.OriginRuleset));
        }
    }

    [Theory]
    [InlineData(BaseRuleset.Civ_V_Vanilla)]
    [InlineData(BaseRuleset.Civ_V_GnK)]
    public void BaseRulesetValidatesWithoutErrors(BaseRuleset baseRuleset)
    {
        var r = LoadBase(baseRuleset);
        var errors = r.GetErrorList();
        // Kotlin's RulesetValidation test requires no Error severity for the base rulesets
        Assert.False(errors.IsError(), errors.GetErrorText());
    }

    [Fact]
    public void ComplexRulesetOfGodsAndKingsValidates()
    {
        LoadBase(BaseRuleset.Civ_V_Vanilla);
        var combined = RulesetCache.GetComplexRuleset(new GameParameters());
        Assert.False(combined.GetErrorList().IsError());
    }
}
