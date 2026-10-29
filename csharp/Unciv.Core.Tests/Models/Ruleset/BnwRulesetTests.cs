using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Validation;

namespace Unciv.Core.Tests.Models.Ruleset;

/// <summary>B002: the generated Brave New World ruleset (bnw-spec/tools/gen_ruleset.py) loads and validates without any message.</summary>
[Collection("RulesetCache")]
public sealed class BnwRulesetTests : IDisposable
{
    public BnwRulesetTests() => RulesetCache.Clear();

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

    internal static Unciv.Models.Ruleset.Ruleset LoadBnw()
    {
        var errors = RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(JsonsPath(), Path.Combine(JsonsPath(), "no mods here")), noMods: true);
        Assert.Empty(errors);
        return RulesetCache.Get(BaseRuleset.Civ_V_BNW.FullName())!;
    }

    [Fact]
    public void BnwValidatesWithoutAnyMessage()
    {
        var messages = LoadBnw().GetErrorList(tryFixUnknownUniques: true).Select(it => $"{it.ErrorSeverityToReport}: {it.Text}").ToList();
        Assert.Empty(messages);
    }

    [Fact]
    public void BnwHasTheGeneratedContentAndTheRenames()
    {
        var r = LoadBnw();
        Assert.Equal(81, r.Technologies.Count);
        Assert.Contains("The Internet", r.Technologies.Keys);
        Assert.Contains("Railroad", r.Technologies.Keys);
        Assert.Contains("Penicillin", r.Technologies.Keys);
        Assert.DoesNotContain("Railroads", r.Technologies.Keys);
        Assert.Equal(85, r.Technologies["Optics"].Cost);
        Assert.Equal(["Railroad", "Flight"], r.Technologies["Ballistics"].Prerequisites.Order(StringComparer.Ordinal).Reverse());
        foreach (var name in new[] { "Caravansary", "Zoo", "East India Company", "Airport", "Hotel", "Parthenon", "Artists' Guild", "Royal Library" })
            Assert.Contains(name, r.Buildings.Keys);
        Assert.DoesNotContain("Theatre", r.Buildings.Keys);
        Assert.DoesNotContain("Utopia Project", r.Buildings.Keys);
        Assert.DoesNotContain("United Nations", r.Buildings.Keys);
        Assert.Equal("Assyria", r.Buildings["Royal Library"].UniqueTo);
        Assert.Equal("Library", r.Buildings["Royal Library"].Replaces);
        Assert.Contains("Immortal", r.Units.Keys);
        Assert.Contains("Work Boat", r.Units.Keys);
        Assert.DoesNotContain("Persian Immortal", r.Units.Keys);
        foreach (var name in new[] { "Caravan", "Cargo Ship", "Archaeologist", "Great Writer", "Great Musician", "Merchant of Venice", "Impi", "Nau" })
            Assert.Contains(name, r.Units.Keys);
        Assert.DoesNotContain("Bazooka", r.Units.Keys);
        foreach (var name in new[] { "Assyria", "Brazil", "Indonesia", "Morocco", "Poland", "Portugal", "The Shoshone", "The Zulus", "Venice", "Ife", "Wellington" })
            Assert.Contains(name, r.Nations.Keys);
        Assert.True(r.Nations["Venice"].IsMajorCiv);
        Assert.False(r.Nations.ContainsKey("Dublin"));
        Assert.Equal(["Bison", "Cloves", "Cocoa", "Nutmeg", "Pepper"], new[] { "Bison", "Cloves", "Cocoa", "Nutmeg", "Pepper" }.Where(r.TileResources.ContainsKey));
        Assert.Contains("Writer", r.Specialists.Keys);
        Assert.False(r.Victories.ContainsKey("Cultural"));
    }

    [Fact]
    public void BnwModOptionsSwitchTheGatesAndTunablesOn()
    {
        var r = LoadBnw();
        var options = (IHasUniques)r.ModOptions;
        Assert.True(options.HasUnique(UniqueType.EnablesInternationalTradeRoutes));
        Assert.True(options.HasUnique(UniqueType.BnwHappinessRules));
        var c = r.ModOptions.Constants;
        Assert.Equal(105, c.MinorLiberationFriendship);
        Assert.Equal(5, c.FriendshipPerUnitGifted);
        Assert.Equal(10, c.ReligionMissionaryPressureMultiplier);
        Assert.Equal(3, c.ResearchAgreementBoostDivisor);
        Assert.Equal(6, c.NumAndTechPrereqs);
        Assert.Equal(2, c.BaseCulturePerGreatWork);
        Assert.Equal(1.443, c.DiploVictoryCivDelegatesCoefficient);
        Assert.Equal(-13.758, c.DiploVictoryCsDelegatesConstant);
        Assert.Equal(-20, c.MinorFriendshipAnchorModWaryOf);
    }

    [Fact]
    public void BnwCombinesAsABaseRulesetAndCanBeRemovedFromTheCache()
    {
        LoadBnw();
        var combined = RulesetCache.GetComplexRuleset(new Unciv.Logic.Map.MapParameters { BaseRuleset = BaseRuleset.Civ_V_BNW.FullName() });
        Assert.Contains("Caravansary", combined.Buildings.Keys);
        Assert.True(RulesetCache.Remove(BaseRuleset.Civ_V_BNW.FullName()));
        Assert.False(RulesetCache.ContainsKey(BaseRuleset.Civ_V_BNW.FullName()));
    }
}
