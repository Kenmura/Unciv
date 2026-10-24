using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Trade;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using City = Unciv.Logic.City.City;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;

namespace Unciv.Core.Tests.Logic.Diplomacy;

// T059: DiplomacyManager, DiplomacyFunctions, Demand, Trade, TradeOffer, TradeOffersList. The Kotlin fixtures (DiplomacyManagerTests,
// GoldGiftingTests, DefensivePactTests) are TestGame-based (T072); the cases that need no map, city or unit are ported here over
// the Civilization seam fakes, the rest are hand-derived from the Kotlin source.
public class DiplomacyCoreTests
{
    private static Civilization AddCiv(FakeGameInfoState info, string name, PlayerType playerType = PlayerType.AI, string? cityStateType = null)
    {
        var civ = new Civilization(new NationObject { Name = name, CityStateType = cityStateType })
        {
            GameInfo = info,
            PlayerType = playerType,
            CityStateFunctions = new FakeCityStateFunctions(),
            QuestManager = new FakeQuestManager(),
        };
        civ.Units = new TestUnitManager(civ) { Count = 1 }; // so they are not considered defeated
        info.Civs.Add(civ);
        return civ;
    }

    private static void Meet(Civilization civ, Civilization other) => civ.DiplomacyFunctions.MakeCivilizationsMeet(other);

    private static (FakeGameInfoState Info, Civilization A, Civilization B, Civilization C, Civilization D) FourCivs()
    {
        var info = new FakeGameInfoState();
        return (info, AddCiv(info, "A"), AddCiv(info, "B"), AddCiv(info, "C"), AddCiv(info, "D"));
    }

    // ---- DiplomacyManagerTests.kt (the ones that need no map)

    [Fact]
    public void GetCommonKnownCivs_DoesNotIncludeEitherDiplomacyManagersCivs()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);
        var common = a.GetDiplomacyManager(b)!.GetCommonKnownCivs();
        Assert.DoesNotContain(a, common);
        Assert.DoesNotContain(b, common);
    }

    [Fact]
    public void GetCommonKnownCivs_IncludesCivsMetByBothCivs()
    {
        var (_, a, b, c, _) = FourCivs();
        Meet(a, b);
        Meet(b, c);
        Meet(c, a);
        Assert.Contains(c, a.GetDiplomacyManager(b)!.GetCommonKnownCivs());
    }

    [Fact]
    public void GetCommonKnownCivs_DoesNotIncludeCivsMetByOnlyOneCiv()
    {
        var (_, a, b, c, _) = FourCivs();
        Meet(a, b);
        Meet(a, c);
        Assert.DoesNotContain(c, a.GetDiplomacyManager(b)!.GetCommonKnownCivs());
    }

    [Fact]
    public void GetCommonKnownCivs_IsEqualForMirroredDiplomacyManagers()
    {
        var (_, a, b, c, d) = FourCivs();
        Meet(a, b);
        Meet(a, c);
        Meet(b, c);
        Meet(a, d);
        Meet(b, d);
        Assert.Equal(
            a.GetDiplomacyManager(b)!.GetCommonKnownCivs().ToHashSet(),
            b.GetDiplomacyManager(a)!.GetCommonKnownCivs().ToHashSet());
        Assert.Equal(2, a.GetDiplomacyManager(b)!.GetCommonKnownCivs().Count);
    }

    [Fact]
    public void Opinion_IsZeroWhenJustMet()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);
        Assert.Equal(0f, a.GetDiplomacyManager(b)!.OpinionOfOtherCiv());
        Assert.Equal(0f, a.GetDiplomacyManager(b)!.SmoothedOpinionOfOtherCiv);
    }

    [Fact]
    public void Denounce_ChangesOnlyTheDenouncedCivsOpinion()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);

        a.GetDiplomacyManager(b)!.Denounce();

        Assert.Equal(DiplomacyManager.InitialOpinionChangeWhenDenounced, b.GetDiplomacyManager(a)!.OpinionOfOtherCiv());
        Assert.Equal(0f, a.GetDiplomacyManager(b)!.OpinionOfOtherCiv());
        Assert.Equal(30, a.GetDiplomacyManager(b)!.GetFlag(DiplomacyFlags.Denunciation));
        var popup = Assert.Single(b.PopupAlerts, it => it.Type == AlertType.Denounced);
        Assert.Equal("A", popup.Value);
    }

    [Fact]
    public void Denounce_IsLessUpsettingTheMoreCivsAlreadyDenouncedTheTarget()
    {
        var (_, a, b, c, _) = FourCivs();
        Meet(a, b);
        Meet(c, b);
        Meet(a, c);
        a.GetDiplomacyManager(b)!.Denounce();
        c.GetDiplomacyManager(b)!.Denounce();
        // b is known to c and a, a has already denounced b: 2^-0.5 * -35
        Assert.Equal(-35f * KotlinMath.Pow(2f, -0.5f), b.GetDiplomacyManager(c)!.OpinionOfOtherCiv());
        // the third civs think about the denounced civ: a is neutral towards b (opinion 0), so nothing changes for them
        Assert.False(c.GetDiplomacyManager(a)!.HasModifier(DiplomaticModifiers.DenouncedOurEnemies));
    }

    // ---- Opinion and relationship levels

    [Theory]
    [InlineData(-100f, RelationshipLevel.Unforgivable)]
    [InlineData(-80f, RelationshipLevel.Unforgivable)]
    [InlineData(-79.9f, RelationshipLevel.Enemy)]
    [InlineData(-40f, RelationshipLevel.Enemy)]
    [InlineData(-39.9f, RelationshipLevel.Competitor)]
    [InlineData(-15f, RelationshipLevel.Competitor)]
    [InlineData(-14.9f, RelationshipLevel.Neutral)]
    [InlineData(0f, RelationshipLevel.Neutral)]
    [InlineData(14.9f, RelationshipLevel.Neutral)]
    [InlineData(15f, RelationshipLevel.Favorable)]
    [InlineData(39.9f, RelationshipLevel.Favorable)]
    [InlineData(40f, RelationshipLevel.Friend)]
    [InlineData(79.9f, RelationshipLevel.Friend)]
    [InlineData(80f, RelationshipLevel.Ally)]
    public void RelationshipLevel_OfAnAICivFollowsTheOpinionThresholds(float opinion, RelationshipLevel expected)
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);
        var manager = a.GetDiplomacyManager(b)!;
        manager.SetModifier(DiplomaticModifiers.YearsOfPeace, opinion);
        Assert.Equal(expected, manager.RelationshipLevel());
        Assert.Equal(expected, manager.RelationshipIgnoreAfraid());
        Assert.True(manager.IsRelationshipLevelEQ(expected));
    }

    [Fact]
    public void RelationshipLevel_AtWarIsAtMostEnemyForAnAICiv()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);
        var manager = a.GetDiplomacyManager(b)!;
        manager.SetModifier(DiplomaticModifiers.YearsOfPeace, 50f);
        Assert.Equal(RelationshipLevel.Friend, manager.RelationshipLevel());
        manager.DiplomaticStatus = DiplomaticStatus.War;
        Assert.Equal(RelationshipLevel.Enemy, manager.RelationshipLevel());
        manager.SetModifier(DiplomaticModifiers.YearsOfPeace, -90f);
        Assert.Equal(RelationshipLevel.Unforgivable, manager.RelationshipLevel());
    }

    [Fact]
    public void RelationshipLevel_HumansAreNeutralToEachOtherAndSeeTheAIsOpinion()
    {
        var info = new FakeGameInfoState();
        var human = AddCiv(info, "Human", PlayerType.Human);
        var otherHuman = AddCiv(info, "Human2", PlayerType.Human);
        var ai = AddCiv(info, "Ai");
        Meet(human, otherHuman);
        Meet(human, ai);
        ai.GetDiplomacyManager(human)!.SetModifier(DiplomaticModifiers.YearsOfPeace, 45f);
        human.GetDiplomacyManager(otherHuman)!.SetModifier(DiplomaticModifiers.YearsOfPeace, 45f);
        Assert.Equal(RelationshipLevel.Neutral, human.GetDiplomacyManager(otherHuman)!.RelationshipLevel());
        Assert.Equal(RelationshipLevel.Friend, human.GetDiplomacyManager(ai)!.RelationshipLevel());
    }

    [Fact]
    public void RelationshipComparisons_MatchTheEnumOrder()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);
        var manager = a.GetDiplomacyManager(b)!;
        manager.SetModifier(DiplomaticModifiers.YearsOfPeace, 45f); // Friend
        Assert.True(manager.IsRelationshipLevelGE(RelationshipLevel.Friend));
        Assert.True(manager.IsRelationshipLevelGE(RelationshipLevel.Unforgivable));
        Assert.False(manager.IsRelationshipLevelGE(RelationshipLevel.Ally));
        Assert.True(manager.IsRelationshipLevelGT(RelationshipLevel.Favorable));
        Assert.False(manager.IsRelationshipLevelGT(RelationshipLevel.Friend));
        Assert.True(manager.IsRelationshipLevelLE(RelationshipLevel.Friend));
        Assert.True(manager.IsRelationshipLevelLE(RelationshipLevel.Ally));
        Assert.False(manager.IsRelationshipLevelLE(RelationshipLevel.Favorable));
        Assert.True(manager.IsRelationshipLevelLT(RelationshipLevel.Ally));
        Assert.False(manager.IsRelationshipLevelLT(RelationshipLevel.Friend));
        Assert.True(manager.IsRelationshipLevelEQ(RelationshipLevel.Friend));
    }

    [Fact]
    public void RelationshipLevel_PlusClampsAtBothEnds()
    {
        Assert.Equal(RelationshipLevel.Ally, RelationshipLevel.Friend.Plus(1));
        Assert.Equal(RelationshipLevel.Ally, RelationshipLevel.Ally.Plus(1));
        Assert.Equal(RelationshipLevel.Unforgivable, RelationshipLevel.Enemy.Plus(-5));
        Assert.Equal(RelationshipLevel.Neutral, RelationshipLevel.Neutral.Plus(0));
        Assert.Equal(new Unciv.Models.EngineColor(0x14 / 255f, 0xcc / 255f, 0x3c / 255f, 1f), RelationshipLevel.Favorable.Color());
    }

    [Fact]
    public void Opinion_DestroyedAndAttackedProtectedMinorDoNotStack()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);
        var manager = a.GetDiplomacyManager(b)!;
        manager.SetModifier(DiplomaticModifiers.DestroyedProtectedMinor, -20f);
        manager.SetModifier(DiplomaticModifiers.AttackedProtectedMinor, -10f);
        manager.SetModifier(DiplomaticModifiers.YearsOfPeace, 5f);
        Assert.Equal(-20f + 5f, manager.OpinionOfOtherCiv()); // -20 -10 +5, minus the attacked one again
    }

    [Fact]
    public void SaveSmoothedOpinion_MovesTowardsTheOpinion()
    {
        var (info, a, b, _, _) = FourCivs();
        info.SpeedData.Modifier = 1f; // period 30
        Meet(a, b);
        var manager = a.GetDiplomacyManager(b)!;
        manager.SetModifier(DiplomaticModifiers.YearsOfPeace, 30f);
        manager.SaveSmoothedOpinionOfOtherCiv(); // smoothed = cached (0), cached = (30 + 0 * 29) / 30 = 1
        Assert.Equal(0f, manager.SmoothedOpinionOfOtherCiv);
        manager.SaveSmoothedOpinionOfOtherCiv(); // smoothed = 1, cached = (30 + 1 * 29) / 30
        Assert.Equal(1f, manager.SmoothedOpinionOfOtherCiv);
        Assert.Equal(29f, manager.SmoothedOpinionDelta());
    }

    // ---- Flags and modifiers

    [Fact]
    public void Flags_SetGetRemoveAndGameSpeedRounding()
    {
        var (info, a, b, _, _) = FourCivs();
        info.SpeedData.Modifier = 0.5f;
        Meet(a, b);
        var manager = a.GetDiplomacyManager(b)!;
        Assert.False(manager.HasFlag(DiplomacyFlags.DeclinedPeace));
        Assert.Equal(0, manager.GetFlag(DiplomacyFlags.DeclinedPeace));
        manager.SetFlag(DiplomacyFlags.DeclinedPeace, 5);
        Assert.Equal(5, manager.GetFlag(DiplomacyFlags.DeclinedPeace));
        manager.SetFlag(DiplomacyFlags.DeclinedPeace, 5, true); // 5 * 0.5 = 2.5 rounds half up
        Assert.Equal(3, manager.GetFlag(DiplomacyFlags.DeclinedPeace));
        manager.SetFlag(DiplomacyFlags.WaryOf, -1); // never expires, but exists
        Assert.True(manager.HasFlag(DiplomacyFlags.WaryOf));
        manager.RemoveFlag(DiplomacyFlags.DeclinedPeace);
        Assert.False(manager.HasFlag(DiplomacyFlags.DeclinedPeace));
    }

    [Fact]
    public void AddModifier_AccumulatesClampsAndRemovesZero()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);
        var manager = a.GetDiplomacyManager(b)!;
        manager.AddModifier(DiplomaticModifiers.YearsOfPeace, 4f);
        manager.AddModifier(DiplomaticModifiers.YearsOfPeace, 4f, 6f); // limit > 0: at most
        Assert.Equal(6f, manager.GetModifier(DiplomaticModifiers.YearsOfPeace));
        manager.AddModifier(DiplomaticModifiers.YearsOfPeace, -20f, -3f); // limit < 0: at least
        Assert.Equal(-3f, manager.GetModifier(DiplomaticModifiers.YearsOfPeace));
        manager.AddModifier(DiplomaticModifiers.YearsOfPeace, 3f);
        Assert.False(manager.HasModifier(DiplomaticModifiers.YearsOfPeace)); // exactly zero is removed
        Assert.Equal(0f, manager.GetModifier(DiplomaticModifiers.YearsOfPeace));
        manager.AddModifier(DiplomaticModifiers.SharedEnemy, 0f);
        Assert.False(manager.HasModifier(DiplomaticModifiers.SharedEnemy));
        manager.AddModifier(DiplomaticModifiers.GaveUsGifts, 2f);
        manager.ReplaceModifier(DiplomaticModifiers.GaveUsGifts, DiplomaticModifiers.GaveUsUnits, 7f);
        Assert.False(manager.HasModifier(DiplomaticModifiers.GaveUsGifts));
        Assert.Equal(7f, manager.GetModifier(DiplomaticModifiers.GaveUsUnits));
    }

    [Fact]
    public void DiplomaticModifiers_TextAndSafeValueOf()
    {
        Assert.Equal("You declared war on us!", DiplomaticModifiers.DeclaredWarOnUs.Text());
        Assert.Equal("We believe in the same religion", DiplomaticModifiers.BelieveSameReligion.Text());
        Assert.Equal(DiplomaticModifiers.GaveUsGifts, DiplomaticModifiersExtensions.SafeValueOf("GaveUsGifts"));
        Assert.Null(DiplomaticModifiersExtensions.SafeValueOf("gaveusgifts"));
        Assert.Null(DiplomaticModifiersExtensions.SafeValueOf("5"));
        Assert.Null(DiplomaticModifiersExtensions.SafeValueOf("999"));
    }

    [Fact]
    public void SignDeclarationOfFriendship_SetsModifiersAndFlagsAndInformsThirdCivs()
    {
        var (_, a, b, c, _) = FourCivs();
        Meet(a, b);
        Meet(a, c);
        Meet(b, c);
        c.GetDiplomacyManager(b)!.SetModifier(DiplomaticModifiers.YearsOfPeace, 85f); // c thinks of b as an Ally

        a.GetDiplomacyManager(b)!.SignDeclarationOfFriendship();

        foreach (var manager in new[] { a.GetDiplomacyManager(b)!, b.GetDiplomacyManager(a)! })
        {
            Assert.Equal(35f, manager.GetModifier(DiplomaticModifiers.DeclarationOfFriendship));
            Assert.Equal(30, manager.GetFlag(DiplomacyFlags.DeclarationOfFriendship));
        }
        // c sees a befriend its ally b
        Assert.Equal(15f, c.GetDiplomacyManager(a)!.GetModifier(DiplomaticModifiers.DeclaredFriendshipWithOurAllies));
        Assert.False(c.GetDiplomacyManager(a)!.HasModifier(DiplomaticModifiers.DeclaredFriendshipWithOurEnemies));
        // ... and b befriend a, whom c is neutral towards: a modifier of 0 is not kept
        Assert.False(c.GetDiplomacyManager(b)!.HasModifier(DiplomaticModifiers.DeclaredFriendshipWithOurAllies));
    }

    [Fact]
    public void SignDefensivePact_SetsStatusFlagAndModifiersOnBothSides()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);

        a.GetDiplomacyManager(b)!.SignDefensivePact(25);

        foreach (var manager in new[] { a.GetDiplomacyManager(b)!, b.GetDiplomacyManager(a)! })
        {
            Assert.Equal(DiplomaticStatus.DefensivePact, manager.DiplomaticStatus);
            Assert.Equal(25, manager.GetFlag(DiplomacyFlags.DefensivePact));
            Assert.Equal(10f, manager.GetModifier(DiplomaticModifiers.DefensivePact));
        }
    }

    // ---- Influence

    [Fact]
    public void Influence_IsClampedReducedAndPinnedWhileAtWar()
    {
        var info = new FakeGameInfoState();
        var cityState = AddCiv(info, "Geneva", cityStateType: "Militaristic");
        var a = AddCiv(info, "A");
        Meet(a, cityState);
        var manager = cityState.GetDiplomacyManager(a)!;
        manager.DiplomaticStatus = DiplomaticStatus.Peace;
        var functions = (FakeCityStateFunctions)cityState.CityStateFunctions;

        manager.AddInfluence(31f);
        Assert.Equal(31f, manager.GetInfluence());
        Assert.Equal(1, functions.AllyUpdates);
        Assert.True(manager.IsRelationshipLevelEQ(RelationshipLevel.Friend));

        manager.ReduceInfluence(100f);
        Assert.Equal(0f, manager.GetInfluence());
        manager.SetInfluence(-500f);
        Assert.Equal(DiplomacyManager.MinimumInfluence, manager.GetInfluence());
        manager.ReduceInfluence(5f); // negative influence: nothing happens
        Assert.Equal(-60f, manager.GetInfluence());

        manager.SetInfluence(45f);
        manager.DiplomaticStatus = DiplomaticStatus.War;
        Assert.Equal(-60f, manager.GetInfluence()); // getInfluence ignores the stored influence at war
        Assert.True(manager.IsRelationshipLevelEQ(RelationshipLevel.Unforgivable));
        manager.SetInfluenceWithoutSideEffects(10f);
        Assert.Equal(3, functions.AllyUpdates);
    }

    [Fact]
    public void RelationshipLevel_OfACityStateFollowsItsInfluence()
    {
        var info = new FakeGameInfoState();
        var cityState = AddCiv(info, "Geneva", cityStateType: "Militaristic");
        var a = AddCiv(info, "A");
        Meet(a, cityState);
        var manager = cityState.GetDiplomacyManager(a)!;
        manager.DiplomaticStatus = DiplomaticStatus.Peace;
        void Check(float influence, RelationshipLevel expected)
        {
            manager.SetInfluenceWithoutSideEffects(influence);
            Assert.Equal(expected, manager.RelationshipIgnoreAfraid());
        }
        Check(-60f, RelationshipLevel.Unforgivable);
        Check(-30f, RelationshipLevel.Unforgivable);
        Check(-29.9f, RelationshipLevel.Enemy);
        Check(-0.1f, RelationshipLevel.Enemy);
        Check(0f, RelationshipLevel.Neutral);
        Check(29.9f, RelationshipLevel.Neutral);
        Check(30f, RelationshipLevel.Friend);
        Check(60f, RelationshipLevel.Friend); // allied only when it is the city-state's ally civ
        cityState.AllyCiv = a;
        Check(60f, RelationshipLevel.Ally);
        Check(59f, RelationshipLevel.Friend);
    }

    // ---- DiplomacyFunctions

    [Fact]
    public void IsAtWarWith_SelfBarbariansUnmetAndStatus()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var barbarians = new Civilization(new NationObject { Name = Constants.Barbarians }) { GameInfo = info };
        Assert.False(a.IsAtWarWith(a));
        Assert.True(a.IsAtWarWith(barbarians));
        Assert.True(barbarians.IsAtWarWith(a));
        Assert.False(a.IsAtWarWith(b)); // not encountered yet
        Meet(a, b);
        Assert.False(a.IsAtWarWith(b)); // met in peace
        a.GetDiplomacyManager(b)!.DiplomaticStatus = DiplomaticStatus.War;
        Assert.True(a.IsAtWarWith(b));
        Assert.False(b.IsAtWarWith(a)); // each side has its own status
    }

    [Fact]
    public void CanPassThroughTiles_BordersWarAndCityStates()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var cityState = AddCiv(info, "Geneva", cityStateType: "Militaristic");
        var human = AddCiv(info, "Human", PlayerType.Human);
        var barbarians = new Civilization(new NationObject { Name = Constants.Barbarians }) { GameInfo = info };
        info.Difficulty.TurnBarbariansCanEnterPlayerTiles = 10;

        Assert.True(a.DiplomacyFunctions.CanPassThroughTiles(a));
        Assert.True(a.DiplomacyFunctions.CanPassThroughTiles(barbarians));
        Assert.False(a.DiplomacyFunctions.CanPassThroughTiles(b)); // unmet
        Meet(a, b);
        Assert.False(a.DiplomacyFunctions.CanPassThroughTiles(b)); // met in peace, closed borders
        a.GetDiplomacyManager(b)!.HasOpenBorders = true;
        Assert.True(a.DiplomacyFunctions.CanPassThroughTiles(b));
        a.GetDiplomacyManager(b)!.HasOpenBorders = false;
        a.GetDiplomacyManager(b)!.DiplomaticStatus = DiplomaticStatus.War;
        Assert.True(a.DiplomacyFunctions.CanPassThroughTiles(b)); // at war: can enter

        Assert.False(a.DiplomacyFunctions.CanPassThroughTiles(cityState)); // AI civs need open borders
        Assert.True(human.DiplomacyFunctions.CanPassThroughTiles(cityState)); // players can always pass through city-state tiles

        Assert.False(barbarians.DiplomacyFunctions.CanPassThroughTiles(a)); // turns (0) < 10
    }

    [Fact]
    public void RemoveEmbassies_RemovesTheModifiersFromBothSides()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);
        foreach (var manager in new[] { a.GetDiplomacyManager(b)!, b.GetDiplomacyManager(a)! })
            foreach (var modifier in new[] { DiplomaticModifiers.EstablishedEmbassy, DiplomaticModifiers.ReceivedEmbassy, DiplomaticModifiers.SharedEmbassies, DiplomaticModifiers.YearsOfPeace })
                manager.SetModifier(modifier, 1f);

        a.DiplomacyFunctions.RemoveEmbassies(b);

        foreach (var manager in new[] { a.GetDiplomacyManager(b)!, b.GetDiplomacyManager(a)! })
        {
            Assert.False(manager.HasModifier(DiplomaticModifiers.EstablishedEmbassy));
            Assert.False(manager.HasModifier(DiplomaticModifiers.ReceivedEmbassy));
            Assert.False(manager.HasModifier(DiplomaticModifiers.SharedEmbassies));
            Assert.True(manager.HasModifier(DiplomaticModifiers.YearsOfPeace));
        }
    }

    [Fact]
    public void CanSignDeclarationOfFriendshipWith_NeedsMajorNoWarNoDenunciationNoPriorDeclaration()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var cityState = AddCiv(info, "Geneva", cityStateType: "Militaristic");
        Meet(a, b);
        Meet(a, cityState);
        var manager = a.GetDiplomacyManager(b)!;
        Assert.True(a.DiplomacyFunctions.CanSignDeclarationOfFriendshipWith(b));
        Assert.False(a.DiplomacyFunctions.CanSignDeclarationOfFriendshipWith(cityState));
        manager.SetFlag(DiplomacyFlags.Denunciation, 30);
        Assert.False(a.DiplomacyFunctions.CanSignDeclarationOfFriendshipWith(b));
        manager.RemoveFlag(DiplomacyFlags.Denunciation);
        manager.SetFlag(DiplomacyFlags.DeclarationOfFriendship, 30);
        Assert.False(a.DiplomacyFunctions.CanSignDeclarationOfFriendshipWith(b));
        manager.RemoveFlag(DiplomacyFlags.DeclarationOfFriendship);
        b.GetDiplomacyManager(a)!.DiplomaticStatus = DiplomaticStatus.War;
        Assert.False(a.DiplomacyFunctions.CanSignDeclarationOfFriendshipWith(b));
    }

    [Fact]
    public void GetResearchAgreementCost_TakesTheMoreExpensiveEraTimesGameSpeed()
    {
        var (info, a, b, _, _) = FourCivs();
        a.Tech.Era = new() { Name = "Ancient era", ResearchAgreementCost = 300 };
        b.Tech.Era = new() { Name = "Medieval era", ResearchAgreementCost = 333 };
        info.SpeedData.GoldCostModifier = 1.5f;
        Assert.Equal(499, a.DiplomacyFunctions.GetResearchAgreementCost(b)); // 333 * 1.5 = 499.5 truncated
        Assert.Equal(499, b.DiplomacyFunctions.GetResearchAgreementCost(a));
    }

    // ---- Trades and gold

    [Fact]
    public void TurnsToPeaceTreaty_GoldPerTurn_AndClone()
    {
        var (info, a, b, _, _) = FourCivs();
        Meet(a, b);
        var manager = a.GetDiplomacyManager(b)!;
        manager.DiplomaticStatus = DiplomaticStatus.Peace;
        Assert.Equal(0, manager.TurnsToPeaceTreaty());
        Assert.True(manager.CanDeclareWar());
        Assert.True(manager.CanAttack());
        var trade = new Trade();
        trade.OurOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, info.SpeedData));
        trade.OurOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold_Per_Turn, 5, 30));
        trade.TheirOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold_Per_Turn, 12, 30));
        manager.Trades.Add(trade);
        Assert.Equal(info.SpeedData.PeaceDealDuration, manager.TurnsToPeaceTreaty());
        Assert.False(manager.CanDeclareWar());
        Assert.False(manager.CanAttack());
        Assert.Equal(12 - 5, manager.GoldPerTurn());

        manager.SetFlag(DiplomacyFlags.Denunciation, 30);
        manager.SetModifier(DiplomaticModifiers.YearsOfPeace, 3f);
        manager.SetInfluenceWithoutSideEffects(7f);
        var clone = manager.Clone();
        Assert.Equal("B", clone.OtherCivName);
        Assert.Equal(DiplomaticStatus.Peace, clone.DiplomaticStatus);
        Assert.Equal(30, clone.FlagsCountdown["Denunciation"]);
        Assert.Equal(3f, clone.DiplomaticModifiers["YearsOfPeace"]);
        Assert.Equal(7f, clone.Influence);
        Assert.NotSame(trade, Assert.Single(clone.Trades)); // trades are cloned...
        Assert.Same(trade.OurOffers[0], clone.Trades[0].OurOffers[0]); // ...but share their offers (Trade.clone)
    }

    [Fact]
    public void MakeCivilizationsMeet_CreatesPeacefulManagersWithZeroSmoothedOpinion()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);
        Assert.True(a.Knows(b));
        Assert.True(b.Knows(a));
        Assert.Equal(DiplomaticStatus.Peace, a.GetDiplomacyManager(b)!.DiplomaticStatus);
        Assert.Equal("A", b.GetDiplomacyManager(a)!.OtherCivName);
        Assert.Same(b, a.GetDiplomacyManager(b)!.OtherCiv);
        Assert.Same(a, a.GetDiplomacyManager(b)!.CivInfo);
        Assert.Equal("B", Assert.Single(a.PopupAlerts, it => it.Type == AlertType.FirstContact).Value);
        // a manager that was never met starts at war with the old-savefile smoothed opinion
        var raw = new DiplomacyManager();
        Assert.Equal(DiplomaticStatus.War, raw.DiplomaticStatus);
        Assert.Equal(DiplomacyManager.OldSavefileInitialSmoothedOpinion, raw.SmoothedOpinionOfOtherCiv);
        Assert.Throws<InvalidOperationException>(() => raw.OtherCivName);
    }

    [Fact]
    public void GiftGold_RoundTripsThroughTheDeteriorationValue()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);
        var manager = a.GetDiplomacyManager(b)!;
        var other = b.GetDiplomacyManager(a)!;
        // a gives a pure gift of 100: a's manager books a modifier on the manager that receives it (this one)
        manager.GiftGold(100, true);
        float booked = manager.GetModifier(DiplomaticModifiers.GaveUsGifts);
        double inflation = 1000.0 / (1.0 + 1.66 * 1000.0) + .4; // gold per turn 0: max(0, 1) ^ 1.2 == 1
        Assert.Equal((float)(100 * inflation / 100f), booked);
        Assert.InRange(manager.GetGoldGifts(), 99, 100);

        // a trade gift is worth 0.8 as much and first pays back what the other civ gave us
        other.GiftGold(50, false); // 40 gold, which is less than the 99-100 we got: reduces what we have
        Assert.InRange(manager.GetGoldGifts(), 59, 60);
        Assert.False(other.HasModifier(DiplomaticModifiers.GaveUsGifts));
    }

    [Fact]
    public void DeclareWar_DeclaresAPlainDirectWar()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);
        a.GetDiplomacyManager(b)!.DeclareWar(); // the war itself is covered by DiplomacyTurnTests (T068)
        Assert.True(a.IsAtWarWith(b));
    }

    // ---- Demand

    [Fact]
    public void Demand_EntriesKeepKotlinOrderAndData()
    {
        Assert.Equal(["DontSpyOnUs", "DoNotSpreadReligion", "DoNotSettleNearUs", "DoNotAttackUs"], Demand.Values.Select(it => it.ToString()));
        Assert.Equal([0, 1, 2, 3], Demand.Values.Select(it => it.Ordinal));
        Assert.Equal(DiplomacyFlags.AgreedToNotAttackUs, Demand.DoNotAttackUs.AgreedToDemand);
        Assert.Equal(DiplomacyFlags.MilitaryPresenceNearBorderOrAttackedUsDespitePromise, Demand.DoNotAttackUs.ViolationOccurred);
        Assert.Equal(DiplomaticModifiers.RefusedToNotSettleCitiesNearUs, Demand.DoNotSettleNearUs.RefusedDiplomaticModifier);
        Assert.Equal(AlertType.SpyingOnUsDespiteOurPromise, Demand.DontSpyOnUs.ViolationDiscoveredAlert);
        Assert.Equal("[civName] agreed to stop spying on us!", Demand.DontSpyOnUs.AgreedToDemandText);

        var (info, a, _, _, _) = FourCivs();
        Assert.False(Demand.DoNotAttackUs.Show(a));
        Assert.True(Demand.DoNotSettleNearUs.Show(a));
        Assert.False(Demand.DontSpyOnUs.Show(a)); // asks gameInfo.isEspionageEnabled()
        Assert.False(Demand.DoNotSpreadReligion.Show(a)); // asks gameInfo.isReligionEnabled()
        Assert.NotNull(info);
    }

    [Fact]
    public void AgreeToDemand_SetsFlagPenaltyNotificationAndPopupOnce()
    {
        var info = new FakeGameInfoState();
        var promiser = AddCiv(info, "A");
        var promisee = AddCiv(info, "B", PlayerType.Human);
        Meet(promiser, promisee);
        info.SpeedData.Modifier = 1.5f;
        var manager = promiser.GetDiplomacyManager(promisee)!;

        manager.AgreeToDemand(Demand.DoNotSettleNearUs);
        manager.AgreeToDemand(Demand.DoNotSettleNearUs);

        Assert.Equal(150, promisee.GetDiplomacyManager(promiser)!.GetFlag(DiplomacyFlags.AgreedToNotSettleNearUs)); // 100 * 1.5
        Assert.Equal(-20f, manager.GetModifier(DiplomaticModifiers.UnacceptableDemands));
        Assert.Equal(2, promisee.Notifications.Count);
        Assert.Equal("[A] agreed to stop settling cities near us!", promisee.Notifications[0].Text);
        Assert.Single(promisee.PopupAlerts, it => it.Type == AlertType.AcceptingDemand); // only queued once
    }

    [Fact]
    public void RefuseDemand_PenalisesAndIgnoresTheViolation()
    {
        var info = new FakeGameInfoState();
        var promiser = AddCiv(info, "A");
        var promisee = AddCiv(info, "B");
        Meet(promiser, promisee);
        var manager = promiser.GetDiplomacyManager(promisee)!;

        manager.RefuseDemand(Demand.DoNotSettleNearUs);

        Assert.Equal(-20f, manager.GetModifier(DiplomaticModifiers.UnacceptableDemands));
        Assert.Equal(100, promisee.GetDiplomacyManager(promiser)!.GetFlag(DiplomacyFlags.IgnoreThemSettlingNearUs));
        Assert.Equal(-15f, promisee.GetDiplomacyManager(promiser)!.GetModifier(DiplomaticModifiers.RefusedToNotSettleCitiesNearUs));
        Assert.Single(promisee.PopupAlerts, it => it.Type == AlertType.RejectingDemand);
    }

    [Fact]
    public void BecomeWary_AndSideWithCityState()
    {
        var info = new FakeGameInfoState();
        var cityState = AddCiv(info, "Geneva", cityStateType: "Militaristic");
        var a = AddCiv(info, "A", PlayerType.Human);
        Meet(a, cityState);
        var manager = cityState.GetDiplomacyManager(a)!;
        manager.BecomeWary();
        manager.BecomeWary();
        Assert.Equal(-1, manager.GetFlag(DiplomacyFlags.WaryOf));
        var notification = Assert.Single(a.Notifications, it => it.Text.StartsWith("City-States grow wary", StringComparison.Ordinal));
        Assert.Equal("City-States grow wary of your aggression. The resting point for Influence has decreased by [20] for [Geneva].", notification.Text);

        a.GetDiplomacyManager(cityState)!.SideWithCityState();
        Assert.Equal(-5f, cityState.GetDiplomacyManager(a)!.GetModifier(DiplomaticModifiers.SidedWithProtectedMinor));
        Assert.Equal(25, cityState.GetDiplomacyManager(a)!.GetFlag(DiplomacyFlags.RememberSidedWithProtectedMinor));
    }

    // ---- Trade data classes

    [Fact]
    public void TradeOffer_DurationDependsOnTypeAndSpeed()
    {
        var speed = new Speed { PeaceDealDuration = 7, DealDuration = 21 };
        Assert.Equal(-1, new TradeOffer("Gold", TradeOfferType.Gold, 50, speed).Duration); // immediate
        Assert.Equal(-1, new TradeOffer("Tech", TradeOfferType.Technology, speed).Duration);
        Assert.Equal(7, new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, speed).Duration);
        Assert.Equal(-1, new TradeOffer(Constants.PeaceTreaty, TradeOfferType.PeaceProposal, speed).Duration); // PeaceProposal is immediate
        Assert.Equal(21, new TradeOffer(Constants.OpenBorders, TradeOfferType.Agreement, speed).Duration);
        Assert.Equal(21, new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 3, speed).Duration);
        Assert.Equal(1, new TradeOffer(Constants.OpenBorders, TradeOfferType.Agreement, speed).Amount);

        var empty = new TradeOffer();
        Assert.Equal("", empty.Name);
        Assert.Equal(TradeOfferType.Gold, empty.Type);
        Assert.Equal(1, empty.Amount);
        Assert.Equal(-1, empty.Duration);
        Assert.True(empty.IsTradable());
        Assert.False(new TradeOffer("x", TradeOfferType.Gold, 0, -1).IsTradable());
    }

    [Fact]
    public void TradeOfferType_NumberTypeAndImmediacy()
    {
        Assert.Equal(TradeTypeNumberType.Gold, TradeOfferType.Gold_Per_Turn.NumberType());
        Assert.Equal(TradeTypeNumberType.Simple, TradeOfferType.Stockpiled_Resource.NumberType());
        Assert.Equal(TradeTypeNumberType.None, TradeOfferType.City.NumberType());
        Assert.False(TradeOfferType.Gold_Per_Turn.IsImmediate());
        Assert.True(TradeOfferType.Stockpiled_Resource.IsImmediate());
        Assert.False(TradeOfferType.Luxury_Resource.IsImmediate());
        Assert.False(TradeOfferType.Treaty.IsImmediate());
        Assert.False(TradeOfferType.Agreement.IsImmediate());
        Assert.True(TradeOfferType.City.IsImmediate());
        // the 13 entries of the Kotlin enum
        Assert.Equal(13, Enum.GetValues<TradeOfferType>().Length);
        foreach (var type in Enum.GetValues<TradeOfferType>()) { _ = type.NumberType(); _ = type.IsImmediate(); }
    }

    [Fact]
    public void TradeOffer_EqualityIsDataClassEqualityButTheOverloadIgnoresDuration()
    {
        var one = new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 2, 30);
        var other = new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 2, 10);
        Assert.True(one.Equals(other)); // fun equals(offer: TradeOffer): name, type, amount
        Assert.False(one.Equals((object)other)); // generated data-class equals(Any?) includes duration
        Assert.True(one.Equals((object)one.Copy()));
        Assert.Equal(one.GetHashCode(), one.Copy().GetHashCode());
        Assert.False(one.Equals(one.Copy(amount: 3)));
        Assert.False(one.Equals(one.Copy(type: TradeOfferType.Luxury_Resource)));
        var copy = one.Copy(name: "Horses", duration: 5);
        Assert.Equal(("Horses", TradeOfferType.Strategic_Resource, 2, 5), (copy.Name, copy.Type, copy.Amount, copy.Duration));
    }

    [Fact]
    public void TradeOffersList_AddMergesEquivalentOffersAndRemovesZeroAmounts()
    {
        var list = new TradeOffersList();
        Assert.True(list.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 2, 30)));
        Assert.True(list.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 3, 99)));
        Assert.True(list.Add(new TradeOffer("Iron", TradeOfferType.Luxury_Resource, 1, 30))); // other type: not equivalent
        var iron = list[0];
        Assert.Equal(2, list.Count);
        Assert.Equal(5, iron.Amount);
        Assert.Equal(30, iron.Duration); // the existing offer's duration is kept
        list.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, -5, 30));
        Assert.Equal(TradeOfferType.Luxury_Resource, Assert.Single(list).Type);
    }

    [Fact]
    public void TradeOffersList_AddRangeDoesNotMerge()
    {
        var list = new TradeOffersList();
        list.AddRange([new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 2, 30), new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 2, 30)]);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void TradeOffersList_WithoutSubtractsCopies()
    {
        var ours = new TradeOffersList();
        ours.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 4, 30));
        ours.Add(new TradeOffer("Gold", TradeOfferType.Gold, 100, -1));
        var theirs = new TradeOffersList();
        theirs.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 4, 30));
        theirs.Add(new TradeOffer("Horses", TradeOfferType.Strategic_Resource, 1, 30));

        var result = ours.Without(theirs);

        Assert.Equal(["Gold", "Horses"], result.Select(it => it.Name));
        Assert.Equal(-1, result[1].Amount);
        Assert.Equal(4, ours[0].Amount); // the sources are untouched
        Assert.Equal(1, theirs[1].Amount);
        Assert.NotSame(ours[1], result[0]);
    }

    [Fact]
    public void Trade_ReverseCloneSetAndEqualTrade()
    {
        var trade = new Trade();
        trade.OurOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, 1, 10));
        trade.TheirOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold, 50, -1));
        Assert.True(trade.IsPeaceTreaty());

        var reversed = trade.Reverse();
        Assert.False(reversed.IsPeaceTreaty());
        Assert.Equal("Gold", reversed.OurOffers[0].Name);
        Assert.Equal(Constants.PeaceTreaty, reversed.TheirOffers[0].Name);
        Assert.NotSame(trade.TheirOffers[0], reversed.OurOffers[0]); // reverse copies the offers
        Assert.True(reversed.Reverse().EqualTrade(trade));

        var clone = trade.Clone();
        Assert.Same(trade.OurOffers[0], clone.OurOffers[0]); // clone shares the offers
        Assert.True(clone.EqualTrade(trade));
        clone.OurOffers[0].Amount = 2;
        Assert.Equal(2, trade.OurOffers[0].Amount);
        Assert.True(clone.EqualTrade(trade));

        var different = new Trade();
        different.OurOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, 1, 10));
        Assert.False(different.EqualTrade(trade)); // different number of their offers
        different.TheirOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold, 51, -1));
        Assert.False(different.EqualTrade(trade));
        different.TheirOffers.Clear();
        different.TheirOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold, 50, 12)); // duration is ignored
        different.OurOffers[0].Amount = 2;
        Assert.True(different.EqualTrade(trade));

        var target = new Trade();
        target.OurOffers.Add(new TradeOffer("Old", TradeOfferType.Gold, 1, -1));
        target.Set(trade);
        Assert.True(target.EqualTrade(trade));
        Assert.Single(target.OurOffers);
    }

    [Fact]
    public void TradeRequest_DeclineSetsTheFlagsOnTheRequestingCiv()
    {
        var info = new FakeGameInfoState();
        var requester = AddCiv(info, "Requester");
        var decliner = AddCiv(info, "Decliner");
        Meet(requester, decliner);
        var manager = requester.GetDiplomacyManager(decliner)!;
        var speed = info.SpeedData;

        var trade = new Trade();
        trade.OurOffers.Add(new TradeOffer("Wine", TradeOfferType.Luxury_Resource, 1, 30));
        trade.TheirOffers.Add(new TradeOffer("Silk", TradeOfferType.Luxury_Resource, 1, 30));
        trade.OurOffers.Add(new TradeOffer(Constants.ResearchAgreement, TradeOfferType.Treaty, 1, 30));
        trade.OurOffers.Add(new TradeOffer(Constants.OpenBorders, TradeOfferType.Agreement, speed));
        trade.OurOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, speed));
        trade.TheirOffers.Add(new TradeOffer(Constants.AcceptEmbassy, TradeOfferType.Embassy, speed));
        trade.TheirOffers.Add(new TradeOffer("Rome", TradeOfferType.WarDeclaration, speed));
        trade.OurOffers.Add(new TradeOffer("Persia", TradeOfferType.WarDeclaration, speed));

        new TradeRequest("Requester", trade).Decline(decliner);

        // not all offers are luxuries, so no DeclinedLuxExchange
        Assert.False(manager.HasFlag(DiplomacyFlags.DeclinedLuxExchange));
        Assert.Equal(15 - 5, manager.GetFlag(DiplomacyFlags.DeclinedResearchAgreement)); // personality: neutral Science focus 5
        Assert.False(manager.HasFlag(DiplomacyFlags.DeclinedDefensivePact));
        Assert.Equal(5, manager.GetFlag(DiplomacyFlags.DeclinedOpenBorders)); // the decliner is an AI
        Assert.Equal(5, manager.GetFlag(DiplomacyFlags.DeclinedEmbassy));
        Assert.Equal(5, manager.GetFlag(DiplomacyFlags.DeclinedJoinWarOffer));
        Assert.Equal(5, manager.OtherCivDiplomacy().GetFlag(DiplomacyFlags.DeclinedJoinWarOffer));
        Assert.Equal(3, manager.GetFlag(DiplomacyFlags.DeclinedPeace));

        var luxuries = new Trade();
        luxuries.OurOffers.Add(new TradeOffer("Wine", TradeOfferType.Luxury_Resource, 1, 30));
        new TradeRequest("Requester", luxuries).Decline(decliner);
        Assert.Equal(5 - 2, manager.GetFlag(DiplomacyFlags.DeclinedLuxExchange)); // personality: neutral Commerce focus 5, / 2 truncated

        var human = AddCiv(info, "Human", PlayerType.Human);
        Meet(requester, human);
        var pact = new Trade();
        pact.OurOffers.Add(new TradeOffer(Constants.DefensivePact, TradeOfferType.Treaty, speed));
        pact.OurOffers.Add(new TradeOffer(Constants.OpenBorders, TradeOfferType.Agreement, speed));
        new TradeRequest("Requester", pact).Decline(human);
        Assert.Equal(10, requester.GetDiplomacyManager(human)!.GetFlag(DiplomacyFlags.DeclinedDefensivePact));
        Assert.Equal(10, requester.GetDiplomacyManager(human)!.GetFlag(DiplomacyFlags.DeclinedOpenBorders)); // a human declined
    }

    [Fact]
    public void TradeRequest_IsLateinitUntilAssigned()
    {
        var request = new TradeRequest();
        Assert.Throws<InvalidOperationException>(() => request.RequestingCiv);
        Assert.Throws<InvalidOperationException>(() => request.Trade);
        var trade = new Trade();
        var assigned = new TradeRequest("A", trade);
        Assert.Equal("A", assigned.RequestingCiv);
        Assert.Same(trade, assigned.Trade);
    }

    // ---- Resources and territory

    [Fact]
    public void ResourcesFromTrade_CountsOnlyKnownNonStockpiledResourcesPerSide()
    {
        var (info, a, b, _, _) = FourCivs();
        var iron = new Unciv.Models.Ruleset.Tile.TileResource { Name = "Iron", ResourceType = Unciv.Models.Ruleset.Tile.ResourceType.Strategic };
        info.RulesetData.TileResources.Put("Iron", iron);
        Meet(a, b);
        var manager = a.GetDiplomacyManager(b)!;
        var trade = new Trade();
        trade.OurOffers.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 2, 30));
        trade.TheirOffers.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 5, 30));
        trade.TheirOffers.Add(new TradeOffer("Unknown", TradeOfferType.Luxury_Resource, 1, 30));
        trade.TheirOffers.Add(new TradeOffer("Iron", TradeOfferType.Gold, 9, 30));
        manager.Trades.Add(trade);

        var supply = manager.ResourcesFromTrade();

        Assert.Equal(5 - 2, supply.SumBy("Iron"));
        Assert.All(supply, it => Assert.Equal("Trade", it.Origin));
    }

    // ---- T114: previously untested DiplomacyManager / DiplomacyFunctions members

    private sealed class TeleportingMovement(MapUnit unit) : UnitMovement(unit)
    {
        public int Teleports { get; set; }
        public override void TeleportToClosestMoveableTile() => Teleports++;
    }

    private static (MapUnit Unit, TeleportingMovement Movement) UnitOn(Civilization? tileOwner)
    {
        var tile = new Unciv.Logic.Map.Tiles.Tile { Position = HexCoord.Of(0, 0), BaseTerrain = "Grassland" };
        var map = new Unciv.Logic.Map.TileMap(1) { GameInfo = new FakeGameInfo() };
        map.TileList.Add(tile);
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Unciv.Models.Ruleset.Tile.Terrain { Name = "Grassland", Type = Unciv.Models.Ruleset.Tile.TerrainType.Land });
        map.SetTransients(ruleset);
        if (tileOwner != null) tile.SetOwningCity(new City { Civ = tileOwner, Name = "Owned", Location = HexCoord.Of(99, 99) });
        var unit = new MapUnit { CurrentTile = tile };
        var movement = new TeleportingMovement(unit);
        unit.Movement = movement;
        return (unit, movement);
    }

    private static void SetUniques(Civilization civ, params string[] uniques)
    {
        // Nation.UniqueMap is built once, so the test uniques live in the (clearable) policy unique map, which Civilization.GetMatchingUniques also reads
        civ.Policies.PolicyUniques.Clear();
        foreach (var text in uniques)
            civ.Policies.PolicyUniques.AddUnique(new Unciv.Models.Ruleset.Unique.Unique(text, UniqueTarget.Global, "test"));
    }

    [Fact]
    public void MakePeace_SetsPeaceOnBothSidesNotifiesCommonAcquaintancesAndTeleportsUnits()
    {
        var (_, a, b, c, d) = FourCivs();
        c.PlayerType = PlayerType.Human; // AI civs get no notifications
        d.PlayerType = PlayerType.Human;
        Meet(a, b);
        Meet(a, c);
        Meet(b, c);
        Meet(a, d); // d is not known to b
        a.GetDiplomacyManager(b)!.DiplomaticStatus = DiplomaticStatus.War;
        b.GetDiplomacyManager(a)!.DiplomaticStatus = DiplomaticStatus.War;
        var (unitOfAInBLand, proxyA) = UnitOn(b);
        var (unitOfAInOwnLand, proxyOwn) = UnitOn(a);
        var (unitOfBInALand, proxyB) = UnitOn(a);
        a.Units.AddUnit(unitOfAInBLand, false);
        a.Units.AddUnit(unitOfAInOwnLand, false);
        b.Units.AddUnit(unitOfBInALand, false);

        a.GetDiplomacyManager(b)!.MakePeace();

        Assert.Equal(DiplomaticStatus.Peace, a.GetDiplomacyManager(b)!.DiplomaticStatus);
        Assert.Equal(DiplomaticStatus.Peace, b.GetDiplomacyManager(a)!.DiplomaticStatus);
        Assert.Equal((1, 0, 1), (proxyA.Teleports, proxyOwn.Teleports, proxyB.Teleports)); // units leave the other side's territory
        Assert.Contains(c.Notifications, it => it.Text == "[A] and [B] have signed a Peace Treaty!");
        Assert.DoesNotContain(d.Notifications, it => it.Text.Contains("Peace Treaty", StringComparison.Ordinal));
    }

    [Fact]
    public void MakePeace_AllyCityStatesMakePeaceWithTreatyAndOtherCityStatesLoseInfluence()
    {
        var info = new FakeGameInfoState();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var allyState = AddCiv(info, "Geneva", cityStateType: "Militaristic");
        var otherState = AddCiv(info, "Zurich", cityStateType: "Mercantile");
        foreach (var state in new[] { allyState, otherState })
        {
            Meet(a, state);
            Meet(b, state);
            state.GetDiplomacyManager(b)!.DiplomaticStatus = DiplomaticStatus.War;
            b.GetDiplomacyManager(state)!.DiplomaticStatus = DiplomaticStatus.War;
        }
        Meet(a, b);
        allyState.AllyCiv = a;
        otherState.GetDiplomacyManager(a)!.SetInfluenceWithoutSideEffects(20f);

        a.GetDiplomacyManager(b)!.MakePeace();

        // Our ally city-state makes peace with b and gets a Peace Treaty trade on both sides
        Assert.Equal(DiplomaticStatus.Peace, allyState.GetDiplomacyManager(b)!.DiplomaticStatus);
        var treaty = Assert.Single(allyState.GetDiplomacyManager(b)!.Trades);
        Assert.Equal(Constants.PeaceTreaty, Assert.Single(treaty.OurOffers).Name);
        Assert.Single(b.GetDiplomacyManager(allyState)!.Trades);
        // The other city-state, still at war with b, likes a less
        Assert.Equal(DiplomaticStatus.War, otherState.GetDiplomacyManager(b)!.DiplomaticStatus);
        Assert.Equal(10f, otherState.GetDiplomacyManager(a)!.GetInfluence());
    }

    [Fact]
    public void UpdateHasOpenBorders_TeleportsUnitsOutWhenBordersCloseButNotWhenAnOfferOrAllianceKeepsThemOpen()
    {
        var (_, a, b, _, _) = FourCivs();
        Meet(a, b);
        var manager = a.GetDiplomacyManager(b)!;
        var (inside, insideProxy) = UnitOn(b);
        var (elsewhere, elsewhereProxy) = UnitOn(null);
        a.Units.AddUnit(inside, false);
        a.Units.AddUnit(elsewhere, false);

        manager.UpdateHasOpenBorders(); // were closed, still closed: nothing happens
        Assert.False(manager.HasOpenBorders);
        Assert.Equal(0, insideProxy.Teleports);

        var trade = new Trade();
        trade.TheirOffers.Add(new TradeOffer(Constants.OpenBorders, TradeOfferType.Agreement, 1, 30));
        manager.Trades.Add(trade);
        manager.UpdateHasOpenBorders();
        Assert.True(manager.HasOpenBorders);
        manager.UpdateHasOpenBorders(); // still open
        Assert.Equal(0, insideProxy.Teleports);

        trade.TheirOffers[0].Duration = 0; // the agreement ran out
        manager.UpdateHasOpenBorders();
        Assert.False(manager.HasOpenBorders);
        Assert.Equal(1, insideProxy.Teleports);
        Assert.Equal(0, elsewhereProxy.Teleports);

        // A city-state may always enter its ally's territory
        var info = new FakeGameInfoState();
        var cityState = AddCiv(info, "Geneva", cityStateType: "Militaristic");
        var human = AddCiv(info, "H", PlayerType.Human);
        Meet(cityState, human);
        cityState.AllyCiv = human;
        var stateManager = cityState.GetDiplomacyManager(human)!;
        stateManager.UpdateHasOpenBorders();
        Assert.True(stateManager.HasOpenBorders);
        cityState.AllyCiv = null;
        stateManager.UpdateHasOpenBorders();
        Assert.False(stateManager.HasOpenBorders);
    }

    [Fact]
    public void IsConsideredFriendlyTerritory_FriendlyCityStatesTheUniqueAndOpenBorders()
    {
        var info = new FakeGameInfoState();
        var cityState = AddCiv(info, "Geneva", cityStateType: "Militaristic");
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        Meet(cityState, a);
        Meet(a, b);
        var stateManager = cityState.GetDiplomacyManager(a)!;
        stateManager.DiplomaticStatus = DiplomaticStatus.Peace;
        Assert.False(stateManager.IsConsideredFriendlyTerritory()); // neutral, no open borders
        stateManager.SetInfluenceWithoutSideEffects(30f);
        Assert.True(stateManager.IsConsideredFriendlyTerritory()); // Friend
        stateManager.SetInfluenceWithoutSideEffects(0f);
        SetUniques(a, "City-State territory always counts as friendly territory");
        Assert.True(stateManager.IsConsideredFriendlyTerritory());

        // between major civs only the open borders of the other side count
        var manager = a.GetDiplomacyManager(b)!;
        Assert.False(manager.IsConsideredFriendlyTerritory());
        b.GetDiplomacyManager(a)!.HasOpenBorders = true;
        Assert.True(manager.IsConsideredFriendlyTerritory());
        Assert.False(b.GetDiplomacyManager(a)!.IsConsideredFriendlyTerritory());
    }

    [Fact]
    public void CityStateInfluence_RestingPointDegradeAndTurnsToRelationshipChange()
    {
        var info = new FakeGameInfoState();
        var cityState = AddCiv(info, "Geneva", cityStateType: "Militaristic");
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        Meet(cityState, a);
        var manager = cityState.GetDiplomacyManager(a)!;
        manager.DiplomaticStatus = DiplomaticStatus.Peace;

        Assert.Equal(0f, manager.GetCityStateInfluenceRestingPoint());
        manager.SetInfluenceWithoutSideEffects(0f);
        Assert.Equal(0f, manager.GetCityStateInfluenceDegrade()); // at the resting point: no degrade

        SetUniques(a, "Resting point for Influence with City-States is increased by [10]", "Resting point for Influence with City-States is increased by [5]");
        manager.DiplomaticStatus = DiplomaticStatus.Protector;
        manager.SetFlag(DiplomacyFlags.WaryOf, 10);
        Assert.Equal(10f + 5f + 10f - 20f, manager.GetCityStateInfluenceRestingPoint());
        manager.RemoveFlag(DiplomacyFlags.WaryOf);
        manager.DiplomaticStatus = DiplomaticStatus.Peace;
        Assert.Equal(15f, manager.GetCityStateInfluenceRestingPoint());

        // religion based resting point: needs a capital whose majority religion is a's religion
        var religion = new Religion("Faith", info, a, 1);
        a.ReligionManager.Religion = religion;
        SetUniques(a, "Resting point for Influence with City-States following this religion [7]");
        Assert.Equal(0f, manager.GetCityStateInfluenceRestingPoint()); // no city
        var capital = new FakeCityState("cap", new FakeCiv("Geneva")) { Capital = true };
        capital.ReligionData.Majority = religion;
        cityState.Cities = [capital];
        Assert.Equal(7f, manager.GetCityStateInfluenceRestingPoint());
        capital.ReligionData.Majority = null;
        Assert.Equal(0f, manager.GetCityStateInfluenceRestingPoint());
        capital.ReligionData.Majority = religion;

        // degrade: 1 per turn; hostile city-states 1.5; aggressors 2; the unique percentages add; same religion -25%; others' uniques add
        SetUniques(a);
        manager.SetInfluenceWithoutSideEffects(10f);
        capital.ReligionData.Majority = null;
        Assert.Equal(1f, manager.GetCityStateInfluenceDegrade());
        cityState.CityStatePersonality = CityStatePersonality.Hostile;
        Assert.Equal(1.5f, manager.GetCityStateInfluenceDegrade());
        cityState.CityStatePersonality = CityStatePersonality.Neutral;
        a.NumMinorCivsAttacked = 2;
        Assert.Equal(2f, manager.GetCityStateInfluenceDegrade());
        a.NumMinorCivsAttacked = 0;
        SetUniques(a, "[50]% City-State Influence degradation");
        Assert.Equal(1.5f, manager.GetCityStateInfluenceDegrade());
        SetUniques(a, "[-300]% City-State Influence degradation");
        Assert.Equal(0f, manager.GetCityStateInfluenceDegrade()); // clamped at -100%
        SetUniques(a);
        SetUniques(b, "Influence of all other civilizations with all city-states degrades [100]% faster");
        Assert.Equal(2f, manager.GetCityStateInfluenceDegrade());
        SetUniques(b);
        a.ReligionManager.Religion = religion;
        SetUniques(a, "Resting point for Influence with City-States following this religion [0]");
        capital.ReligionData.Majority = religion;
        Assert.Equal(0.75f, manager.GetCityStateInfluenceDegrade());

        // turns to relationship change: Ally at 60, Friend at 30; asked from either side, only ever relevant for city-state vs major civ
        SetUniques(a);
        capital.ReligionData.Majority = null;
        manager.SetInfluenceWithoutSideEffects(70f);
        cityState.AllyCiv = a;
        Assert.Equal(11, manager.GetTurnsToRelationshipChange()); // ceil((70 - 60) / 1) + 1
        Assert.Equal(11, a.GetDiplomacyManager(cityState)!.GetTurnsToRelationshipChange());
        cityState.AllyCiv = null;
        manager.SetInfluenceWithoutSideEffects(35.5f);
        Assert.Equal(7, manager.GetTurnsToRelationshipChange()); // Friend: ceil(5.5) + 1
        manager.SetInfluenceWithoutSideEffects(10f);
        Assert.Equal(0, manager.GetTurnsToRelationshipChange()); // neither Ally nor Friend
        manager.SetInfluenceWithoutSideEffects(0f);
        Assert.Equal(0, manager.GetTurnsToRelationshipChange());
    }

    [Fact]
    public void SetReligionBasedModifier_AddsAndRemovesBelieveSameReligion()
    {
        var (info, a, b, _, _) = FourCivs();
        Meet(a, b);
        foreach (var civ in new[] { a, b }) civ.ReligionManager.CivInfo = civ;
        var religion = new Religion("Faith", info, a, 1);
        var manager = a.GetDiplomacyManager(b)!;
        manager.SetReligionBasedModifier();
        Assert.False(manager.HasModifier(DiplomaticModifiers.BelieveSameReligion)); // nobody has a majority religion

        foreach (var civ in new[] { a, b })
            civ.Cities = [new FakeCityState(civ.CivId + "City", new FakeCiv(civ.CivId)) { ReligionData = { Majority = religion } }];
        manager.SetReligionBasedModifier();
        Assert.Equal(5f, manager.GetModifier(DiplomaticModifiers.BelieveSameReligion));

        b.Cities = [new FakeCityState("other", new FakeCiv("B"))]; // b's city follows nothing
        manager.SetReligionBasedModifier();
        Assert.False(manager.HasModifier(DiplomaticModifiers.BelieveSameReligion));
    }

    [Fact]
    public void CanSignEmbassyAndResearchAndDefensivePactChecks()
    {
        var (info, a, b, _, _) = FourCivs();
        Meet(a, b);
        var ab = a.GetDiplomacyManager(b)!;
        var ba = b.GetDiplomacyManager(a)!;
        a.Tech.Era = new() { Name = "Ancient era", ResearchAgreementCost = 100 };
        b.Tech.Era = new() { Name = "Ancient era", ResearchAgreementCost = 100 };
        a.Cities = [new FakeCityState("a", new FakeCiv("A")) { Capital = true }];
        b.Cities = [new FakeCityState("b", new FakeCiv("B")) { Capital = true }];

        // embassies
        Assert.False(a.DiplomacyFunctions.CanOfferEmbassyTo(b)); // needs the unique
        Assert.False(a.DiplomacyFunctions.CanEstablishEmbassyWith(b));
        SetUniques(a, "Enables establishment of embassies");
        Assert.True(a.DiplomacyFunctions.CanOfferEmbassyTo(b));
        Assert.True(a.DiplomacyFunctions.CanEstablishEmbassyWith(b));
        ba.SetModifier(DiplomaticModifiers.EstablishedEmbassy, 1f);
        Assert.False(a.DiplomacyFunctions.CanOfferEmbassyTo(b)); // they already hold our embassy
        ab.SetModifier(DiplomaticModifiers.SharedEmbassies, 1f);
        Assert.False(a.DiplomacyFunctions.CanEstablishEmbassyWith(b));
        ba.RemoveModifier(DiplomaticModifiers.EstablishedEmbassy);
        ab.RemoveModifier(DiplomaticModifiers.SharedEmbassies);
        ab.SetFlag(DiplomacyFlags.Denunciation, 30);
        Assert.False(a.DiplomacyFunctions.CanEstablishEmbassyWith(b)); // denounced this turn (30 turns left) by either side
        Assert.False(a.DiplomacyFunctions.CanOfferEmbassyTo(b));
        ab.SetFlag(DiplomacyFlags.Denunciation, 29);
        Assert.True(a.DiplomacyFunctions.CanEstablishEmbassyWith(b)); // only the turn of the denunciation blocks
        ab.RemoveFlag(DiplomacyFlags.Denunciation);
        ba.SetFlag(DiplomacyFlags.Denunciation, 30);
        Assert.False(a.DiplomacyFunctions.CanEstablishEmbassyWith(b));
        ba.RemoveFlag(DiplomacyFlags.Denunciation);
        ab.DiplomaticStatus = DiplomaticStatus.War;
        Assert.False(a.DiplomacyFunctions.CanEstablishEmbassyWith(b));
        ab.DiplomaticStatus = DiplomaticStatus.Peace;
        b.Cities = [];
        Assert.False(a.DiplomacyFunctions.CanEstablishEmbassyWith(b)); // no capital to put it in
        var cityState = AddCiv(info, "Geneva", cityStateType: "Militaristic");
        Meet(a, cityState);
        Assert.False(a.DiplomacyFunctions.CanOfferEmbassyTo(cityState)); // major civs only

        // embassy requirement
        Assert.True(a.DiplomacyFunctions.MeetsEmbassyRequirementFor(b));
        SetUniques(a, "Requires establishing embassies to conduct advanced diplomacy");
        Assert.False(a.DiplomacyFunctions.MeetsEmbassyRequirementFor(b));
        ab.SetModifier(DiplomaticModifiers.SharedEmbassies, 1f);
        Assert.True(a.DiplomacyFunctions.MeetsEmbassyRequirementFor(b));
        ab.RemoveModifier(DiplomaticModifiers.SharedEmbassies);

        // research agreements
        SetUniques(a, "Enables Research agreements");
        SetUniques(b, "Enables Research agreements");
        ab.SetFlag(DiplomacyFlags.DeclarationOfFriendship, 30);
        Assert.True(a.DiplomacyFunctions.CanSignResearchAgreement());
        Assert.True(a.DiplomacyFunctions.CanSignResearchAgreementNoCostWith(b));
        Assert.False(a.DiplomacyFunctions.CanSignResearchAgreementWith(b)); // no gold
        a.AddGold(100);
        Assert.False(a.DiplomacyFunctions.CanSignResearchAgreementWith(b)); // b is short
        b.AddGold(100);
        Assert.True(a.DiplomacyFunctions.CanSignResearchAgreementWith(b));
        ba.SetFlag(DiplomacyFlags.ResearchAgreement, 10);
        Assert.False(a.DiplomacyFunctions.CanSignResearchAgreementNoCostWith(b)); // already signed (checked on the other side too)
        ba.RemoveFlag(DiplomacyFlags.ResearchAgreement);
        SetUniques(b);
        Assert.False(a.DiplomacyFunctions.CanSignResearchAgreementNoCostWith(b));
        Assert.False(b.DiplomacyFunctions.CanSignResearchAgreement());
        Assert.False(a.DiplomacyFunctions.CanSignResearchAgreement() && cityState.DiplomacyFunctions.CanSignResearchAgreement());

        // defensive pacts
        SetUniques(a, "Enables Defensive Pacts");
        SetUniques(b, "Enables Defensive Pacts");
        Assert.True(a.DiplomacyFunctions.CanSignDefensivePact());
        Assert.True(a.DiplomacyFunctions.CanSignDefensivePactWith(b));
        ab.SetFlag(DiplomacyFlags.DefensivePact, 10);
        Assert.False(a.DiplomacyFunctions.CanSignDefensivePactWith(b));
        ab.RemoveFlag(DiplomacyFlags.DefensivePact);
        ab.DiplomaticStatus = DiplomaticStatus.DefensivePact;
        Assert.False(a.DiplomacyFunctions.CanSignDefensivePactWith(b));
        ab.DiplomaticStatus = DiplomaticStatus.Peace;
        ab.RemoveFlag(DiplomacyFlags.DeclarationOfFriendship);
        Assert.False(a.DiplomacyFunctions.CanSignDefensivePactWith(b)); // needs a declaration of friendship
        SetUniques(b);
        Assert.False(b.DiplomacyFunctions.CanSignDefensivePact());
        Assert.False(cityState.DiplomacyFunctions.CanSignDefensivePact());
    }

    [Fact]
    public void GetKnownCivsSorted_ForwardsTheFlagsAndExcludesSelfAndUnmetCivs()
    {
        var (info, a, b, c, _) = FourCivs();
        Meet(a, b);
        Meet(b, c);
        Assert.Equal([b], a.DiplomacyFunctions.GetKnownCivsSorted());
        Assert.Equal((true, false, null), info.CivsSortedCalls[^1]);
        Assert.Equal([a, c], b.DiplomacyFunctions.GetKnownCivsSorted(includeCityStates: false, includeDefeated: true));
        Assert.Equal((false, true, null), info.CivsSortedCalls[^1]);
    }

    [Fact]
    public void TradeOffer_HashCodeIsAFixedCombineOfTheFields()
    {
        // Kotlin's data-class hash: ((name.hashCode() * 31 + type) * 31 + amount) * 31 + duration, with Java's String.hashCode
        var offer = new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 2, 30);
        var expected = ((("Iron".Aggregate(0, (h, ch) => 31 * h + ch) * 31 + (int)TradeOfferType.Strategic_Resource) * 31 + 2) * 31) + 30;
        Assert.Equal(expected, offer.GetHashCode());
        Assert.NotEqual(offer.GetHashCode(), offer.Copy(duration: 31).GetHashCode());
    }

    [Fact]
    public void TradeOffersList_AddHidesListAddSoThatOnlyThroughTheTypeItMerges()
    {
        var list = new TradeOffersList();
        IList<TradeOffer> asList = list;
        asList.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 2, 30));
        asList.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 3, 30));
        Assert.Equal(2, list.Count); // IList.Add does not merge: save loading must go through TradeOffersList.Add
        list.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 1, 30));
        Assert.Equal(3, list[0].Amount);
    }
}
