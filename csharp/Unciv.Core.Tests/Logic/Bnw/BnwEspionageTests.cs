using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Empire;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Tourism;
using Unciv.Logic.WorldCongress;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.StatTypes;
using Unciv.Queries;
using Unciv.Queries.Empire;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Bnw;

// B020 (bnw-spec/SPEC.md 4.9): diplomats, the coup level table, the spy rank bonus from influence and the espionage policies, all on the REAL generated
// Brave New World ruleset; the G&K cases at the end prove that nothing changed there. A, B and C are majors with a capital each (A knows Printing Press), S is a
// city-state; everyone has met everyone. Diplomat/tourism/votes numbers are the hand-computed ones of SPEC 4.3.2, 4.7.3 and 4.9a.
[Collection("RulesetCache")]
public sealed class BnwEspionageTests
{
    private readonly TestGame game;
    private readonly CivilizationObject a, b, c, s;
    private readonly CityObject capitalA, capitalB, capitalC, capitalS;
    private GameInfo G => game.GameInfo;

    public BnwEspionageTests() : this(BaseRuleset.Civ_V_BNW)
    {
    }

    private BnwEspionageTests(BaseRuleset baseRuleset)
    {
        game = new TestGame(baseRuleset);
        game.MakeHexagonalMap(8);
        a = game.AddCiv(isPlayer: true);
        b = game.AddCiv(isPlayer: true);
        c = game.AddCiv();
        s = game.AddCiv(cityStateType: "Maritime");
        capitalA = game.AddCity(a, game.GetTile(-5, 0));
        capitalB = game.AddCity(b, game.GetTile(0, 0));
        capitalC = game.AddCity(c, game.GetTile(5, 0));
        capitalS = game.AddCity(s, game.GetTile(0, -5));
        foreach (var civ in new[] { a, b, c, s })
            foreach (var other in new[] { a, b, c, s })
                if (civ != other) civ.DiplomacyFunctions.MakeCivilizationsMeet(other);
        G.GameParameters.EspionageEnabled = true;
        G.EngineOptions.VisibleMap = true;
        a.Tech.AddTechnology("Printing Press");
        a.Tech.Era = game.Ruleset.Eras["Modern era"];
    }

    private static BnwEspionageTests GnK() => new(BaseRuleset.Civ_V_GnK);

    private Spy Diplomat(CivilizationObject civ, CityObject city, SpyAction setUp = SpyAction.Surveillance)
    {
        var spy = civ.EspionageManager.AddSpy();
        spy.MoveTo(city, asDiplomat: true);
        spy.SetAction(setUp);
        return spy;
    }

    private static void FollowIdeology(CivilizationObject civ, string ideology, string tenet)
    {
        civ.Tech.Era = civ.GameInfo.Ruleset.Eras["Modern era"];
        civ.Policies.ChooseIdeology(civ.GameInfo.Ruleset.PolicyBranches[ideology]);
        civ.Policies.FreeTenets = 100;
        civ.Policies.Adopt(civ.GameInfo.Ruleset.Policies[tenet]);
    }

    // ---- who may be a diplomat

    [Fact]
    public void ADiplomatNeedsTheCapitalOfAnotherMajorCivInAWorldCongressRuleset()
    {
        var spy = a.EspionageManager.AddSpy();
        Assert.True(spy.CanBeDiplomatIn(capitalB));
        Assert.False(spy.CanBeDiplomatIn(capitalA)); // own capital
        Assert.False(spy.CanBeDiplomatIn(capitalS)); // a city-state's capital
        var secondCity = game.AddCity(b, game.GetTile(2, 2));
        Assert.False(spy.CanBeDiplomatIn(secondCity)); // not a capital

        spy.MoveTo(capitalB, asDiplomat: true);
        Assert.True(spy.IsDiplomat);
        spy.MoveTo(secondCity, asDiplomat: true); // the request is ignored where it is not allowed
        Assert.False(spy.IsDiplomat);
        spy.MoveTo(capitalB, asDiplomat: true);
        spy.MoveTo(null);
        Assert.False(spy.IsDiplomat);
        Assert.False(spy.Clone().IsDiplomat);
        spy.MoveTo(capitalB, asDiplomat: true);
        Assert.True(spy.Clone().IsDiplomat);
    }

    [Fact]
    public void TheMoveSpyCommandDesignatesADiplomatAndRejectsTheOthers()
    {
        var spy = a.EspionageManager.AddSpy();
        var session = new GameSession(G);
        G.CurrentPlayerCiv = a;
        G.CurrentPlayer = a.CivId;
        CommandResult Run(MoveSpy command) => session.Submit(a.CivId, command);
        Assert.False(Run(new MoveSpy { Spy = spy.Name, CityId = capitalS.Id, Diplomat = true }).Accepted); // a city-state
        Assert.False(Run(new MoveSpy { Spy = spy.Name, CityId = null, Diplomat = true }).Accepted); // the hideout
        Assert.True(Run(new MoveSpy { Spy = spy.Name, CityId = capitalB.Id, Diplomat = true }).Accepted);
        Assert.True(spy.IsDiplomat);
        Assert.False(Run(new MoveSpy { Spy = spy.Name, CityId = capitalB.Id, Diplomat = true }).Accepted); // already there as a diplomat
        Assert.True(Run(new MoveSpy { Spy = spy.Name, CityId = capitalB.Id }).Accepted); // the same city as a plain spy
        Assert.False(spy.IsDiplomat);
    }

    [Fact]
    public void TheDiplomatFlagSurvivesSavingAndAnOlderSaveLoadsAsFalse()
    {
        var spy = Diplomat(a, capitalB);
        string json = JsonSerializer.Serialize(a.EspionageManager, UncivJson.DefaultOptions);
        Assert.True(JsonSerializer.Deserialize<EspionageManager>(json, UncivJson.DefaultOptions)!.SpyList.Single(it => it.Name == spy.Name).IsDiplomat);
        var old = JsonSerializer.Deserialize<EspionageManager>(json.Replace("\"isDiplomat\":true", "\"x\":1"), UncivJson.DefaultOptions)!;
        Assert.False(old.SpyList.Single(it => it.Name == spy.Name).IsDiplomat);
        Assert.True(spy.IsDiplomat);
    }

    [Fact]
    public void ADiplomatWhoseCityStopsBeingACapitalBecomesAPlainSpyAndMayThenSteal()
    {
        b.Tech.AddTechnology("Pottery");
        capitalB.CityStats.CurrentCityStats[Stat.Science] = 10f;
        var spy = Diplomat(a, capitalB);
        capitalB.CityConstructions.RemoveBuilding("Palace");
        spy.EndTurn();
        Assert.False(spy.IsDiplomat);
        Assert.Contains(a.Notifications, it => it.Text.Contains("no longer in a capital"));
        Assert.Equal(SpyAction.StealingTech, spy.Action);
        Assert.Equal(0, a.EspionageManager.CountDiplomats());
    }

    [Fact]
    public void ADiplomatOnlyObservesAndNeverStealsTechnology()
    {
        b.Tech.AddTechnology("Pottery"); // a tech A could steal
        capitalB.CityStats.CurrentCityStats[Stat.Science] = 10f;
        var diplomat = a.EspionageManager.AddSpy();
        diplomat.MoveTo(capitalB, asDiplomat: true);
        for (int turn = 0; turn < 3 + 1 + 10; turn++) diplomat.EndTurn(); // moving (1), establishing a network (3), then many turns
        Assert.Equal(SpyAction.Surveillance, diplomat.Action);
        Assert.True(diplomat.IsAlive());
        Assert.False(a.Tech.IsResearched("Pottery"));

        var plain = a.EspionageManager.AddSpy(); // the control: a plain spy in the same capital would start stealing
        capitalB.Espionage.RemoveAllPresentSpies(Unciv.Logic.City.SpyFleeReason.Other);
        plain.MoveTo(capitalB);
        plain.EndTurn();
        plain.EndTurn();
        plain.EndTurn();
        plain.EndTurn();
        Assert.Equal(SpyAction.StealingTech, plain.Action);
    }

    // ---- what a diplomat gives

    [Fact]
    public void ADiplomatAddsTheTourismBonusOnlyAgainstADifferentIdeology()
    {
        Diplomat(a, capitalB);
        int none = TourismRules.ModifierWith(a, b);
        a.Policies.Ideology = "Order";
        Assert.Equal(none, TourismRules.ModifierWith(a, b)); // b has no ideology: no bonus
        b.Policies.Ideology = "Order";
        Assert.Equal(none, TourismRules.ModifierWith(a, b)); // the same ideology: no bonus
        b.Policies.Ideology = "Freedom";
        int different = game.Ruleset.ModOptions.Constants.TourismModifierDifferentIdeologies + game.Ruleset.ModOptions.Constants.TourismModifierDiplomat;
        Assert.Equal(none + different, TourismRules.ModifierWith(a, b)); // -34 + 25
        c.Policies.Ideology = "Freedom";
        Assert.Equal(none + different - 25, TourismRules.ModifierWith(a, c)); // a diplomat in b does not count for c
    }

    [Fact]
    public void ADiplomatCountsOnlyOnceItIsSetUpInAMajorCapital()
    {
        var spy = Diplomat(a, capitalB, SpyAction.Moving);
        Assert.Equal(0, a.EspionageManager.CountDiplomats());
        spy.SetAction(SpyAction.Surveillance);
        Assert.Equal(1, a.EspionageManager.CountDiplomats());
        Assert.True(a.EspionageManager.HasDiplomatIn(b));
        Assert.False(a.EspionageManager.HasDiplomatIn(c));
        Assert.False(b.EspionageManager.HasDiplomatIn(a));
    }

    [Fact]
    public void EachDiplomatAddsTheVotesPerDiplomatOfGlobalizationToTheDelegates()
    {
        var league = new League { Host = b.CivId };
        league.Members.Add(new LeagueMember { Civ = a.CivId });
        G.League = league;
        int before = LeagueRules.StartingVotes(G, league, a);
        int beforeB = LeagueRules.StartingVotes(G, league, b);
        Diplomat(a, capitalB);
        Assert.Equal(before, LeagueRules.StartingVotes(G, league, a)); // without Globalization the diplomat gives nothing
        a.Tech.AddTechnology("Globalization");
        a.Cache.UpdateCivResources();
        Assert.Equal(before + 1, LeagueRules.StartingVotes(G, league, a));
        Diplomat(a, capitalC);
        Assert.Equal(before + 2, LeagueRules.StartingVotes(G, league, a));
        Assert.Equal(beforeB, LeagueRules.StartingVotes(G, league, b)); // B gets nothing from A's diplomats
    }

    [Fact]
    public void ADiplomatShowsTheVotesOfTheCivWhoseCapitalItIsIn()
    {
        var league = new League { Host = b.CivId, InSession = true };
        league.Members.Add(new LeagueMember { Civ = a.CivId, Delegates = 2 });
        league.Members.Add(new LeagueMember { Civ = b.CivId, Delegates = 3 });
        league.Members.Add(new LeagueMember { Civ = c.CivId, Delegates = 4 });
        var proposal = new LeagueProposal { Id = 1, Resolution = "Standing Army Tax" };
        proposal.Votes.Add(new LeagueVote { Civ = a.CivId, Choice = LeagueRules.Yes, Count = 2 });
        proposal.Votes.Add(new LeagueVote { Civ = b.CivId, Choice = LeagueRules.No, Count = 3 });
        proposal.Votes.Add(new LeagueVote { Civ = c.CivId, Choice = LeagueRules.No, Count = 4 });
        league.Proposals.Add(proposal);
        G.League = league;
        var registry = new QueryRegistry(UncivJson.DefaultOptions);
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        LeagueProposalRow Row()
        {
            var response = registry.Execute(registry.ToEnvelope(a.CivName, new LeagueQuery()), new QueryContext(G));
            Assert.True(response.Ok, response.Message);
            return JsonSerializer.Deserialize<LeagueResult>(response.Result!.Value.GetRawText(), UncivJson.DefaultOptions)!.Proposals.Single();
        }

        Assert.Equal([KeyValuePair.Create(LeagueRules.Yes, 2)], Row().Tally);
        Diplomat(a, capitalB);
        Assert.Equal(KeyValuePair.Create(LeagueRules.No, 3), Assert.Single(Row().Tally, it => it.Key == LeagueRules.No)); // B's votes show, C's do not
        Assert.Equal([KeyValuePair.Create(LeagueRules.Yes, 2)], Row().YourVotes);
    }

    // ---- the spy rank bonus from influence

    [Fact]
    public void ExoticInfluenceOverAMajorCivAddsASpyRankThereButNeverBeyondTheMaximum()
    {
        int max = game.Ruleset.ModOptions.Constants.MaxSpyRank;
        var spy = a.EspionageManager.AddSpy();
        spy.MoveTo(capitalB);
        Assert.Equal(1, spy.GetEffectiveRank());
        b.TotalCultureForContests = 1000;
        (a.Culture ??= new CivCultureManager()).GetOrAdd(b.CivId).Influence = 99; // 9%: below Exotic (10%)
        Assert.Equal(1, spy.GetEffectiveRank());
        a.Culture.GetOrAdd(b.CivId).Influence = 100; // exactly Exotic
        Assert.Equal(2, spy.GetEffectiveRank());
        Assert.Equal(1, spy.Rank); // the stored rank is unchanged
        spy.LevelUpSpy(max);
        Assert.Equal(max, spy.GetEffectiveRank()); // clamped
        var home = a.EspionageManager.AddSpy();
        home.MoveTo(capitalA);
        Assert.Equal(1, home.GetEffectiveRank()); // not in a foreign city
    }

    // ---- coups

    private Spy CoupSpy(int rank)
    {
        var spy = a.EspionageManager.AddSpy();
        spy.LevelUpSpy(rank - 1);
        spy.MoveTo(capitalS);
        spy.SetAction(SpyAction.Surveillance);
        return spy;
    }

    // No ally, no influence: 50 - 60 / 2 = 20 percent before the ranks.
    [Theory]
    [InlineData(1, 35f)] // the Kotlin 15 per rank, and table entry 1.5 gives 15 as well
    [InlineData(2, 42.5f)] // BNW 2.25 gives 22.5 (Kotlin 30)
    [InlineData(3, 46f)] // BNW 2.6 gives 26 (Kotlin 45)
    public void TheCoupChanceFollowsTheBnwLevelTable(int rank, float percent)
    {
        Assert.Equal(percent / 100f, CoupSpy(rank).GetCoupChanceOfSuccess(false), 4);
    }

    [Fact]
    public void ACoupLevelDifferenceBeyondTheTableCountsAsFourAndNegativeDifferencesSubtract()
    {
        var constants = game.Ruleset.ModOptions.Constants;
        constants.MaxSpyRank = 6; // so that rank 5 and 6 exist
        Assert.Equal((20f + 28f) / 100f, CoupSpy(5).GetCoupChanceOfSuccess(false), 4); // 150 percent over zero ranks: difference 5 counts as 4 (2.8)
        constants.SpyRankSkillPercentBonus = 0; // a ruleset without rank skill: no division by zero, no bonus
        Assert.Equal(20f / 100f, CoupSpy(1).GetCoupChanceOfSuccess(false), 4);
        constants.SpyRankSkillPercentBonus = 30;
        constants.EspionageCoupSpyLevelDeltaFour = 0f; // an incomplete table is ignored: the linear Kotlin rule (rank 3 is 45 percent)
        Assert.Equal(65f / 100f, CoupSpy(3).GetCoupChanceOfSuccess(false), 4);
        constants.EspionageCoupSpyLevelDeltaFour = 2.8f;
        // the defender (the city-state's ally) is better: a negative difference lowers the chance
        var defender = b.EspionageManager.AddSpy();
        defender.LevelUpSpy(2); // rank 3: 90 percent
        defender.MoveTo(capitalS);
        s.GetDiplomacyManager(b)!.SetInfluence(100f);
        s.CityStateFunctions.UpdateAllyCivForCityState();
        var weak = CoupSpy(1);
        // influence difference 100 - 0: 50 - 50 = 0; skill 30 - 90 = -60: two ranks, -22.5, clamped at 0
        Assert.Equal(0f, weak.GetCoupChanceOfSuccess(true));
        Assert.Equal(0.15f, weak.GetCoupChanceOfSuccess(false), 4); // without the unknown defender it is the plain +15
        s.GetDiplomacyManager(b)!.SetInfluence(70f);
        s.GetDiplomacyManager(a)!.SetInfluence(40f); // 70 - 40 = 30: 50 - 15 = 35 percent
        Assert.Equal((35f - 22.5f) / 100f, weak.GetCoupChanceOfSuccess(true), 4);
    }

    [Fact]
    public void ASuccessfulCoupDropsTheInfluenceOfTheOtherCivsByTheBnwConstant()
    {
        Assert.Equal(20, DropOnCoup(this));
        Assert.Equal(10, DropOnCoup(GnK()));
    }

    private static int DropOnCoup(BnwEspionageTests w)
    {
        var spy = w.CoupSpy(3);
        Assert.Equal(SpyAction.Surveillance, spy.Action);
        w.s.GetDiplomacyManager(w.c)!.SetInfluence(50f);
        for (int turn = 0; turn < 200; turn++)
        {
            w.G.Turns = turn;
            spy.SetAction(SpyAction.Coup, 1);
            spy.EndTurn();
            if (spy.IsAlive()) break; // succeeded (a failed coup kills the spy)
            spy.EndTurn(); // dead: five turns to revive
            while (!spy.IsAlive()) spy.EndTurn();
            spy.MoveTo(w.capitalS);
            spy.SetAction(SpyAction.Surveillance);
            w.s.GetDiplomacyManager(w.a)!.SetInfluence(0f);
        }
        Assert.True(spy.IsAlive());
        return (int)(50f - w.s.GetDiplomacyManager(w.c)!.Influence);
    }

    // ---- the espionage policies

    private int TurnsToStealWith(CivilizationObject thief, CivilizationObject victim, CityObject victimCapital)
    {
        victim.Tech.AddTechnology("Pottery");
        victimCapital.CityStats.CurrentCityStats[Stat.Science] = 10f;
        var spy = thief.EspionageManager.AddSpy();
        spy.MoveTo(victimCapital);
        spy.SetAction(SpyAction.StealingTech);
        spy.EndTurn();
        return spy.TurnsRemainingForAction;
    }

    [Fact]
    public void IndustrialEspionageDoublesTheTechnologyStealingProgress()
    {
        int slow = TurnsToStealWith(a, b, capitalB);
        float cost = game.Ruleset.Technologies["Pottery"].Cost * 1.25f * G.Speed.ScienceCostModifier;
        Assert.Equal((int)MathF.Ceiling((cost - 10) / 10f), slow); // 10 science, rank 1: (25 + 75) percent, efficiency 1
        FollowIdeology(c, "Autocracy", "Industrial Espionage");
        int fast = TurnsToStealWith(c, b, capitalB);
        Assert.Equal((int)MathF.Ceiling((cost - 20) / 20f), fast);
        Assert.True(fast < slow);
    }

    [Fact]
    public void CovertActionMakesTheRiggingSpyWinTheElectionsMoreOften()
    {
        s.GetDiplomacyManager(a)!.SetInfluence(0f);
        int Wins()
        {
            int wins = 0;
            var spy = a.EspionageManager.SpyList.Count > 0 ? a.EspionageManager.SpyList[0] : a.EspionageManager.AddSpy();
            spy.MoveTo(capitalS);
            spy.SetAction(SpyAction.RiggingElections, 5);
            for (int turn = 0; turn < 30; turn++)
            {
                G.Turns = turn;
                float before = s.GetDiplomacyManager(a)!.Influence;
                s.CityStateFunctions.HoldElections();
                if (s.GetDiplomacyManager(a)!.Influence > before) wins++;
                s.GetDiplomacyManager(a)!.SetInfluence(0f);
            }
            return wins;
        }
        int without = Wins();
        FollowIdeology(a, "Freedom", "Covert Action");
        int with = Wins();
        Assert.True(with > without, $"{with} vs {without}"); // votes 30 + 0 against the neutral 20 become 60: win chance 60 percent to 75 percent
    }

    [Fact]
    public void DoubleAgentsMakeTheDefendingSpyBetterAgainstACoup()
    {
        var defender = b.EspionageManager.AddSpy();
        defender.MoveTo(capitalS);
        s.GetDiplomacyManager(b)!.SetInfluence(100f);
        s.CityStateFunctions.UpdateAllyCivForCityState();
        Assert.Same(b, s.AllyCiv);
        var attacker = CoupSpy(3);
        // the attacker is rank 3 (90), the ally's spy rank 1 (30): (90 - 30) = two ranks, 22.5; influence 100 - 0: 50 - 50
        float plain = attacker.GetCoupChanceOfSuccess(true);
        Assert.Equal(22.5f / 100f, plain, 4);
        FollowIdeology(b, "Order", "Double Agents");
        // the defender counts double: 90 - 60 = 30: one rank, 15
        Assert.Equal(15f / 100f, attacker.GetCoupChanceOfSuccess(true), 4);
    }

    // ---- G&K is unchanged

    [Fact]
    public void GnKHasNoDiplomatsAndKeepsTheLinearCoupChanceAndTheTenPercentDrop()
    {
        var w = GnK();
        var spy = w.a.EspionageManager.AddSpy();
        Assert.False(spy.CanBeDiplomatIn(w.capitalB)); // no World Congress
        spy.MoveTo(w.capitalB, asDiplomat: true);
        Assert.False(spy.IsDiplomat);
        Assert.Equal(0, w.a.EspionageManager.CountDiplomats());
        Assert.Equal(65f / 100f, w.CoupSpy(3).GetCoupChanceOfSuccess(false), 4); // 20 + 45
        Assert.Equal(1, w.CoupSpy(1).GetEffectiveRank()); // no influence bonus without tourism
    }
}
