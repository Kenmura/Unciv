using Unciv.Core.Tests.Support;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Core.Tests.Models.Ruleset;

/// <summary>B050 (bnw-spec 4.6.1): the Brave New World social policy tree: nine branches with era gates, the Piety religion lock, the
/// twelve branch-gated wonders and the finisher policies.</summary>
[Collection("RulesetCache")]
public sealed class BnwPolicyTreeTests
{
    private static readonly (string Branch, string Era)[] SocialBranches =
    [
        ("Tradition", "Ancient era"), ("Liberty", "Ancient era"), ("Honor", "Ancient era"), ("Piety", "Ancient era"),
        ("Patronage", "Classical era"), ("Aesthetics", "Classical era"), ("Commerce", "Medieval era"), ("Exploration", "Medieval era"),
        ("Rationalism", "Renaissance era"),
    ];

    private static readonly (string Wonder, string Branch)[] GatedWonders =
    [
        ("The Pyramids", "Liberty"), ("Hanging Gardens", "Tradition"), ("Statue of Zeus", "Honor"), ("Great Mosque of Djenne", "Piety"),
        ("Forbidden Palace", "Patronage"), ("Uffizi", "Aesthetics"), ("Big Ben", "Commerce"), ("The Louvre", "Exploration"),
        ("Porcelain Tower", "Rationalism"), ("Statue of Liberty", "Freedom"), ("Kremlin", "Order"), ("Prora", "Autocracy"),
    ];

    private readonly TestGame game = new();

    [Fact]
    public void TheTreeHasNineSocialBranchesAndTheThreeIdeologies()
    {
        var branches = BnwRulesetTests.LoadBnw().PolicyBranches;
        Assert.Equal(SocialBranches.Select(it => it.Branch).Concat(["Freedom", "Order", "Autocracy"]), branches.Keys);
    }

    [Fact]
    public void BranchesAreGatedByTheEraOfTheirTable()
    {
        var branches = BnwRulesetTests.LoadBnw().PolicyBranches;
        foreach (var (branch, era) in SocialBranches)
            Assert.Equal(era, branches[branch].Era);
        foreach (var ideology in new[] { "Freedom", "Order", "Autocracy" })
            Assert.Equal("Industrial era", branches[ideology].Era);
    }

    [Fact]
    public void EachSocialBranchHasFivePoliciesAndItsFinisherLast()
    {
        var ruleset = BnwRulesetTests.LoadBnw();
        foreach (var (name, _) in SocialBranches)
        {
            var branch = ruleset.PolicyBranches[name];
            Assert.Equal(5, branch.Policies.Count - 1);
            Assert.Equal(name + " Complete", branch.Policies[^1].Name);
            Assert.Equal(Unciv.Models.Ruleset.Policy.PolicyBranchTypes.BranchComplete, branch.Policies[^1].PolicyBranchType);
            var members = branch.Policies.SkipLast(1).ToList();
            Assert.Equal(members.Count, members.Select(it => (it.Row, it.Column)).Distinct().Count());
            Assert.All(members, it => Assert.All(it.Requires ?? [], req => Assert.Contains(req, members.Select(m => m.Name).Append(name))));
        }
    }

    [Fact]
    public void TheNewAndRenamedPoliciesFollowThePrerequisiteTable()
    {
        var policies = BnwRulesetTests.LoadBnw().Policies;
        Assert.Equal(["Cultural Centers", "Fine Arts"], policies["Flourishing of the Arts"].Requires);
        Assert.Equal(["Flourishing of the Arts"], policies["Cultural Exchange"].Requires);
        Assert.Equal(["Naval Tradition", "Maritime Infrastructure"], policies["Merchant Navy"].Requires);
        Assert.Equal(["Merchant Navy"], policies["Treasure Fleets"].Requires);
        Assert.Equal(["Scholasticism", "Consulates"], policies["Merchant Confederacy"].Requires);
        Assert.Equal(["Religious Tolerance"], policies["Reformation"].Requires);
        Assert.Equal(["Organized Religion"], policies["Religious Tolerance"].Requires);
        Assert.Equal(["Mercantilism", "Entrepreneurship"], policies["Protectionism"].Requires);
        Assert.Equal(["Wagon Trains"], policies["Entrepreneurship"].Requires);
        Assert.Equal(["Mercenary Army"], policies["Mercantilism"].Requires);
        foreach (var gone in new[] { "Free Religion", "Philantropy", "Trade Unions", "Educated Elite", "Mandate Of Heaven", "Caravans" })
            Assert.DoesNotContain(gone, policies.Keys);
    }

    [Fact]
    public void FinishersCarryTheBnwEffects()
    {
        var policies = BnwRulesetTests.LoadBnw().Policies;
        Assert.Contains("Free [Great Prophet] appears", policies["Piety Complete"].Uniques);
        Assert.Contains("[+3 Culture] from every [Holy site]", policies["Piety Complete"].Uniques);
        Assert.Contains("[+1 Gold] from every [Trading post]", policies["Commerce Complete"].Uniques);
        Assert.Contains("[1] Free Technologies", policies["Rationalism Complete"].Uniques);
        Assert.Contains("[+100]% theming bonuses [in all cities]", policies["Aesthetics Complete"].Uniques);
        Assert.Contains("Allied City-States will occasionally gift Great People", policies["Patronage Complete"].Uniques);
        Assert.Empty(policies["Exploration Complete"].Uniques); // reveals Antiquity Sites: B040
        Assert.DoesNotContain(policies.Values, p => p.Uniques.Any(u => u.StartsWith("May buy [Great", StringComparison.Ordinal) && !p.Name.EndsWith(" Complete", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheTwelveWondersNeedTheirBranchAdopted()
    {
        var ruleset = BnwRulesetTests.LoadBnw();
        foreach (var (wonder, branch) in GatedWonders)
        {
            var building = ruleset.Buildings[wonder];
            Assert.True(building.IsWonder, wonder);
            Assert.Single(building.Uniques, $"Only available <after adopting [{branch}]>");
            Assert.Contains(branch, ruleset.Policies.Keys);
        }
        Assert.Equal(12, ruleset.Buildings.Values.Count(b => b.Uniques.Any(u => u.StartsWith("Only available <after adopting [", StringComparison.Ordinal)
            && ruleset.PolicyBranches.ContainsKey(u["Only available <after adopting [".Length..^2]))));
    }

    [Fact]
    public void AWonderIsOnlyBuildableOnceTheBranchIsAdopted()
    {
        game.MakeHexagonalMap(2);
        var civ = game.AddCiv();
        var city = game.AddCity(civ, game.TileMap[0, 0]);
        var wonder = game.CreateWonder("Only available <after adopting [Tradition]>");
        Assert.False(wonder.IsBuildable(city.CityConstructions));
        civ.Policies.AdoptedPolicies.Add("Tradition");
        Assert.True(wonder.IsBuildable(city.CityConstructions));
    }

    [Fact]
    public void PietyIsLockedWithoutReligion()
    {
        var piety = BnwRulesetTests.LoadBnw().PolicyBranches["Piety"];
        var withReligion = new TestGame().AddCiv();
        Assert.True(withReligion.Policies.IsAdoptable(piety));

        var noReligion = new TestGame();
        noReligion.Ruleset.ModOptions.Uniques.Add(UniqueType.DisableReligion.Text());
        Assert.False(noReligion.AddCiv().Policies.IsAdoptable(piety));
        // a branch without the lock is unaffected
        Assert.True(noReligion.AddCiv().Policies.IsAdoptable(BnwRulesetTests.LoadBnw().PolicyBranches["Tradition"]));
    }

    [Fact]
    public void ABranchIsAdoptableOnlyFromItsEra()
    {
        var ruleset = BnwRulesetTests.LoadBnw();
        game.MakeHexagonalMap(2);
        var civ = game.AddCiv();
        civ.Tech.AddTechnology(game.Ruleset.Technologies.Values.OrderBy(it => it.Column!.ColumnNumber).First().Name);
        Assert.True(civ.Policies.IsAdoptable(ruleset.PolicyBranches["Tradition"]));
        Assert.False(civ.Policies.IsAdoptable(ruleset.PolicyBranches["Aesthetics"]));
        Assert.False(civ.Policies.IsAdoptable(ruleset.PolicyBranches["Exploration"]));
        Assert.False(civ.Policies.IsAdoptable(ruleset.PolicyBranches["Rationalism"]));
        Assert.True(civ.Policies.IsAdoptable(ruleset.PolicyBranches["Aesthetics"], checkEra: false));
    }

    [Fact]
    public void TheSocialTreeHasNoGodsAndKingsExclusivityLeft()
    {
        var branches = BnwRulesetTests.LoadBnw().PolicyBranches;
        foreach (var name in new[] { "Piety", "Rationalism" })
            Assert.DoesNotContain(branches[name].Uniques, it => it.Contains("before adopting", StringComparison.Ordinal));
    }

    [Fact]
    public void AdoptingTheLastMemberAdoptsTheFinisher()
    {
        var branch = BnwRulesetTests.LoadBnw().PolicyBranches["Aesthetics"];
        var civ = new TestGame().AddCiv();
        civ.Policies.FreePolicies = 10;
        foreach (var policy in branch.Policies.SkipLast(1))
        {
            Assert.False(civ.Policies.IsAdopted(branch.Policies[^1].Name));
            civ.Policies.Adopt(policy);
        }
        Assert.True(civ.Policies.IsAdopted("Aesthetics Complete"));
        Assert.False(civ.Policies.IsAdoptable(branch.Policies[^1]));
        Assert.Equal(5, civ.Policies.FreePolicies); // the opener and four members were free picks, the finisher costs none
    }
}
