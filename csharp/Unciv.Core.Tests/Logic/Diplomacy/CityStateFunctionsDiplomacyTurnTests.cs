using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;

namespace Unciv.Core.Tests.Logic.Diplomacy;

// T068: CityStateFunctions over the real class (the Civilization default), the QuestManager seam recorded by FakeQuestManager.
public class CityStateFunctionsDiplomacyTurnTests
{
    private static Civilization AddCiv(FakeGameInfoState info, string name, PlayerType playerType = PlayerType.AI, string? cityStateType = null,
        IEnumerable<string>? uniques = null, string? personality = null)
    {
        if (cityStateType != null && !info.RulesetData.CityStateTypes.ContainsKey(cityStateType))
            info.RulesetData.CityStateTypes.Put(cityStateType, new CityStateType { Name = cityStateType });
        var civ = new Civilization(new NationObject { Name = name, CityStateType = cityStateType, Uniques = uniques?.ToList() ?? [], Personality = personality })
        {
            GameInfo = info,
            PlayerType = playerType,
            QuestManager = new FakeQuestManager(),
        };
        civ.ReligionManager.CivInfo = civ;
        civ.Units = new TestUnitManager(civ) { Count = 1 };
        info.Civs.Add(civ);
        return civ;
    }

    private static void Meet(Civilization civ, Civilization other) => civ.DiplomacyFunctions.MakeCivilizationsMeet(other);

    private static DiplomacyManager Manager(Civilization civ, Civilization other) => civ.GetDiplomacyManager(other)!;

    private static FakeQuestManager Quests(Civilization civ) => (FakeQuestManager)civ.QuestManager;

    /// <summary>A city state and a major civ that have met, at peace and without any first-contact influence.</summary>
    private static (FakeGameInfoState Info, Civilization Cs, Civilization A) CityStateAndMajor(PlayerType playerType = PlayerType.Human)
    {
        var info = new FakeGameInfoState();
        var cs = AddCiv(info, "Geneva", cityStateType: "Cultured");
        var a = AddCiv(info, "A", playerType);
        Meet(cs, a);
        Manager(cs, a).DiplomaticStatus = DiplomaticStatus.Peace;
        Manager(a, cs).DiplomaticStatus = DiplomaticStatus.Peace;
        Manager(cs, a).SetInfluenceWithoutSideEffects(0f);
        a.AddGold(-a.Gold); // the first meeting with a city state pays a gift
        a.Notifications.Clear();
        return (info, cs, a);
    }

    private static FakeCityState AddCity(Civilization civ, int population = 5)
    {
        var city = new FakeCityState(civ.CivName + "City", new FakeCiv(civ.CivName)) { CivData = civ, PopulationData = population };
        city.City.Espionage.SetTransients(city.City);
        civ.Cities = [city.City];
        return city;
    }

    // ---- protectors

    [Fact]
    public void ProtectorCivs_AreTheNonDefeatedCivsWithProtectorStatus_AndNeverExistForMajorCivs()
    {
        var (info, cs, a) = CityStateAndMajor();
        var b = AddCiv(info, "B");
        Meet(cs, b);
        Manager(cs, a).DiplomaticStatus = DiplomaticStatus.Protector;
        Manager(cs, b).DiplomaticStatus = DiplomaticStatus.Protector;
        Assert.Equal([a, b], cs.CityStateFunctions.GetProtectorCivs());
        ((TestUnitManager)b.Units).Count = 0; // defeated: no cities, no units
        Assert.Equal([a], cs.CityStateFunctions.GetProtectorCivs());
        Assert.Empty(a.CityStateFunctions.GetProtectorCivs());
    }

    [Fact]
    public void PledgingProtection_NeedsAKnownMajorCivWithNonNegativeInfluenceAtPeace()
    {
        var (_, cs, a) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        var diplomacy = Manager(cs, a);

        Assert.True(functions.OtherCivCanPledgeProtection(a));
        Assert.False(functions.OtherCivCanPledgeProtection(cs)); // not a major civ
        Assert.False(((CityStateFunctions)a.CityStateFunctions).OtherCivCanPledgeProtection(cs)); // not a city state
        diplomacy.SetInfluenceWithoutSideEffects(-1f);
        Assert.False(functions.OtherCivCanPledgeProtection(a));
        diplomacy.SetInfluenceWithoutSideEffects(0f);
        diplomacy.SetFlag(DiplomacyFlags.RecentlyWithdrewProtection, 5);
        Assert.False(functions.OtherCivCanPledgeProtection(a));
        diplomacy.RemoveFlag(DiplomacyFlags.RecentlyWithdrewProtection);
        diplomacy.DiplomaticStatus = DiplomaticStatus.War;
        Assert.False(functions.OtherCivCanPledgeProtection(a));
        diplomacy.DiplomaticStatus = DiplomaticStatus.Peace;

        functions.AddProtectorCiv(a);
        Assert.Equal(DiplomaticStatus.Protector, diplomacy.DiplomaticStatus);
        Assert.Equal(10, diplomacy.GetFlag(DiplomacyFlags.RecentlyPledgedProtection));
        Assert.False(functions.OtherCivCanPledgeProtection(a)); // already protected
        functions.AddProtectorCiv(a); // ignored
        Assert.Equal(10, diplomacy.GetFlag(DiplomacyFlags.RecentlyPledgedProtection));
    }

    [Fact]
    public void WithdrawingProtection_IsBlockedRightAfterPledgingUnlessForced()
    {
        var (_, cs, a) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        var diplomacy = Manager(cs, a);
        Assert.False(functions.OtherCivCanWithdrawProtection(a)); // not protecting
        Assert.False(functions.OtherCivCanWithdrawProtection(cs));
        functions.AddProtectorCiv(a);
        Assert.False(functions.OtherCivCanWithdrawProtection(a)); // pledged too recently

        functions.RemoveProtectorCiv(a);
        Assert.Equal(DiplomaticStatus.Protector, diplomacy.DiplomaticStatus);

        diplomacy.RemoveFlag(DiplomacyFlags.RecentlyPledgedProtection);
        Assert.True(functions.OtherCivCanWithdrawProtection(a));
        functions.RemoveProtectorCiv(a);
        Assert.Equal(DiplomaticStatus.Peace, diplomacy.DiplomaticStatus);
        Assert.Equal(20, diplomacy.GetFlag(DiplomacyFlags.RecentlyWithdrewProtection));
        Assert.Equal(-20f, diplomacy.Influence);

        diplomacy.RemoveFlag(DiplomacyFlags.RecentlyWithdrewProtection);
        diplomacy.SetInfluenceWithoutSideEffects(0f);
        functions.AddProtectorCiv(a);
        functions.RemoveProtectorCiv(a, forced: true);
        Assert.Equal(DiplomaticStatus.Peace, diplomacy.DiplomaticStatus);
    }

    // ---- gold

    [Fact]
    public void InfluenceGainedByGift_RoundsDownToFiveAndHasAMinimumOfFive()
    {
        var (info, cs, a) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;

        Assert.Equal(10, functions.InfluenceGainedByGift(a, 100)); // 100^1.01 / 9.8 = 10.68
        Assert.Equal(5, functions.InfluenceGainedByGift(a, 1));
        Assert.Equal(5, functions.InfluenceGainedByGift(a, 0));
        Assert.Equal(30, functions.InfluenceGainedByGift(a, 300)); // 31.8

        info.Turns = 400; // the gifts lose two thirds of their worth at the end of the game
        Assert.Equal(10, functions.InfluenceGainedByGift(a, 300)); // 10.6

        info.Turns = 0;
        Quests(cs).InvestmentMultiplier = 2f;
        Assert.Equal(20, functions.InfluenceGainedByGift(a, 100)); // 21.4

        var rich = AddCiv(info, "Rich", uniques: ["Gifts of Gold to City-States generate [50]% more Influence"]);
        Quests(cs).InvestmentMultiplier = 1f;
        Assert.Equal(15, functions.InfluenceGainedByGift(rich, 100)); // 16.0
    }

    [Fact]
    public void ReceiveGoldGift_MovesTheGoldRaisesInfluenceAndTellsTheQuests()
    {
        var (_, cs, a) = CityStateAndMajor();
        a.AddGold(500);
        var functions = (CityStateFunctions)cs.CityStateFunctions;

        functions.ReceiveGoldGift(a, 100);

        Assert.Equal(400, a.Gold);
        Assert.Equal(100, cs.Gold);
        Assert.Equal(10f, Manager(cs, a).Influence);
        Assert.Equal(["A"], Quests(cs).GoldGifts);
        Assert.Throws<InvalidOperationException>(() => ((CityStateFunctions)a.CityStateFunctions).ReceiveGoldGift(cs, 1));
    }

    [Fact]
    public void GoldGainedByTribute_GrowsLinearlyThroughoutTheGame()
    {
        var (info, cs, _) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        Assert.Equal(50, functions.GoldGainedByTribute());
        info.Turns = 13; // 13 / 6.5 = 2
        Assert.Equal(60, functions.GoldGainedByTribute());
        info.Turns = 19; // 2.92 rounds down
        Assert.Equal(60, functions.GoldGainedByTribute());
    }

    [Fact]
    public void TributeGold_PaysTheBullyInfluenceAndRemembersIt_AndTheProtectorsGetUpset()
    {
        var (info, cs, a) = CityStateAndMajor();
        var protector = AddCiv(info, "P", PlayerType.AI);
        var humanProtector = AddCiv(info, "H", PlayerType.Human);
        foreach (var p in new[] { protector, humanProtector })
        {
            Meet(cs, p);
            Meet(a, p);
            Manager(cs, p).DiplomaticStatus = DiplomaticStatus.Protector;
        }
        var other = AddCiv(info, "Other", cityStateType: "Cultured");
        var functions = (CityStateFunctions)cs.CityStateFunctions;

        functions.TributeGold(a);

        Assert.Equal(50, a.Gold);
        Assert.Equal(-15f, Manager(cs, a).Influence);
        Assert.Equal(20, cs.GetRecentBullyingCountdown());
        Assert.Equal(20, Manager(cs, a).GetFlag(DiplomacyFlags.Bullied));
        foreach (var p in new[] { protector, humanProtector })
        {
            Assert.Equal(-15f, Manager(p, a).GetModifier(DiplomaticModifiers.BulliedProtectedMinor));
            Assert.Equal(75, Manager(p, a).GetFlag(DiplomacyFlags.RememberBulliedProtectedMinor));
        }
        Assert.Contains(a.Notifications, it => it.Text == "[P] is upset that you demanded tribute from [Geneva], whom they have pledged to protect!");
        Assert.Equal("A@Geneva", Assert.Single(humanProtector.PopupAlerts, it => it.Type == AlertType.BulliedProtectedMinor).Value);
        Assert.Equal([("Geneva", "A")], Quests(other).Bullied);
        Assert.Equal([("Geneva", "A")], Quests(cs).Bullied);

        // a second offence while the memory is still fresh is less severe
        Manager(protector, a).SetFlag(DiplomacyFlags.RememberBulliedProtectedMinor, 60);
        functions.TributeGold(a);
        Assert.Equal(-25f, Manager(protector, a).GetModifier(DiplomaticModifiers.BulliedProtectedMinor));
        Assert.Throws<InvalidOperationException>(() => ((CityStateFunctions)a.CityStateFunctions).TributeGold(cs));
        Assert.Throws<InvalidOperationException>(() => ((CityStateFunctions)a.CityStateFunctions).TributeWorker(cs));
    }

    // ---- tribute

    [Fact]
    public void TributeModifiers_AMajorCivAndACityStateWithoutACityCannotBeBullied()
    {
        var (_, cs, a) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        Assert.Equal([("No Cities", -999)], functions.GetTributeModifiers(a).Select(it => (it.Key, it.Value)));
        Assert.Equal(-999, functions.GetTributeWillingness(a));
        var majorFunctions = (CityStateFunctions)a.CityStateFunctions;
        Assert.Equal([("Major Civ", -999)], majorFunctions.GetTributeModifiers(cs).Select(it => (it.Key, it.Value)));
    }

    [Fact]
    public void TributeModifiers_StopEarlyWhenTheSumIsHopeless_AndListTheReasonsInOrder()
    {
        var (info, cs, a) = CityStateAndMajor();
        var protector = AddCiv(info, "P");
        Meet(cs, protector);
        AddCity(cs, population: 3);
        Manager(cs, protector).DiplomaticStatus = DiplomaticStatus.Protector;
        var ally = AddCiv(info, "Ally");
        cs.AllyCiv = ally;
        cs.CityStatePersonality = CityStatePersonality.Hostile;
        cs.AddFlag(CivFlags.RecentlyBullied.ToString(), 11);
        Manager(cs, a).SetInfluenceWithoutSideEffects(-31f);
        var functions = (CityStateFunctions)cs.CityStateFunctions;

        Assert.Equal(
            [("Base value", -110), ("Hostile", -10), ("Has Ally", -10), ("Has Protector", -20), ("Demanding a Worker", -30),
                ("Demanding a Worker from small City-State", -300), ("Very recently paid tribute", -300), ("Influence below -30", -300)],
            functions.GetTributeModifiers(a, demandingWorker: true).Select(it => (it.Key, it.Value)));

        cs.AddFlag(CivFlags.RecentlyBullied.ToString(), 5);
        Assert.Contains(("Recently paid tribute", -40), functions.GetTributeModifiers(a).Select(it => (it.Key, it.Value)));
        Assert.Equal(-110 - 10 - 10 - 20 - 40 - 300, functions.GetTributeWillingness(a));
    }

    [Fact]
    public void TributeModifiers_DemandingAWorkerFromAnAllyOrProtectorSkipsThePenaltiesForThem()
    {
        var (_, cs, a) = CityStateAndMajor();
        AddCity(cs, population: 5);
        cs.AllyCiv = a;
        Manager(cs, a).DiplomaticStatus = DiplomaticStatus.Protector;
        Manager(cs, a).SetInfluenceWithoutSideEffects(-31f);
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        Assert.Equal([("Base value", -110), ("Influence below -30", -300)], functions.GetTributeModifiers(a).Select(it => (it.Key, it.Value)));
    }

    // ---- bonuses

    [Fact]
    public void CityStateBonuses_ComeFromTheAllyOrFriendUniqueMapOnly()
    {
        var type = new CityStateType { Name = "Cultured", FriendBonusUniques = ["[+1 Culture] [in all cities]"], AllyBonusUniques = ["[+3 Faith] [in all cities]", "Provides military units every ≈[20] turns"] };

        Assert.Equal(2, CityStateFunctions.GetCityStateBonuses(type, RelationshipLevel.Ally).Count());
        Assert.Single(CityStateFunctions.GetCityStateBonuses(type, RelationshipLevel.Friend));
        Assert.Empty(CityStateFunctions.GetCityStateBonuses(type, RelationshipLevel.Neutral));
        var units = Assert.Single(CityStateFunctions.GetCityStateBonuses(type, RelationshipLevel.Ally, UniqueType.CityStateMilitaryUnits));
        Assert.Equal("20", units.Params[0]);
        Assert.Empty(CityStateFunctions.GetCityStateBonuses(type, RelationshipLevel.Friend, UniqueType.CityStateMilitaryUnits));
    }

    [Fact]
    public void CanProvideStat_LooksAtTheAllyBonusesOfACityState()
    {
        var info = new FakeGameInfoState();
        info.RulesetData.CityStateTypes.Put("Religious", new CityStateType { Name = "Religious", AllyBonusUniques = ["[+3 Faith] [in all cities]"] });
        var cs = AddCiv(info, "Geneva", cityStateType: "Religious");
        var major = AddCiv(info, "A");

        Assert.True(cs.CityStateFunctions.CanProvideStat(Stat.Faith));
        Assert.False(cs.CityStateFunctions.CanProvideStat(Stat.Gold));
        Assert.False(major.CityStateFunctions.CanProvideStat(Stat.Faith));
    }

    // ---- allies

    [Fact]
    public void UpdateAllyCivForCityState_TheMajorWithTheMostInfluenceOfAtLeastSixtyBecomesAlly()
    {
        var (info, cs, a) = CityStateAndMajor();
        var b = AddCiv(info, "B", PlayerType.Human);
        Meet(cs, b);
        Manager(cs, b).DiplomaticStatus = DiplomaticStatus.Peace;
        Manager(cs, b).SetInfluenceWithoutSideEffects(0f);
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        functions.UpdateAllyCivForCityState();
        Assert.Null(cs.AllyCiv);

        Manager(cs, a).SetInfluenceWithoutSideEffects(59.9f);
        functions.UpdateAllyCivForCityState();
        Assert.Null(cs.AllyCiv);

        Manager(cs, a).SetInfluenceWithoutSideEffects(60f);
        functions.UpdateAllyCivForCityState();
        Assert.Same(a, cs.AllyCiv);
        Assert.Contains(a.Notifications, it => it.Text == "We have allied with [Geneva].");

        // B overtakes A
        Manager(cs, b).SetInfluenceWithoutSideEffects(61f);
        a.Notifications.Clear();
        functions.UpdateAllyCivForCityState();
        Assert.Same(b, cs.AllyCiv);
        Assert.Contains(a.Notifications, it => it.Text == "We have lost our alliance with [Geneva] to [B].");
        Assert.Contains(b.Notifications, it => it.Text == "We have allied with [Geneva].");

        // and then nobody
        Manager(cs, b).SetInfluenceWithoutSideEffects(0f);
        Manager(cs, a).SetInfluenceWithoutSideEffects(0f);
        functions.UpdateAllyCivForCityState();
        Assert.Null(cs.AllyCiv);
        Assert.Contains(b.Notifications, it => it.Text == "We have lost our alliance with [Geneva].");
        ((CityStateFunctions)a.CityStateFunctions).UpdateAllyCivForCityState(); // not a city state: nothing
        Assert.Null(a.AllyCiv);
    }

    [Fact]
    public void UpdateAllyCivForCityState_ANewAllyDrawsTheCityStateIntoItsWars_AndTheLostAllyResentsIt()
    {
        var (info, cs, a) = CityStateAndMajor();
        var b = AddCiv(info, "B");
        var enemy = AddCiv(info, "Enemy");
        Meet(cs, b);
        Meet(a, enemy);
        Meet(a, b);
        Manager(a, enemy).DiplomaticStatus = DiplomaticStatus.War;
        Manager(enemy, a).DiplomaticStatus = DiplomaticStatus.War;
        Manager(cs, b).DiplomaticStatus = DiplomaticStatus.Peace;
        Manager(a, b).DiplomaticStatus = DiplomaticStatus.Peace;
        cs.AllyCiv = b;
        Manager(cs, a).SetInfluenceWithoutSideEffects(70f);
        Manager(cs, b).SetInfluenceWithoutSideEffects(0f);
        Manager(a, cs).SetFlag(DiplomacyFlags.MarriageCooldown, 0);
        a.Nation.Uniques = ["Can spend Gold to annex or puppet a City-State that has been your Ally for [12] turns"];
        Assert.False(cs.Knows(enemy));

        ((CityStateFunctions)cs.CityStateFunctions).UpdateAllyCivForCityState();

        Assert.Same(a, cs.AllyCiv);
        Assert.True(cs.Knows(enemy));
        Assert.True(cs.IsAtWarWith(enemy));
        Assert.Equal(12, Manager(a, cs).GetFlag(DiplomacyFlags.MarriageCooldown));
        Assert.Equal(-10f, Manager(b, a).GetModifier(DiplomaticModifiers.StoleOurAlly));
    }

    [Fact]
    public void GetNotificationActions_PointAtTheCapitalIfThereIsOne_ThenAtTheDiplomacyScreen()
    {
        var (_, cs, _) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        var noCapital = functions.GetNotificationActions().ToList();
        Assert.Equal(["DiplomacyAction"], noCapital.Select(it => it.GetType().Name));

        AddCity(cs);
        Assert.Equal(["LocationAction", "DiplomacyAction"], functions.GetNotificationActions().Select(it => it.GetType().Name));
    }

    // ---- attacks

    [Fact]
    public void CityStateAttacked_MakesTheCityStateWaryOfAWarmongerAndUpsetsProtectorsAndAllies()
    {
        var (info, cs, attacker) = CityStateAndMajor();
        var protector = AddCiv(info, "P", PlayerType.AI);
        var humanProtector = AddCiv(info, "H", PlayerType.Human);
        var ally = AddCiv(info, "Ally", PlayerType.Human);
        var aiAlly = AddCiv(info, "AiAlly", PlayerType.AI);
        foreach (var civ in new[] { protector, humanProtector, ally, aiAlly })
        {
            Meet(cs, civ);
            Meet(attacker, civ);
        }
        Manager(cs, protector).DiplomaticStatus = DiplomaticStatus.Protector;
        Manager(cs, humanProtector).DiplomaticStatus = DiplomaticStatus.Protector;
        cs.AllyCiv = ally;
        var neighbour = AddCiv(info, "Zurich", cityStateType: "Cultured");
        Meet(neighbour, attacker);
        neighbour.Proximity["A"] = Proximity.Neighbors;
        var far = AddCiv(info, "Bern", cityStateType: "Cultured");
        Meet(far, attacker);
        var unwilling = AddCiv(info, "Basel", cityStateType: "Cultured");
        Meet(unwilling, attacker);
        unwilling.Proximity["A"] = Proximity.Neighbors;
        Quests(unwilling).WantsDeadCivs.Add("Geneva");
        var closeButAllied = AddCiv(info, "Lugano", cityStateType: "Cultured");
        Meet(closeButAllied, attacker);
        closeButAllied.Proximity["A"] = Proximity.Neighbors;
        closeButAllied.AllyCiv = attacker;
        attacker.NumMinorCivsAttacked = 4;

        cs.CityStateFunctions.CityStateAttacked(attacker);

        Assert.True(Manager(cs, attacker).HasFlag(DiplomacyFlags.WaryOf)); // a warmonger always becomes wary
        Assert.Equal(2, Manager(cs, attacker).GetFlag(DiplomacyFlags.RecentlyAttacked));
        Assert.Equal(["A"], Quests(cs).AttackedBy);
        Assert.True(Manager(neighbour, attacker).HasFlag(DiplomacyFlags.WaryOf)); // neighbours are 100% for warmongers
        Assert.False(Manager(unwilling, attacker).HasFlag(DiplomacyFlags.WaryOf));
        Assert.False(Manager(closeButAllied, attacker).HasFlag(DiplomacyFlags.WaryOf));
        Assert.False(Manager(far, attacker).HasFlag(DiplomacyFlags.WaryOf)); // proximity none: 0%
        Assert.Equal(-20f, Manager(protector, attacker).GetModifier(DiplomaticModifiers.AttackedProtectedMinor));
        Assert.Equal(75, Manager(protector, attacker).GetFlag(DiplomacyFlags.RememberAttackedProtectedMinor));
        Assert.Contains(attacker.Notifications, it => it.Text == "[P] is upset that you attacked [Geneva], whom they have pledged to protect!");
        Assert.Equal("A@Geneva", Assert.Single(humanProtector.PopupAlerts, it => it.Type == AlertType.AttackedProtectedMinor).Value);
        Assert.Equal(-10f, Manager(ally, attacker).GetModifier(DiplomaticModifiers.AttackedAlliedMinor));
        Assert.Equal("A@Geneva", Assert.Single(ally.PopupAlerts, it => it.Type == AlertType.AttackedAllyMinor).Value);

        // second offence, fresh memory: less severe; an AI ally notifies instead of asking
        Manager(protector, attacker).SetFlag(DiplomacyFlags.RememberAttackedProtectedMinor, 60);
        cs.AllyCiv = aiAlly;
        cs.CityStateFunctions.CityStateAttacked(attacker);
        Assert.Equal(-35f, Manager(protector, attacker).GetModifier(DiplomaticModifiers.AttackedProtectedMinor));
        Assert.Contains(attacker.Notifications, it => it.Text == "[AiAlly] is upset that you attacked [Geneva], whom they are allied with!");

        // city states are not upset by each other; majors cannot be attacked in this sense
        var before = Quests(cs).AttackedBy.Count;
        cs.CityStateFunctions.CityStateAttacked(neighbour);
        attacker.CityStateFunctions.CityStateAttacked(neighbour);
        Assert.Equal(before, Quests(cs).AttackedBy.Count);
    }

    [Fact]
    public void CityStateAttacked_OtherCityStatesOnlyBecomeWaryOfAggressorsAtTheRightDistance()
    {
        var (info, cs, attacker) = CityStateAndMajor();
        var neighbour = AddCiv(info, "Zurich", cityStateType: "Cultured");
        var farAway = AddCiv(info, "Basel", cityStateType: "Cultured");
        foreach (var civ in new[] { neighbour, farAway })
        {
            Meet(civ, attacker);
            Manager(civ, attacker).DiplomaticStatus = DiplomaticStatus.War; // +50 percentage points
        }
        neighbour.Proximity["A"] = Proximity.Neighbors; // 50 + 50 for a mere aggressor: always
        farAway.Proximity["A"] = Proximity.Far; // no chance for an aggressor, 0 + 50: strictly below 50 only
        attacker.NumMinorCivsAttacked = 2;
        Assert.False(attacker.IsMinorCivWarmonger());

        cs.CityStateFunctions.CityStateAttacked(attacker);

        Assert.True(Manager(neighbour, attacker).HasFlag(DiplomacyFlags.WaryOf));
        Assert.Equal(2, Manager(cs, attacker).GetFlag(DiplomacyFlags.RecentlyAttacked));
        // not an aggressor at all: the others are left alone
        var calm = AddCiv(info, "Bern", cityStateType: "Cultured");
        Meet(calm, attacker);
        calm.Proximity["A"] = Proximity.Neighbors;
        attacker.NumMinorCivsAttacked = 1;
        cs.CityStateFunctions.CityStateAttacked(attacker);
        Assert.False(Manager(calm, attacker).HasFlag(DiplomacyFlags.WaryOf));
    }

    [Fact]
    public void CityStateDestroyed_ProtectorsAreOutragedAndEveryCityStateIsTold()
    {
        var (info, cs, attacker) = CityStateAndMajor();
        var protector = AddCiv(info, "P", PlayerType.Human);
        var aiProtector = AddCiv(info, "Q", PlayerType.AI);
        var unaware = AddCiv(info, "Unaware", PlayerType.Human);
        foreach (var civ in new[] { protector, aiProtector, unaware })
        {
            Meet(cs, civ);
            Manager(cs, civ).DiplomaticStatus = DiplomaticStatus.Protector;
        }
        Meet(attacker, protector);
        Meet(attacker, aiProtector);
        var other = AddCiv(info, "Zurich", cityStateType: "Cultured");

        ((CityStateFunctions)cs.CityStateFunctions).CityStateDestroyed(attacker);

        Assert.Equal(-40f, Manager(protector, attacker).GetModifier(DiplomaticModifiers.DestroyedProtectedMinor));
        Assert.Equal(125, Manager(protector, attacker).GetFlag(DiplomacyFlags.RememberDestroyedProtectedMinor));
        Assert.Contains(attacker.Notifications, it => it.Text == "[Q] is outraged that you destroyed [Geneva], whom they had pledged to protect!");
        Assert.DoesNotContain(attacker.Notifications, it => it.Text.StartsWith("[P] is outraged", StringComparison.Ordinal)); // humans have their own emotions
        Assert.Contains(protector.Notifications, it => it.Text == "[A] has destroyed [Geneva], whom you had pledged to protect!");
        Assert.DoesNotContain(unaware.Notifications, it => it.Text.Contains("destroyed", StringComparison.Ordinal)); // does not know the attacker
        Assert.Equal([("Geneva", "A")], Quests(other).Conquered);

        Manager(protector, attacker).SetFlag(DiplomacyFlags.RememberDestroyedProtectedMinor, 100);
        ((CityStateFunctions)cs.CityStateFunctions).CityStateDestroyed(attacker);
        Assert.Equal(-50f, Manager(protector, attacker).GetModifier(DiplomaticModifiers.DestroyedProtectedMinor));
        ((CityStateFunctions)attacker.CityStateFunctions).CityStateDestroyed(attacker); // not a city state
    }

    [Fact]
    public void AskForUnitGifts_NotifiesTheMajorCivsThatAreNotYetAtWarWithTheAttacker()
    {
        var (info, cs, attacker) = CityStateAndMajor();
        var friend = AddCiv(info, "Friend", PlayerType.Human);
        var enemy = AddCiv(info, "Enemy", PlayerType.Human);
        var stranger = AddCiv(info, "Stranger", PlayerType.Human);
        foreach (var civ in new[] { friend, enemy })
        {
            Meet(cs, civ);
            Meet(civ, attacker);
            Manager(civ, attacker).DiplomaticStatus = DiplomaticStatus.Peace;
        }
        Manager(enemy, attacker).DiplomaticStatus = DiplomaticStatus.War;
        Meet(cs, stranger); // does not know the attacker
        foreach (var civ in new[] { friend, enemy, stranger }) civ.Notifications.Clear(); // first-contact gifts
        var functions = (CityStateFunctions)cs.CityStateFunctions;

        functions.AskForUnitGifts(attacker); // no cities: it can't receive units
        Assert.Empty(friend.Notifications);

        AddCity(cs);
        functions.AskForUnitGifts(attacker);

        Assert.Single(friend.Notifications);
        Assert.StartsWith("[Geneva] is being attacked by [A] and asks all major civilizations", friend.Notifications[0].Text, StringComparison.Ordinal);
        Assert.Empty(enemy.Notifications);
        Assert.Empty(stranger.Notifications);

        friend.Notifications.Clear();
        ((TestUnitManager)attacker.Units).Count = 0;
        functions.AskForUnitGifts(attacker); // the attacker has been defeated meanwhile
        Assert.Empty(friend.Notifications);
    }

    // ---- barbarians and borders

    [Fact]
    public void ThreateningBarbarianKilled_ThanksTheKillerUnlessAtWar_AndExtendsTheAngerFreeIntrusion()
    {
        var (_, cs, a) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        Manager(cs, a).DiplomaticStatus = DiplomaticStatus.War;
        functions.ThreateningBarbarianKilledBy(a);
        Assert.False(Manager(cs, a).HasFlag(DiplomacyFlags.AngerFreeIntrusion));
        Manager(cs, a).DiplomaticStatus = DiplomaticStatus.Peace;

        functions.ThreateningBarbarianKilledBy(a);
        Assert.Equal(12f, Manager(cs, a).Influence);
        Assert.Equal(5, Manager(cs, a).GetFlag(DiplomacyFlags.AngerFreeIntrusion));
        Assert.Contains(a.Notifications, it => it.Text == "[Geneva] is grateful that you killed a Barbarian that was threatening them!");

        functions.ThreateningBarbarianKilledBy(a);
        Assert.Equal(10, Manager(cs, a).GetFlag(DiplomacyFlags.AngerFreeIntrusion));
    }

    [Fact]
    public void NumThreateningBarbarians_IsZeroWithoutBarbarians()
    {
        var (info, cs, _) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        Assert.Equal(0, functions.GetNumThreateningBarbarians()); // no barbarian civilization
        info.GameParameters.NoBarbarians = true;
        Assert.Equal(0, functions.GetNumThreateningBarbarians());
        info.GameParameters.NoBarbarians = false;
        var barbarians = AddCiv(info, Constants.Barbarians);
        barbarians.Nation.Name = Constants.Barbarians;
        Assert.True(barbarians.IsBarbarian);
        Assert.Equal(0, functions.GetNumThreateningBarbarians()); // no units
        functions.UpdateDiplomaticRelationshipForCityState(); // nobody has units inside the borders
    }

    // ---- misc

    [Fact]
    public void TurnsForGreatPersonFromCityState_IsAtLeastThirtySevenTurnsScaledBySpeed()
    {
        var (info, cs, _) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        Assert.InRange(functions.TurnsForGreatPersonFromCityState(), 37, 43);
        info.SpeedData.Modifier = 2f;
        Assert.InRange(functions.TurnsForGreatPersonFromCityState(), 74, 86);
    }

    [Fact]
    public void DiplomaticMarriageCost_RoundsDownToAMultipleOfFive()
    {
        var (info, cs, a) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        Assert.Equal(500, functions.GetDiplomaticMarriageCost());
        info.SpeedData.GoldCostModifier = 1.333f;
        Assert.Equal(665, functions.GetDiplomaticMarriageCost()); // 666.5 truncates to 666, which rounds down to 665
        Assert.False(functions.CanBeMarriedBy(a)); // no cities
        functions.DiplomaticMarriage(a); // ignored when it can't be done
        Assert.False(cs.IsDefeated() && a.Cities.Count > 0);
    }

    [Fact]
    public void ResourcesAndUniquesProvidedByCityStates_AreEmptyForCityStates()
    {
        var (_, cs, a) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        Assert.Empty(functions.GetCityStateResourcesForAlly());
        Assert.Empty(functions.GetUniquesProvidedByCityStates(UniqueType.CityStateMilitaryUnits, cs.State));
        var seen = new List<Unique>();
        functions.ForEachUniqueProvidedByCityStates(UniqueType.CityStateMilitaryUnits, cs.State, seen.Add);
        Assert.Empty(seen);
        Assert.Empty(((CityStateFunctions)a.CityStateFunctions).GetUniquesProvidedByCityStates(UniqueType.CityStateMilitaryUnits, a.State));
    }

    [Fact]
    public void HoldElections_RestartTheCountdownAndNeedACapitalWithRiggingSpies()
    {
        var (_, cs, _) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        functions.HoldElections();
        Assert.Equal(15, cs.FlagsCountdown[CivFlags.TurnsTillCityStateElection.ToString()]);
        AddCity(cs);
        functions.HoldElections(); // no spies: nothing else happens
        Assert.Equal(15, cs.FlagsCountdown[CivFlags.TurnsTillCityStateElection.ToString()]);
    }

    [Fact]
    public void InitCityState_ChoosesAPersonalityAndTheTypeSpecificExtras()
    {
        var info = new FakeGameInfoState();
        var ruleset = info.RulesetData;
        ruleset.CityStateTypes.Put("Mercantile", new CityStateType { Name = "Mercantile", FriendBonusUniques = ["Provides a unique luxury"], AllyBonusUniques = ["Provides military units every ≈[20] turns"] });
        ruleset.TileResources.Put("Porcelain", new Unciv.Models.Ruleset.Tile.TileResource { Name = "Porcelain", Uniques = ["Can only be created by Mercantile City-States"] });
        ruleset.TileResources.Put("Wine", new Unciv.Models.Ruleset.Tile.TileResource { Name = "Wine" });
        var cs = AddCiv(info, "Geneva", cityStateType: "Mercantile", personality: "Hostile");

        Assert.True(((CityStateFunctions)cs.CityStateFunctions).InitCityState(ruleset, "Ancient era", [], new Unciv.Utils.GameRandom(42)));

        Assert.Equal(CityStatePersonality.Hostile, cs.CityStatePersonality);
        Assert.Equal("Porcelain", cs.CityStateResource);
        Assert.Null(cs.CityStateUniqueUnit); // no major-civ unique units in this ruleset

        var other = AddCiv(info, "Zurich", cityStateType: "Mercantile");
        ((CityStateFunctions)other.CityStateFunctions).InitCityState(ruleset, "Ancient era", [], new Unciv.Utils.GameRandom(42));
        Assert.Contains(other.CityStatePersonality, Enum.GetValues<CityStatePersonality>()); // chosen by the seeded RNG
    }

    [Fact]
    public void GivingUnitsToAPatron_NeedsTheNextTurnAutomationPort()
    {
        var (info, cs, a) = CityStateAndMajor();
        var functions = (CityStateFunctions)cs.CityStateFunctions;
        Assert.Throws<NotImplementedException>(() => functions.GiveMilitaryUnitToPatron(a)); // TODO(T094): getForeignCityNearCapital
        functions.GiveGreatPersonToPatron(a); // no great person units in the ruleset: returns before it needs the foreign city
        info.RulesetData.Units["Great Engineer"] = new Unciv.Models.Ruleset.Unit.BaseUnit { Name = "Great Engineer", Uniques = ["Great Person - [Industrial]"] };
        info.RulesetData.Units["Great Prophet"] = new Unciv.Models.Ruleset.Unit.BaseUnit { Name = "Great Prophet", Uniques = ["Great Person - [Faith]", "May found a religion"] };
        Assert.Throws<NotImplementedException>(() => functions.GiveGreatPersonToPatron(a));
    }

    [Fact]
    public void TributeWorker_DoesNothingWithoutABuildableWorker()
    {
        var (_, cs, a) = CityStateAndMajor();
        AddCity(cs);
        ((CityStateFunctions)cs.CityStateFunctions).TributeWorker(a);
        Assert.Equal(0f, Manager(cs, a).Influence);
        Assert.False(cs.FlagsCountdown.ContainsKey(CivFlags.RecentlyBullied.ToString()));
    }

    [Fact]
    public void CityStateSeams_AreSingleCallSitesForLaterTickets()
    {
        var (_, cs, a) = CityStateAndMajor();
        var city = AddCity(cs).City;
        Assert.Throws<NotImplementedException>(() => CityStateSeams.GetForeignCityNearCapital(city, a)); // TODO(T094)
        Assert.Throws<NotImplementedException>(() => CityStateSeams.GetCityDefendingStrength(city)); // TODO(T081)
    }

    [Fact]
    public void QuestManagerSeamMembersAddedForCityStates_AreNotSupportedUntilTheQuestManagerIsPorted()
    {
        IQuestManager bare = new BareQuestManager();
        var (_, cs, a) = CityStateAndMajor();
        Assert.Throws<NotSupportedException>(() => bare.GetInvestmentMultiplier(a));
        Assert.Throws<NotSupportedException>(() => bare.ReceivedGoldGift(a));
        Assert.Throws<NotSupportedException>(() => bare.CityStateBullied(cs, a));
        Assert.Throws<NotSupportedException>(() => bare.CityStateConquered(cs, a));
        Assert.Throws<NotSupportedException>(() => bare.WasAttackedBy(a));
        Assert.Throws<NotSupportedException>(() => bare.WantsDead("A"));
    }

    private sealed class BareQuestManager : IQuestManager
    {
        public IQuestManager Clone() => new BareQuestManager();
        public void SetTransients(Civilization civInfo) { }
        public void JustMet(Civilization otherCiv) { }
        public void BarbarianCampCleared(Civilization civ, Unciv.Logic.Map.HexCoord position) { }
        public void HandleIndividualQuests() { }
    }
}
