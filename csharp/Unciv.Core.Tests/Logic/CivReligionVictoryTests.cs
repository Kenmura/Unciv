using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using PolicyObject = Unciv.Models.Ruleset.Policy;
using SpeedObject = Unciv.Models.Ruleset.Speed;

namespace Unciv.Core.Tests.Logic;

// T057: Religion, ReligionManager, RuinsManager, VictoryManager, EspionageManager, Spy and Milestone's game-state members.
// The Kotlin fixtures for these are TestGame-based (EspionageTests: T072; VictoryAvailabilityTests/VictoryMilestoneTests: deferred by T030);
// these are hand-derived expectations over the seam fakes in CivilizationTestFakes.cs.
public class CivReligionVictoryTests
{
    private static (Civilization Civ, FakeGameInfoState Info) NewCiv(string name = "Rome", PlayerType playerType = PlayerType.Human)
    {
        var info = new FakeGameInfoState { ReligionEnabled = true };
        var civ = new Civilization(new NationObject { Name = name }) { GameInfo = info, PlayerType = playerType };
        Wire(civ);
        civ.Units = new TestUnitManager(civ) { Count = 1 };
        civ.CityStateFunctions = new FakeCityStateFunctions();
        info.Civs.Add(civ);
        return (civ, info);
    }

    /// <summary>Nation uniques are cached by the first query: give the civ a fresh nation to change them.</summary>
    private static void SetNationUniques(Civilization civ, params string[] uniques) =>
        civ.Nation = new NationObject { Name = civ.Nation.Name, LeaderName = civ.Nation.LeaderName, CityStateType = civ.Nation.CityStateType, Uniques = [.. uniques] };

    private static void Wire(Civilization civ)
    {
        civ.ReligionManager.CivInfo = civ;
        civ.VictoryManager.CivInfo = civ;
        civ.Policies.CivInfo = civ;
        civ.Tech.CivInfo = civ;
        civ.EspionageManager.CivInfo = civ;
    }

    private static Belief AddBelief(FakeGameInfoState info, string name, BeliefType type, params string[] uniques)
    {
        var belief = new Belief(type) { Name = name, Uniques = [.. uniques] };
        info.RulesetData.Beliefs[name] = belief;
        return belief;
    }

    // ---- Religion

    [Fact]
    public void Religion_AddBeliefs_SplitsByTypeAndOrdersPantheonFounderFollowerEnhancer()
    {
        var (civ, info) = NewCiv();
        var pantheon = AddBelief(info, "p1", BeliefType.Pantheon);
        var founder = AddBelief(info, "f1", BeliefType.Founder);
        var follower = AddBelief(info, "fo1", BeliefType.Follower);
        var enhancer = AddBelief(info, "e1", BeliefType.Enhancer);
        var none = AddBelief(info, "n1", BeliefType.None);
        var religion = new Religion("Faith", info, civ, 5);

        religion.AddBeliefs([enhancer, follower, founder, pantheon, none]);

        Assert.Equal(["p1", "f1", "fo1", "e1"], religion.GetAllBeliefsOrdered().Select(it => it.Name));
        Assert.Equal(["fo1"], religion.GetBeliefs(BeliefType.Follower).Select(it => it.Name));
        Assert.Equal(["e1"], religion.GetBeliefs(BeliefType.Enhancer).Select(it => it.Name));
        Assert.Equal(4, religion.GetBeliefs(BeliefType.Any).Count());
        Assert.True(religion.HasBelief("f1"));
        Assert.False(religion.HasBelief("n1")); // None is not a valid belief type for a religion
        Assert.True(religion.IsMajorReligion());
        Assert.True(religion.IsEnhancedReligion());
        Assert.False(religion.IsPantheon());
        Assert.Equal("Faith", religion.GetIconName());
        Assert.Throws<NullReferenceException>(() => religion.GetBeliefs(BeliefType.None)); // Kotlin: null!!
    }

    [Fact]
    public void Religion_PantheonOnly_IsPantheonWithTheirIconAndDisplayName()
    {
        var (civ, info) = NewCiv();
        AddBelief(info, "p1", BeliefType.Pantheon);
        var religion = new Religion("p1", info, civ, 1);
        religion.AddBelief("p1");
        religion.AddBelief("does not exist"); // ignored

        Assert.True(religion.IsPantheon());
        Assert.False(religion.IsMajorReligion());
        Assert.Equal("Pantheon", religion.GetIconName());
        Assert.Equal("p1", religion.GetReligionDisplayName());
        religion.DisplayName = "Shown";
        Assert.Equal("Shown", religion.GetReligionDisplayName());
    }

    [Fact]
    public void Religion_FoundingCiv_IsLookedUpByIdOnce_AndCloneCopiesTheBeliefsNotTheUniqueMaps()
    {
        var (civ, info) = NewCiv();
        AddBelief(info, "f1", BeliefType.Founder, "Cannot be traded");
        var byName = new Religion("Faith", info, civ.CivId, 7);
        Assert.Same(civ, byName.FoundingCiv);
        byName.AddBelief("f1");
        byName.DisplayName = "Shown";
        Assert.True(byName.FounderBeliefUniqueMap.HasTagUnique("Cannot be traded"));

        var clone = byName.Clone();
        Assert.NotSame(byName, clone);
        Assert.Equal("Shown", clone.DisplayName);
        Assert.Equal(7, clone.FoundingTurn);
        Assert.True(clone.HasBelief("f1"));
        Assert.True(clone.FounderBeliefUniqueMap.IsEmpty()); // transient: rebuilt by setTransients
        clone.SetTransients(info);
        Assert.True(clone.FounderBeliefUniqueMap.HasTagUnique("Cannot be traded"));
    }

    [Fact]
    public void Religion_MatchesFilter_CoversTheKeywordsBeliefNamesAndUniqueTags()
    {
        var (civ, info) = NewCiv();
        var (other, _) = NewCiv("Egypt");
        info.Civs.Add(other);
        var founder = AddBelief(info, "f1", BeliefType.Founder, "Cannot be traded");
        var religion = new Religion("Faith", info, civ, 1);
        religion.AddBeliefs([founder]);

        Assert.True(religion.MatchesFilter("any"));
        Assert.True(religion.MatchesFilter("major"));
        Assert.False(religion.MatchesFilter("enhanced"));
        Assert.True(religion.MatchesFilter("your", civ: civ));
        Assert.False(religion.MatchesFilter("your", civ: other));
        Assert.False(religion.MatchesFilter("your")); // null != foundingCiv
        Assert.True(religion.MatchesFilter("foreign", civ: other));
        Assert.False(religion.MatchesFilter("foreign", civ: civ));
        Assert.False(religion.MatchesFilter("foreign"));
        Assert.False(religion.MatchesFilter("enemy"));
        Assert.True(religion.MatchesFilter("Faith"));
        Assert.True(religion.MatchesFilter("f1"));
        Assert.True(religion.MatchesFilter("Cannot be traded"));
        Assert.False(religion.MatchesFilter("unknown"));
        Assert.True(religion.MatchesFilter("non-[enhanced]"));
        Assert.False(religion.MatchesFilter("{major} {enhanced}"));
        Assert.True(((IGameContextReligion)religion).MatchesFilter("major", GameContext.IgnoreConditionalsState, null));
    }

    [Fact]
    public void Religion_MatchesFilter_EnemyNeedsKnownAndAtWar()
    {
        var (civ, info) = NewCiv();
        var (other, _) = NewCiv("Egypt");
        info.Civs.Add(other);
        var religion = new Religion("Faith", info, civ, 1);
        Assert.False(religion.MatchesFilter("enemy", civ: other)); // does not know the founder
        var otherSeesRome = new DiplomacyManager(other, civ) { DiplomaticStatus = DiplomaticStatus.Peace };
        other.Diplomacy["Rome"] = otherSeesRome;
        Assert.False(religion.MatchesFilter("enemy", civ: other)); // known, but at peace
        otherSeesRome.DiplomaticStatus = DiplomaticStatus.War;
        Assert.True(religion.MatchesFilter("enemy", civ: other));
    }

    [Fact]
    public void Religion_BuildingsPurchasableByBeliefs_ListsTheBuildingFiltersThatExist()
    {
        var (civ, info) = NewCiv();
        info.RulesetData.Buildings["Temple"] = new Building { Name = "Temple" };
        var follower = AddBelief(info, "fo1", BeliefType.Follower,
            "May buy [Temple] buildings with [Faith] [in all cities]", "May buy [Missing] buildings with [Faith] [in all cities]");
        var religion = new Religion("Faith", info, civ, 1);
        religion.AddBeliefs([follower]);
        Assert.Equal(["Temple"], religion.BuildingsPurchasableByBeliefs);
    }

    [Fact]
    public void ReligionState_ToDisplayString_SplitsCamelCaseLikeKotlin()
    {
        Assert.Equal("None", ReligionState.None.ToDisplayString());
        Assert.Equal("Founding religion", ReligionState.FoundingReligion.ToDisplayString());
        Assert.Equal("Enhanced religion", ReligionState.EnhancedReligion.ToDisplayString());
        Assert.True(ReligionState.Pantheon < ReligionState.Religion); // Conditionals compares by declaration order
    }

    // ---- ReligionManager

    [Fact]
    public void ReligionManager_SetTransients_PrefersAMajorReligionOverAPantheon()
    {
        var (civ, info) = NewCiv();
        AddBelief(info, "p1", BeliefType.Pantheon);
        AddBelief(info, "f1", BeliefType.Founder);
        var pantheon = new Religion("p1", info, civ, 1);
        pantheon.AddBelief("p1");
        var major = new Religion("Faith", info, civ, 2);
        major.AddBelief("f1");
        info.ReligionMap["p1"] = pantheon;
        civ.ReligionManager.SetTransients(civ);
        Assert.Same(pantheon, civ.ReligionManager.Religion);
        info.ReligionMap["Faith"] = major;
        civ.ReligionManager.SetTransients(civ);
        Assert.Same(major, civ.ReligionManager.Religion);
    }

    [Fact]
    public void ReligionManager_FaithForPantheon_GrowsWithFoundedPantheonsAndRoundsHalfUp()
    {
        var (civ, info) = NewCiv();
        Assert.Equal(10, civ.ReligionManager.FaithForPantheon()); // pantheonBase 10, pantheonGrowth 5
        Assert.Equal(20, civ.ReligionManager.FaithForPantheon(2));
        info.SpeedData = new SpeedObject { FaithCostModifier = 1.5f };
        Assert.Equal(38, civ.ReligionManager.FaithForPantheon(3)); // (10 + 3 * 5) * 1.5 = 37.5
        civ.ReligionManager.Religion = new Religion("p1", info, civ, 1);
        Assert.Equal(23, civ.ReligionManager.FaithForPantheon()); // one civ has a religion: 15 * 1.5 = 22.5
    }

    [Fact]
    public void ReligionManager_GreatProphetCost_FollowsTheNumberEarnedAndTheUniques()
    {
        var (civ, info) = NewCiv();
        var prophet = new BaseUnit { Name = "Great Prophet", Uniques = ["May found a religion"] };
        info.RulesetData.Units["Great Prophet"] = prophet;
        Assert.Same(prophet, civ.ReligionManager.GetGreatProphetEquivalent());
        Assert.Equal(200, civ.ReligionManager.FaithForNextGreatProphet());
        civ.CivConstructions.BoughtItemsWithIncreasingPrice["Great Prophet"] = 2;
        Assert.Equal(2, civ.ReligionManager.GreatProphetsEarned());
        Assert.Equal(500, civ.ReligionManager.FaithForNextGreatProphet()); // 200 + 100 * 2 * 3 / 2
        SetNationUniques(civ, "[50]% Faith cost of generating Great Prophet equivalents");
        Assert.Equal(750, civ.ReligionManager.FaithForNextGreatProphet());
    }

    [Fact]
    public void ReligionManager_CanGenerateProphet_NeedsAPantheonFaithAndNoPreventingUnique()
    {
        var (civ, info) = NewCiv();
        var manager = civ.ReligionManager;
        Assert.False(manager.CanGenerateProphet()); // no religion yet
        AddBelief(info, "p1", BeliefType.Pantheon);
        AddBelief(info, "f1", BeliefType.Founder);
        AddBelief(info, "fo1", BeliefType.Follower);
        info.RulesetData.Units["Great Prophet"] = new BaseUnit { Name = "Great Prophet", Uniques = ["May found a religion"] };
        info.RulesetData.Religions.Add("Faith");
        civ.Nation.Name = "Rome";
        manager.ChooseBeliefs([info.RulesetData.Beliefs["p1"]], useFreeBeliefs: true);
        Assert.Equal(ReligionState.Pantheon, manager.ReligionState);
        Assert.False(manager.CanGenerateProphet()); // 0 < 200 faith
        Assert.True(manager.CanGenerateProphet(ignoreFaithAmount: true));
        manager.StoredFaith = 200;
        Assert.True(manager.CanGenerateProphet());
        SetNationUniques(civ, "May not generate great prophet equivalents naturally");
        Assert.False(manager.CanGenerateProphet());
        info.ReligionEnabled = false;
        Assert.False(manager.CanGenerateProphet(true));
    }

    [Fact]
    public void ReligionManager_NumberOfBeliefsAvailable_CountsSharedBeliefsOnce()
    {
        var (civ, info) = NewCiv();
        var p1 = AddBelief(info, "p1", BeliefType.Pantheon);
        AddBelief(info, "p2", BeliefType.Pantheon);
        var f1 = AddBelief(info, "f1", BeliefType.Founder);
        var fo1 = AddBelief(info, "fo1", BeliefType.Follower);
        Assert.Equal(2, civ.ReligionManager.NumberOfBeliefsAvailable(BeliefType.Pantheon));
        Assert.Equal(4, civ.ReligionManager.NumberOfBeliefsAvailable(BeliefType.Any));

        var pantheon = new Religion("p1", info, civ, 1);
        pantheon.AddBeliefs([p1]);
        var religion = new Religion("Faith", info, civ, 1);
        religion.AddBeliefs([p1, f1, fo1]); // founded out of the pantheon: shares p1
        info.ReligionMap["p1"] = pantheon;
        info.ReligionMap["Faith"] = religion;

        Assert.Equal(1, civ.ReligionManager.NumberOfBeliefsAvailable(BeliefType.Pantheon));
        Assert.Equal(0, civ.ReligionManager.NumberOfBeliefsAvailable(BeliefType.Founder));
        Assert.Equal(1, civ.ReligionManager.NumberOfBeliefsAvailable(BeliefType.Any));
        Assert.Same(pantheon, civ.ReligionManager.GetReligionWithBelief(p1)); // the first in map order that has it, or the other: both have it
    }

    [Fact]
    public void ReligionManager_CanFoundOrExpandPantheon_NeedsFaithOrAFreeBelief()
    {
        var (civ, info) = NewCiv();
        AddBelief(info, "p1", BeliefType.Pantheon);
        var manager = civ.ReligionManager;
        Assert.False(manager.CanFoundOrExpandPantheon()); // 0 < 10 faith
        manager.StoredFaith = 10;
        Assert.True(manager.CanFoundOrExpandPantheon());
        Assert.False(manager.UsingFreeBeliefs()); // enough faith: it is paid for
        manager.StoredFaith = 0;
        Assert.True(manager.UsingFreeBeliefs()); // the first pantheon is free if one cannot afford it
        manager.FreeBeliefs[BeliefType.Pantheon.ToString()] = 1;
        Assert.True(manager.CanFoundOrExpandPantheon());
        Assert.True(manager.HasFreeBeliefs());
        Assert.Equal(1, manager.FreeBeliefsAsEnums()[BeliefType.Pantheon]);
        info.ReligionEnabled = false;
        Assert.False(manager.CanFoundOrExpandPantheon());
    }

    [Fact]
    public void ReligionManager_ChooseBeliefs_FoundsAPantheonPaidForWithFaithAndSeedsTheCities()
    {
        var (civ, info) = NewCiv();
        var (observer, _) = NewCiv("Egypt");
        info.Civs.Add(observer);
        observer.PlayerType = PlayerType.Human;
        var p1 = AddBelief(info, "p1", BeliefType.Pantheon);
        var city = new FakeCityState("c", new FakeCiv("Rome")) { PopulationData = 3, CivData = civ };
        civ.Cities = [city];
        info.Turns = 12;
        var manager = civ.ReligionManager;
        manager.StoredFaith = 25;
        manager.FreeBeliefs["Follower"] = 2;

        manager.ChooseBeliefs([p1]);

        Assert.Equal(15, manager.StoredFaith); // paid the 10 faith of the first pantheon
        Assert.Equal(ReligionState.Pantheon, manager.ReligionState);
        Assert.False(manager.HasFreeBeliefs()); // cleared first
        Assert.Equal("p1", manager.Religion!.Name);
        Assert.Equal(12, manager.Religion.FoundingTurn);
        Assert.Same(manager.Religion, info.ReligionMap["p1"]);
        Assert.Equal(600, city.ReligionData.Pressure["p1"]); // 200 * population
        var notification = Assert.Single(observer.Notifications);
        Assert.Equal("[An unknown civilization] has founded pantheon [p1]!", notification.Text);
        Assert.Equal(NotificationCategory.Religion, notification.Category);
        Assert.Empty(civ.Notifications); // the founder is not told

        // a second belief while in the pantheon state does not found anything new
        var p2 = AddBelief(info, "p2", BeliefType.Pantheon);
        manager.ChooseBeliefs([p2], useFreeBeliefs: true);
        Assert.Equal(15, manager.StoredFaith);
        Assert.True(manager.Religion.HasBelief("p2"));
        Assert.Equal(ReligionState.Pantheon, manager.ReligionState);
    }

    [Fact]
    public void ReligionManager_GetBeliefsToChoose_AccountsForAvailableBeliefsAndUniques()
    {
        var (civ, info) = NewCiv();
        AddBelief(info, "f1", BeliefType.Founder);
        AddBelief(info, "fo1", BeliefType.Follower);
        AddBelief(info, "fo2", BeliefType.Follower);
        AddBelief(info, "e1", BeliefType.Enhancer);
        var manager = civ.ReligionManager;
        var founding = manager.GetBeliefsToChooseAtFounding();
        Assert.Equal(1, founding[BeliefType.Founder]);
        Assert.Equal(1, founding[BeliefType.Follower]);
        Assert.Equal(0, founding[BeliefType.Pantheon]); // none exists, and shouldChoosePantheonBelief is false
        Assert.Equal(0, founding[BeliefType.Enhancer]);

        SetNationUniques(civ, "May choose [3] additional [Follower] beliefs when [founding] a religion");
        Assert.Equal(2, manager.GetBeliefsToChooseAtFounding()[BeliefType.Follower]); // only two follower beliefs exist
        var enhancing = manager.GetBeliefsToChooseAtEnhancing();
        Assert.Equal(1, enhancing[BeliefType.Enhancer]);
        Assert.Equal(1, enhancing[BeliefType.Follower]); // "founding" uniques do not apply when enhancing
    }

    [Fact]
    public void ReligionManager_MajorityReligion_NeedsMoreThanHalfOfTheCities()
    {
        var (civ, info) = NewCiv();
        var religion = new Religion("Faith", info, civ, 1);
        var other = new Religion("Other", info, civ, 1);
        var rome = new FakeCiv("Rome");
        var cities = Enumerable.Range(0, 5).Select(i => new FakeCityState($"c{i}", rome) { CivData = civ }).ToList();
        civ.Cities = [.. cities];
        var manager = civ.ReligionManager;
        Assert.Null(manager.GetMajorityReligion());
        cities[0].ReligionData.Majority = religion;
        cities[1].ReligionData.Majority = religion;
        cities[2].ReligionData.Majority = other;
        Assert.Null(manager.GetMajorityReligion()); // 2 of 5 is not more than 5 / 2 = 2
        Assert.False(manager.IsMajorityReligionForCiv(religion));
        cities[3].ReligionData.Majority = religion;
        Assert.Same(religion, manager.GetMajorityReligion());
        Assert.True(manager.IsMajorityReligionForCiv(religion));
        Assert.False(manager.IsMajorityReligionForCiv(other));
    }

    [Fact]
    public void ReligionManager_Clone_CopiesTheSavedFieldsOnly()
    {
        var (civ, info) = NewCiv();
        var manager = civ.ReligionManager;
        manager.StoredFaith = 17;
        manager.FreeBeliefs["Pantheon"] = 2;
        manager.Religion = new Religion("Faith", info, civ, 1);
        var clone = manager.Clone();
        Assert.Equal(17, clone.StoredFaith);
        Assert.Equal(2, clone.FreeBeliefs["Pantheon"]);
        Assert.NotSame(manager.FreeBeliefs, clone.FreeBeliefs);
        Assert.Null(clone.Religion); // transient
        Assert.Equal(ReligionState.None, clone.ReligionState);
        manager.EndTurn(3);
        Assert.Equal(20, manager.StoredFaith);
    }

    // ---- RuinsManager

    [Fact]
    public void RuinsManager_Clone_DeepCopiesTheRememberedRewards()
    {
        var (civ, info) = NewCiv();
        info.RulesetData.RuinRewards["Gold"] = new RuinReward { Name = "Gold" };
        var manager = new RuinsManager(["a", "b"]);
        manager.SetTransients(civ);
        Assert.Same(civ, manager.CivInfo);
        Assert.Equal(["Gold"], manager.ValidRewards.Select(it => it.Name));
        var clone = manager.Clone();
        Assert.NotSame(manager, clone);
        Assert.Throws<InvalidOperationException>(() => clone.CivInfo); // transient
        Assert.Throws<InvalidOperationException>(() => new RuinsManager().ValidRewards);
    }

    // ---- VictoryManager and Milestone

    private static Victory AddVictory(FakeGameInfoState info, string name, params string[] milestones)
    {
        var victory = new Victory { Name = name, Milestones = [.. milestones] };
        info.RulesetData.Victories[name] = victory;
        info.GameParameters.VictoryTypes.Add(name);
        return victory;
    }

    [Fact]
    public void VictoryManager_AvailableVictories_ExcludeNeutralDisabledAndUniqueRestricted()
    {
        var (civ, info) = NewCiv();
        AddVictory(info, "Scientific", "Win diplomatic vote");
        AddVictory(info, Constants.NeutralVictoryType);
        info.RulesetData.Victories["Disabled"] = new Victory { Name = "Disabled" };
        info.RulesetData.Victories["Unavailable"] = new Victory { Name = "Unavailable", Uniques = ["Unavailable"] };
        info.GameParameters.VictoryTypes.Add("Unavailable");
        civ.VictoryManager.CivInfo = civ;

        Assert.Equal(["Scientific"], civ.VictoryManager.GetAvailableVictories().Select(it => it.Name));
        info.RulesetData.Victories["Scientific"].HiddenInVictoryScreen = true;
        Assert.Equal(["Scientific"], civ.VictoryManager.GetAvailableVictories().Select(it => it.Name));
        Assert.Empty(civ.VictoryManager.GetVictoriesShownInVictoryScreen());
    }

    [Fact]
    public void VictoryManager_VictoryTypeAchieved_WalksTheMilestonesInOrder()
    {
        var (civ, info) = NewCiv();
        var victory = AddVictory(info, "Diplomatic", "Win diplomatic vote", "Complete [2] Policy branches");
        civ.VictoryManager.CivInfo = civ;
        var manager = civ.VictoryManager;

        Assert.Equal(victory.MilestoneObjects[0], manager.GetNextMilestone(victory));
        Assert.Equal(0, manager.AmountMilestonesCompleted(victory));
        Assert.Null(manager.GetVictoryTypeAchieved());
        Assert.False(manager.HasWon());

        manager.HasEverWonDiplomaticVote = true;
        Assert.Equal(victory.MilestoneObjects[1], manager.GetNextMilestone(victory));
        Assert.Equal(1, manager.AmountMilestonesCompleted(victory));
        CompleteBranch(info, civ, "Tradition");
        Assert.Null(manager.GetVictoryTypeAchieved());
        CompleteBranch(info, civ, "Liberty");
        Assert.Null(manager.GetNextMilestone(victory));
        Assert.Equal(2, manager.AmountMilestonesCompleted(victory));
        Assert.Equal("Diplomatic", manager.GetVictoryTypeAchieved());
        Assert.True(manager.HasWon());
    }

    [Fact]
    public void VictoryManager_VictoryTypeAchieved_FallsBackToTheNeutralVictoryUniqueAndSkipsMinorCivs()
    {
        var (civ, info) = NewCiv();
        civ.VictoryManager.CivInfo = civ;
        Assert.Null(civ.VictoryManager.GetVictoryTypeAchieved());
        SetNationUniques(civ, "Triggers victory");
        Assert.Equal(Constants.NeutralVictoryType, civ.VictoryManager.GetVictoryTypeAchieved());

        var cityState = new Civilization(new NationObject { Name = "Geneva", CityStateType = "Cultured" }) { GameInfo = info };
        SetNationUniques(cityState, "Triggers victory");
        cityState.VictoryManager.CivInfo = cityState;
        Assert.Null(cityState.VictoryManager.GetVictoryTypeAchieved()); // only major civs can win
    }

    [Fact]
    public void VictoryManager_Clone_CopiesTheSpaceshipPartsAndTheVote()
    {
        var manager = new VictoryManager { HasEverWonDiplomaticVote = true };
        manager.CurrentsSpaceshipParts["Booster"] = 2;
        var clone = manager.Clone();
        Assert.True(clone.HasEverWonDiplomaticVote);
        Assert.Equal(2, clone.CurrentsSpaceshipParts["Booster"]);
        Assert.NotSame(manager.CurrentsSpaceshipParts, clone.CurrentsSpaceshipParts);
        Assert.Throws<InvalidOperationException>(() => clone.CivInfo);
    }

    private static List<Civilization> AddMajorCivs(FakeGameInfoState info, Civilization first, int total)
    {
        var civs = new List<Civilization> { first };
        for (int i = 1; i < total; i++)
        {
            var civ = new Civilization(new NationObject { Name = $"Civ{i}" }) { GameInfo = info };
            Wire(civ);
            civ.CityStateFunctions = new FakeCityStateFunctions();
            civ.Units = new TestUnitManager(civ) { Count = 1 };
            info.Civs.Add(civ);
            civs.Add(civ);
        }
        first.Units = new TestUnitManager(first) { Count = 1 };
        return civs;
    }

    [Theory]
    [InlineData(5, 4)] // "4 votes needed to win in a game with 5 players, 7 with 13 and 11 with 28"
    [InlineData(13, 7)]
    [InlineData(28, 11)]
    [InlineData(30, 10)] // above 28: 35 percent
    public void VictoryManager_DiplomaticVote_NeedsTheKotlinVoteCount(int players, int needed)
    {
        var (civ, info) = NewCiv();
        var civs = AddMajorCivs(info, civ, players);
        civ.VictoryManager.CivInfo = civ;
        foreach (var other in civs) info.Votes[other.CivId] = civ.CivId;
        // everybody votes for civ: exactly "players" votes; remove votes until it is one short of the needed count
        foreach (var voter in civs.Skip(needed - 1)) info.Votes[voter.CivId] = null;
        Assert.False(civ.VictoryManager.HasEnoughVotesForDiplomaticVictory());
        info.Votes[civs[needed - 1].CivId] = civ.CivId;
        Assert.True(civ.VictoryManager.HasEnoughVotesForDiplomaticVictory());
    }

    [Fact]
    public void VictoryManager_DiplomaticVote_TiesAndOtherWinnersDoNotCount()
    {
        var (civ, info) = NewCiv();
        var civs = AddMajorCivs(info, civ, 4); // 3 votes are needed: 4 * (67 - 4) / 100 + 1
        civ.VictoryManager.CivInfo = civ;
        info.Votes[civs[0].CivId] = civ.CivId;
        info.Votes[civs[1].CivId] = civ.CivId;
        info.Votes[civs[2].CivId] = civ.CivId;
        info.Votes[civs[3].CivId] = civs[3].CivId;
        Assert.True(civ.VictoryManager.HasEnoughVotesForDiplomaticVictory());
        Assert.False(civs[3].VictoryManager.HasEnoughVotesForDiplomaticVictory()); // not the best civ

        var breakdown = civ.VictoryManager.GetDiplomaticVictoryVoteBreakdown();
        Assert.Equal(3, breakdown.Results[civ.CivId]);
        Assert.Equal("{You have been elected world leader!}", breakdown.WinnerText);
    }

    [Fact]
    public void VictoryManager_VoteBreakdown_ReportsMissingVotesTiesAndOtherLeaders()
    {
        var (civ, info) = NewCiv();
        var civs = AddMajorCivs(info, civ, 4);
        civs[1].Nation.LeaderName = "Cleo";
        civ.VictoryManager.CivInfo = civ;
        Assert.Equal("No valid votes were cast.", civ.VictoryManager.GetDiplomaticVictoryVoteBreakdown().WinnerText);
        info.Votes[civs[0].CivId] = null; // abstained
        Assert.Equal("No valid votes were cast.", civ.VictoryManager.GetDiplomaticVictoryVoteBreakdown().WinnerText);

        info.Votes[civs[1].CivId] = civs[1].CivId;
        info.Votes[civs[2].CivId] = civs[2].CivId;
        var tie = civ.VictoryManager.GetDiplomaticVictoryVoteBreakdown().WinnerText;
        Assert.Equal("{Minimum votes for electing a world leader: [3]}\n{Tied in first position: [Civ1, Civ2]}\n{No world leader was elected.}", tie);

        info.Votes[civs[3].CivId] = civs[1].CivId;
        info.Votes[civs[0].CivId] = civs[1].CivId;
        var other = civ.VictoryManager.GetDiplomaticVictoryVoteBreakdown().WinnerText;
        Assert.Equal("{[Cleo] of [Civ1] has been elected world leader!}", other); // Nation.getLeaderDisplayName: "[leaderName] of [name]"
    }

    [Fact]
    public void Milestone_HasBeenCompletedBy_SimpleTypes()
    {
        var (civ, info) = NewCiv();
        civ.VictoryManager.CivInfo = civ;
        var victory = AddVictory(info, "V",
            "Build [Apollo Program]", "Anyone should build [Manhattan Project]", "Destroy all players", "Win diplomatic vote", "Have highest score after max turns");
        var city = new FakeCityState("c", new FakeCiv("Rome")) { CivData = civ };
        civ.Cities = [city];
        var other = AddMajorCivs(info, civ, 2)[1];
        var otherCity = new FakeCityState("o", new FakeCiv("Civ1")) { CivData = other };
        other.Cities = [otherCity];
        var milestones = victory.MilestoneObjects;

        Assert.False(milestones[0].HasBeenCompletedBy(civ));
        city.Buildings.Add("Apollo Program");
        Assert.True(milestones[0].HasBeenCompletedBy(civ));
        Assert.False(milestones[1].HasBeenCompletedBy(civ));
        otherCity.Buildings.Add("Manhattan Project");
        Assert.True(milestones[1].HasBeenCompletedBy(civ)); // anyone

        Assert.False(milestones[2].HasBeenCompletedBy(civ)); // the other civ still lives
        other.Units = new TestUnitManager(other) { Count = 0 };
        other.Cities = [];
        Assert.True(milestones[2].HasBeenCompletedBy(civ));

        Assert.False(milestones[3].HasBeenCompletedBy(civ));
        civ.VictoryManager.HasEverWonDiplomaticVote = true;
        Assert.True(milestones[3].HasBeenCompletedBy(civ));

        info.GameParameters.MaxTurns = 100;
        info.Turns = 99;
        Assert.False(milestones[4].HasBeenCompletedBy(civ)); // before the time out the scores are not even compared
    }

    [Fact]
    public void Milestone_WorldReligion_NeedsTheMajorityInEveryLivingMajorCiv()
    {
        var (civ, info) = NewCiv();
        var victory = AddVictory(info, "Religious", "Become the world religion");
        var religion = new Religion("Faith", info, civ, 1);
        var milestone = victory.MilestoneObjects[0];
        Assert.False(milestone.HasBeenCompletedBy(civ));
        civ.ReligionManager.Religion = religion;
        civ.Units = new TestUnitManager(civ) { Count = 1 };
        civ.Cities = [new FakeCityState("c", new FakeCiv("Rome")) { CivData = civ, ReligionData = { Majority = religion } }];
        Assert.True(milestone.HasBeenCompletedBy(civ));
        info.ReligionEnabled = false;
        Assert.False(milestone.HasBeenCompletedBy(civ));
        info.ReligionEnabled = true;
        civ.Cities = [new FakeCityState("c", new FakeCiv("Rome")) { CivData = civ }];
        Assert.False(milestone.HasBeenCompletedBy(civ));
    }

    [Fact]
    public void Milestone_Focus_FollowsTheMilestoneType()
    {
        var (civ, info) = NewCiv();
        info.RulesetData.Buildings["Apollo Program"] = new Building { Name = "Apollo Program" };
        var victory = AddVictory(info, "V",
            "Build [Apollo Program]", "Destroy all players", "Capture all capitals", "Complete [3] Policy branches", "Win diplomatic vote",
            "Have highest score after max turns", "Become the world religion", "Have at least [5] [Gold]", "Have at least [5] [Cities]",
            "Have at least [5] [[Land] Units]", "Have at least [5] [Adopted [Tradition] Policies]", "Have at least [5] [Food]");
        var focuses = victory.MilestoneObjects.Select(it => it.GetFocus(civ)).ToList();
        Assert.Equal([
            Victory.FocusType.Production, Victory.FocusType.Military, Victory.FocusType.Military, Victory.FocusType.Culture,
            Victory.FocusType.CityStates, Victory.FocusType.Score, Victory.FocusType.Faith, Victory.FocusType.Gold, Victory.FocusType.Production,
            Victory.FocusType.Military, Victory.FocusType.Culture, Victory.FocusType.Production,
        ], focuses);
    }

    [Fact]
    public void Milestone_HeaderText_ShowsTheProgress()
    {
        var (civ, info) = NewCiv();
        civ.VictoryManager.CivInfo = civ;
        var victory = AddVictory(info, "V",
            "Complete [3] Policy branches", "Have at least [5] [Gold]", "Destroy all players", "Win diplomatic vote");
        var milestones = victory.MilestoneObjects;
        Assert.Equal("{Complete [3] Policy branches} (0/3)", milestones[0].GetVictoryScreenButtonHeaderText(false, civ));
        Assert.Equal("{Complete [3] Policy branches} (3/3)", milestones[0].GetVictoryScreenButtonHeaderText(true, civ));
        Assert.Equal("{Destroy all players} (0/0)", milestones[2].GetVictoryScreenButtonHeaderText(false, civ));
        Assert.Equal("Win diplomatic vote", milestones[3].GetVictoryScreenButtonHeaderText(false, civ));
    }

    [Fact]
    public void Milestone_SpaceshipParts_AreCompleteWhenAllRequiredPartsWereAdded()
    {
        var (civ, info) = NewCiv();
        civ.VictoryManager.CivInfo = civ;
        var victory = AddVictory(info, "Space", "Add all [spaceship parts] in capital");
        victory.RequiredSpaceshipParts = ["Booster", "Booster", "Cockpit"];
        var milestone = victory.MilestoneObjects[0];
        Assert.False(milestone.HasBeenCompletedBy(civ));
        civ.VictoryManager.CurrentsSpaceshipParts["Booster"] = 2;
        Assert.False(milestone.HasBeenCompletedBy(civ));
        Assert.Equal("{Add all [spaceship parts] in capital} (2/3)", milestone.GetVictoryScreenButtonHeaderText(false, civ));
        civ.VictoryManager.CurrentsSpaceshipParts["Cockpit"] = 1;
        Assert.True(milestone.HasBeenCompletedBy(civ));
        Assert.Equal(3, victory.RequiredSpaceshipPartsAsCounter.SumValues()); // the requirement itself is untouched
    }

    /// <summary>Adds a policy branch to the ruleset whose only policy is the branch completion and adopts it.</summary>
    private static void CompleteBranch(FakeGameInfoState info, Civilization civ, string name)
    {
        var complete = new PolicyObject { Name = name + PolicyObject.BranchCompleteSuffix };
        info.RulesetData.PolicyBranches.Put(name, new PolicyBranch { Name = name, Policies = [complete] });
        civ.Policies.AdoptedPolicies.Add(complete.Name);
    }

    private static Unciv.Models.Ruleset.Tech.Technology AddTech(FakeGameInfoState info, string name, params string[] prerequisites)
    {
        if (!info.RulesetData.Eras.ContainsKey("Ancient era"))
            info.RulesetData.Eras.Put("Ancient era", new Unciv.Models.Ruleset.Tech.Era { Name = "Ancient era", EraNumber = 0 });
        var tech = new Unciv.Models.Ruleset.Tech.Technology
        {
            Name = name,
            Cost = 100,
            Prerequisites = [.. prerequisites],
            Column = new Unciv.Models.Ruleset.Tech.TechColumn { ColumnNumber = 0, Era = "Ancient era" },
        };
        info.RulesetData.Technologies.Put(name, tech);
        return tech;
    }

    // ---- Spy and EspionageManager

    private static (Civilization Civ, FakeGameInfoState Info, TechManager Tech) NewSpyCiv(string name = "Rome")
    {
        var (civ, info) = NewCiv(name);
        civ.EspionageManager.SetTransients(civ);
        return (civ, info, civ.Tech);
    }

    [Fact]
    public void SpyAction_Metadata_MatchesTheKotlinEntries()
    {
        Assert.Equal("Establishing Network", SpyAction.EstablishNetwork.GetDisplayString());
        Assert.Equal("Counter-intelligence", SpyAction.CounterIntelligence.GetDisplayString());
        Assert.Equal([SpyAction.Moving, SpyAction.EstablishNetwork, SpyAction.Dead],
            Enum.GetValues<SpyAction>().Where(it => it.HasCountdownTurns()));
        Assert.Equal([SpyAction.Moving, SpyAction.EstablishNetwork, SpyAction.StealingTech, SpyAction.RiggingElections, SpyAction.Coup, SpyAction.Dead],
            Enum.GetValues<SpyAction>().Where(it => it.ShowTurns()));
        Assert.Equal([SpyAction.Surveillance, SpyAction.StealingTech, SpyAction.RiggingElections, SpyAction.Coup, SpyAction.CounterIntelligence],
            Enum.GetValues<SpyAction>().Where(it => it.IsSetUp()));
    }

    [Fact]
    public void EspionageManager_AddSpy_NamesFromTheNationThenNumbersAndUsesTheStartingRank()
    {
        var (civ, _, _) = NewSpyCiv();
        civ.Nation.SpyNames = ["Alpha", "Beta"];
        var first = civ.EspionageManager.AddSpy();
        var second = civ.EspionageManager.AddSpy();
        Assert.Equal(new[] { "Alpha", "Beta" }, new[] { first.Name, second.Name }.OrderBy(it => it));
        Assert.Equal("Spy 3", civ.EspionageManager.AddSpy().Name); // names used up: +1 as non-programmers count from 1
        Assert.Equal(1, first.Rank);
        Assert.Same(civ, first.CivInfo);

        SetNationUniques(civ, "New spies start with [2] level(s)");
        Assert.Equal(3, civ.EspionageManager.AddSpy().Rank);
        Assert.Equal(4, civ.EspionageManager.SpyList.Count);
        Assert.Equal(4, civ.EspionageManager.GetIdleSpies().Count);
    }

    [Fact]
    public void EspionageManager_Clone_CopiesTheSpiesAndErasButNotTheTransients()
    {
        var (civ, _, _) = NewSpyCiv();
        var spy = civ.EspionageManager.AddSpy();
        civ.EspionageManager.ErasSpyEarnedFor.Add("Ancient era");
        civ.EspionageManager.DismissedShouldMoveSpies = true;
        var clone = civ.EspionageManager.Clone();
        Assert.NotSame(civ.EspionageManager.SpyList[0], clone.SpyList[0]);
        Assert.Equal(spy.Name, clone.SpyList[0].Name);
        Assert.Equal(["Ancient era"], clone.ErasSpyEarnedFor);
        Assert.False(clone.DismissedShouldMoveSpies);
    }

    [Fact]
    public void Spy_InTheHideout_HasNoCityAndLevelsUpWithANotification()
    {
        var (civ, _, _) = NewSpyCiv();
        var spy = civ.EspionageManager.AddSpy();
        Assert.Null(spy.GetCityOrNull());
        Assert.Throws<NullReferenceException>(() => spy.GetCity());
        Assert.Equal(Constants.SpyHideout, spy.GetLocationName());
        Assert.True(spy.IsIdle());
        Assert.True(spy.IsAlive());
        Assert.False(spy.IsSetUp());
        Assert.False(spy.IsDoingWork());
        Assert.Equal(30, spy.GetSkillModifierPercent()); // rank 1 * spyRankSkillPercentBonus 30

        spy.LevelUpSpy();
        Assert.Equal(2, spy.Rank);
        Assert.Equal("Your spy [Spy 1] has leveled up!", civ.Notifications.Single().Text);
        spy.LevelUpSpy(5); // capped at maxSpyRank 3: one more level
        Assert.Equal(3, spy.Rank);
        Assert.Equal("Your spy [Spy 1] has leveled up!", civ.Notifications.Last().Text);
        spy.LevelUpSpy(); // already at the maximum
        Assert.Equal(2, civ.Notifications.Count);
        Assert.Equal(90, spy.GetSkillModifierPercent());
        var second = civ.EspionageManager.AddSpy();
        second.LevelUpSpy(5); // from rank 1: two levels at once
        Assert.Equal(3, second.Rank);
        Assert.Equal("Your spy [Spy 2] has leveled up [2] times!", civ.Notifications.Last().Text);
    }

    [Fact]
    public void Spy_EfficiencyModifier_CombinesFriendlyUniquesAndNeverGoesNegative()
    {
        var (civ, _, _) = NewSpyCiv();
        var spy = civ.EspionageManager.AddSpy();
        Assert.Equal(1.0, spy.GetEfficiencyModifier());
        SetNationUniques(civ, "[25]% spy effectiveness [in all cities]");
        // the hideout has no city to match the cityFilter against: the friendly uniques apply in full
        Assert.Equal(1.25, spy.GetEfficiencyModifier());
        SetNationUniques(civ, "[-250]% spy effectiveness [in all cities]");
        Assert.Equal(0.0, spy.GetEfficiencyModifier());
    }

    [Fact]
    public void Spy_MovingToACity_BecomesCounterIntelligenceAtHomeAndEstablishesANetworkAbroad()
    {
        var (civ, info, _) = NewSpyCiv();
        var (other, _) = NewCiv("Egypt");
        info.Civs.Add(other);
        var home = new FakeCityState("home", new FakeCiv("Rome")) { CivData = civ, LocationData = HexCoord.Of(3, 4) };
        var abroad = new FakeCityState("abroad", new FakeCiv("Egypt")) { CivData = other, LocationData = HexCoord.Of(5, 6) };
        var spy = civ.EspionageManager.AddSpy();

        spy.MoveTo(home);
        Assert.Equal(SpyAction.Moving, spy.Action);
        Assert.Equal(1, spy.TurnsRemainingForAction);
        Assert.Equal("home", spy.GetLocationName());
        spy.EndTurn();
        Assert.Equal(SpyAction.CounterIntelligence, spy.Action);
        Assert.Equal(10, spy.TurnsRemainingForAction);
        Assert.True(spy.IsDoingWork());
        spy.EndTurn();
        Assert.Equal(9, spy.TurnsRemainingForAction);

        spy.MoveTo(abroad);
        spy.EndTurn();
        Assert.Equal(SpyAction.EstablishNetwork, spy.Action);
        Assert.Equal(3, spy.TurnsRemainingForAction);
        spy.EndTurn();
        spy.EndTurn();
        Assert.Equal(SpyAction.EstablishNetwork, spy.Action);
        Assert.Equal(1, spy.TurnsRemainingForAction);
        spy.EndTurn();
        Assert.Equal(SpyAction.StealingTech, spy.Action); // a major civ: start stealing
        Assert.Equal(0, spy.TurnsRemainingForAction);

        spy.MoveTo(null);
        Assert.Equal(SpyAction.None, spy.Action);
        Assert.Null(spy.GetCityOrNull());
    }

    [Fact]
    public void Spy_NetworkInACityState_StartsRiggingElectionsFromTheElectionFlag()
    {
        var (civ, info, _) = NewSpyCiv();
        var cityState = new Civilization(new NationObject { Name = "Geneva", CityStateType = "Cultured" }) { GameInfo = info };
        info.Civs.Add(cityState);
        cityState.FlagsCountdown[CivFlags.TurnsTillCityStateElection.ToString()] = 8;
        var city = new FakeCityState("g", new FakeCiv("Geneva")) { CivData = cityState };
        var spy = civ.EspionageManager.AddSpy();
        spy.MoveTo(city);
        spy.EndTurn();
        for (int i = 0; i < 3; i++) spy.EndTurn();
        Assert.Equal(SpyAction.RiggingElections, spy.Action);
        Assert.Equal(7, spy.TurnsRemainingForAction); // the flag's countdown - 1
        cityState.FlagsCountdown[CivFlags.TurnsTillCityStateElection.ToString()] = 5;
        spy.EndTurn();
        Assert.Equal(4, spy.TurnsRemainingForAction);
        Assert.True(spy.CanDoCoup());
    }

    [Fact]
    public void Spy_StealingTech_WithNothingToStealReturnsToSurveillanceAndSaysSo()
    {
        var (civ, info, _) = NewSpyCiv();
        var (other, _) = NewCiv("Egypt");
        AddTech(info, "Writing");
        info.Civs.Add(other);
        var city = new FakeCityState("abroad", new FakeCiv("Egypt")) { CivData = other, LocationData = HexCoord.Of(5, 6) };
        var spy = civ.EspionageManager.AddSpy();
        spy.MoveTo(city);
        spy.SetAction(SpyAction.StealingTech);

        spy.EndTurn();

        Assert.Equal(SpyAction.Surveillance, spy.Action);
        Assert.Equal("Your spy [Spy 1] cannot steal any more techs from [Egypt] as we've already researched all the technology they know!",
            civ.Notifications.Single().Text);

        // techs to steal but no science in the city: silently back to surveillance
        other.Tech.TechsResearched.Add("Writing");
        spy.SetAction(SpyAction.StealingTech);
        spy.EndTurn();
        Assert.Equal(SpyAction.Surveillance, spy.Action);
        Assert.Single(civ.Notifications);
    }

    [Fact]
    public void Spy_StealingTech_ProgressCountsDownAndTheStealIsDeterministic()
    {
        var (civ, info, tech) = NewSpyCiv();
        var (other, _) = NewCiv("Egypt");
        other.Tech.TechsResearched.Add("Writing");
        other.Diplomacy["Rome"] = new DiplomacyManager(other, civ);
        other.EspionageManager.SetTransients(other);
        info.Civs.Add(other);
        AddTech(info, "Writing");
        var city = new FakeCityState("abroad", new FakeCiv("Egypt")) { CivData = other, LocationData = HexCoord.Of(5, 6) };
        city.CurrentCityStats[Unciv.Models.StatTypes.Stat.Science] = 10f;
        var spy = civ.EspionageManager.AddSpy();
        spy.MoveTo(city);
        spy.SetAction(SpyAction.StealingTech);

        spy.EndTurn();

        // cost 100 * spyTechStealCostModifier 1.25 * speed 1 = 125; this turn's progress 10 * (1 * 25 + 75) / 100 = 10 -> ceil(115 / 10) = 12 turns
        Assert.Equal(SpyAction.StealingTech, spy.Action);
        Assert.Equal(12, spy.TurnsRemainingForAction);

        // The steal itself: 123f * turns + x * y is the seed (5 * 6 = 30), spyResult is in 0..299 minus the skill bonus (30)
        info.Turns = 7;
        city.CurrentCityStats[Unciv.Models.StatTypes.Stat.Science] = 1000f;
        spy.SetAction(SpyAction.StealingTech);
        spy.EndTurn(); // progress is 1000, which pays the whole cost
        int seed = (int)(30 + 123f * 7) + JavaHash.HashCode("Spy 1");
        int spyResult = new GameRandom(seed).NextInt(300) - 30;
        if (spyResult >= 200)
        {
            Assert.Contains(other.Notifications, it => it.Text.Contains("was found and killed", StringComparison.Ordinal));
            Assert.Equal(SpyAction.Dead, spy.Action);
            Assert.False(tech.IsResearched("Writing"));
        }
        else
        {
            Assert.True(tech.IsResearched("Writing"));
            Assert.Contains(civ.Notifications, it => it.Text == "Your spy [Spy 1] stole the Technology [Writing] from [abroad]!");
            Assert.Equal(2, spy.Rank);
            Assert.Equal(SpyAction.StealingTech, spy.Action); // progress was reset
        }
    }

    [Fact]
    public void Spy_Coup_RequiresACityStateWhoseAllyIsNotUs_AndTheChanceIsClamped()
    {
        var (civ, info, _) = NewSpyCiv();
        var cityState = new Civilization(new NationObject { Name = "Geneva", CityStateType = "Cultured" }) { GameInfo = info, CityStateFunctions = new FakeCityStateFunctions() };
        info.Civs.Add(cityState);
        var influence = new DiplomacyManager(cityState, civ) { DiplomaticStatus = DiplomaticStatus.Peace };
        cityState.Diplomacy["Rome"] = influence;
        var city = new FakeCityState("g", new FakeCiv("Geneva")) { CivData = cityState, LocationData = HexCoord.Of(1, 1) };
        var spy = civ.EspionageManager.AddSpy();
        Assert.False(spy.CanDoCoup()); // hideout
        spy.MoveTo(city);
        Assert.False(spy.CanDoCoup()); // not set up yet
        spy.SetAction(SpyAction.Surveillance);
        Assert.True(spy.CanDoCoup());

        // no ally: base 50 - (60 - 0) / 2 + 30 / 2 = 35 percent
        Assert.Equal(0.35f, spy.GetCoupChanceOfSuccess(true));
        influence.SetInfluenceWithoutSideEffects(-200f);
        Assert.Equal(0f, spy.GetCoupChanceOfSuccess(false)); // clamped to 0
        influence.SetInfluenceWithoutSideEffects(300f);
        Assert.Equal(0.85f, spy.GetCoupChanceOfSuccess(false)); // clamped to 85
    }

    [Fact]
    public void Spy_DeadSpy_IsRevivedWithANewNameAfterFiveTurns()
    {
        var (civ, _, _) = NewSpyCiv();
        civ.Nation.SpyNames = ["Alpha", "Beta"];
        var spy = civ.EspionageManager.AddSpy();
        string oldName = spy.Name;
        spy.SetAction(SpyAction.Dead, 2);
        Assert.False(spy.IsAlive());
        spy.EndTurn();
        Assert.Equal(SpyAction.Dead, spy.Action);
        spy.EndTurn();
        Assert.Equal(SpyAction.None, spy.Action);
        Assert.NotEqual(oldName, spy.Name);
        Assert.Equal($"We have recruited a new spy name [{spy.Name}] after [{oldName}] was killed.", civ.Notifications.Single().Text);
    }

    [Fact]
    public void EspionageManager_TechsToSteal_AreTheirResearchedTechsWeCanResearch()
    {
        var (civ, info, tech) = NewSpyCiv();
        var (other, _) = NewCiv("Egypt");
        info.Civs.Add(other);
        foreach (var name in new[] { "Writing", "Pottery", "Bronze" }) AddTech(info, name);
        AddTech(info, "Masonry", "Writing");
        foreach (var name in new[] { "Writing", "Pottery", "Masonry", "Bronze" }) other.Tech.TechsResearched.Add(name);
        tech.TechsResearched.Add("Pottery"); // we know it already
        Assert.Equal(["Writing", "Bronze"], civ.EspionageManager.GetTechsToSteal(other).OrderByDescending(it => it)); // Masonry is not researchable yet
        Assert.Equal(2, civ.EspionageManager.GetTechsToSteal(other).Count);
    }

    [Fact]
    public void EspionageManager_RemoveAllSpies_SendsEverySpyHome()
    {
        var (civ, info, _) = NewSpyCiv();
        var (other, _) = NewCiv("Egypt");
        info.Civs.Add(other);
        var city = new FakeCityState("abroad", new FakeCiv("Egypt")) { CivData = other, LocationData = HexCoord.Of(5, 6) };
        other.Cities = [city];
        var spy = civ.EspionageManager.AddSpy();
        spy.MoveTo(city);
        Assert.Equal([spy], civ.EspionageManager.GetSpiesInCity(city));
        Assert.Same(spy, civ.EspionageManager.GetSpyAssignedToCity(city));
        civ.EspionageManager.RecallAllSpiesFrom(other);
        Assert.Null(spy.GetCityOrNull());
        spy.MoveTo(city);
        civ.EspionageManager.RemoveAllSpies();
        Assert.Null(spy.GetCityOrNull());
        Assert.Empty(civ.EspionageManager.GetSpiesInCity(city));
    }

    // ---- T111: review follow-ups of T057

    [Fact]
    public void VictoryManager_VoteBreakdown_TiedLeadersAreListedInJavaHashMapOrder()
    {
        var (civ, info) = NewCiv();
        var civs = AddMajorCivs(info, civ, 3);
        // voter -> candidate; each candidate gets exactly one vote, so all three are tied in first position
        info.Votes[civs[0].CivId] = civs[1].CivId;
        info.Votes[civs[1].CivId] = civs[2].CivId;
        info.Votes[civs[2].CivId] = civs[0].CivId;
        var order = info.Votes.Select(it => it.Value!).ToList(); // java.util.HashMap order of the voters
        var breakdown = civ.VictoryManager.GetDiplomaticVictoryVoteBreakdown();
        Assert.Contains($"Tied in first position: [{string.Join(", ", order)}]", breakdown.WinnerText);
    }

    [Fact]
    public void KotlinMath_SumOf_WrapsOnIntOverflowLikeKotlinSumOf()
    {
        Assert.Equal(int.MinValue, KotlinMath.SumOf(new[] { int.MaxValue, 1 }, it => it));
        Assert.Equal(0, KotlinMath.SumOf(Array.Empty<int>(), it => it));
        Assert.Equal(-12, KotlinMath.ParseInt("-12"));
        Assert.Equal(7, KotlinMath.ParseInt("+7"));
        Assert.Throws<FormatException>(() => KotlinMath.ParseInt(" 7"));
        Assert.Throws<FormatException>(() => KotlinMath.ParseInt("1,000"));
    }

    [Fact]
    public void EspionageManager_StartingSpyRank_WrapsOnOverflow()
    {
        var (civ, _, _) = NewSpyCiv();
        SetNationUniques(civ, "New spies start with [2147483647] level(s)", "New spies start with [1] level(s)");
        Assert.Equal(int.MinValue + 1, civ.EspionageManager.GetStartingSpyRank()); // 1 + wrapped(MaxValue + 1)
    }

    [Fact]
    public void Spy_EfficiencyModifier_SumsOfUniquesWrapLikeKotlin()
    {
        var (civ, _, _) = NewSpyCiv();
        var spy = civ.EspionageManager.AddSpy();
        // 100.0 + wrapped(MaxValue + 1) = 100 - 2^31 -> negative -> clamped to 0 (a throwing Sum would crash instead)
        SetNationUniques(civ, "[2147483647]% spy effectiveness [in all cities]", "[1]% spy effectiveness [in all cities]");
        Assert.Equal(0.0, spy.GetEfficiencyModifier());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Pantheon, Follower")]
    [InlineData("pantheon")]
    [InlineData("")]
    public void ReligionManager_FreeBeliefs_OnlyExactBeliefTypeNamesParse(string key)
    {
        var (civ, _) = NewCiv();
        civ.ReligionManager.FreeBeliefs[key] = 1;
        Assert.Throws<ArgumentException>(() => civ.ReligionManager.FreeBeliefsAsEnums());
    }

    [Fact]
    public void ReligionManager_FreeBeliefs_ExactNamesParse()
    {
        var (civ, _) = NewCiv();
        civ.ReligionManager.FreeBeliefs["Follower"] = 2;
        var result = civ.ReligionManager.FreeBeliefsAsEnums();
        Assert.Equal(2, result[BeliefType.Follower]);
    }

    [Fact]
    public void ReligionManager_BeliefsToChoose_UsesTheFreeExtraBeliefUniquesAndRejectsBadTypeNames()
    {
        var (civ, info) = NewCiv();
        AddBelief(info, "fo1", BeliefType.Follower);
        AddBelief(info, "fo2", BeliefType.Follower);
        AddBelief(info, "f1", BeliefType.Founder);
        SetNationUniques(civ, "May choose [2] additional [Follower] beliefs when [founding] a religion");
        var choose = civ.ReligionManager.GetBeliefsToChooseAtFounding();
        Assert.Equal(2, choose[BeliefType.Follower]); // 1 + 2 wanted, only 2 exist
        Assert.Equal(1, choose[BeliefType.Founder]);
        SetNationUniques(civ, "May choose [2] additional [Bogus] beliefs when [founding] a religion");
        Assert.Throws<ArgumentException>(() => civ.ReligionManager.GetBeliefsToChooseAtFounding());
    }

    [Fact]
    public void ReligionManager_GenerateProphet_SpawnsTheProphetAndPaysTheFaithWhenTheSeededRollSucceeds()
    {
        var (civ, info) = NewCiv();
        var units = (TestUnitManager)civ.Units;
        var prophetUnit = new BaseUnit { Name = "Great Prophet", Uniques = ["May found a religion"] };
        info.RulesetData.Units["Great Prophet"] = prophetUnit;
        info.RulesetData.Religions.Add("Faith");
        AddBelief(info, "p1", BeliefType.Pantheon);
        AddBelief(info, "f1", BeliefType.Founder);
        AddBelief(info, "fo1", BeliefType.Follower);
        var manager = civ.ReligionManager;
        manager.ChooseBeliefs([info.RulesetData.Beliefs["p1"]], useFreeBeliefs: true);
        units.AddedUnitResult = new MapUnit();

        // chance (5 + 195 - 200) / 100 = 0.05 and 6.0: use a turn whose seeded roll fails resp. succeeds
        info.Turns = Enumerable.Range(0, 200).First(it => new GameRandom(it).NextFloat() >= 0.05f);
        manager.StoredFaith = 195 + 200 - 195; // 200: chance 0.05, the roll of this turn is at least that
        manager.StartTurn();
        Assert.Empty(units.AddedUnits);
        Assert.Equal(200, manager.StoredFaith);

        manager.StoredFaith = 395; // chance 2.0: always
        manager.StartTurn();
        var added = Assert.Single(units.AddedUnits);
        Assert.Same(prophetUnit, added.Unit);
        Assert.Null(added.City); // no capital
        Assert.Equal("p1", units.AddedUnitResult.Religion);
        Assert.Equal(195, manager.StoredFaith);
        Assert.Equal(1, civ.ReligionManager.GreatProphetsEarned());
    }

    /// <summary>FoundReligion/UseProphetForEnhancingReligion need a map; the state they set is what ChooseBeliefs reacts to.</summary>
    private static void SetReligionState(ReligionManager manager, ReligionState state) =>
        typeof(ReligionManager).GetProperty(nameof(ReligionManager.ReligionState))!.SetValue(manager, state);

    [Fact]
    public void ReligionManager_ChooseBeliefs_FoundingAndEnhancingMoveThroughTheReligionStates()
    {
        var (civ, info) = NewCiv();
        var f1 = AddBelief(info, "f1", BeliefType.Founder);
        var fo1 = AddBelief(info, "fo1", BeliefType.Follower);
        var e1 = AddBelief(info, "e1", BeliefType.Enhancer);
        var fo2 = AddBelief(info, "fo2", BeliefType.Follower);
        var p1 = AddBelief(info, "p1", BeliefType.Pantheon);
        var manager = civ.ReligionManager;
        manager.ChooseBeliefs([p1], useFreeBeliefs: true);

        SetReligionState(manager, ReligionState.FoundingReligion);
        manager.ChooseBeliefs([f1, fo1]);
        Assert.Equal(ReligionState.Religion, manager.ReligionState);
        Assert.True(manager.Religion!.HasBelief("f1"));
        Assert.True(manager.Religion.HasBelief("fo1"));

        SetReligionState(manager, ReligionState.EnhancingReligion);
        manager.ChooseBeliefs([e1, fo2]);
        Assert.Equal(ReligionState.EnhancedReligion, manager.ReligionState);
        Assert.True(manager.Religion.HasBelief("e1"));
        Assert.True(manager.Religion.HasBelief("fo2"));

        // in a settled state nothing changes
        manager.ChooseBeliefs([]);
        Assert.Equal(ReligionState.EnhancedReligion, manager.ReligionState);
    }

    [Fact]
    public void Spy_InitiateCoup_SucceedsOrFailsOnTheSeededRollAndAdjustsInfluence()
    {
        // success: previous influence 80 without an ally; the roll of the spy's seed must be <= the chance
        for (bool success = true; ; success = !success)
        {
            var (civ, info, _) = NewSpyCiv();
            var cityState = new Civilization(new NationObject { Name = "Geneva", CityStateType = "Cultured" }) { GameInfo = info, CityStateFunctions = new FakeCityStateFunctions() };
            info.Civs.Add(cityState);
            var influence = new DiplomacyManager(cityState, civ) { DiplomaticStatus = DiplomaticStatus.Peace };
            cityState.Diplomacy["Rome"] = influence;
            var city = new FakeCityState("g", new FakeCiv("Geneva")) { CivData = cityState, LocationData = HexCoord.Of(2, 3) };
            var spy = civ.EspionageManager.AddSpy();
            spy.MoveTo(city);
            spy.SetAction(SpyAction.Surveillance);
            // chance: 50 - (60 - infl) / 2 + rank 1 * 30 / 2 -> influence 60 gives 0.65, 0 gives 0.35
            influence.SetInfluenceWithoutSideEffects(success ? 60f : 0f);
            float chance = spy.GetCoupChanceOfSuccess(true);
            // the seed: floor(x * y + 123 * turns) + javaHash(name)
            info.Turns = Enumerable.Range(0, 500).First(turn =>
            {
                float roll = new GameRandom(KotlinMath.FloatToInt(2 * 3 + 123f * turn) + JavaHash.HashCode(spy.Name)).NextFloat();
                return success ? roll <= chance : roll > chance;
            });
            spy.SetAction(SpyAction.Coup);
            spy.EndTurn();

            if (success)
            {
                Assert.Equal(80f, influence.GetInfluence()); // no previous ally: 80
                Assert.Equal(SpyAction.RiggingElections, spy.Action);
                Assert.Contains(civ.Notifications, it => it.Text == "Your spy [" + spy.Name + "] successfully staged a coup in [Geneva]!");
            }
            else
            {
                Assert.Equal(-20f, influence.Influence); // AddInfluence(-20)
                Assert.Contains(civ.Notifications, it => it.Text == "Our spy [" + spy.Name + "] failed to stage a coup in [Geneva] and was killed!");
            }
            if (!success) break;
        }
    }

    [Fact]
    public void Spy_CanMoveTo_AllowsTheCurrentCityAndHonoursTheNoSpiesAgreement()
    {
        var (civ, info, _) = NewSpyCiv();
        var (other, _) = NewCiv("Egypt");
        info.Civs.Add(other);
        var abroad = new FakeCityState("abroad", new FakeCiv("Egypt")) { CivData = other, LocationData = HexCoord.Of(5, 6) };
        var spy = civ.EspionageManager.AddSpy();
        spy.MoveTo(abroad);
        Assert.True(spy.CanMoveTo(abroad)); // already there (the center tile is never consulted)

        var agreement = new DiplomacyManager(other, civ) { DiplomaticStatus = DiplomaticStatus.Peace };
        other.Diplomacy["Rome"] = agreement;
        agreement.SetFlag(DiplomacyFlags.AgreedToNotSendSpies, 10);
        info.RulesetData.Technologies.Put("t", new Unciv.Models.Ruleset.Tech.Technology { Name = "t" });
        // equal tech levels (0%): the agreement forbids sending spies there, even to the city the spy is in
        Assert.False(spy.CanMoveTo(abroad));
    }

    [Fact]
    public void EspionageManager_TilesVisibleViaSpies_IgnoresSpiesThatAreNotSetUp()
    {
        var (civ, _, _) = NewSpyCiv();
        var spy = civ.EspionageManager.AddSpy();
        Assert.Empty(civ.EspionageManager.GetTilesVisibleViaSpies()); // hideout
        Assert.Equal(spy, civ.EspionageManager.SpyList[0]);
        spy.SetAction(SpyAction.Moving, 1);
        Assert.Empty(civ.EspionageManager.GetTilesVisibleViaSpies());
    }
}

// T153: the map-backed leftovers of T057/T111, over the real TestGame (ruins, prophets and spies need real tiles, units and cities).
[Collection("RulesetCache")]
public sealed class CivReligionVictoryMapTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization civ;

    public CivReligionVictoryMapTests()
    {
        testGame.MakeHexagonalMap(4);
        civ = testGame.AddCiv(isPlayer: true);
    }

    private RuinReward AddReward(string name, params string[] uniques)
    {
        var reward = new RuinReward { Name = name, Notification = name };
        foreach (var unique in uniques) reward.Uniques.Add(unique);
        testGame.Ruleset.RuinRewards[name] = reward;
        return reward;
    }

    private static List<string> LastChosen(RuinsManager manager) =>
        (List<string>)typeof(RuinsManager).GetField("lastChosenRewards", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(manager)!;

    [Fact]
    public void RuinsManager_SelectNextRuinsReward_SkipsRewardsWithoutEffectAndRemembersTheWinner()
    {
        testGame.Ruleset.RuinRewards.Clear();
        AddReward("Dud", "[This Unit] gains [10] XP <after turn number [999]>");
        AddReward("Xp", "[This Unit] gains [10] XP");
        var unit = testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.TileMap[0, 0]);

        civ.RuinsManager.SelectNextRuinsReward(unit);

        Assert.Equal(10, unit.Promotions.XP);
        Assert.Equal(["", "Xp"], LastChosen(civ.RuinsManager));
    }

    [Fact]
    public void RuinsManager_SelectNextRuinsReward_NeverRepeatsTheLastTwoChosenRewards()
    {
        testGame.Ruleset.RuinRewards.Clear();
        AddReward("A", "[This Unit] gains [10] XP");
        AddReward("B", "[This Unit] gains [10] XP");
        AddReward("C", "[This Unit] gains [10] XP");
        var unit = testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.TileMap[0, 0]);

        civ.RuinsManager.SelectNextRuinsReward(unit);
        civ.RuinsManager.SelectNextRuinsReward(unit);
        civ.RuinsManager.SelectNextRuinsReward(unit);

        var chosen = LastChosen(civ.RuinsManager);
        Assert.Equal(2, chosen.Distinct().Count());
        Assert.Contains(chosen[1], new[] { "A", "B", "C" });
    }

    [Fact]
    public void RuinsManager_SelectNextRuinsReward_UnavailableAndDifficultyExcludedRewardsAreNeverPicked()
    {
        testGame.Ruleset.RuinRewards.Clear();
        AddReward("Hidden", "[This Unit] gains [10] XP", "Unavailable");
        var excluded = AddReward("Excluded", "[This Unit] gains [10] XP");
        excluded.ExcludedDifficulties = [testGame.GameInfo.Difficulty];
        var unit = testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.TileMap[0, 0]);

        civ.RuinsManager.SelectNextRuinsReward(unit);

        Assert.Equal(0, unit.Promotions.XP);
        Assert.Equal(["", ""], LastChosen(civ.RuinsManager));
    }

    [Fact]
    public void ReligionManager_FoundReligion_NeedsACityCenterAndMarksTheFoundingCity()
    {
        var city = testGame.AddCity(civ, testGame.TileMap[0, 0]);
        var prophet = testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.TileMap[1, 1]);
        var manager = civ.ReligionManager;

        Assert.False(manager.MayFoundReligionHere(prophet.GetTile()));
        manager.FoundReligion(prophet); // not a city centre: ignored
        Assert.Equal(ReligionState.None, manager.ReligionState);

        prophet.PutInTile(city.GetCenterTile());
        Assert.True(manager.MayFoundReligionHere(city.GetCenterTile()));
        manager.FoundReligion(prophet);
        Assert.Equal(ReligionState.FoundingReligion, manager.ReligionState);
        Assert.True((bool)typeof(ReligionManager).GetField("shouldChoosePantheonBelief", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(manager)!);
    }

    [Fact]
    public void ReligionManager_UseProphetForEnhancingReligion_NotifiesOtherHumansWithTheirKnowledge()
    {
        var city = testGame.AddCity(civ, testGame.TileMap[0, 0]);
        var religion = testGame.AddReligion(civ);
        var manager = civ.ReligionManager;
        typeof(ReligionManager).GetProperty(nameof(ReligionManager.ReligionState))!.SetValue(manager, ReligionState.Religion);
        var prophet = testGame.AddDefaultMeleeUnitWithUniques(civ, city.GetCenterTile());
        var other = testGame.AddCiv(isPlayer: true);
        var unaware = testGame.AddCiv(isPlayer: true);
        civ.DiplomacyFunctions.MakeCivilizationsMeet(other);

        // MayEnhanceReligionAtAll needs enough follower and enhancer beliefs left to choose from.
        if (!manager.MayEnhanceReligionHere(city.GetCenterTile()))
        {
            testGame.CreateBelief(BeliefType.Follower);
            testGame.CreateBelief(BeliefType.Enhancer);
        }
        Assert.True(manager.MayEnhanceReligionHere(city.GetCenterTile()));
        manager.UseProphetForEnhancingReligion(prophet);

        Assert.Equal(ReligionState.EnhancingReligion, manager.ReligionState);
        Assert.Contains(other.Notifications, it => it.Text == $"[{civ.CivName}] has enhanced [{religion.DisplayName}]!");
        Assert.Contains(unaware.Notifications, it => it.Text == $"[An unknown civilization] has enhanced [{religion.DisplayName}]!");
        Assert.DoesNotContain(civ.Notifications, it => it.Text.Contains("has enhanced"));
    }

    [Fact]
    public void Spy_CanMoveTo_NeedsAnExploredCityNobodyElseIsWatching_AndAlwaysItsOwnCity()
    {
        var other = testGame.AddCiv();
        var city = testGame.AddCity(other, testGame.TileMap[2, 2]);
        var spy = civ.EspionageManager.AddSpy();
        var rival = civ.EspionageManager.AddSpy();

        Assert.False(spy.CanMoveTo(city)); // unexplored
        city.GetCenterTile().SetExplored(civ, true);
        Assert.True(spy.CanMoveTo(city));

        rival.MoveTo(city);
        Assert.False(spy.CanMoveTo(city)); // taken by the other spy
        Assert.True(rival.CanMoveTo(city)); // already there
    }

    [Fact]
    public void EspionageManager_GetTilesVisibleViaSpies_ListsTheRingAroundEverySetUpSpyCity()
    {
        var other = testGame.AddCiv();
        var city = testGame.AddCity(other, testGame.TileMap[2, 2]);
        var spy = civ.EspionageManager.AddSpy();
        Assert.Empty(civ.EspionageManager.GetTilesVisibleViaSpies()); // hideout

        spy.MoveTo(city);
        Assert.Empty(civ.EspionageManager.GetTilesVisibleViaSpies()); // still moving, not set up

        spy.SetAction(SpyAction.EstablishNetwork, 3);
        Assert.Empty(civ.EspionageManager.GetTilesVisibleViaSpies()); // a network under construction sees nothing yet

        spy.SetAction(SpyAction.Surveillance);
        var visible = civ.EspionageManager.GetTilesVisibleViaSpies().ToList();
        Assert.Equal(7, visible.Count);
        Assert.Contains(city.GetCenterTile(), visible);
        Assert.All(visible, tile => Assert.True(tile.AerialDistanceTo(city.GetCenterTile()) <= 1));
    }
}
