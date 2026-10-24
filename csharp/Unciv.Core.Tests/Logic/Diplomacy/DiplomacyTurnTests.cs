using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using TradeObject = Unciv.Logic.Trade.Trade;

namespace Unciv.Core.Tests.Logic.Diplomacy;

// T068: DiplomacyTurnManager and DeclareWar. The Kotlin fixtures that exercise them (DiplomacyManagerTests, DefensivePactTests) are TestGame-based
// (T072); the cases below are hand-derived from the Kotlin source over the Civilization seam fakes.
public class DiplomacyTurnTests
{
    private static Civilization AddCiv(FakeGameInfoState info, string name, PlayerType playerType = PlayerType.AI, string? cityStateType = null, bool fakeCityStateFunctions = true)
    {
        var civ = new Civilization(new NationObject { Name = name, CityStateType = cityStateType })
        {
            GameInfo = info,
            PlayerType = playerType,
            QuestManager = new FakeQuestManager(),
        };
        if (fakeCityStateFunctions) civ.CityStateFunctions = new FakeCityStateFunctions();
        civ.ReligionManager.CivInfo = civ;
        civ.Units = new TestUnitManager(civ) { Count = 1 }; // so they are not considered defeated
        info.Civs.Add(civ);
        return civ;
    }

    private static void Meet(Civilization civ, Civilization other) => civ.DiplomacyFunctions.MakeCivilizationsMeet(other);

    private static DiplomacyManager Manager(Civilization civ, Civilization other) => civ.GetDiplomacyManager(other)!;

    private static void MakePeace(Civilization a, Civilization b)
    {
        Manager(a, b).DiplomaticStatus = DiplomaticStatus.Peace;
        Manager(b, a).DiplomaticStatus = DiplomaticStatus.Peace;
    }

    // ---- DeclareWar

    [Fact]
    public void DirectWar_SetsStatusFlagsModifiersAndNotifiesTheVictim()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B", PlayerType.Human);
        var c = AddCiv(info, "C", PlayerType.Human);
        Meet(a, b);
        Meet(a, c);
        Meet(b, c);
        MakePeace(a, b);
        Manager(a, b).SetModifier(DiplomaticModifiers.YearsOfPeace, 3f);
        Manager(b, a).Trades.Add(MakeTrade("Spice", 5));
        Manager(b, a).SetModifier(DiplomaticModifiers.ReturnedCapturedUnits, 4f);
        b.TradeRequests.Add(new TradeRequest("A", new TradeObject()));
        b.TradeRequests.Add(new TradeRequest("C", new TradeObject()));

        Manager(a, b).DeclareWar();

        foreach (var manager in new[] { Manager(a, b), Manager(b, a) })
        {
            Assert.Equal(DiplomaticStatus.War, manager.DiplomaticStatus);
            Assert.Equal(10, manager.GetFlag(DiplomacyFlags.DeclinedPeace));
            Assert.Equal(10, manager.GetFlag(DiplomacyFlags.DeclaredWar));
            Assert.False(manager.HasModifier(DiplomaticModifiers.YearsOfPeace));
            Assert.Empty(manager.Trades);
        }
        Assert.Equal(-20f, Manager(b, a).GetModifier(DiplomaticModifiers.DeclaredWarOnUs));
        Assert.False(Manager(b, a).HasModifier(DiplomaticModifiers.ReturnedCapturedUnits));
        Assert.Equal("C", Assert.Single(b.TradeRequests).RequestingCiv);
        Assert.Equal("A", Assert.Single(b.PopupAlerts, it => it.Type == AlertType.WarDeclaration).Value);
        Assert.Contains(b.Notifications, it => it.Text == "[A] has declared war on us!");
        Assert.Contains(b.Notifications, it => it.Text == "[Spice] from [A] has ended");
        Assert.Contains(c.Notifications, it => it.Text == "[A] has declared war on [B]!");
    }

    private static TradeObject MakeTrade(string name, int duration)
    {
        var trade = new TradeObject();
        trade.TheirOffers.Add(new TradeOffer(name, TradeOfferType.Luxury_Resource, 1, duration));
        return trade;
    }

    [Fact]
    public void DirectWar_BreaksTreatiesAndPunishesTheBetrayal()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var c = AddCiv(info, "C");
        Meet(a, b);
        Meet(a, c);
        Meet(b, c);
        var ab = Manager(a, b);
        var ba = Manager(b, a);
        MakePeace(a, b);
        ab.SetFlag(DiplomacyFlags.DeclarationOfFriendship, 20);
        ba.SetFlag(DiplomacyFlags.DeclarationOfFriendship, 20);
        ba.SetModifier(DiplomaticModifiers.DeclarationOfFriendship, 35f);
        ab.SetFlag(DiplomacyFlags.DefensivePact, 20);
        ba.SetFlag(DiplomacyFlags.DefensivePact, 20);
        ba.SetModifier(DiplomaticModifiers.DefensivePact, 10f);
        ab.SetFlag(DiplomacyFlags.ResearchAgreement, 20);
        ba.SetFlag(DiplomacyFlags.ResearchAgreement, 20);
        ab.TotalOfScienceDuringRA = 7;
        ba.TotalOfScienceDuringRA = 9;
        ba.SetModifier(DiplomaticModifiers.GaveUsGifts, 12f);
        Manager(c, a).SetModifier(DiplomaticModifiers.DeclaredFriendshipWithOurAllies, 5f);
        Manager(c, a).SetModifier(DiplomaticModifiers.SignedDefensivePactWithOurAllies, 5f);

        ab.DeclareWar();

        Assert.False(ab.HasFlag(DiplomacyFlags.DeclarationOfFriendship));
        Assert.False(ba.HasFlag(DiplomacyFlags.DeclarationOfFriendship));
        Assert.False(ba.HasModifier(DiplomaticModifiers.DeclarationOfFriendship));
        Assert.False(ab.HasFlag(DiplomacyFlags.DefensivePact));
        Assert.False(ba.HasFlag(DiplomacyFlags.DefensivePact));
        Assert.False(ba.HasModifier(DiplomaticModifiers.DefensivePact));
        Assert.False(ab.HasFlag(DiplomacyFlags.ResearchAgreement));
        Assert.False(ba.HasFlag(DiplomacyFlags.ResearchAgreement));
        Assert.Equal(0, ab.TotalOfScienceDuringRA);
        Assert.Equal(0, ba.TotalOfScienceDuringRA);
        Assert.False(ba.HasModifier(DiplomaticModifiers.GaveUsGifts));

        float loyal = b.GetPersonality().ModifierFocus(Unciv.Models.Ruleset.Nation.PersonalityValue.Loyal, .3f);
        Assert.Equal(-40f * loyal + -20f * 0f, Manager(b, a).GetModifier(DiplomaticModifiers.BetrayedDeclarationOfFriendship));
        Assert.Equal(-20f * loyal, Manager(b, a).GetModifier(DiplomaticModifiers.BetrayedDefensivePact));
        float loyalC = c.GetPersonality().ModifierFocus(Unciv.Models.Ruleset.Nation.PersonalityValue.Loyal, .3f);
        Assert.Equal(-20f * loyalC, Manager(c, a).GetModifier(DiplomaticModifiers.BetrayedDeclarationOfFriendship));
        Assert.Equal(-10f * loyalC, Manager(c, a).GetModifier(DiplomaticModifiers.BetrayedDefensivePact));
        Assert.False(Manager(c, a).HasModifier(DiplomaticModifiers.DeclaredFriendshipWithOurAllies));
        Assert.False(Manager(c, a).HasModifier(DiplomaticModifiers.SignedDefensivePactWithOurAllies));
    }

    [Fact]
    public void DirectWar_AppliesWarmongeringAndSharedEnemyModifiers()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var c = AddCiv(info, "C"); // at peace with both and not at war with b: warmongering and nothing else
        var d = AddCiv(info, "D"); // at war with b: a shared enemy of a
        var e = AddCiv(info, "E"); // at war with a: a shared enemy of b
        var cs = AddCiv(info, "Geneva", cityStateType: "Cultured");
        foreach (var x in new[] { b, c, d, e, cs }) Meet(a, x);
        foreach (var x in new[] { c, d, e, cs }) Meet(b, x);
        MakePeace(a, b);
        MakePeace(a, c);
        MakePeace(b, c);
        MakePeace(a, d);
        MakePeace(a, cs);
        MakePeace(b, cs);
        // d is at war with b, e is at war with a (the default status), c and cs are at peace with everybody
        Manager(a, e).DiplomaticStatus = DiplomaticStatus.War;
        Manager(e, a).DiplomaticStatus = DiplomaticStatus.War;
        MakePeace(b, e);
        Manager(b, d).DiplomaticStatus = DiplomaticStatus.War;
        Manager(d, b).DiplomaticStatus = DiplomaticStatus.War;

        Manager(a, b).DeclareWar();

        float loyal = a.GetPersonality().ModifierFocus(Unciv.Models.Ruleset.Nation.PersonalityValue.Loyal, .3f);
        // warmongering: everybody a knows that is not at war with b (and not worse than Competitor towards b)
        Assert.Equal(-5f, Manager(c, a).GetModifier(DiplomaticModifiers.WarMongerer));
        Assert.Equal(-5f, Manager(cs, a).GetModifier(DiplomaticModifiers.WarMongerer));
        Assert.False(Manager(d, a).HasModifier(DiplomaticModifiers.WarMongerer));
        Assert.Equal(-5f, Manager(e, a).GetModifier(DiplomaticModifiers.WarMongerer));
        // shared enemy: d (at war with b, not with a) likes a more; e (at war with a) likes b more; the city state gets nothing
        Assert.Equal(5f * loyal, Manager(d, a).GetModifier(DiplomaticModifiers.SharedEnemy));
        Assert.Equal(5f * loyal, Manager(e, b).GetModifier(DiplomaticModifiers.SharedEnemy));
        Assert.False(Manager(c, a).HasModifier(DiplomaticModifiers.SharedEnemy));
    }

    [Fact]
    public void DirectWar_BreakingAPromiseNotToAttackIsRemembered()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        Meet(a, b);
        MakePeace(a, b);
        info.SpeedData.Modifier = 1.5f;
        Manager(b, a).SetFlag(DiplomacyFlags.AgreedToNotAttackUs, 50);

        Manager(a, b).DeclareWar();

        Assert.Equal(45, Manager(b, a).GetFlag(DiplomacyFlags.MilitaryPresenceNearBorderOrAttackedUsDespitePromise)); // 30 * 1.5

        // a join war does not count as a direct attack on a promise: DefensivePactWar neither
        var c = AddCiv(info, "C");
        Meet(c, b);
        MakePeace(c, b);
        Manager(b, c).SetFlag(DiplomacyFlags.AgreedToNotAttackUs, 50);
        Manager(c, b).DeclareWar(new DeclareWarReason(WarType.DefensivePactWar, a));
        Assert.False(Manager(b, c).HasFlag(DiplomacyFlags.MilitaryPresenceNearBorderOrAttackedUsDespitePromise));
    }

    [Fact]
    public void DirectWar_AgainstACityStateSetsInfluenceAndTellsTheCityState()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var cs = AddCiv(info, "Geneva", cityStateType: "Cultured");
        var friend = AddCiv(info, "F");
        Meet(a, cs);
        Meet(a, friend);
        Meet(cs, friend);
        MakePeace(a, cs);
        MakePeace(a, friend);
        var functions = (FakeCityStateFunctions)cs.CityStateFunctions;

        Manager(a, cs).DeclareWar();

        Assert.Equal(1, a.NumMinorCivsAttacked);
        Assert.Equal(["A"], functions.Attacked);
        Assert.Equal(-60f, Manager(cs, a).GetInfluence());
        Assert.Equal(1, functions.AllyUpdates); // setInfluence updates the ally

        // attacking the city state's own ally: ally recalculated, influence even lower, everybody a knows remembers the betrayal
        var b = AddCiv(info, "B");
        Meet(a, b);
        MakePeace(a, b);
        var cs2 = AddCiv(info, "Zurich", cityStateType: "Cultured");
        Meet(a, cs2);
        Meet(b, cs2);
        MakePeace(a, cs2);
        cs2.AllyCiv = a;
        var functions2 = (FakeCityStateFunctions)cs2.CityStateFunctions;
        int updatesBefore = functions2.AllyUpdates;
        // the real updateAllyCivForCityState drops the ally once the influence is down; the explicit update is the second call of the war
        functions2.OnAllyUpdate = () =>
        {
            if (functions2.AllyUpdates == updatesBefore + 2) cs2.AllyCiv = null;
        };

        Manager(a, cs2).DeclareWar();

        Assert.Equal(2, a.NumMinorCivsAttacked);
        Assert.Equal(updatesBefore + 3, functions2.AllyUpdates); // setInfluence(-60), the explicit update, setInfluence(-120)
        Assert.Equal(-60f, Manager(cs2, a).GetInfluence()); // the stored influence is -120 but getInfluence is pinned at war
        Assert.Equal(-60f, Manager(cs2, a).Influence); // setInfluence clamps -120 to the minimum
        Assert.Equal(-10f, Manager(b, a).GetModifier(DiplomaticModifiers.BetrayedDeclarationOfFriendship));
    }

    [Fact]
    public void DefensivePactAlliesAreCalledInOnTheDefendingSide()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var c = AddCiv(info, "C", PlayerType.Human);
        a.PlayerType = PlayerType.Human;
        Meet(a, b);
        Meet(b, c);
        MakePeace(a, b);
        Manager(b, c).DiplomaticStatus = DiplomaticStatus.DefensivePact;
        Manager(c, b).DiplomaticStatus = DiplomaticStatus.DefensivePact;
        Assert.False(a.Knows(c));

        Manager(a, b).DeclareWar();

        Assert.True(a.Knows(c)); // they had to meet first
        Assert.True(a.IsAtWarWith(c));
        Assert.True(a.IsAtWarWith(b));
        Assert.Contains(c.Notifications, it => it.Text == "We have joined [B] in the war against [A]!"); // C is the aggressor, called in by the pact
        Assert.Contains(a.Notifications, it => it.Text == "[C] has joined [B] in the war against us!");
    }

    [Fact]
    public void OffensiveWarEndsTheAggressorsDefensivePacts()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A", PlayerType.Human);
        var b = AddCiv(info, "B");
        var c = AddCiv(info, "C", PlayerType.Human);
        var d = AddCiv(info, "D", PlayerType.Human);
        foreach (var x in new[] { b, c, d }) Meet(a, x);
        Meet(b, c);
        Meet(b, d);
        Meet(c, d);
        MakePeace(a, b);
        foreach (var x in new[] { c, d })
        {
            Manager(a, x).DiplomaticStatus = DiplomaticStatus.DefensivePact;
            Manager(x, a).DiplomaticStatus = DiplomaticStatus.DefensivePact;
            Manager(a, x).SetFlag(DiplomacyFlags.DefensivePact, 20);
            Manager(x, a).SetFlag(DiplomacyFlags.DefensivePact, 20);
            var pact = new TradeObject();
            pact.OurOffers.Add(new TradeOffer(Constants.DefensivePact, TradeOfferType.Treaty, 1, 20));
            Manager(a, x).Trades.Add(pact);
            Manager(x, a).Trades.Add(pact.Reverse());
        }
        Manager(d, b).DiplomaticStatus = DiplomaticStatus.Peace;
        Manager(a, b).DeclareWar();

        foreach (var x in new[] { c, d })
        {
            Assert.Equal(DiplomaticStatus.Peace, Manager(a, x).DiplomaticStatus);
            Assert.Equal(DiplomaticStatus.Peace, Manager(x, a).DiplomaticStatus);
            Assert.False(Manager(a, x).HasFlag(DiplomacyFlags.DefensivePact));
            Assert.Empty(Manager(a, x).Trades);
            Assert.Contains(x.Notifications, it => it.Text == "[A] cancelled their Defensive Pact with us!");
        }
        Assert.Contains(a.Notifications, it => it.Text == "We have cancelled our Defensive Pact with [C]!");
        Assert.Contains(d.Notifications, it => it.Text == "[A] cancelled their Defensive Pact with [C]!");
    }

    [Fact]
    public void CityStateAlliesJoinTheWar()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var cs = AddCiv(info, "Geneva", cityStateType: "Cultured");
        Meet(a, b);
        Meet(a, cs);
        MakePeace(a, b);
        MakePeace(a, cs);
        cs.AllyCiv = a;
        Assert.False(cs.Knows(b));

        Manager(a, b).DeclareWar();

        Assert.True(cs.Knows(b));
        Assert.True(cs.IsAtWarWith(b));
        Assert.Empty(((FakeCityStateFunctions)cs.CityStateFunctions).Attacked); // an indirect attack: B is not a city state
    }

    [Fact]
    public void WarWithAProtectedCityStateEndsTheProtection()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var cs = AddCiv(info, "Geneva", cityStateType: "Cultured");
        Meet(a, cs);
        MakePeace(a, cs);
        var functions = (FakeCityStateFunctions)cs.CityStateFunctions;
        functions.Protectors.Add(a);

        Manager(cs, a).DeclareWar(new DeclareWarReason(WarType.ProtectedCityStateWar, a));

        Assert.Equal([("A", true)], functions.RemovedProtectors);
    }

    [Fact]
    public void JoinWarAndCityStateWarsNotifyEverybodyInvolved()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A", PlayerType.Human);
        var b = AddCiv(info, "B", PlayerType.Human);
        var ally = AddCiv(info, "Ally", PlayerType.Human);
        var bystander = AddCiv(info, "Bystander", PlayerType.Human);
        foreach (var x in new[] { b, ally, bystander }) Meet(a, x);
        Meet(b, ally);
        Meet(b, bystander);
        Meet(ally, bystander);

        Manager(a, b).DeclareWar(new DeclareWarReason(WarType.JoinWar, ally));

        Assert.Contains(b.Notifications, it => it.Text == "[A] has joined [Ally] in the war against us!");
        Assert.Contains(a.Notifications, it => it.Text == "We have joined [Ally] in the war against [B]!");
        Assert.Contains(bystander.Notifications, it => it.Text == "[A] has joined [Ally] in the war against [B]!");
        Assert.Contains(ally.Notifications, it => it.Text == "[A] has joined us in the war against [B]!");
        Assert.DoesNotContain(ally.Notifications, it => it.Text == "[A] has joined [Ally] in the war against [B]!");
        Assert.Equal("A", Assert.Single(b.PopupAlerts, it => it.Type == AlertType.WarDeclaration).Value);
        // A join war also costs the declarer warmongering, but not with the ally
        Assert.False(Manager(ally, a).HasModifier(DiplomaticModifiers.WarMongerer));
        Assert.Equal(-5f, Manager(bystander, a).GetModifier(DiplomaticModifiers.WarMongerer));
    }

    [Fact]
    public void TeamWarNotifiesOnlyFromTheCivThatComesFirst()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A", PlayerType.Human);
        var b = AddCiv(info, "B", PlayerType.Human);
        var ally = AddCiv(info, "Ally", PlayerType.Human);
        var bystander = AddCiv(info, "Bystander", PlayerType.Human);
        foreach (var x in new[] { b, ally, bystander }) Meet(a, x);
        Meet(b, ally);
        Meet(b, bystander);
        Meet(ally, bystander);

        // Ally comes after A in the civilization list: the declaration from Ally's side sends nothing
        Manager(ally, b).DeclareWar(new DeclareWarReason(WarType.TeamWar, a));
        Assert.DoesNotContain(b.PopupAlerts, it => it.Type == AlertType.WarDeclaration);
        Assert.DoesNotContain(a.Notifications, it => it.Text.Contains("declared war against", StringComparison.Ordinal));

        Manager(a, b).DeclareWar(new DeclareWarReason(WarType.TeamWar, ally));

        Assert.Equal(["A", "Ally"], b.PopupAlerts.Where(it => it.Type == AlertType.WarDeclaration).Select(it => it.Value));
        Assert.Contains(a.Notifications, it => it.Text == "You and [Ally] have declared war against [B]!");
        Assert.Contains(ally.Notifications, it => it.Text == "You and [A] have declared war against [B]!");
        Assert.Contains(b.Notifications, it => it.Text == "[A] and [Ally] have declared war against us!");
        Assert.Contains(bystander.Notifications, it => it.Text == "[A] and [Ally] have declared war against [B]!");
    }

    // ---- DiplomacyTurnManager

    [Fact]
    public void NextTurn_PeaceGrowsYearsOfPeaceUpToItsMaximumAndWarForgetsIt()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        Meet(a, b);
        MakePeace(a, b);
        var manager = Manager(a, b);

        manager.NextTurn();
        Assert.Equal(0.67f * 0.5f / 1f, manager.GetModifier(DiplomaticModifiers.YearsOfPeace));

        manager.SetModifier(DiplomaticModifiers.YearsOfPeace, 29.9f);
        manager.NextTurn();
        Assert.Equal(30f, manager.GetModifier(DiplomaticModifiers.YearsOfPeace));
        manager.NextTurn(); // already at the maximum
        Assert.Equal(30f, manager.GetModifier(DiplomaticModifiers.YearsOfPeace));

        manager.DiplomaticStatus = DiplomaticStatus.War;
        manager.NextTurn();
        Assert.Equal(30f - 0.67f * 0.5f, manager.GetModifier(DiplomaticModifiers.YearsOfPeace));
        manager.SetModifier(DiplomaticModifiers.YearsOfPeace, 0.1f);
        manager.NextTurn();
        Assert.False(manager.HasModifier(DiplomaticModifiers.YearsOfPeace));
    }

    [Fact]
    public void NextTurn_ModifiersRevertToZeroAtTheirOwnRateAdjustedToGameSpeed()
    {
        var info = new FakeGameInfoState();
        info.SpeedData.Modifier = 2f;
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        Meet(a, b);
        MakePeace(a, b);
        var manager = Manager(a, b);
        manager.SetModifier(DiplomaticModifiers.DeclaredWarOnUs, -20f);
        manager.SetModifier(DiplomaticModifiers.LiberatedCity, 10f);
        manager.SetModifier(DiplomaticModifiers.WarMongerer, -0.1f);
        manager.SetModifier(DiplomaticModifiers.BetrayedDefensivePact, 0.01f);
        manager.SetModifier(DiplomaticModifiers.BetrayedPromiseToNotAttackOtherCiv, -3f);
        manager.SetModifier(DiplomaticModifiers.DeclarationOfFriendship, 8f);
        manager.SetModifier(DiplomaticModifiers.DefensivePact, 8f);

        manager.NextTurn();

        Assert.Equal(-20f + 0.67f * (1 / 8f) / 2f, manager.GetModifier(DiplomaticModifiers.DeclaredWarOnUs));
        Assert.Equal(10f - 0.67f * (1 / 8f) / 2f, manager.GetModifier(DiplomaticModifiers.LiberatedCity));
        Assert.False(manager.HasModifier(DiplomaticModifiers.WarMongerer)); // smaller than one step: gone
        Assert.False(manager.HasModifier(DiplomaticModifiers.BetrayedDefensivePact));
        Assert.Equal(-3f + 0.67f * (1 / 8f) / 2f, manager.GetModifier(DiplomaticModifiers.BetrayedPromiseToNotAttackOtherCiv));
        Assert.Equal(8f - 0.67f * (1 / 2f) / 2f, manager.GetModifier(DiplomaticModifiers.DeclarationOfFriendship)); // no such flag
        Assert.Equal(8f - 0.67f * 1f / 2f, manager.GetModifier(DiplomaticModifiers.DefensivePact));

        manager.SetFlag(DiplomacyFlags.DeclarationOfFriendship, 5);
        manager.SetFlag(DiplomacyFlags.DefensivePact, 5);
        Manager(b, a).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 5);
        Manager(b, a).SetFlag(DiplomacyFlags.DefensivePact, 5);
        manager.SetModifier(DiplomaticModifiers.DeclarationOfFriendship, 8f);
        manager.SetModifier(DiplomaticModifiers.DefensivePact, 8f);
        manager.NextTurn();
        Assert.Equal(8f, manager.GetModifier(DiplomaticModifiers.DeclarationOfFriendship)); // not reverted while the flag is set
        Assert.Equal(8f, manager.GetModifier(DiplomaticModifiers.DefensivePact));
    }

    [Fact]
    public void NextTurn_OpenBordersGrowByHowManySidesHaveThem()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        Meet(a, b);
        MakePeace(a, b);
        var manager = Manager(a, b);
        manager.SetModifier(DiplomaticModifiers.OpenBorders, 1f);
        var theirs = new TradeObject();
        theirs.TheirOffers.Add(new TradeOffer(Constants.OpenBorders, TradeOfferType.Agreement, 1, 20));
        manager.Trades.Add(theirs);
        Manager(b, a).HasOpenBorders = true; // updated by their own nextTurn

        manager.NextTurn();
        Assert.Equal(1f + 0.67f * (2 / 8f), manager.GetModifier(DiplomaticModifiers.OpenBorders), 5);

        Manager(b, a).HasOpenBorders = false;
        manager.NextTurn(); // only one side has them now
        Assert.Equal(1f + 0.67f * (2 / 8f) + 0.67f * (1 / 8f), manager.GetModifier(DiplomaticModifiers.OpenBorders), 5);
        manager.Trades.Clear();
        manager.NextTurn(); // neither: reverts
        Assert.Equal(1f + 0.67f * (2 / 8f) + 0.67f * (1 / 8f) - 0.67f * (1 / 8f), manager.GetModifier(DiplomaticModifiers.OpenBorders), 5);
    }

    [Fact]
    public void NextTurn_GiftsAreForgottenFasterTheWorseTheRelationship()
    {
        var info = new FakeGameInfoState();
        info.RulesetData.ModOptions.Constants.GoldGiftDegradationMultiplier = 2f;
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        Meet(a, b);
        MakePeace(a, b);
        var manager = Manager(a, b);
        manager.SetModifier(DiplomaticModifiers.GaveUsGifts, 1000f);

        manager.NextTurn();

        // The relationship is Neutral-ish after the setFriendshipBasedModifier call: the loss is 2.5 * multiplier (the relationship level
        // is read before the revert), 2.5% of the amount or a fifth of the loss, whichever is higher
        float giftLoss = manager.GetModifier(DiplomaticModifiers.GaveUsGifts);
        Assert.InRange(giftLoss, 900f, 999.9f);
        manager.SetModifier(DiplomaticModifiers.GaveUsGifts, 0.5f);
        manager.NextTurn();
        Assert.False(manager.HasModifier(DiplomaticModifiers.GaveUsGifts)); // the minimum step (a fifth of the loss) empties it
    }

    [Fact]
    public void NextTurn_FlagsCountDownAndExpiryTriggersTheirEffects()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        Meet(a, b);
        MakePeace(a, b);
        var manager = Manager(a, b);
        manager.SetFlag(DiplomacyFlags.DeclinedPeace, 2);
        manager.SetFlag(DiplomacyFlags.DefensivePact, 1);
        manager.DiplomaticStatus = DiplomaticStatus.DefensivePact;
        manager.SetFlag(DiplomacyFlags.WaryOf, -10);
        manager.SetFlag(DiplomacyFlags.RememberDestroyedProtectedMinor, 1);
        manager.SetModifier(DiplomaticModifiers.DestroyedProtectedMinor, -40f);
        manager.SetFlag(DiplomacyFlags.RememberAttackedProtectedMinor, 51);
        manager.SetFlag(DiplomacyFlags.RememberBulliedProtectedMinor, 51);
        manager.SetFlag(DiplomacyFlags.RememberSidedWithProtectedMinor, 1);
        manager.SetModifier(DiplomaticModifiers.SidedWithProtectedMinor, -5f);
        manager.SetFlag(DiplomacyFlags.AgreedToNotAttackUs, 1);

        manager.NextTurn();

        Assert.Equal(1, manager.GetFlag(DiplomacyFlags.DeclinedPeace));
        Assert.False(manager.HasFlag(DiplomacyFlags.DefensivePact));
        Assert.Equal(DiplomaticStatus.Peace, manager.DiplomaticStatus);
        Assert.False(manager.HasFlag(DiplomacyFlags.WaryOf)); // more than 10 turns of preparation
        Assert.False(manager.HasFlag(DiplomacyFlags.RememberDestroyedProtectedMinor));
        Assert.False(manager.HasModifier(DiplomaticModifiers.DestroyedProtectedMinor));
        Assert.False(manager.HasModifier(DiplomaticModifiers.SidedWithProtectedMinor));
        // the "slightly decreases at 50" modifiers (0.67 * 0.5 per turn of revert comes on top)
        Assert.Equal(5f, manager.GetModifier(DiplomaticModifiers.AttackedProtectedMinor));
        Assert.Equal(5f, manager.GetModifier(DiplomaticModifiers.BulliedProtectedMinor));
        Assert.False(manager.HasFlag(DiplomacyFlags.AgreedToNotAttackUs));
        Assert.False(manager.HasModifier(DiplomaticModifiers.AttackedAlliedMinor));

        manager.SetFlag(DiplomacyFlags.RememberAttackedProtectedMinor, 1);
        manager.SetFlag(DiplomacyFlags.RememberBulliedProtectedMinor, 1);
        manager.NextTurn();
        Assert.False(manager.HasModifier(DiplomaticModifiers.AttackedProtectedMinor));
        Assert.False(manager.HasModifier(DiplomaticModifiers.BulliedProtectedMinor));
    }

    [Fact]
    public void NextTurn_AKeptPromiseEarnsAModifierWhenItsFlagExpires()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        Meet(a, b);
        MakePeace(a, b);
        var manager = Manager(a, b);
        manager.SetFlag(Demand.DoNotAttackUs.AgreedToDemand, 1);

        manager.NextTurn();

        Assert.False(manager.HasFlag(Demand.DoNotAttackUs.AgreedToDemand));
        Assert.Equal(10f, manager.GetModifier(Demand.DoNotAttackUs.FulfilledPromiseDiplomacyModifier));
    }

    [Fact]
    public void NextTurn_ResearchAgreementCountsScienceAndPaysOutOnceWhenBothSidesExpire()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        Meet(a, b);
        MakePeace(a, b);
        a.Stats.StatsForNextTurn.Science = 12.7f;
        var ab = Manager(a, b);
        var ba = Manager(b, a);
        ab.SetFlag(DiplomacyFlags.ResearchAgreement, 2);
        ba.SetFlag(DiplomacyFlags.ResearchAgreement, 2);
        ba.TotalOfScienceDuringRA = 4;

        ab.NextTurn();
        Assert.Equal(12, ab.TotalOfScienceDuringRA);
        ab.NextTurn();
        Assert.Equal(24, ab.TotalOfScienceDuringRA);
        Assert.False(ab.HasFlag(DiplomacyFlags.ResearchAgreement));
        Assert.Equal(0, a.Tech.ScienceFromResearchAgreements); // the other side still has the flag: it pays out when they expire

        ba.SetFlag(DiplomacyFlags.ResearchAgreement, 1);
        b.Stats.StatsForNextTurn.Science = 0f;
        ba.NextTurn();
        Assert.Equal(4, a.Tech.ScienceFromResearchAgreements); // the smaller of both totals, to both
        Assert.Equal(4, b.Tech.ScienceFromResearchAgreements);
        Assert.Equal(0, ab.TotalOfScienceDuringRA);
        Assert.Equal(0, ba.TotalOfScienceDuringRA);
    }

    [Fact]
    public void NextTurn_TimedTradesCountDownWarnAndEnd()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A", PlayerType.Human);
        var b = AddCiv(info, "B", PlayerType.Human);
        Meet(a, b);
        MakePeace(a, b);
        var manager = Manager(a, b);
        var trade = new TradeObject();
        trade.OurOffers.Add(new TradeOffer("Our gold", TradeOfferType.Gold_Per_Turn, 3, 4));
        trade.TheirOffers.Add(new TradeOffer("Their gold", TradeOfferType.Gold_Per_Turn, 5, 4));
        trade.TheirOffers.Add(new TradeOffer("Gift", TradeOfferType.Gold, 5, -1));
        manager.Trades.Add(trade);

        manager.NextTurn();
        Assert.Equal(3, trade.TheirOffers[0].Duration);
        Assert.Equal(3, trade.OurOffers[0].Duration);
        Assert.Equal(-1, trade.TheirOffers[1].Duration); // immediate offers do not count down
        Assert.Equal("[Their gold] from [B] will end in [3] turns", Assert.Single(a.Notifications).Text);

        manager.NextTurn();
        manager.NextTurn();
        Assert.Equal(1, trade.TheirOffers[0].Duration);
        Assert.Equal("[Their gold] from [B] will end next turn", a.Notifications[^1].Text);
        Assert.Single(manager.Trades);

        manager.NextTurn();
        Assert.Empty(manager.Trades);
        Assert.Contains(a.Notifications, it => it.Text == "[Their gold] from [B] has ended");
        Assert.Contains(a.Notifications, it => it.Text == "[Our gold] to [B] has ended");
    }

    [Fact]
    public void NextTurn_EqualOffersOnBothSidesOfATradeAreCountedDownOnlyOnce()
    {
        // Kotlin: ourOffers.union(theirOffers) is a set over the data-class equals, so identical offers (e.g. both halves of a peace treaty) merge
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        Meet(a, b);
        MakePeace(a, b);
        var manager = Manager(a, b);
        var treaty = new TradeObject();
        treaty.OurOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, 1, 10));
        treaty.TheirOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, 1, 10));
        manager.Trades.Add(treaty);

        manager.NextTurn();

        Assert.Equal(9, treaty.OurOffers[0].Duration);
        Assert.Equal(10, treaty.TheirOffers[0].Duration);
    }

    [Fact]
    public void NextTurn_TradesOfAResourceThatNoLongerExistsAreCutShort()
    {
        var info = new FakeGameInfoState();
        info.SpeedData.DealDuration = 30;
        var a = AddCiv(info, "A", PlayerType.Human);
        var b = AddCiv(info, "B", PlayerType.Human);
        Meet(a, b);
        MakePeace(a, b);
        var manager = Manager(a, b);
        var trade = new TradeObject();
        trade.OurOffers.Add(new TradeOffer("Unobtainium", TradeOfferType.Luxury_Resource, 1, 20));
        trade.TheirOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, 1, 7));
        manager.Trades.Add(trade);
        Manager(b, a).Trades.Add(trade.Reverse());
        Manager(b, a).Trades.Add(new TradeObject());

        manager.NextTurn();

        // the trade is gone on both sides, but a peace treaty cannot be cut short: it is remade with the time left
        var remade = Assert.Single(manager.Trades);
        Assert.Equal(6, remade.OurOffers.Single().Duration); // 7 minus this turn's countdown, which runs before the untenable trades are removed
        Assert.Equal(6, remade.TheirOffers.Single().Duration);
        Assert.Equal(2, Manager(b, a).Trades.Count);
        Assert.Contains(a.Notifications, it => it.Text == "One of our trades with [B] has been cut short");
        Assert.Contains(b.Notifications, it => it.Text == "One of our trades with [A] has been cut short");
        Assert.Equal(60, Manager(b, a).GetFlag(DiplomacyFlags.ResourceTradesCutShort));
    }

    [Fact]
    public void NextTurn_CityStateInfluenceMovesTowardsItsRestingPointAndWarnsTheMajorCiv()
    {
        var info = new FakeGameInfoState();
        var cs = AddCiv(info, "Geneva", cityStateType: "Cultured");
        var a = AddCiv(info, "A", PlayerType.Human);
        Meet(cs, a);
        MakePeace(cs, a);
        var manager = Manager(cs, a);

        manager.SetInfluenceWithoutSideEffects(10f);
        manager.NextTurn();
        Assert.Equal(9f, manager.Influence); // decays by one towards the resting point

        manager.SetInfluenceWithoutSideEffects(-10f);
        manager.NextTurn();
        Assert.Equal(-9f, manager.Influence); // recovers by one (no uniques, no shared religion)

        manager.SetInfluenceWithoutSideEffects(0.4f);
        manager.NextTurn();
        Assert.Equal(0f, manager.Influence); // never overshoots the resting point
    }

    [Fact]
    public void NextTurn_ARelationshipAboutToDegradeOrDegradedIsAnnounced()
    {
        var info = new FakeGameInfoState();
        var cs = AddCiv(info, "Geneva", cityStateType: "Cultured");
        var a = AddCiv(info, "A", PlayerType.Human);
        Meet(cs, a);
        MakePeace(cs, a);
        var manager = Manager(cs, a);
        a.Notifications.Clear();

        manager.SetInfluenceWithoutSideEffects(31f); // Friend now and after this turn (30): next turn it drops below 30
        manager.NextTurn();
        Assert.Equal(30f, manager.Influence);
        Assert.Equal(["Your relationship with [Geneva] is about to degrade"], a.Notifications.Select(it => it.Text));

        a.Notifications.Clear();
        manager.SetInfluenceWithoutSideEffects(30.5f);
        manager.NextTurn();
        Assert.Equal(29.5f, manager.Influence);
        Assert.Equal(["Your relationship with [Geneva] degraded"], a.Notifications.Select(it => it.Text));
    }

    [Fact]
    public void NextTurn_AfraidNotificationIsSentOnceEveryTwentyTurns()
    {
        var info = new FakeGameInfoState();
        var cs = AddCiv(info, "Geneva", cityStateType: "Cultured");
        var a = AddCiv(info, "A", PlayerType.Human);
        Meet(cs, a);
        MakePeace(cs, a);
        ((FakeCityStateFunctions)cs.CityStateFunctions).TributeWillingness = 5;
        var manager = Manager(cs, a);

        manager.NextTurn();
        Assert.Contains(a.Notifications, it => it.Text == "[Geneva] is afraid of your military power!");
        Assert.Equal(20, manager.GetFlag(DiplomacyFlags.NotifiedAfraid));
        a.Notifications.Clear();
        manager.NextTurn();
        Assert.DoesNotContain(a.Notifications, it => it.Text == "[Geneva] is afraid of your military power!");
    }

    [Fact]
    public void NextTurn_ACityStateBelowFriendStopsProvidingMilitaryUnits()
    {
        var info = new FakeGameInfoState();
        var cs = AddCiv(info, "Geneva", cityStateType: "Militaristic");
        var a = AddCiv(info, "A");
        Meet(a, cs);
        MakePeace(a, cs);
        var manager = Manager(a, cs);
        manager.SetFlag(DiplomacyFlags.ProvideMilitaryUnit, 5);
        Manager(cs, a).SetInfluenceWithoutSideEffects(10f);

        manager.NextTurn();

        Assert.False(manager.HasFlag(DiplomacyFlags.ProvideMilitaryUnit));
    }
}
