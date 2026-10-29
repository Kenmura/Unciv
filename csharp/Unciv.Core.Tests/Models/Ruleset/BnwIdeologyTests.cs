using System.Text.Json;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Validation;
using Unciv.Queries.Empire;
using Unciv.View;
using PolicyObject = Unciv.Models.Ruleset.Policy;

namespace Unciv.Core.Tests.Models.Ruleset;

/// <summary>B013 (bnw-spec 4.6.2 and 4.6.3): the three ideologies, how a civ unlocks one (era above Industrial or three Factories), the free tenets of the
/// first and second adopter, and the tenet level rule (Level n+1 needs more Level n than Level n+1 tenets). Hand-computed cases plus the boundaries of SPEC 6.1.</summary>
[Collection("RulesetCache")]
public sealed class BnwIdeologyTests
{
    private readonly TestGame game = new(BaseRuleset.Civ_V_BNW);

    private PolicyBranch Branch(string name) => game.Ruleset.PolicyBranches[name];

    private Civilization Major(string? ideology = null)
    {
        game.MakeHexagonalMap(4);
        var civ = game.AddCiv(game.Ruleset.Nations.Values.First(it => it.IsMajorCiv));
        civ.Tech.Era = game.Ruleset.Eras["Modern era"];
        if (ideology != null) civ.Policies.ChooseIdeology(Branch(ideology));
        return civ;
    }

    private static List<PolicyObject> Tenets(PolicyBranch branch, int level) => branch.Policies.Where(it => it.Level == level).ToList();

    [Fact]
    public void ThreeIdeologiesHaveSixteenTenetsOfLevel7_6_3()
    {
        foreach (var name in new[] { "Freedom", "Order", "Autocracy" })
        {
            var branch = Branch(name);
            Assert.True(branch.PurchaseByLevel);
            Assert.Equal((2, 1), (branch.FirstAdopterFreePolicies, branch.SecondAdopterFreePolicies));
            Assert.Equal([7, 6, 3], new[] { 1, 2, 3 }.Select(level => Tenets(branch, level).Count));
            Assert.Equal(16, branch.Policies.Count);
            Assert.All(branch.Policies, it => Assert.True(it.IsTenet));
            Assert.All(branch.Policies, it => Assert.Equal(PolicyBranchTypesMember, it.PolicyBranchType)); // no finisher, no opener inside the list
            Assert.All(branch.Policies, it => Assert.Empty(it.Requires!));
            Assert.False(branch.IsTenet);
        }
        foreach (var name in new[] { "Tradition", "Piety", "Rationalism" })
        {
            Assert.False(Branch(name).PurchaseByLevel);
            Assert.All(Branch(name).Policies, it => Assert.Equal(0, it.Level));
        }
    }

    private const PolicyObject.PolicyBranchTypes PolicyBranchTypesMember = PolicyObject.PolicyBranchTypes.Member;

    [Fact]
    public void TheUniversalHealthcareTenetsAreToldApartAndFactoriesUnlockIdeologies()
    {
        var policies = game.Ruleset.Policies;
        foreach (var (name, level) in new[] { ("Freedom", 1), ("Order", 1), ("Autocracy", 1) })
            Assert.Equal(level, policies[$"Universal Healthcare ({name})"].Level);
        Assert.DoesNotContain("Universal Healthcare", policies.Keys);
        Assert.Contains("Building [3] of these unlocks Ideologies", game.Ruleset.Buildings["Factory"].Uniques);
        Assert.Contains("Enables ideologies", game.Ruleset.ModOptions.Uniques);
    }

    [Fact]
    public void AnIdeologyIsUnlockedOnlyAfterTheIndustrialEra()
    {
        var civ = Major();
        var freedom = Branch("Freedom");
        Assert.True(civ.Policies.CanChooseIdeology());
        Assert.True(civ.Policies.IsAdoptable(freedom));

        civ.Tech.Era = game.Ruleset.Eras["Industrial era"]; // the boundary: the era of the branch itself is not enough
        Assert.False(civ.Policies.CanChooseIdeology());
        Assert.False(civ.Policies.IsAdoptable(freedom));
        Assert.True(civ.Policies.IsAdoptable(freedom, checkEra: false)); // adoptable "now or in the future"
        civ.Tech.Era = game.Ruleset.Eras["Atomic era"];
        Assert.True(civ.Policies.CanChooseIdeology());
    }

    [Fact]
    public void ThreeFactoriesUnlockAnIdeologyEarly()
    {
        var civ = Major();
        civ.Tech.Era = game.Ruleset.Eras["Industrial era"];
        var cities = new[] { game.TileMap[0, 0], game.TileMap[3, 0], game.TileMap[-3, 0], game.TileMap[0, 3] }
            .Select(tile => game.AddCity(civ, tile)).ToList();
        cities[0].CityConstructions.BuiltBuildings.Add("Factory");
        cities[1].CityConstructions.BuiltBuildings.Add("Factory");
        Assert.False(civ.Policies.CanChooseIdeology()); // two is not three
        Assert.False(civ.Policies.IdeologyUnlockedBecauseOfEra());
        cities[3].CityConstructions.BuiltBuildings.Add("Factory");
        Assert.True(civ.Policies.CanChooseIdeology());
        Assert.False(civ.Policies.IdeologyUnlockedBecauseOfEra());
        cities[2].CityConstructions.BuiltBuildings.Add("Workshop"); // another building does not matter
        Assert.False(civ.Policies.IdeologyUnlockedBecauseOfEra());
    }

    [Fact]
    public void NoIdeologyWithoutTheGateOrAsAnObserverOrWithOne()
    {
        var civ = Major();
        Assert.True(civ.Policies.CanChooseIdeology());
        civ.Policies.ChooseIdeology(Branch("Order"));
        Assert.False(civ.Policies.CanChooseIdeology()); // chosen once
        Assert.False(civ.Policies.IsAdoptable(Branch("Freedom")));
        Assert.Throws<InvalidOperationException>(() => civ.Policies.ChooseIdeology(Branch("Freedom")));
        Assert.Throws<InvalidOperationException>(() => civ.Policies.ChooseIdeology(Branch("Tradition")));

        var other = new TestGame(BaseRuleset.Civ_V_BNW);
        other.Ruleset.ModOptions.Uniques.Remove("Enables ideologies");
        other.MakeHexagonalMap(2);
        var noGate = other.AddCiv(other.Ruleset.Nations.Values.First(it => it.IsMajorCiv));
        noGate.Tech.Era = other.Ruleset.Eras["Modern era"];
        Assert.False(noGate.Policies.CanChooseIdeology());
        Assert.False(noGate.Policies.IsAdoptable(other.Ruleset.PolicyBranches["Freedom"]));
        Assert.False(noGate.Policies.CanAdoptPolicy());

        var cityState = other.AddCiv(cityStateType: "Cultured");
        cityState.Tech.Era = other.Ruleset.Eras["Modern era"];
        other.Ruleset.ModOptions.Uniques.Add("Enables ideologies");
        Assert.False(cityState.Policies.CanChooseIdeology()); // not a major civ
    }

    [Fact]
    public void TheFirstAdopterGetsTwoFreeTenetsTheSecondOneAndLaterNone()
    {
        var first = Major();
        first.Policies.ChooseIdeology(Branch("Freedom"));
        Assert.Equal(2, first.Policies.FreeTenets);
        Assert.Equal("Freedom", first.Policies.Ideology);
        Assert.True(first.Policies.IsAdopted("Freedom")); // the branch itself marks the choice (the Statue of Liberty gate reads it)
        Assert.True(first.Policies.ShouldOpenPolicyPicker);

        var second = game.AddCiv(game.Ruleset.Nations.Values.Where(it => it.IsMajorCiv).ElementAt(1));
        second.Tech.Era = game.Ruleset.Eras["Modern era"];
        second.Policies.ChooseIdeology(Branch("Freedom"));
        Assert.Equal(1, second.Policies.FreeTenets);

        var third = game.AddCiv(game.Ruleset.Nations.Values.Where(it => it.IsMajorCiv).ElementAt(2));
        third.Tech.Era = game.Ruleset.Eras["Modern era"];
        third.Policies.ChooseIdeology(Branch("Freedom"));
        Assert.Equal(0, third.Policies.FreeTenets);
        Assert.False(third.Policies.ShouldOpenPolicyPicker);

        var otherIdeology = game.AddCiv(game.Ruleset.Nations.Values.Where(it => it.IsMajorCiv).ElementAt(3));
        otherIdeology.Tech.Era = game.Ruleset.Eras["Modern era"];
        otherIdeology.Policies.ChooseIdeology(Branch("Order")); // first of ITS ideology
        Assert.Equal(2, otherIdeology.Policies.FreeTenets);
    }

    [Fact]
    public void ALevelTwoTenetNeedsMoreLevelOneTenetsThanLevelTwoOnes()
    {
        var civ = Major("Freedom");
        civ.Policies.FreeTenets = 20;
        var branch = Branch("Freedom");
        var (l1, l2, l3) = (Tenets(branch, 1), Tenets(branch, 2), Tenets(branch, 3));
        Assert.True(civ.Policies.IsAdoptable(l1[0]));
        Assert.False(civ.Policies.IsAdoptable(l2[0])); // 0 > 0 is false
        Assert.False(civ.Policies.IsAdoptable(l3[0]));
        civ.Policies.Adopt(l1[0]);
        Assert.True(civ.Policies.IsAdoptable(l2[0])); // 1 > 0
        civ.Policies.Adopt(l2[0]);
        Assert.False(civ.Policies.IsAdoptable(l2[1])); // 1 > 1 is false
        Assert.True(civ.Policies.IsAdoptable(l3[0])); // a Level 3 tenet needs more Level 2 than Level 3 tenets: 1 > 0
        civ.Policies.Adopt(l1[1]);
        Assert.True(civ.Policies.IsAdoptable(l2[1])); // 2 > 1
    }

    [Fact]
    public void ALevelThreeTenetNeedsMoreLevelTwoTenetsThanLevelThreeOnes()
    {
        var civ = Major("Order");
        civ.Policies.FreeTenets = 20;
        var branch = Branch("Order");
        var (l1, l2, l3) = (Tenets(branch, 1), Tenets(branch, 2), Tenets(branch, 3));
        civ.Policies.Adopt(l1[0]);
        civ.Policies.Adopt(l1[1]);
        Assert.False(civ.Policies.IsAdoptable(l3[0])); // no Level 2 tenet yet
        civ.Policies.Adopt(l2[0]);
        Assert.True(civ.Policies.IsAdoptable(l3[0])); // 1 > 0
        civ.Policies.Adopt(l3[0]);
        Assert.False(civ.Policies.IsAdoptable(l3[1])); // 1 > 1 is false
        Assert.Equal(1, civ.Policies.GetTenetCount(branch, 3));
        Assert.Equal(2, civ.Policies.GetTenetCount(branch, 1));
    }

    [Fact]
    public void ATenetOfAnotherIdeologyOrWithoutAChoiceIsNeverAdoptable()
    {
        var civ = Major();
        Assert.False(civ.Policies.IsAdoptable(Tenets(Branch("Freedom"), 1)[0])); // no ideology yet
        civ.Policies.ChooseIdeology(Branch("Freedom"));
        Assert.False(civ.Policies.IsAdoptable(Tenets(Branch("Autocracy"), 1)[0]));
        Assert.True(civ.Policies.IsAdoptable(Tenets(Branch("Freedom"), 1)[0]));
    }

    [Fact]
    public void FreePoliciesNeverBuyTenetsAndFreeTenetsNeverBuySocialPolicies()
    {
        var civ = Major("Freedom");
        civ.Policies.FreeTenets = 0;
        civ.Policies.FreePolicies = 3; // Poland's Solidarity grants these: they are social-branch picks only
        var tenet = Tenets(Branch("Freedom"), 1)[0];
        var social = game.Ruleset.PolicyBranches["Tradition"];
        Assert.False(civ.Policies.CanAdoptPolicy(tenet));
        Assert.True(civ.Policies.CanAdoptPolicy(social));
        Assert.True(civ.Policies.CanAdoptPolicy()); // a social policy can be bought with the free ones

        civ.Policies.FreePolicies = 0;
        civ.Policies.FreeTenets = 1;
        Assert.True(civ.Policies.CanAdoptPolicy(tenet));
        Assert.False(civ.Policies.CanAdoptPolicy(social));
        Assert.True(civ.Policies.CanAdoptPolicy());

        civ.Policies.Adopt(tenet);
        Assert.Equal(0, civ.Policies.FreeTenets);
        Assert.False(civ.Policies.CanAdoptPolicy());
    }

    [Fact]
    public void ATenetBoughtWithCultureCostsAPolicyPickAndAFreeOneDoesNot()
    {
        var civ = Major("Autocracy");
        civ.Policies.FreeTenets = 1;
        var cost = civ.Policies.GetCultureNeededForNextPolicy();
        var tenets = Tenets(Branch("Autocracy"), 1);
        civ.Policies.Adopt(tenets[0]);
        Assert.Equal(cost, civ.Policies.GetCultureNeededForNextPolicy()); // a free tenet leaves the price alone

        civ.Policies.StoredCulture = cost - 1;
        Assert.False(civ.Policies.CanAdoptPolicy(tenets[1]));
        civ.Policies.StoredCulture = cost;
        Assert.True(civ.Policies.CanAdoptPolicy(tenets[1]));
        civ.Policies.Adopt(tenets[1]);
        Assert.Equal(0, civ.Policies.StoredCulture);
        Assert.True(civ.Policies.GetCultureNeededForNextPolicy() > cost); // the culture-bought tenet counts as a policy
    }

    [Fact]
    public void ChoosingAnIdeologyCostsNothing()
    {
        var civ = Major();
        civ.Policies.StoredCulture = 0;
        civ.Policies.FreePolicies = 0;
        var freedom = Branch("Freedom");
        Assert.True(civ.Policies.CanAdoptPolicy(freedom));
        Assert.True(civ.Policies.CanAdoptPolicy());
        var cost = civ.Policies.GetCultureNeededForNextPolicy();
        civ.Policies.Adopt(freedom);
        Assert.Equal("Freedom", civ.Policies.Ideology);
        Assert.Equal(cost, civ.Policies.GetCultureNeededForNextPolicy());
        Assert.Equal(0, civ.Policies.FreePolicies);
    }

    [Fact]
    public void IdeologyBranchesDoNotCountAsSocialBranches()
    {
        var civ = Major("Freedom");
        civ.Policies.FreeTenets = 16;
        Assert.DoesNotContain(civ.Policies.SocialBranches, it => it.PurchaseByLevel);
        Assert.Equal(3, civ.Policies.IdeologyBranches.Count());
        Assert.DoesNotContain(civ.Policies.CompletedBranches, it => it.PurchaseByLevel);
        Assert.DoesNotContain(civ.Policies.AdoptableBranches, it => it.PurchaseByLevel);
        Assert.DoesNotContain(civ.Policies.IncompleteBranches, it => it.PurchaseByLevel);
        Assert.DoesNotContain(civ.Policies.BranchCompletionMap.Keys, it => it.PurchaseByLevel);
        Assert.Contains(civ.Policies.PriorityMap.Keys, it => it.PurchaseByLevel);
    }

    [Fact]
    public void TheIdeologyAndTheFreeTenetsSurviveSaveCloneAndLoad()
    {
        var civ = Major("Freedom");
        civ.Policies.IdeologyPrompted = true;
        var clone = civ.Policies.Clone();
        Assert.Equal(("Freedom", 2, true), (clone.Ideology, clone.FreeTenets, clone.IdeologyPrompted));

        var json = JsonSerializer.Serialize(civ.Policies, UncivJson.DefaultOptions);
        Assert.Contains("\"ideology\":\"Freedom\"", json);
        Assert.Contains("\"freeTenets\":2", json);
        var loaded = JsonSerializer.Deserialize<PolicyManager>(json, UncivJson.DefaultOptions)!;
        Assert.Equal(("Freedom", 2), (loaded.Ideology, loaded.FreeTenets));
    }

    [Fact]
    public void RemovingATenetKeepsTheBranchAndNeverCompletesIt()
    {
        var civ = Major("Freedom");
        civ.Policies.FreeTenets = 16;
        var tenet = Tenets(Branch("Freedom"), 1)[0];
        civ.Policies.Adopt(tenet);
        civ.Policies.RemovePolicy(tenet, assumeWasFree: true);
        Assert.False(civ.Policies.IsAdopted(tenet.Name));
        Assert.True(civ.Policies.IsAdopted("Freedom"));
    }

    [Fact]
    public void ThePolicyTreeQueryNamesTheIdeologyState()
    {
        var civ = Major();
        var freedom = Branch("Freedom");
        var tenet = Tenets(freedom, 2)[0];
        civ.Tech.Era = game.Ruleset.Eras["Industrial era"];
        game.GameInfo.CurrentPlayerCiv = civ;
        var view = new GameView(game.GameInfo, civ);
        var tree = new PolicyTreeQuery().Run(view);
        Assert.False(tree.CanChooseIdeology);
        Assert.Equal("ideology-locked", tree.Branches.Single(it => it.Name == "Freedom").Start.LockedReason);

        civ.Tech.Era = game.Ruleset.Eras["Modern era"];
        tree = new PolicyTreeQuery().Run(view);
        Assert.True(tree.CanChooseIdeology);
        Assert.Equal("Adoptable", tree.Branches.Single(it => it.Name == "Freedom").Start.State);
        Assert.Equal("ideology:Freedom", tree.Branches.Single(it => it.Name == "Freedom").Policies.Single(it => it.Name == tenet.Name).LockedReason);

        civ.Policies.ChooseIdeology(freedom);
        tree = new PolicyTreeQuery().Run(view);
        Assert.Equal(("Freedom", 2, false), (tree.Ideology, tree.FreeTenets, tree.CanChooseIdeology));
        Assert.Equal("tenet-level:1", tree.Branches.Single(it => it.Name == "Freedom").Policies.Single(it => it.Name == tenet.Name).LockedReason);
        Assert.Equal("Adoptable", tree.Branches.Single(it => it.Name == "Freedom").Policies.First(it => it.Name == Tenets(freedom, 1)[0].Name).State);
    }

    [Fact]
    public void TheIdeologyChoiceIsAPendingDecisionThatNeverBlocksAndFreeTenetsDo()
    {
        var civ = Major();
        var decisions = PendingDecisions.For(civ);
        var ideology = Assert.Single(decisions, it => it.Kind == DecisionKind.Ideology);
        Assert.False(ideology.Blocking);
        Assert.Equal("adopt-policy", ideology.ResolvedBy);
        Assert.DoesNotContain(PendingDecisions.BlockingFor(civ), it => it.Kind == DecisionKind.Ideology);

        civ.Policies.ChooseIdeology(Branch("Freedom"));
        Assert.DoesNotContain(PendingDecisions.For(civ), it => it.Kind == DecisionKind.Ideology);
        Assert.Contains(PendingDecisions.BlockingFor(civ), it => it.Kind == DecisionKind.Policy); // two free tenets to place
    }

    [Fact]
    public void TheAiChoosesAnIdeologyAndSpendsItsFreeTenets()
    {
        var civ = Major();
        game.GameInfo.CurrentPlayerCiv = civ;
        civ.PlayerType = PlayerType.AI;
        NextTurnAutomation.AdoptPolicyForced(civ);
        Assert.NotNull(civ.Policies.Ideology);
        Assert.Equal(0, civ.Policies.FreeTenets);
        var branch = Branch(civ.Policies.Ideology!);
        Assert.Equal(2, branch.Policies.Count(it => civ.Policies.IsAdopted(it.Name)));
        Assert.All(branch.Policies.Where(it => civ.Policies.IsAdopted(it.Name)), it => Assert.Equal(1, it.Level)); // only Level 1 is open at first
    }

    [Fact]
    public void TheAiLeavesSocialBranchesAloneWhenOnlyFreeTenetsAreLeft()
    {
        var civ = Major("Order");
        civ.PlayerType = PlayerType.AI;
        game.GameInfo.CurrentPlayerCiv = civ;
        civ.Policies.FreeTenets = 3;
        civ.Policies.StoredCulture = 0;
        NextTurnAutomation.AdoptPolicyForced(civ);
        Assert.Equal(0, civ.Policies.FreeTenets);
        Assert.Equal(1 + 3, civ.Policies.AdoptedPolicies.Count); // the ideology and three tenets, no social policy
    }

    [Fact]
    public void TheTurnStartNotifiesOnceAndOnlyWhenAnIdeologyCanBeChosen()
    {
        var civ = Major();
        civ.PlayerType = PlayerType.Human; // only a human gets notifications
        game.AddCity(civ, game.TileMap[0, 0]);
        civ.Tech.Era = game.Ruleset.Eras["Industrial era"];
        new TurnManager(civ).StartTurn();
        Assert.DoesNotContain(civ.Notifications, it => it.Text.Contains("ideology", StringComparison.Ordinal));
        Assert.False(civ.Policies.IdeologyPrompted);

        civ.Tech.Era = game.Ruleset.Eras["Modern era"];
        new TurnManager(civ).StartTurn();
        Assert.Single(civ.Notifications, it => it.Text == "Your era allows you to choose an ideology!");
        Assert.True(civ.Policies.IdeologyPrompted);
        new TurnManager(civ).StartTurn();
        Assert.Single(civ.Notifications, it => it.Text.Contains("ideology", StringComparison.Ordinal));
    }

    [Fact]
    public void TheQueryExplainsWhyAnotherIdeologyOrAnyIdeologyWithoutTheGateIsLocked()
    {
        var civ = Major("Freedom");
        game.GameInfo.CurrentPlayerCiv = civ;
        var view = new GameView(game.GameInfo, civ);
        var tree = new PolicyTreeQuery().Run(view);
        Assert.Equal("unavailable", tree.Branches.Single(it => it.Name == "Order").Start.LockedReason); // an ideology was chosen already
        Assert.Equal("Adopted", tree.Branches.Single(it => it.Name == "Freedom").Start.State);

        var off = new TestGame(BaseRuleset.Civ_V_BNW);
        off.Ruleset.ModOptions.Uniques.Remove("Enables ideologies"); // before the first use: the ruleset caches its uniques
        off.MakeHexagonalMap(2);
        var offCiv = off.AddCiv(off.Ruleset.Nations.Values.First(it => it.IsMajorCiv));
        offCiv.Tech.Era = off.Ruleset.Eras["Modern era"];
        off.GameInfo.CurrentPlayerCiv = offCiv;
        tree = new PolicyTreeQuery().Run(new GameView(off.GameInfo, offCiv));
        Assert.Equal("unavailable", tree.Branches.Single(it => it.Name == "Autocracy").Start.LockedReason);
        Assert.Equal("unavailable", tree.Branches.Single(it => it.Name == "Freedom").Policies.First().LockedReason);
    }

    [Fact]
    public void ChoosingAnIdeologyRefreshesTheCities()
    {
        var civ = Major();
        var city = game.AddCity(civ, game.TileMap[0, 0]);
        civ.Policies.ChooseIdeology(Branch("Autocracy"));
        Assert.Equal("Autocracy", civ.Policies.Ideology);
        Assert.Same(city, civ.Cities.Single());
    }

    [Fact]
    public void NoIdeologyBranchesMeansNothingToChooseEvenWithFactories()
    {
        var other = new TestGame(BaseRuleset.Civ_V_BNW);
        foreach (var name in new[] { "Freedom", "Order", "Autocracy" }) other.Ruleset.PolicyBranches.Remove(name);
        other.MakeHexagonalMap(2);
        var civ = other.AddCiv(other.Ruleset.Nations.Values.First(it => it.IsMajorCiv));
        civ.Tech.Era = other.Ruleset.Eras["Modern era"];
        Assert.False(civ.Policies.CanChooseIdeology());
    }

    [Fact]
    public void RemovingTheIdeologyDropsItsTenetsWithoutRefundAndUniquesCannotTargetThem()
    {
        var civ = Major("Freedom");
        var freedom = Branch("Freedom");
        var tenet = Tenets(freedom, 1)[0];
        civ.Policies.FreeTenets = 1;
        civ.Policies.Adopt(tenet);
        Assert.Empty(civ.Policies.GetAdoptedPoliciesMatching("All", forRemoval: true));

        civ.Policies.RemovePolicy(freedom);
        Assert.Null(civ.Policies.Ideology);
        Assert.False(civ.Policies.IsAdopted(tenet.Name));
        Assert.False(civ.Policies.IsAdopted(freedom.Name));
    }

    [Fact]
    public void ValidatorRejectsTenetLevelOutOfRange()
    {
        var other = new TestGame(BaseRuleset.Civ_V_BNW);
        var tenet = other.Ruleset.PolicyBranches["Freedom"].Policies[0];
        var original = tenet.Level;
        tenet.Level = 7;
        List<RulesetError> errors;
        try { errors = RulesetValidator.Create(other.Ruleset).GetErrorList().ToList(); }
        finally { tenet.Level = original; } // the ruleset instance is shared between tests
        Assert.Contains(errors, it => it.Text.Contains("expected 1 to 3", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsIdeologyBranchWithUnknownEra()
    {
        var other = new TestGame(BaseRuleset.Civ_V_BNW);
        var branch = other.Ruleset.PolicyBranches["Order"];
        var original = branch.Era;
        branch.Era = "";
        List<RulesetError> errors;
        try { errors = RulesetValidator.Create(other.Ruleset).GetErrorList().ToList(); }
        finally { branch.Era = original; } // the ruleset instance is shared between tests
        Assert.Contains(errors, it => it.Text.Contains("Order requires era", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsTwoTenetsAtTheSameLocation()
    {
        var other = new TestGame(BaseRuleset.Civ_V_BNW);
        var tenets = other.Ruleset.PolicyBranches["Autocracy"].Policies;
        var (row, column) = (tenets[1].Row, tenets[1].Column);
        (tenets[1].Row, tenets[1].Column) = (tenets[0].Row, tenets[0].Column);
        List<RulesetError> errors;
        try { errors = RulesetValidator.Create(other.Ruleset).GetErrorList().ToList(); }
        finally { (tenets[1].Row, tenets[1].Column) = (row, column); } // the ruleset instance is shared between tests
        Assert.Contains(errors, it => it.Text.Contains("are both located at", StringComparison.Ordinal));
    }
}
