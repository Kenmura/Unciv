using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using EraObject = Unciv.Models.Ruleset.Tech.Era;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using PolicyObject = Unciv.Models.Ruleset.Policy;
using TileImprovementObject = Unciv.Models.Ruleset.Tile.TileImprovement;
using TileResourceObject = Unciv.Models.Ruleset.Tile.TileResource;

namespace Unciv.Core.Tests.Logic;

// T056: TechManager, PolicyManager, GoldenAgeManager, GreatPersonManager. The Kotlin fixtures (GoldenAgeManagerTest,
// GreatPersonSelectionTest) are TestGame-based (T035/T072); the golden age cases are ported over the seam fakes, the rest are
// hand-derived expectations.
public class CivManagerTests
{
    private static (Civilization Civ, FakeGameInfoState Info) NewCiv(string[]? uniques = null, PlayerType playerType = PlayerType.Human)
    {
        var info = new FakeGameInfoState { Map = new TileMap() };
        var civ = new Civilization(new NationObject { Name = "Rome", Uniques = uniques?.ToList() ?? [] })
        {
            GameInfo = info,
            PlayerType = playerType,
            Units = new FakeUnitManager { Count = 1 },
            CityStateFunctions = new FakeCityStateFunctions(),
        };
        civ.State = new GameContext(civ);
        civ.Tech.CivInfo = civ;
        civ.Policies.CivInfo = civ;
        civ.GoldenAges.CivInfo = civ;
        civ.GreatPeople.CivInfo = civ;
        civ.CivConstructions.SetTransients(civ);
        info.Civs.Add(civ);
        return (civ, info);
    }

    private static FakeCityState AddCity(Civilization civ, string id)
    {
        var city = new FakeCityState(id, new FakeCiv("Rome")) { CivData = civ };
        civ.Cities = [.. civ.Cities, city];
        return city;
    }

    // ---- GoldenAgeManager (ported from GoldenAgeManagerTest.kt)

    [Fact]
    public void GoldenAge_GainsPointsWhenHappy()
    {
        var (civ, _) = NewCiv();
        civ.GoldenAges.EndTurn(10);
        Assert.Equal(10, civ.GoldenAges.StoredHappiness);
    }

    [Fact]
    public void GoldenAge_LosesPointsWhenUnhappy_ButNeverGoesNegative()
    {
        var (civ, _) = NewCiv();
        civ.GoldenAges.StoredHappiness = 100;
        civ.GoldenAges.EndTurn(-10);
        Assert.Equal(90, civ.GoldenAges.StoredHappiness);

        civ.GoldenAges.StoredHappiness = 5;
        civ.GoldenAges.EndTurn(-10);
        Assert.Equal(0, civ.GoldenAges.StoredHappiness);
    }

    [Fact]
    public void GoldenAge_StoresNoExcessHappinessDuringAGoldenAge_AndCountsDown()
    {
        var (civ, _) = NewCiv();
        civ.GoldenAges.StoredHappiness = 5;
        civ.GoldenAges.EnterGoldenAge();
        civ.GoldenAges.EndTurn(10);
        Assert.Equal(5, civ.GoldenAges.StoredHappiness);
        Assert.Equal(9, civ.GoldenAges.TurnsLeftForCurrentGoldenAge);
    }

    [Fact]
    public void GoldenAge_StartsWithEnoughHappiness_AndTheCostGrowsOnlyForHappinessTriggeredOnes()
    {
        var (civ, _) = NewCiv();
        var golden = civ.GoldenAges;
        golden.StoredHappiness = 700;
        golden.EndTurn(0);
        Assert.True(golden.IsGoldenAge());
        Assert.Equal(10, golden.TurnsLeftForCurrentGoldenAge);
        Assert.Equal(200, golden.StoredHappiness); // 700 - 500 (no cities: 500 * toPercent(0) = 500)

        var (other, _) = NewCiv();
        var first = other.GoldenAges.HappinessRequiredForNextGoldenAge();
        other.GoldenAges.StoredHappiness = first;
        other.GoldenAges.EndTurn(10);
        Assert.Equal(750, other.GoldenAges.HappinessRequiredForNextGoldenAge()); // (500 + 1 * 250)

        var (outside, _) = NewCiv();
        var before = outside.GoldenAges.HappinessRequiredForNextGoldenAge();
        outside.GoldenAges.EnterGoldenAge(10);
        outside.GoldenAges.EndTurn(10);
        Assert.Equal(before, outside.GoldenAges.HappinessRequiredForNextGoldenAge());
    }

    [Fact]
    public void GoldenAge_CostScalesWithCitiesAndSpeed()
    {
        var (civ, info) = NewCiv();
        AddCity(civ, "a");
        var oneCity = civ.GoldenAges.HappinessRequiredForNextGoldenAge();
        Assert.Equal(505, oneCity); // 500 * 1.01
        AddCity(civ, "b");
        Assert.True(civ.GoldenAges.HappinessRequiredForNextGoldenAge() > oneCity);
        info.SpeedData.Modifier = 2f;
        Assert.Equal(1020, civ.GoldenAges.HappinessRequiredForNextGoldenAge()); // 500 * 1.02 * 2
    }

    [Fact]
    public void GoldenAge_LengthFollowsUniquesAndSpeed_AndEntersUpdateCities()
    {
        var (civ, info) = NewCiv(["[+50]% Golden Age length"]);
        var city = AddCity(civ, "a");
        civ.GoldenAges.EnterGoldenAge(10);
        Assert.Equal(15, civ.GoldenAges.TurnsLeftForCurrentGoldenAge);
        Assert.Equal(1, city.StatsUpdateCalls);
        var alert = Assert.Single(civ.PopupAlerts);
        Assert.Equal(AlertType.GoldenAge, alert.Type);
        Assert.Single(civ.Notifications);

        info.SpeedData.GoldenAgeLengthModifier = 0.67f;
        Assert.Equal(10, civ.GoldenAges.CalculateGoldenAgeLength(10)); // 10 * 1.5 * 0.67 = 10.05
    }

    [Fact]
    public void GoldenAge_CloneCopiesTheData_NotTheCiv()
    {
        var (civ, _) = NewCiv();
        civ.GoldenAges.StoredHappiness = 3;
        civ.GoldenAges.EnterGoldenAge(4);
        civ.GoldenAges.EndTurn(0);
        var clone = civ.GoldenAges.Clone();
        Assert.Equal(3, clone.StoredHappiness);
        Assert.Equal(3, clone.TurnsLeftForCurrentGoldenAge);
        Assert.Throws<InvalidOperationException>(() => clone.CivInfo);
    }

    // ---- GreatPersonManager

    /// <summary>A placed unit: only its identity is checked, every member throws.</summary>
    public class ThrowingUnitProxy : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException(targetMethod?.Name);
    }

    private static IMapUnitState NewPlacedUnit() => System.Reflection.DispatchProxy.Create<IMapUnitState, ThrowingUnitProxy>();

    private static BaseUnit GreatPerson(string name, string? pool = null, string? replaces = null)
    {
        var uniques = new List<string> { "Great Person - [" + name + "]" };
        if (pool != null) uniques.Add("Points from Great Person generation [" + pool + "]");
        return new BaseUnit { Name = name, Uniques = uniques, Replaces = replaces };
    }

    [Fact]
    public void GreatPerson_PointsRequiredDoubleAndTheNewPersonIsPicked()
    {
        var (civ, info) = NewCiv();
        info.RulesetData.Units.Put("Great Scientist", GreatPerson("Great Scientist"));
        var manager = civ.GreatPeople;
        Assert.Equal(100, manager.GetPointsRequiredForGreatPerson("Great Scientist"));
        info.SpeedData.Modifier = 1.5f;
        Assert.Equal(150, manager.GetPointsRequiredForGreatPerson("Great Scientist"));

        manager.GreatPersonPointsCounter["Great Scientist"] = 160;
        Assert.Equal("Great Scientist", manager.GetNewGreatPerson());
        Assert.Equal(10, manager.GreatPersonPointsCounter["Great Scientist"]); // 160 - 150
        Assert.Equal(200, manager.PointsForNextGreatPersonCounter[""]); // doubled, base points without speed
        Assert.Null(manager.GetNewGreatPerson());
    }

    [Fact]
    public void GreatPerson_GreatGeneralsCostMoreEachTime()
    {
        var (civ, _) = NewCiv();
        var manager = civ.GreatPeople;
        manager.GreatGeneralPointsCounter["Great General"] = 250;
        Assert.Equal("Great General", manager.GetNewGreatPerson());
        Assert.Equal(50, manager.GreatGeneralPointsCounter["Great General"]);
        Assert.Equal(250, manager.PointsForNextGreatGeneralCounter["Great General"]);
        Assert.Null(manager.GetNewGreatPerson());
    }

    [Fact]
    public void GreatPerson_PointsForNextTurnSumTheCities()
    {
        var (civ, _) = NewCiv();
        var a = AddCity(civ, "a");
        var b = AddCity(civ, "b");
        a.GreatPersonPoints["Great Artist"] = 3;
        b.GreatPersonPoints["Great Artist"] = 4;
        b.GreatPersonPoints["Great Writer"] = 1;
        civ.GreatPeople.AddGreatPersonPoints();
        civ.GreatPeople.AddGreatPersonPoints();
        Assert.Equal(14, civ.GreatPeople.GreatPersonPointsCounter["Great Artist"]);
        Assert.Equal(2, civ.GreatPeople.GreatPersonPointsCounter["Great Writer"]);
    }

    [Fact]
    public void GreatPerson_FreeChoice_OptionsAndPlacement()
    {
        var (civ, info) = NewCiv();
        var scientist = GreatPerson("Great Scientist");
        var engineer = GreatPerson("Great Engineer");
        var khan = GreatPerson("Khan", replaces: "Great General");
        var general = GreatPerson("Great General");
        foreach (var unit in new[] { scientist, engineer, general, khan }) info.RulesetData.Units.Put(unit.Name, unit);
        var units = (FakeUnitManager)civ.Units;
        var manager = civ.GreatPeople;
        manager.FreeGreatPeople = 1;
        Assert.Empty(manager.GetFreeGreatPersonOptions()); // no cities
        Assert.Null(manager.ChooseFreeGreatPerson("Great Scientist"));

        var city = AddCity(civ, "a");
        civ.Cache.UniqueUnits.Add(khan);
        Assert.Equal(["Great Scientist", "Great Engineer", "Khan"], manager.GetFreeGreatPersonOptions().Select(it => it.Name));
        Assert.Null(manager.ChooseFreeGreatPerson("Great Warrior"));
        Assert.Empty(units.AddedUnits);

        // placement fails: nothing is consumed
        Assert.Null(manager.ChooseFreeGreatPerson("Great Scientist"));
        Assert.Equal(1, manager.FreeGreatPeople);
        Assert.Same(scientist, units.AddedUnits.Single().Unit);
        Assert.Same(city.City, units.AddedUnits.Single().City);

        units.AddedUnitResult = NewPlacedUnit();
        manager.LongCountGPPool.Add("Great Scientist");
        Assert.Same(units.AddedUnitResult, manager.ChooseFreeGreatPerson("Great Scientist"));
        Assert.Equal(0, manager.FreeGreatPeople);
        Assert.Contains("Great Scientist", manager.LongCountGPPool); // not a Maya choice: pool untouched
    }

    [Fact]
    public void GreatPerson_MayaChoiceIsLimitedToThePoolAndConsumesIt()
    {
        var (civ, info) = NewCiv(playerType: PlayerType.Human);
        foreach (var name in new[] { "Great Scientist", "Great Engineer" }) info.RulesetData.Units.Put(name, GreatPerson(name));
        AddCity(civ, "a");
        var units = (FakeUnitManager)civ.Units;
        units.AddedUnitResult = NewPlacedUnit();
        var manager = civ.GreatPeople;
        manager.FreeGreatPeople = 2;
        manager.MayaLimitedFreeGP = 1;
        manager.LongCountGPPool = JavaHashSet<string>.HashSetOf("Great Scientist", "stale choice");

        Assert.Equal(["Great Scientist"], manager.GetFreeGreatPersonOptions().Select(it => it.Name));
        Assert.Null(manager.ChooseFreeGreatPerson("Great Engineer"));
        Assert.Equal(2, manager.FreeGreatPeople);

        Assert.NotNull(manager.ChooseFreeGreatPerson("Great Scientist"));
        Assert.Equal(1, manager.FreeGreatPeople);
        Assert.Equal(0, manager.MayaLimitedFreeGP);
        Assert.Equal(["stale choice"], manager.LongCountGPPool);
        Assert.Contains("Great Engineer", manager.GetFreeGreatPersonOptions().Select(it => it.Name));
    }

    [Fact]
    public void GreatPerson_TriggerMayanFillsThePoolOnlyWhenEmpty_AndSpectatorsGetNothing()
    {
        var (civ, info) = NewCiv();
        info.RulesetData.Units.Put("Great Scientist", GreatPerson("Great Scientist"));
        civ.GreatPeople.TriggerMayanGreatPerson();
        Assert.Equal(1, civ.GreatPeople.FreeGreatPeople);
        Assert.Equal(1, civ.GreatPeople.MayaLimitedFreeGP);
        Assert.Equal(["Great Scientist"], civ.GreatPeople.LongCountGPPool);
        Assert.Single(civ.Notifications);

        civ.GreatPeople.LongCountGPPool = JavaHashSet<string>.HashSetOf("other");
        civ.GreatPeople.TriggerMayanGreatPerson();
        Assert.Equal(["other"], civ.GreatPeople.LongCountGPPool);
        Assert.Equal(2, civ.GreatPeople.FreeGreatPeople);

        var spectator = new Civilization(new NationObject { Name = Constants.Spectator }) { GameInfo = info };
        spectator.GreatPeople.CivInfo = spectator;
        spectator.GreatPeople.TriggerMayanGreatPerson();
        Assert.Equal(0, spectator.GreatPeople.FreeGreatPeople);
    }

    [Fact]
    public void GreatPerson_CloneCopiesCountersAndPool()
    {
        var (civ, _) = NewCiv();
        var manager = civ.GreatPeople;
        manager.GreatPersonPointsCounter["a"] = 5;
        manager.LongCountGPPool.Add("x");
        manager.FreeGreatPeople = 2;
        manager.PointsForNextGreatGeneral = 250;
        var clone = manager.Clone();
        manager.GreatPersonPointsCounter["a"] = 9;
        manager.LongCountGPPool.Add("y");
        Assert.Equal(5, clone.GreatPersonPointsCounter["a"]);
        Assert.Equal(["x"], clone.LongCountGPPool);
        Assert.Equal(2, clone.FreeGreatPeople);
        Assert.Equal(250, clone.PointsForNextGreatGeneral);
    }

    // ---- PolicyManager

    private static (PolicyBranch Branch, PolicyObject Member, PolicyObject Complete) AddBranch(Ruleset ruleset, string name = "Tradition", string era = "Ancient era")
    {
        var branch = new PolicyBranch { Name = name, Era = era, Requires = [] };
        branch.Branch = branch;
        var member = new PolicyObject { Name = name + " Member", Requires = [name], Branch = branch, Uniques = ["[+1 Culture] <in cities>"] };
        var complete = new PolicyObject { Name = name + PolicyObject.BranchCompleteSuffix, Requires = [name], Branch = branch };
        branch.Policies = [member, complete];
        ruleset.PolicyBranches.Put(name, branch);
        ruleset.Policies.Put(name, branch);
        ruleset.Policies.Put(member.Name, member);
        ruleset.Policies.Put(complete.Name, complete);
        return (branch, member, complete);
    }

    private static (Civilization Civ, FakeGameInfoState Info) NewCivWithPolicies()
    {
        var (civ, info) = NewCiv();
        var ancient = new EraObject { Name = "Ancient era", EraNumber = 0 };
        info.RulesetData.Eras.Put(ancient.Name, ancient);
        civ.Tech.Era = ancient;
        AddCity(civ, "capital");
        return (civ, info);
    }

    [Fact]
    public void Policy_CultureCostFollowsTheAdoptedCountCitiesAndRounding()
    {
        var (civ, info) = NewCivWithPolicies();
        var policies = civ.Policies;
        Assert.Equal(25, policies.GetPolicyCultureCost(0)); // 25 + 0^2.01, one city: no city modifier
        Assert.Equal(30, policies.GetPolicyCultureCost(1)); // 25 + 3^2.01 = 34.09 -> 34 -> rounded down to a multiple of 5
        Assert.Equal(25, policies.GetCultureNeededForNextPolicy());

        AddCity(civ, "second");
        Assert.Equal(25, policies.GetPolicyCultureCost(0)); // 25 * 1.1 = 27.5 -> 28 -> 25
        info.SpeedData.CultureCostModifier = 2f;
        Assert.Equal(55, policies.GetPolicyCultureCost(0)); // 25 * 2 * 1.1 = 55
    }

    [Fact]
    public void Policy_AddCultureOpensThePickerWhenAPolicyBecomesAffordable()
    {
        var (civ, info) = NewCivWithPolicies();
        AddBranch(info.RulesetData);
        var policies = civ.Policies;
        policies.AddCulture(24);
        Assert.False(policies.ShouldOpenPolicyPicker);
        Assert.False(policies.ShouldShowPolicyPicker());
        policies.AddCulture(1);
        Assert.True(policies.ShouldOpenPolicyPicker);
        Assert.True(policies.ShouldShowPolicyPicker());
        Assert.Equal(25, policies.StoredCulture);
    }

    [Fact]
    public void Policy_AdoptingAllMembersCompletesTheBranch_AndRemovingRevertsIt()
    {
        var (civ, info) = NewCivWithPolicies();
        var (branch, member, complete) = AddBranch(info.RulesetData);
        var city = FakeCityState.Of(civ.Cities[0]);
        var policies = civ.Policies;
        policies.StoredCulture = 200;
        policies.ShouldOpenPolicyPicker = true;

        Assert.True(policies.IsAdoptable(branch));
        Assert.False(policies.IsAdoptable(member)); // requires the branch start
        Assert.False(policies.IsAdoptable(complete));

        policies.Adopt(branch);
        Assert.Equal(175, policies.StoredCulture);
        Assert.True(policies.IsAdopted("Tradition"));
        Assert.True(policies.IsAdoptable(member));
        Assert.Equal(1, city.StatsUpdateCalls);
        Assert.Equal(1, city.PopulationReassignments);

        policies.Adopt(member);
        Assert.Equal(["Tradition", "Tradition Member", "Tradition Complete"], policies.AdoptedPolicies);
        Assert.Equal(175 - 30, policies.StoredCulture); // the second policy costs 30; the completion is free
        Assert.False(policies.ShouldOpenPolicyPicker); // nothing left to adopt
        Assert.Single(policies.PolicyUniques.GetUniques(UniqueType.Stats));
        Assert.Equal([branch], policies.CompletedBranches);
        Assert.Empty(policies.IncompleteBranches);
        Assert.Equal(2, policies.BranchCompletionMap[branch]); // the member and the completion

        policies.RemovePolicy(member);
        Assert.False(policies.IsAdopted("Tradition Complete"));
        Assert.False(policies.IsAdopted("Tradition Member"));
        Assert.Empty(policies.PolicyUniques.GetUniques(UniqueType.Stats));
        Assert.Equal([branch], policies.IncompleteBranches);
        Assert.Throws<InvalidOperationException>(() => policies.RemovePolicy(member));
    }

    [Fact]
    public void Policy_AdoptWithoutEnoughCultureThrows_UnlessFreeOrGodMode()
    {
        var (civ, info) = NewCivWithPolicies();
        var (branch, member, _) = AddBranch(info.RulesetData);
        var policies = civ.Policies;
        Assert.Throws<InvalidOperationException>(() => policies.Adopt(branch));
        Assert.False(policies.IsAdopted("Tradition"));

        policies.FreePolicies = 1;
        policies.Adopt(branch);
        Assert.Equal(0, policies.FreePolicies);
        Assert.Equal(0, policies.StoredCulture);
        Assert.Equal(25, policies.GetCultureNeededForNextPolicy()); // a free policy does not count as bought

        info.GameParameters.GodMode = true;
        policies.Adopt(member);
        Assert.True(policies.IsAdopted("Tradition Member"));
    }

    [Fact]
    public void Policy_CultureRefundMapMarksSurplusPoliciesAsFree()
    {
        var (civ, info) = NewCivWithPolicies();
        var (branch, member, complete) = AddBranch(info.RulesetData);
        civ.Policies.StoredCulture = 1000;
        civ.Policies.Adopt(branch);
        civ.Policies.Adopt(member);

        var refunds = civ.Policies.GetCultureRefundMap([member, complete, branch], 50);
        Assert.Equal([member, branch], refunds.Keys); // the branch completion is skipped
        Assert.Equal(15, refunds[member]); // cost of the second policy (30) at 50%
        Assert.Equal(13, refunds[branch]); // cost of the first (25) at 50% = 12.5 rounds half up
        Assert.Equal(PolicyManagerFreeMarker, civ.Policies.GetCultureRefundMap([member, branch, member], 50)[member]);
    }

    private const int PolicyManagerFreeMarker = Unciv.Logic.Civilization.Managers.PolicyManager.FreePolicyMarker;

    [Fact]
    public void Policy_AdoptedPoliciesMatchingCanBeSortedForRemoval()
    {
        var (civ, info) = NewCivWithPolicies();
        var (branch, member, _) = AddBranch(info.RulesetData);
        civ.Policies.StoredCulture = 1000;
        civ.Policies.Adopt(branch);
        civ.Policies.Adopt(member);

        Assert.Equal(["Tradition", "Tradition Complete", "Tradition Member"], civ.Policies.GetAdoptedPoliciesMatching("All").Select(it => it.Name)); // java.util.HashSet order
        Assert.Equal(["Tradition Member", "Tradition"], civ.Policies.GetAdoptedPoliciesMatching("All", forRemoval: true).Select(it => it.Name));
        Assert.Empty(civ.Policies.GetAdoptedPoliciesMatching("Something else"));
    }

    [Fact]
    public void Policy_PrioritiesComeFromThePreferredVictoryAndTheAiWeight()
    {
        var (civ, info) = NewCivWithPolicies();
        civ.Nation.PreferredVictoryType = "Cultural";
        civ.PlayerType = PlayerType.AI;
        civ.VictoryManager.CivInfo = civ;
        foreach (var name in new[] { "Cultural", "Scientific" })
        {
            info.RulesetData.Victories[name] = new Victory { Name = name };
            info.GameParameters.VictoryTypes.Add(name);
        }
        var (branch, _, _) = AddBranch(info.RulesetData);
        var (second, _, _) = AddBranch(info.RulesetData, "Liberty");
        branch.Priorities["Cultural"] = 3;
        second.Priorities["Cultural"] = 1;
        second.Uniques = ["[+100]% weight to this choice for AI decisions"]; // x2
        var map = civ.Policies.PriorityMap;
        Assert.Equal(2, map.Count);
        Assert.Equal(3, map[branch]);
        Assert.Equal(2, map[second]);
        Assert.Equal(3, civ.Policies.GetMaxPriority(civ.Policies.Branches));
        Assert.Equal(2, civ.Policies.GetMaxPriority(new LinkedHashSet<PolicyBranch>([second])));
        Assert.Null(civ.Policies.GetMaxPriority(new LinkedHashSet<PolicyBranch>()));
    }

    [Fact]
    public void Policy_GreatWriterCultureAndRecordedTurns()
    {
        var (civ, info) = NewCivWithPolicies();
        AddBranch(info.RulesetData);
        info.Turns = 3;
        civ.Policies.EndTurn(10);
        info.Turns = 11; // 11 % 8 == 3 again: overwritten
        civ.Policies.EndTurn(6);
        info.Turns = 4;
        civ.Policies.EndTurn(5);
        Assert.Equal(11, civ.Policies.GetCultureFromGreatWriter());
        info.SpeedData.CultureCostModifier = 1.5f;
        Assert.Equal(16, civ.Policies.GetCultureFromGreatWriter()); // 16.5 truncated
        Assert.Equal(21, civ.Policies.StoredCulture);
    }

    [Fact]
    public void Policy_CloneCopiesTheDataButNotTheTransients()
    {
        var (civ, info) = NewCivWithPolicies();
        var (branch, _, _) = AddBranch(info.RulesetData);
        civ.Policies.FreePolicies = 1;
        civ.Policies.Adopt(branch);
        civ.Policies.StoredCulture = 12;
        var clone = civ.Policies.Clone();
        Assert.Equal(["Tradition"], clone.AdoptedPolicies);
        Assert.Equal(12, clone.StoredCulture);
        Assert.Empty(clone.PolicyUniques.GetAllUniques());
        clone.SetTransients(civ);
        Assert.Empty(clone.PolicyUniques.GetAllUniques()); // the branch start policy has no uniques
        Assert.Equal(25, clone.GetCultureNeededForNextPolicy()); // free policies do not count towards the cost
    }

    // ---- TechManager

    private sealed class TechWorld
    {
        public required Civilization Civ { get; init; }
        public required FakeGameInfoState Info { get; init; }
        public required Technology Agriculture { get; init; }
        public required Technology Writing { get; init; }
        public required Technology Philosophy { get; init; }
    }

    private static TechWorld NewTechWorld(string[]? uniques = null)
    {
        var (civ, info) = NewCiv(uniques);
        var ancient = new EraObject { Name = "Ancient era", EraNumber = 0 };
        var classical = new EraObject { Name = "Classical era", EraNumber = 1 };
        info.RulesetData.Eras.Put(ancient.Name, ancient);
        info.RulesetData.Eras.Put(classical.Name, classical);
        var column0 = new TechColumn { ColumnNumber = 0, Era = ancient.Name };
        var column1 = new TechColumn { ColumnNumber = 1, Era = ancient.Name };
        var column2 = new TechColumn { ColumnNumber = 2, Era = classical.Name };
        var agriculture = new Technology { Name = "Agriculture", Cost = 100, Column = column0 };
        var writing = new Technology { Name = "Writing", Cost = 100, Column = column1, Prerequisites = ["Agriculture"] };
        var philosophy = new Technology { Name = "Philosophy", Cost = 200, Column = column2, Prerequisites = ["Writing"] };
        foreach (var tech in new[] { agriculture, writing, philosophy }) info.RulesetData.Technologies.Put(tech.Name, tech);
        civ.Tech.Era = ancient;
        return new TechWorld { Civ = civ, Info = info, Agriculture = agriculture, Writing = writing, Philosophy = philosophy };
    }

    [Fact]
    public void Tech_CostScalesWithDifficultyMapSizeAndCities()
    {
        var world = NewTechWorld();
        var tech = world.Civ.Tech;
        // 100 * 1.1 (medium map) * (1 - 0.05) for having no cities
        Assert.Equal(104, tech.CostOfTech("Agriculture"));
        AddCity(world.Civ, "a");
        Assert.Equal(110, tech.CostOfTech("Agriculture"));
        AddCity(world.Civ, "b");
        Assert.Equal(115, tech.CostOfTech("Agriculture")); // 110 * 1.05
        world.Info.Difficulty = new Unciv.Models.Ruleset.Nation.Difficulty { Name = "Hard", ResearchCostModifier = 2f };
        Assert.Equal(230, tech.CostOfTech("Agriculture")); // 100 * 2 * 1.1 * 1.05 = 230.99999 in Float, truncated
        world.Civ.PlayerType = PlayerType.AI;
        Assert.Equal(115, tech.CostOfTech("Agriculture")); // the difficulty only applies to humans
    }

    [Fact]
    public void Tech_ScienceFlowsIntoTheCurrentTechnologyAndOverflows()
    {
        var world = NewTechWorld();
        AddCity(world.Civ, "a");
        var tech = world.Civ.Tech;
        Assert.Null(tech.CurrentTechnology());
        tech.AddScience(5); // nothing being researched: stored as overflow
        Assert.Equal(5, tech.GetOverflowScience());

        tech.TechsToResearch.AddRange(["Agriculture", "Writing"]);
        Assert.Same(world.Agriculture, tech.CurrentTechnology());
        Assert.Equal(105, tech.RemainingScienceToTech("Agriculture")); // 110 - 0 - 5 overflow
        tech.AddScience(50);
        Assert.Equal(50, tech.ResearchOfTech("Agriculture"));
        Assert.False(tech.IsResearched("Agriculture"));

        world.Civ.Stats.StatsForNextTurn = new Unciv.Models.StatTypes.Stats(science: 3f);
        tech.AddScience(70); // 120 of 110: done, 10 left over, limited to max(3 * 5, 100) = 100
        Assert.True(tech.IsResearched("Agriculture"));
        Assert.Equal(["Writing"], tech.TechsToResearch);
        Assert.Equal(0, tech.ResearchOfTech("Agriculture"));
        Assert.Equal(15, tech.GetOverflowScience()); // 5 + 10
        Assert.Single(world.Civ.PopupAlerts);
        Assert.Equal(AlertType.TechResearched, world.Civ.PopupAlerts[0].Type);
        Assert.Equal(["Agriculture"], tech.ResearchedTechnologies.Select(it => it.Name));
    }

    [Fact]
    public void Tech_EndTurnRecordsScienceAndAppliesOverflowAndResearchAgreements()
    {
        var world = NewTechWorld();
        AddCity(world.Civ, "a");
        var tech = world.Civ.Tech;
        world.Info.Turns = 9;
        tech.EndTurn(7); // no technology: only recorded
        Assert.Equal(7, tech.ScienceOfLast8Turns[1]);
        Assert.Equal(0, tech.GetOverflowScience());

        tech.TechsToResearch.Add("Agriculture");
        tech.ScienceFromResearchAgreements = 30; // 30 / 3 * 0.5 = 5
        tech.EndTurn(10);
        Assert.Equal(15, tech.ResearchOfTech("Agriculture"));
        Assert.Equal(0, tech.ScienceFromResearchAgreements);
        Assert.Contains(world.Civ.Notifications, it => it.Text == "We gained [5] Science from Research Agreement");
        Assert.Equal(10, tech.GetScienceFromGreatScientist()); // both turns are slot 1 (9 % 8 and 9 % 8): the later one wins
    }

    [Fact]
    public void Tech_PrerequisitesAndRequiredPath()
    {
        var world = NewTechWorld();
        var tech = world.Civ.Tech;
        Assert.True(tech.CanBeResearched("Agriculture"));
        Assert.False(tech.CanBeResearched("Writing"));
        Assert.Equal(["Agriculture", "Writing", "Philosophy"], tech.GetRequiredTechsToDestination(world.Philosophy).Select(it => it.Name));
        tech.TechsResearched.Add("Agriculture");
        Assert.False(tech.CanBeResearched("Agriculture"));
        Assert.True(tech.CanBeResearched("Writing"));
        Assert.Equal(["Writing", "Philosophy"], tech.GetRequiredTechsToDestination(world.Philosophy).Select(it => it.Name));
        Assert.True(tech.CanResearchTech());

        var future = new Technology { Name = "Future Tech", Cost = 1000, Column = world.Philosophy.Column, Prerequisites = ["Philosophy"], Uniques = [UniqueType.ResearchableMultipleTimes.Text()] };
        world.Info.RulesetData.Technologies.Put(future.Name, future);
        tech.TechsResearched.Add("Future Tech");
        Assert.False(tech.CanBeResearched("Future Tech")); // prerequisites unmet
        tech.TechsResearched.Add("Philosophy");
        Assert.True(tech.CanBeResearched("Future Tech")); // researched before, but repeatable
    }

    [Fact]
    public void Tech_UnavailableTechsAreUnresearchable()
    {
        var world = NewTechWorld();
        world.Writing.Uniques = [UniqueType.Unavailable.Text()];
        world.Philosophy.Uniques = [UniqueType.OnlyAvailable.Text() + " <after adopting [Liberty]>"];
        Assert.False(world.Civ.Tech.IsUnresearchable(world.Agriculture));
        Assert.True(world.Civ.Tech.IsUnresearchable(world.Writing));
        Assert.True(world.Civ.Tech.IsUnresearchable(world.Philosophy));
        world.Civ.Policies.AdoptedPolicies.Add("Liberty");
        Assert.False(world.Civ.Tech.IsUnresearchable(world.Philosophy));
        // a path through an unresearchable technology does not exist
        Assert.Empty(world.Civ.Tech.GetRequiredTechsToDestination(world.Writing));
        Assert.Empty(world.Civ.Tech.GetRequiredTechsToDestination(world.Philosophy));
    }

    [Fact]
    public void Tech_AddingTechnologiesMovesThroughTheEras()
    {
        var world = NewTechWorld();
        AddCity(world.Civ, "a");
        var tech = world.Civ.Tech;
        tech.AddTechnology("Agriculture");
        Assert.Equal("Ancient era", tech.Era.Name);
        Assert.Contains(world.Civ.Notifications, it => it.Text == "Research of [Agriculture] has completed!");
        tech.AddTechnology("Writing", showNotification: false);
        // everything below the classical column is researched, so the minimum era of the non-researched techs is the classical one
        Assert.Equal("Classical era", tech.Era.Name);
        Assert.DoesNotContain(world.Civ.Notifications, it => it.Text.Contains("Writing", StringComparison.Ordinal));
        tech.AddTechnology("Philosophy");
        Assert.Equal("Classical era", tech.Era.Name);
        Assert.True(tech.AllTechsAreResearched());
        Assert.Equal(3, tech.GetNumberOfTechsResearched());
        Assert.False(tech.CanResearchTech());
    }

    [Fact]
    public void Tech_RevealedResourcesAreNotifiedToHumansOnly()
    {
        var world = NewTechWorld();
        var iron = new TileResourceObject { Name = "Iron", RevealedBy = "Agriculture" };
        world.Info.RulesetData.TileResources.Put("Iron", iron);
        world.Civ.Tech.AddTechnology("Agriculture");
        var notified = Assert.Single(world.Info.ExploredResourceNotifications);
        Assert.Same(iron, notified.Resource);
        Assert.Equal(5, notified.MaxDistance);
        Assert.True(world.Civ.Tech.IsRevealed(iron));
        Assert.True(world.Civ.Tech.IsRevealed(new TileResourceObject { Name = "Wheat" }));
        Assert.False(world.Civ.Tech.IsRevealed(new TileResourceObject { Name = "Gold", RevealedBy = "Writing" }));

        var ai = NewTechWorld();
        ai.Civ.PlayerType = PlayerType.AI;
        ai.Info.RulesetData.TileResources.Put("Iron", iron);
        ai.Civ.Tech.AddTechnology("Agriculture");
        Assert.Empty(ai.Info.ExploredResourceNotifications);
    }

    [Fact]
    public void Tech_ObsoleteUnitsAreReplacedInTheConstructionQueues()
    {
        var world = NewTechWorld();
        var city = AddCity(world.Civ, "Rome");
        var other = AddCity(world.Civ, "Veii");
        city.LocationData = HexCoord.Of(1, 2);
        var warrior = new BaseUnit { Name = "Warrior", ObsoleteTech = "Agriculture", UpgradesTo = "Swordsman" };
        var swordsman = new BaseUnit { Name = "Swordsman" };
        var archer = new BaseUnit { Name = "Archer", ObsoleteTech = "Agriculture" }; // no upgrade: nothing to replace
        foreach (var unit in new[] { warrior, swordsman, archer }) world.Info.RulesetData.Units.Put(unit.Name, unit);
        city.ConstructionQueue.AddRange(["Warrior", "Granary", "Warrior"]);
        other.ConstructionQueue.AddRange(["Granary"]);

        world.Civ.Tech.AddTechnology("Agriculture");

        Assert.Equal(["Swordsman", "Granary", "Swordsman"], city.ConstructionQueue);
        Assert.Equal(["Granary"], other.ConstructionQueue);
        Assert.Equal(1, city.InProgressValidations);
        Assert.Equal(0, other.InProgressValidations);
        var notification = Assert.Single(world.Civ.Notifications, it => it.Category == NotificationCategoryProduction);
        Assert.Equal("[Rome] changed production from [Warrior] to [Swordsman]", notification.Text);
        Assert.Equal(["Warrior", "StatIcons/Production", "Swordsman"], notification.Icons);
        Assert.Equal(1, city.PopulationReassignments);
    }

    private const Unciv.Logic.Civilization.Notification.NotificationCategory NotificationCategoryProduction = Unciv.Logic.Civilization.Notification.NotificationCategory.Production;

    [Fact]
    public void Tech_SetTransientsRebuildsTheTransientStateFromTheNames()
    {
        var world = NewTechWorld();
        world.Writing.Uniques = ["[+1 Culture] <in cities>"];
        world.Civ.Tech.TechsResearched.Add("Agriculture");
        world.Civ.Tech.TechsResearched.Add("Writing");
        var clone = world.Civ.Tech.Clone();
        Assert.Empty(clone.ResearchedTechnologies);
        clone.SetTransients(world.Civ);
        Assert.Equal(2, clone.ResearchedTechnologies.Count);
        Assert.Single(clone.TechUniques.GetUniques(UniqueType.Stats));
        Assert.Equal("Classical era", clone.Era.Name); // every ancient technology is researched: the era of the lowest missing column applies
        Assert.Equal(0.5f, clone.MovementSpeedOnRoads); // RoadStatus.Road.movement
        Assert.False(clone.UnitsCanEmbark);
    }

    [Fact]
    public void Tech_CloneCopiesTheSerializedState()
    {
        var world = NewTechWorld();
        var tech = world.Civ.Tech;
        tech.FreeTechs = 2;
        tech.RepeatingTechsResearched = 3;
        tech.TechsResearched.Add("Agriculture");
        tech.TechsInProgress["Writing"] = 4;
        tech.TechsToResearch.Add("Writing");
        tech.ScienceOfLast8Turns[2] = 9;
        tech.ScienceFromResearchAgreements = 6;
        tech.GoldPercentConvertedToScience = 0.25f;
        var clone = tech.Clone();
        tech.TechsResearched.Add("Writing");
        tech.ScienceOfLast8Turns[2] = 1;
        Assert.Equal(2, clone.FreeTechs);
        Assert.Equal(3, clone.RepeatingTechsResearched);
        Assert.Equal(["Agriculture"], clone.TechsResearched);
        Assert.Equal(4, clone.TechsInProgress["Writing"]);
        Assert.Equal(["Writing"], clone.TechsToResearch);
        Assert.Equal(9, clone.ScienceOfLast8Turns[2]);
        Assert.Equal(6, clone.ScienceFromResearchAgreements);
        Assert.Equal(0.25f, clone.GoldPercentConvertedToScience);
        Assert.Equal(4, clone.GetNumberOfTechsResearched()); // one researched, three repeated
    }

    [Fact]
    public void Tech_FreeTechnologyAndTurnsToTech()
    {
        var world = NewTechWorld();
        AddCity(world.Civ, "a");
        var tech = world.Civ.Tech;
        tech.FreeTechs = 1;
        tech.GetFreeTechnology("Agriculture");
        Assert.Equal(0, tech.FreeTechs);
        Assert.True(tech.IsResearched("Agriculture"));
        Assert.Equal("\u221e", tech.TurnsToTech("Writing")); // no science per turn
        world.Civ.Stats.StatsForNextTurn = new Unciv.Models.StatTypes.Stats(science: 20f);
        Assert.Equal("6", tech.TurnsToTech("Writing")); // 110 / 20 = 5.5
        tech.TechsInProgress["Writing"] = 500;
        Assert.Equal("0", tech.TurnsToTech("Writing")); // nothing left to do
    }

    // ---- T114: cases the T056 review found missing

    private static TileImprovementObject Improvement(string name, string? techRequired = null, params string[] uniques) =>
        new() { Name = name, TechRequired = techRequired, Uniques = [.. uniques] };

    [Fact]
    public void Tech_BestRoadAvailableFollowsTheRequiredTechsAndBuildingProblems()
    {
        var world = NewTechWorld();
        world.Info.RulesetData.TileImprovements.Put("Road", Improvement("Road", "Agriculture"));
        world.Info.RulesetData.TileImprovements.Put("Railroad", Improvement("Railroad", "Writing"));
        var tech = world.Civ.Tech;
        Assert.Equal(RoadStatus.None, tech.GetBestRoadAvailable());
        tech.AddTechnology("Agriculture");
        Assert.Equal(RoadStatus.Road, tech.GetBestRoadAvailable());
        tech.AddTechnology("Writing");
        Assert.Equal(RoadStatus.Railroad, tech.GetBestRoadAvailable());

        // A railroad that can never be built falls back to the road
        var blocked = NewTechWorld();
        blocked.Info.RulesetData.TileImprovements.Put("Road", Improvement("Road"));
        blocked.Info.RulesetData.TileImprovements.Put("Railroad", Improvement("Railroad", null, "Unbuildable"));
        Assert.Equal(RoadStatus.Road, blocked.Civ.Tech.GetBestRoadAvailable());

        // Mods may have no roads at all
        Assert.Equal(RoadStatus.None, NewTechWorld().Civ.Tech.GetBestRoadAvailable());
    }

    [Fact]
    public void Tech_TransientBooleansReadTheOceanAndRoadUniques()
    {
        var plain = NewTechWorld();
        plain.Civ.Tech.SetTransients(plain.Civ);
        var tech = plain.Civ.Tech;
        Assert.Equal((false, false, false, false, false, false), (tech.UnitsCanEmbark, tech.AllUnitsCanEnterOcean, tech.EmbarkedUnitsCanEnterOcean, tech.SpecificUnitsCanEnterOcean, tech.RoadsConnectAcrossRivers, false));
        Assert.Equal(0.5f, tech.MovementSpeedOnRoads);

        var all = NewTechWorld(["Enables [All] units to enter ocean tiles", "Roads connect tiles across rivers", "Improves movement speed on roads", "Enables embarkation for land units"]);
        all.Civ.Tech.SetTransients(all.Civ);
        Assert.Equal((true, true, true, false, true), (all.Civ.Tech.UnitsCanEmbark, all.Civ.Tech.AllUnitsCanEnterOcean, all.Civ.Tech.EmbarkedUnitsCanEnterOcean, all.Civ.Tech.SpecificUnitsCanEnterOcean, all.Civ.Tech.RoadsConnectAcrossRivers));
        Assert.Equal(1f / 3f, all.Civ.Tech.MovementSpeedOnRoads);

        var embarked = NewTechWorld(["Enables [Embarked] units to enter ocean tiles"]);
        embarked.Civ.Tech.SetTransients(embarked.Civ);
        Assert.Equal((false, true, false), (embarked.Civ.Tech.AllUnitsCanEnterOcean, embarked.Civ.Tech.EmbarkedUnitsCanEnterOcean, embarked.Civ.Tech.SpecificUnitsCanEnterOcean));

        var specific = NewTechWorld(["Enables [Great Admiral] units to enter ocean tiles"]);
        specific.Civ.Tech.SetTransients(specific.Civ);
        Assert.Equal((false, false, true), (specific.Civ.Tech.AllUnitsCanEnterOcean, specific.Civ.Tech.EmbarkedUnitsCanEnterOcean, specific.Civ.Tech.SpecificUnitsCanEnterOcean));
    }

    [Fact]
    public void Tech_IsObsoleteOnceTheObsoletingTechIsResearched()
    {
        var world = NewTechWorld();
        var warrior = new BaseUnit { Name = "Warrior", ObsoleteTech = "Writing" };
        var archer = new BaseUnit { Name = "Archer" };
        Assert.False(world.Civ.Tech.IsObsolete(warrior));
        world.Civ.Tech.AddTechnology("Agriculture");
        Assert.False(world.Civ.Tech.IsObsolete(warrior));
        world.Civ.Tech.AddTechnology("Writing");
        Assert.True(world.Civ.Tech.IsObsolete(warrior));
        Assert.False(world.Civ.Tech.IsObsolete(archer)); // never obsolete
    }

    [Fact]
    public void Tech_OverflowScienceIsLimitedToTheLargerOfFiveTurnsOfScienceAndTheTechCost()
    {
        var world = NewTechWorld();
        var tech = world.Civ.Tech;
        tech.TechsToResearch.Add("Agriculture");
        tech.AddScience(1000); // cost 104: 896 left over, limited to max(0 * 5, base cost 100)
        Assert.True(tech.IsResearched("Agriculture"));
        Assert.Equal(100, tech.GetOverflowScience());

        var rich = NewTechWorld();
        rich.Civ.Stats.StatsForNextTurn = new Unciv.Models.StatTypes.Stats(science: 30.9f);
        rich.Civ.Tech.TechsToResearch.Add("Agriculture");
        rich.Civ.Tech.AddScience(1000); // max(30 * 5, 100) = 150
        Assert.Equal(150, rich.Civ.Tech.GetOverflowScience());

        var small = NewTechWorld();
        small.Civ.Tech.TechsToResearch.Add("Agriculture");
        small.Civ.Tech.AddScience(104 + 7);
        Assert.Equal(7, small.Civ.Tech.GetOverflowScience()); // below the limit: untouched
    }

    [Fact]
    public void Tech_UpdateResearchProgressCompletesATechThatStoredOverflowNowPays()
    {
        var world = NewTechWorld();
        var tech = world.Civ.Tech;
        tech.UpdateResearchProgress(); // nothing being researched
        tech.AddScience(5); // stored as overflow
        tech.TechsToResearch.Add("Agriculture");
        tech.TechsInProgress["Agriculture"] = 90;
        tech.UpdateResearchProgress(); // 90 + 5 < 104: nothing changes
        Assert.False(tech.IsResearched("Agriculture"));
        Assert.Equal(5, tech.GetOverflowScience());
        Assert.Equal(90, tech.ResearchOfTech("Agriculture"));

        tech.TechsInProgress["Agriculture"] = 100; // 100 + 5 >= 104
        tech.UpdateResearchProgress();
        Assert.True(tech.IsResearched("Agriculture"));
        Assert.Equal(1, tech.GetOverflowScience()); // 105 - 104 left over
    }

    [Fact]
    public void Tech_ScienceOfLast8TurnsMayBeNegative_AndTruncatesTowardsZero()
    {
        var world = NewTechWorld();
        var tech = world.Civ.Tech;
        world.Info.Turns = 0;
        tech.EndTurn(-7);
        world.Info.Turns = 1;
        tech.EndTurn(-4);
        Assert.Equal(-11, tech.GetScienceFromGreatScientist());
        world.Info.SpeedData.ScienceCostModifier = 1.5f;
        Assert.Equal(-16, tech.GetScienceFromGreatScientist()); // -16.5 truncated towards zero
    }

    [Fact]
    public void Tech_EnteringAnEraAnnouncesItToKnownCivsAndUnlockedPolicyBranches()
    {
        var world = NewTechWorld();
        AddBranch(world.Info.RulesetData, "Tradition", "Classical era");
        AddBranch(world.Info.RulesetData, "Liberty", "Ancient era"); // adoptable already, not announced
        var other = new Civilization(new NationObject { Name = "Greece" })
        {
            GameInfo = world.Info,
            PlayerType = PlayerType.Human,
            Units = new FakeUnitManager { Count = 1 },
            CityStateFunctions = new FakeCityStateFunctions(),
        };
        world.Info.Civs.Add(other);
        world.Civ.DiplomacyFunctions.MakeCivilizationsMeet(other);

        world.Civ.Tech.AddTechnology("Agriculture", showNotification: false);
        world.Civ.Tech.AddTechnology("Writing", showNotification: false); // era change without notifications
        Assert.Empty(other.Notifications);
        Assert.DoesNotContain(world.Civ.Notifications, it => it.Text.Contains("entered", StringComparison.Ordinal));

        var again = NewTechWorld();
        AddBranch(again.Info.RulesetData, "Tradition", "Classical era");
        AddBranch(again.Info.RulesetData, "Liberty", "Ancient era");
        var neighbour = new Civilization(new NationObject { Name = "Greece" })
        {
            GameInfo = again.Info,
            PlayerType = PlayerType.Human,
            Units = new FakeUnitManager { Count = 1 },
            CityStateFunctions = new FakeCityStateFunctions(),
        };
        again.Info.Civs.Add(neighbour);
        again.Civ.DiplomacyFunctions.MakeCivilizationsMeet(neighbour);
        again.Civ.Tech.AddTechnology("Agriculture");
        Assert.DoesNotContain(neighbour.Notifications, it => it.Text.Contains("entered", StringComparison.Ordinal)); // still the Ancient era
        again.Civ.Tech.AddTechnology("Writing");
        Assert.Contains(again.Civ.Notifications, it => it.Text == "You have entered the [Classical era]!");
        Assert.Contains(neighbour.Notifications, it => it.Text == "[Rome] has entered the [Classical era]!");
        var unlocked = Assert.Single(again.Civ.Notifications, it => it.Text.EndsWith("policy branch unlocked!", StringComparison.Ordinal));
        Assert.Equal("[Tradition] policy branch unlocked!", unlocked.Text);
    }

    [Fact]
    public void Tech_TheMayanLongCountIsAnnouncedOnTheNamedTechOnly()
    {
        var world = NewTechWorld(["Receive a free Great Person at the end of every [Long Count cycle] (every 394 years), after researching [Writing]. Each bonus person can only be chosen once."]);
        world.Civ.Tech.AddTechnology("Agriculture");
        Assert.DoesNotContain(world.Civ.Notifications, it => it.Text.Contains("Long Count", StringComparison.Ordinal));
        world.Civ.Tech.AddTechnology("Writing");
        var notification = Assert.Single(world.Civ.Notifications, it => it.Text == "You have unlocked [The Long Count]!");
        Assert.IsType<MayaLongCountAction>(Assert.Single(notification.Actions!));
    }

    [Fact]
    public void Policy_AdoptableBranchesAreInRulesetOrderAndRespectTheEra()
    {
        var (civ, info) = NewCivWithPolicies();
        var classical = new EraObject { Name = "Classical era", EraNumber = 1 };
        info.RulesetData.Eras.Put(classical.Name, classical);
        var (tradition, _, _) = AddBranch(info.RulesetData, "Tradition", "Ancient era");
        var (liberty, _, _) = AddBranch(info.RulesetData, "Liberty", "Classical era");
        var policies = civ.Policies;
        policies.StoredCulture = 100;
        Assert.Equal([tradition], policies.AdoptableBranches);
        civ.Tech.Era = classical;
        Assert.Equal([tradition, liberty], policies.AdoptableBranches);
        policies.Adopt(tradition);
        Assert.Equal([liberty], policies.AdoptableBranches); // adopted: no longer new
        Assert.Equal([tradition], policies.IncompleteBranches); // Liberty's members need Liberty itself first
    }

    [Fact]
    public void Policy_RemovingAPolicyKeepsTheAdoptionCountWhenAssumedFreeOrAlreadyZeroAndThrowsForNonAdopted()
    {
        var (civ, info) = NewCivWithPolicies();
        var (branch, _, complete) = AddBranch(info.RulesetData);
        var policies = civ.Policies;
        policies.StoredCulture = 1000;
        policies.Adopt(branch);
        policies.Adopt(branch.Policies[0]);
        policies.RemovePolicy(branch.Policies[0], assumeWasFree: true); // the member
        // Kotlin quirk kept: the recursive removal of the branch completion does not pass assumeWasFree on, so it still counts one down
        Assert.False(policies.IsAdopted(complete.Name));
        Assert.Equal(policies.GetPolicyCultureCost(1), policies.GetCultureNeededForNextPolicy());

        policies.RemovePolicy(branch, assumeWasFree: true);
        Assert.Equal(policies.GetPolicyCultureCost(1), policies.GetCultureNeededForNextPolicy()); // the count was not touched

        policies.StoredCulture = 1000;
        policies.Adopt(branch);
        Assert.Equal(policies.GetPolicyCultureCost(2), policies.GetCultureNeededForNextPolicy());
        policies.RemovePolicy(branch);
        Assert.Equal(policies.GetPolicyCultureCost(1), policies.GetCultureNeededForNextPolicy());

        // Free policies leave the count at 0, and removing one must not drive it negative
        var (freeCiv, freeInfo) = NewCivWithPolicies();
        var (freeBranch, _, _) = AddBranch(freeInfo.RulesetData);
        freeCiv.Policies.FreePolicies = 1;
        freeCiv.Policies.Adopt(freeBranch);
        freeCiv.Policies.RemovePolicy(freeBranch);
        Assert.Equal(25, freeCiv.Policies.GetCultureNeededForNextPolicy());

        var error = Assert.Throws<InvalidOperationException>(() => freeCiv.Policies.RemovePolicy(freeBranch));
        Assert.Equal("Attempt to remove non-adopted Policy Tradition", error.Message);
        Assert.Equal(25, freeCiv.Policies.GetCultureNeededForNextPolicy()); // a failed removal changes nothing
    }

    [Fact]
    public void Policy_CultureRefundRoundsHalfUpForOddPercentages()
    {
        var (civ, info) = NewCivWithPolicies();
        var (branch, member, _) = AddBranch(info.RulesetData);
        civ.Policies.StoredCulture = 1000;
        civ.Policies.Adopt(branch);
        civ.Policies.Adopt(member);

        var refunds = civ.Policies.GetCultureRefundMap([member, branch], 33);
        Assert.Equal(10, refunds[member]); // 30 * 33 / 100f = 9.9
        Assert.Equal(8, refunds[branch]); // 25 * 33 / 100f = 8.25
        refunds = civ.Policies.GetCultureRefundMap([member, branch], 2);
        Assert.Equal(1, refunds[member]); // 0.6
        Assert.Equal(1, refunds[branch]); // 0.5 rounds up, where .NET's Math.Round would give 0
    }

    [Fact]
    public void Policy_CultureOfLast8TurnsMayBeNegative_AndTruncatesTowardsZero()
    {
        var (civ, info) = NewCivWithPolicies();
        AddBranch(info.RulesetData);
        info.Turns = 1;
        civ.Policies.EndTurn(-6);
        info.Turns = 2;
        civ.Policies.EndTurn(-5);
        Assert.Equal(-11, civ.Policies.GetCultureFromGreatWriter());
        info.SpeedData.CultureCostModifier = 1.5f;
        Assert.Equal(-16, civ.Policies.GetCultureFromGreatWriter()); // -16.5 truncated towards zero
    }
}
