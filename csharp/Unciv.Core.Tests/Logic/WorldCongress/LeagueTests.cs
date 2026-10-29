using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Commands.WorldCongress;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Files;
using Unciv.Logic.Tourism;
using Unciv.Logic.WorldCongress;
using Unciv.Models.Metadata;
using Unciv.Queries;
using Unciv.Queries.Empire;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.WorldCongress;

// B015 (bnw-spec/SPEC.md 4.7.1 to 4.7.4): founding, special sessions, delegates, proposals and voting of the World Congress. A and B are humans, C is
// an AI; each has a city, A knows Printing Press and all have met. The game speed is Standard (100%).
[Collection("RulesetCache")]
public sealed class LeagueTests
{
    private readonly TestGame game = new(BaseRuleset.Civ_V_BNW);
    private readonly CivilizationObject a, b, c;
    private readonly GameSession session;
    private GameInfo G => game.GameInfo;

    public LeagueTests()
    {
        game.MakeHexagonalMap(8);
        a = game.AddCiv(isPlayer: true);
        b = game.AddCiv(isPlayer: true);
        c = game.AddCiv();
        game.AddCity(a, game.GetTile(-5, 0));
        game.AddCity(b, game.GetTile(0, 0));
        game.AddCity(c, game.GetTile(5, 0));
        foreach (var civ in new[] { a, b, c })
            foreach (var other in new[] { a, b, c })
                if (civ != other) civ.DiplomacyFunctions.MakeCivilizationsMeet(other);
        a.Tech.AddTechnology("Printing Press");
        SetEra(a, "Renaissance era");
        G.EngineOptions.VisibleMap = true;
        G.CurrentPlayerCiv = a;
        G.CurrentPlayer = a.CivId;
        session = new GameSession(G);
    }

    private Unciv.Models.Religion MajorReligion(CivilizationObject founder)
    {
        var religion = game.AddReligion(founder);
        religion.AddBelief(game.Ruleset.Beliefs.Values.First(it => it.Type == Unciv.Models.Ruleset.BeliefType.Founder).Name);
        return religion;
    }

    private void SetEra(CivilizationObject civ, string era) => civ.Tech.Era = game.Ruleset.Eras[era];

    private static void Turn(GameInfo g) => Unciv.Logic.WorldCongress.LeagueRules.ProcessTurn(g);

    /// <summary>A valid sample of each league command for GameCommandTests: the host A with a league in a quiet state (in session for the votes).</summary>
    internal static (GameInfo Game, string Actor, GameCommand Command) CommandSample(string type)
    {
        var world = new LeagueTests();
        var league = world.Quiet(world.a.CivId);
        league.Active.Add(new ActiveResolution { Id = 5, Resolution = "Arts Funding" });
        GameCommand command = type switch
        {
            "propose-resolution" => new ProposeResolution { Resolution = "Standing Army Tax" },
            "propose-repeal" => new ProposeRepeal { ActiveId = 5 },
            _ => new CommitVotes { ProposalId = 1, Choice = LeagueRules.Yes, Count = 1 },
        };
        if (type == "commit-votes")
        {
            league.InSession = true;
            league.GetMember(world.a.CivId)!.Delegates = 2;
            league.Proposals.Add(new LeagueProposal { Id = 1, Resolution = "Standing Army Tax" });
        }
        return (world.G, world.a.CivId, command);
    }

    internal static readonly string[] CommandTypes = ["propose-resolution", "propose-repeal", "commit-votes"];

    private League Found()
    {
        Turn(G);
        return G.League!;
    }

    private LeagueProposal Single(League league) => Assert.Single(league.Proposals);

    private CommandResult Submit(CivilizationObject actor, GameCommand command)
    {
        G.CurrentPlayerCiv = actor;
        G.CurrentPlayer = actor.CivId;
        return session.Submit(actor.CivId, command);
    }

    private ulong Hash() => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(G, forceZip: false)));

    private void Refused(CivilizationObject actor, GameCommand command, string code)
    {
        G.CurrentPlayerCiv = actor;
        G.CurrentPlayer = actor.CivId;
        ulong before = Hash();
        var result = Submit(actor, command);
        Assert.False(result.Accepted);
        Assert.Equal(code, result.Validation.Code);
        Assert.Equal(before, Hash());
    }

    private void Accepted(CivilizationObject actor, GameCommand command)
    {
        var result = Submit(actor, command);
        Assert.True(result.Accepted, result.Validation.Message);
    }

    /// <summary>The league after founding and one decided session in which B became host: a quiet state to propose in.</summary>
    private League Quiet(string host)
    {
        var league = Found();
        league.Proposals.Clear();
        league.InSession = false;
        league.Host = host;
        league.TurnsUntilSession = 30;
        foreach (var member in league.Members) member.Delegates = 0;
        return league;
    }

    // ---- ruleset data

    [Fact]
    public void TheRulesetCarriesTheCongressHooks()
    {
        var ruleset = game.Ruleset;
        Assert.Contains("Allows founding the World Congress", ruleset.Technologies["Printing Press"].Uniques);
        Assert.Contains("[+1] votes per Diplomat in the World Congress", ruleset.Technologies["Globalization"].Uniques);
        Assert.Contains("[+2] Delegates in the World Congress", ruleset.Buildings["Forbidden Palace"].Uniques);
        Assert.True(LeagueRules.IsEnabled(ruleset));
        Assert.False(LeagueRules.IsEnabled(new TestGame().Ruleset));
        Assert.Equal(18, LeagueData.Resolutions.Count);
        Assert.Equal(4, LeagueData.Sessions.Count);
    }

    // ---- founding (4.7.1)

    [Fact]
    public void NothingIsFoundedWithoutThePrintingPressAndEveryMajorMet()
    {
        var gk = new TestGame();
        gk.AddCiv(isPlayer: true);
        gk.AddCiv();
        Turn(gk.GameInfo);
        Assert.Null(gk.GameInfo.League);

        var unmet = game.AddCiv();
        game.AddCity(unmet, game.GetTile(0, 5));
        a.Diplomacy.Remove(unmet.CivId); // founding the city may have introduced them
        Turn(G);
        Assert.Null(G.League); // A has not met the new major civ
        a.DiplomacyFunctions.MakeCivilizationsMeet(unmet);
        unmet.DiplomacyFunctions.MakeCivilizationsMeet(b);
        unmet.DiplomacyFunctions.MakeCivilizationsMeet(c);
        Turn(G);
        Assert.NotNull(G.League);
    }

    [Fact]
    public void TwoLivingMajorsAreNeeded()
    {
        var lone = new TestGame(BaseRuleset.Civ_V_BNW);
        lone.MakeHexagonalMap(3);
        var only = lone.AddCiv(isPlayer: true);
        lone.AddCity(only, lone.GetTile(0, 0));
        only.Tech.AddTechnology("Printing Press");
        Turn(lone.GameInfo);
        Assert.Null(lone.GameInfo.League);
    }

    [Fact]
    public void TheFirstCivInOrderThatQualifiesHostsAndThePrintingPressTechIsRequired()
    {
        b.Tech.AddTechnology("Printing Press");
        var league = Found();
        Assert.Equal(a.CivId, league.Host); // A comes first in civ order
        Assert.Equal([a.CivId, b.CivId, c.CivId], league.Members.Select(it => it.Civ));
        Assert.True(league.InSession);
        Assert.Equal(0, league.SessionIndex);
    }

    [Fact]
    public void FoundingPicksTheSessionOfTheGameEraAndAtLeastTheFirst()
    {
        SetEra(a, "Ancient era");
        Assert.Equal(0, Found().SessionIndex); // below the first trigger: the earliest session
        Assert.Equal(1, LeagueRules.SessionIndexForEra(game.Ruleset, game.Ruleset.Eras["Industrial era"].EraNumber));
        Assert.Equal(2, LeagueRules.SessionIndexForEra(game.Ruleset, game.Ruleset.Eras["Modern era"].EraNumber));
        Assert.Equal(3, LeagueRules.SessionIndexForEra(game.Ruleset, game.Ruleset.Eras["Information era"].EraNumber)); // above the last trigger
        Assert.Equal(0, LeagueRules.SessionIndexForEra(game.Ruleset, game.Ruleset.Eras["Medieval era"].EraNumber));
    }

    [Fact]
    public void FoundingInTheIndustrialEraStartsTheSecondSession()
    {
        SetEra(a, "Industrial era");
        var league = Found();
        Assert.Equal(1, league.SessionIndex);
        Assert.Equal(League2Delegates(), LeagueRules.StartingVotes(G, league, a));
    }

    private static int League2Delegates() => 2 + 1; // civ 2 + host 1 (Welcome City-States)

    [Fact]
    public void FoundingOpensASpecialSessionWithTheHostChangeAndTellsTheHumans()
    {
        var league = Found();
        var proposal = Single(league);
        Assert.Equal((LeagueData.ChangeLeagueHost, ""), (proposal.Resolution, proposal.Proposer));
        Assert.Contains(a.Notifications, it => it.Text.Contains("founded the World Congress"));
        Assert.Contains(a.Notifications, it => it.Text.Contains("in session"));
        Assert.DoesNotContain(c.Notifications, it => it.Text.Contains("in session")); // the AI has no popup
    }

    [Fact]
    public void TheUnitedNationsSessionAlsoProposesTheDiplomaticVictory()
    {
        SetEra(a, "Atomic era");
        var league = Found();
        Assert.Equal(3, league.SessionIndex);
        Assert.Equal([LeagueData.ChangeLeagueHost, LeagueData.DiplomaticVictory], league.Proposals.Select(it => it.Resolution));
    }

    // ---- delegates (4.7.3)

    [Fact]
    public void DelegatesAreCivHostAllyWonderReligionIdeologyAndExtra()
    {
        SetEra(a, "Modern era");
        var league = Found(); // Leadership Council: 3 civ, 2 host, 1 per city-state ally
        Assert.Equal(2, league.SessionIndex);
        Assert.Equal(5, LeagueRules.StartingVotes(G, league, a));
        Assert.Equal(3, LeagueRules.StartingVotes(G, league, b));

        var cityState = game.AddCiv(cityStateType: "Maritime");
        game.AddCity(cityState, game.GetTile(0, -5));
        cityState.AllyCiv = b;
        Assert.Equal(4, LeagueRules.StartingVotes(G, league, b));

        game.AddCity(b, game.GetTile(2, 2)).CityConstructions.AddBuilding("Forbidden Palace"); // +2 delegates
        Assert.Equal(6, LeagueRules.StartingVotes(G, league, b));

        LeagueRules.AddExtraVotes(league, b.CivId, 2);
        Assert.Equal(8, LeagueRules.StartingVotes(G, league, b));

        var religion = game.AddReligion(c);
        league.Active.Add(new ActiveResolution { Id = 90, Resolution = LeagueData.WorldReligion, Choice = religion.Name });
        Assert.Equal(3 + 2, LeagueRules.StartingVotes(G, league, c)); // the founder of the chosen religion
        Assert.Equal(8, LeagueRules.StartingVotes(G, league, b));

        b.Policies.Ideology = "Freedom";
        a.Policies.Ideology = "Freedom";
        league.Active.Add(new ActiveResolution { Id = 91, Resolution = LeagueData.WorldIdeology, Choice = "Freedom" });
        Assert.Equal(10, LeagueRules.StartingVotes(G, league, b));
        Assert.Equal(7, LeagueRules.StartingVotes(G, league, a));
        Assert.Equal(5, LeagueRules.StartingVotes(G, league, c)); // no ideology: nothing
    }

    [Fact]
    public void AwayFromTheFirstSessionLevelEachExtraEraAddsCivDelegates()
    {
        var league = Found();
        league.SessionIndex = 3;
        Assert.Equal(4 + 2, LeagueRules.StartingVotes(G, league, a));
        Assert.Equal(4, LeagueRules.StartingVotes(G, league, b));
    }

    // ---- the session (4.7.4)

    [Fact]
    public void TheAiVotesForItselfAtOnceAndHumansCommitDuringTheSession()
    {
        var league = Found();
        var proposal = Single(league);
        var ai = Assert.Single(proposal.Votes); // C: 1 delegate, for itself
        Assert.Equal((c.CivId, c.CivId, 1), (ai.Civ, ai.Choice, ai.Count));

        Accepted(a, new CommitVotes { ProposalId = proposal.Id, Choice = b.CivId, Count = 2 });
        Accepted(b, new CommitVotes { ProposalId = proposal.Id, Choice = b.CivId, Count = 1 });
        Assert.Equal(0, LeagueRules.RemainingVotes(league, a.CivId));
        Turn(G);

        Assert.Equal(b.CivId, league.Host); // 3 votes for B against 1 for C
        Assert.False(league.InSession);
        Assert.Empty(league.Proposals);
        Assert.Equal(30, league.TurnsUntilSession); // Standard speed, first level
        Assert.Equal([0, 0, 0], league.Members.Select(it => it.Delegates + it.ProposalsUsed));
    }

    [Fact]
    public void ATiedVoteOnACivFailsAndSoDoesNoVote()
    {
        var league = Found();
        var proposal = Single(league);
        Accepted(a, new CommitVotes { ProposalId = proposal.Id, Choice = a.CivId, Count = 1 });
        Accepted(b, new CommitVotes { ProposalId = proposal.Id, Choice = b.CivId, Count = 1 });
        Turn(G);
        Assert.Equal(a.CivId, league.Host); // A, B and C have one vote each: nobody wins

        var quiet = Quiet(a.CivId);
        quiet.InSession = true;
        quiet.Proposals.Add(new LeagueProposal { Id = 50, Resolution = LeagueData.ChangeLeagueHost });
        Turn(G); // not one vote
        Assert.Equal(a.CivId, quiet.Host);
    }

    [Fact]
    public void TheHostChangesOnlyToALivingMember()
    {
        var league = Found();
        var proposal = Single(league);
        Refused(a, new CommitVotes { ProposalId = proposal.Id, Choice = "Nobody", Count = 1 }, CommandErrors.NotAllowed);
        Refused(a, new CommitVotes { ProposalId = proposal.Id, Choice = LeagueRules.Yes, Count = 1 }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void YesMustBeatNoAndRepealRemovesTheResolution()
    {
        var league = Quiet(a.CivId);
        league.InSession = true;
        league.Proposals.Add(new LeagueProposal { Id = 60, Resolution = "Standing Army Tax", Proposer = a.CivId, Votes = [new() { Civ = a.CivId, Choice = LeagueRules.Yes, Count = 2 }, new() { Civ = b.CivId, Choice = LeagueRules.No, Count = 2 }] });
        league.Proposals.Add(new LeagueProposal { Id = 61, Resolution = "Arts Funding", Proposer = a.CivId, Votes = [new() { Civ = a.CivId, Choice = LeagueRules.Yes, Count = 2 }, new() { Civ = b.CivId, Choice = LeagueRules.No, Count = 1 }] });
        league.Proposals.Add(new LeagueProposal { Id = 62, Resolution = "Sciences Funding", Proposer = a.CivId });
        Turn(G);
        var enacted = Assert.Single(league.Active); // 2:2 and 0:0 fail, 2:1 passes
        Assert.Equal(("Arts Funding", a.CivId, G.Turns), (enacted.Resolution, enacted.Proposer, enacted.Turn));
        Assert.Equal(30, league.TurnsUntilSession);

        league.InSession = true;
        league.Proposals.Add(new LeagueProposal { Id = 70, Resolution = "Arts Funding", RepealId = enacted.Id, Votes = [new() { Civ = a.CivId, Choice = LeagueRules.Yes, Count = 2 }] });
        Turn(G);
        Assert.Empty(league.Active);
    }

    [Fact]
    public void EnactingEmitsTheEventsOfTheOutcome()
    {
        var league = Quiet(a.CivId);
        var sink = new GameInfoEventSink();
        G.EventSink = sink;
        league.InSession = true;
        league.Proposals.Add(new LeagueProposal { Id = 60, Resolution = "Standing Army Tax", Votes = [new() { Civ = a.CivId, Choice = LeagueRules.Yes, Count = 1 }] });
        league.Proposals.Add(new LeagueProposal { Id = 61, Resolution = "Arts Funding" });
        Turn(G);
        var events = sink.Drain().Where(it => it is not NotificationAdded).ToList();
        Assert.Equal(new ResolutionEnacted("Standing Army Tax", ""), events[0]);
        Assert.Equal(new ResolutionFailed("Arts Funding", ""), events[1]);
        Assert.Contains(a.Notifications, it => it.Text == "Standing Army Tax: passed");
    }

    // ---- the countdown

    [Fact]
    public void SessionsFollowTheIntervalOfTheSpeedAndWarnFiveRoundsBefore()
    {
        var league = Quiet(a.CivId);
        a.Notifications.Clear();
        league.TurnsUntilSession = 7;
        Turn(G);
        Turn(G);
        Assert.Equal(5, league.TurnsUntilSession);
        Assert.Contains(a.Notifications, it => it.Text == "The World Congress meets in 5 turns");
        Assert.False(league.InSession);
        for (int i = 0; i < 4; i++) Turn(G);
        Assert.False(league.InSession);
        Turn(G);
        Assert.True(league.InSession);
        Assert.Contains(c.CivId, LeagueRules.Proposers(G, league).Select(it => it.CivId).Concat([c.CivId])); // sanity: the AI civ exists
    }

    [Theory]
    [InlineData("Quick", 0, 20)]
    [InlineData("Standard", 0, 30)]
    [InlineData("Epic", 0, 45)]
    [InlineData("Marathon", 0, 90)]
    [InlineData("Standard", 3, 10)]
    [InlineData("Quick", 1, 16)]
    public void TheIntervalScalesTheSessionLevelBySpeed(string speed, int level, int expected)
    {
        game.SetSpeed(speed);
        Assert.Equal(expected, LeagueRules.SessionInterval(G, level));
    }

    [Fact]
    public void ANewEraStartsTheMatchingSpecialSessionAfterTheRunningOneEnds()
    {
        var league = Quiet(a.CivId);
        SetEra(b, "Industrial era");
        Turn(G);
        Assert.Equal(1, league.SessionIndex);
        Assert.True(league.InSession);
        Assert.Equal(LeagueData.ChangeLeagueHost, Single(league).Resolution);
        SetEra(b, "Information era");
        Turn(G); // the open session ends first
        Assert.False(league.InSession);
        Assert.Equal(1, league.SessionIndex);
        Turn(G);
        Assert.Equal(3, league.SessionIndex); // skips Modern: the highest session for the era
    }

    [Fact]
    public void ADeadHostIsReplacedByTheMemberWithTheMostDelegates()
    {
        var league = Quiet(c.CivId);
        LeagueRules.AddExtraVotes(league, b.CivId, 1);
        foreach (var city in c.Cities.ToList()) city.DestroyCity(overrideSafeties: true);
        Assert.False(c.IsAlive());
        Turn(G);
        Assert.Equal(b.CivId, league.Host);
        Assert.Equal(0, LeagueRules.RemainingVotes(league, c.CivId));
    }

    // ---- proposals

    [Fact]
    public void OnlyTheHostAndTheTopMemberPropose()
    {
        var league = Quiet(a.CivId);
        Assert.Equal([a.CivId, b.CivId], LeagueRules.Proposers(G, league).Select(it => it.CivId)); // B and C tie: civ order
        LeagueRules.AddExtraVotes(league, c.CivId, 1);
        Assert.Equal([a.CivId, c.CivId], LeagueRules.Proposers(G, league).Select(it => it.CivId));
        league.Host = c.CivId;
        Assert.Equal([c.CivId, a.CivId], LeagueRules.Proposers(G, league).Select(it => it.CivId)); // C is host: the next in line follows
    }

    [Fact]
    public void ProposingUsesTheOneProposalAndTheVoteDecides()
    {
        var league = Quiet(a.CivId);
        Accepted(a, new ProposeResolution { Resolution = "Standing Army Tax" });
        var proposal = Single(league);
        Assert.Equal(("Standing Army Tax", a.CivId), (proposal.Resolution, proposal.Proposer));
        Refused(a, new ProposeResolution { Resolution = "Arts Funding" }, CommandErrors.NotAllowed); // no proposal left
        Refused(b, new ProposeResolution { Resolution = "Standing Army Tax" }, CommandErrors.NotAllowed); // already proposed
        Accepted(b, new ProposeResolution { Resolution = "Arts Funding" });
        Refused(a, new CommitVotes { ProposalId = proposal.Id, Choice = LeagueRules.Yes, Count = 1 }, CommandErrors.NotAllowed); // not in session

        league.TurnsUntilSession = 1;
        Turn(G);
        Assert.True(league.InSession);
        Assert.Equal(2, league.Proposals.Count);
        Refused(a, new ProposeResolution { Resolution = "Natural Heritage Sites" }, CommandErrors.NotAllowed); // in session

        Accepted(a, new CommitVotes { ProposalId = proposal.Id, Choice = LeagueRules.Yes, Count = 1 });
        Accepted(a, new CommitVotes { ProposalId = proposal.Id, Choice = LeagueRules.Yes, Count = 1 });
        Refused(a, new CommitVotes { ProposalId = league.Proposals[1].Id, Choice = LeagueRules.Yes, Count = 1 }, CommandErrors.NotAllowed); // none left
        Assert.Equal(2, Assert.Single(proposal.Votes).Count); // the two commits add up
        Turn(G);
        Assert.Equal("Standing Army Tax", Assert.Single(league.Active).Resolution);
    }

    [Fact]
    public void ProposalsAreCheckedAgainstTheRules()
    {
        var league = Quiet(a.CivId);
        Refused(c, new ProposeResolution { Resolution = "Arts Funding" }, CommandErrors.NotHuman);
        Refused(a, new ProposeResolution { Resolution = "Nonsense" }, CommandErrors.NotAllowed);
        Refused(a, new ProposeResolution { Resolution = LeagueData.DiplomaticVictory }, CommandErrors.NotAllowed); // sessions only
        Refused(a, new ProposeResolution { Resolution = LeagueData.ChangeLeagueHost }, CommandErrors.NotAllowed);
        Refused(a, new ProposeResolution { Resolution = "International Games" }, CommandErrors.NotAllowed); // nobody knows Radio
        Refused(a, new ProposeResolution { Resolution = "Arts Funding", Choice = "x" }, CommandErrors.NotAllowed); // takes no choice
        Refused(a, new ProposeResolution { Resolution = "Player Embargo", Choice = a.CivId }, CommandErrors.NotAllowed); // not yourself
        Refused(a, new ProposeResolution { Resolution = "Player Embargo", Choice = "" }, CommandErrors.NotAllowed);
        Refused(a, new ProposeResolution { Resolution = "Ban Luxury", Choice = "Iron" }, CommandErrors.NotAllowed); // not a luxury
        Refused(a, new ProposeResolution { Resolution = "World Religion", Choice = "Nothing" }, CommandErrors.NotAllowed);
        Refused(a, new ProposeResolution { Resolution = "World Ideology", Choice = "Freedom" }, CommandErrors.NotAllowed); // needs Radio and a follower

        b.Tech.AddTechnology("Radio");
        Refused(a, new ProposeResolution { Resolution = "World Ideology", Choice = "Freedom" }, CommandErrors.NotAllowed); // nobody follows an ideology yet
        Refused(a, new ProposeResolution { Resolution = "World Ideology", Choice = "Tradition" }, CommandErrors.NotAllowed);
        c.Policies.Ideology = "Order";
        Accepted(a, new ProposeResolution { Resolution = "World Ideology", Choice = "Freedom" });
        Assert.Equal("Freedom", Single(league).Choice);
    }

    [Fact]
    public void ChoicesNameACivLuxuryReligionOrIdeology()
    {
        var league = Quiet(a.CivId);
        var luxury = game.Ruleset.TileResources.Values.First(it => it.ResourceType == Unciv.Models.Ruleset.Tile.ResourceType.Luxury).Name;
        var religion = MajorReligion(c);
        Accepted(a, new ProposeResolution { Resolution = "Ban Luxury", Choice = luxury });
        Accepted(b, new ProposeResolution { Resolution = "Player Embargo", Choice = c.CivId });
        Assert.Equal(2, league.Proposals.Count);
        league.Proposals.Clear();
        foreach (var member in league.Members) member.ProposalsUsed = 0;
        Accepted(a, new ProposeResolution { Resolution = "World Religion", Choice = religion.Name });
        Refused(a, new ProposeResolution { Resolution = "World Religion", Choice = religion.Name }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void ARepealNeedsAResolutionInForceAndIsProposedOnce()
    {
        var league = Quiet(a.CivId);
        Refused(a, new ProposeRepeal { ActiveId = 5 }, CommandErrors.NotAllowed);
        league.Active.Add(new ActiveResolution { Id = 5, Resolution = "Arts Funding" });
        Refused(a, new ProposeResolution { Resolution = "Arts Funding" }, CommandErrors.NotAllowed); // already in force
        Accepted(a, new ProposeRepeal { ActiveId = 5 });
        var repeal = Single(league);
        Assert.Equal((5, "Arts Funding"), (repeal.RepealId, repeal.Resolution));
        Refused(b, new ProposeRepeal { ActiveId = 5 }, CommandErrors.NotAllowed); // already proposed
        league.Active.Add(new ActiveResolution { Id = 6, Resolution = "Sciences Funding" });
        Refused(a, new ProposeRepeal { ActiveId = 6 }, CommandErrors.NotAllowed); // A's proposal is used
        Accepted(b, new ProposeRepeal { ActiveId = 6 });
    }

    [Fact]
    public void WithoutALeagueEveryCommandIsRefused()
    {
        Refused(a, new ProposeResolution { Resolution = "Arts Funding" }, CommandErrors.NotAllowed);
        Refused(a, new ProposeRepeal { ActiveId = 1 }, CommandErrors.NotAllowed);
        Refused(a, new CommitVotes { ProposalId = 1, Choice = LeagueRules.Yes, Count = 1 }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void VotesAreCheckedAgainstTheDelegatesAndOneChoicePerProposal()
    {
        Found();
        var league = G.League!;
        var id = Single(league).Id;
        Refused(a, new CommitVotes { ProposalId = 99, Choice = a.CivId, Count = 1 }, CommandErrors.NotAllowed);
        Refused(a, new CommitVotes { ProposalId = id, Choice = a.CivId, Count = 0 }, CommandErrors.NotAllowed);
        Refused(a, new CommitVotes { ProposalId = id, Choice = a.CivId, Count = 3 }, CommandErrors.NotAllowed); // A has 2
        Accepted(a, new CommitVotes { ProposalId = id, Choice = a.CivId, Count = 1 });
        Refused(a, new CommitVotes { ProposalId = id, Choice = b.CivId, Count = 1 }, CommandErrors.NotAllowed); // one choice only
        Assert.Equal(1, LeagueRules.RemainingVotes(league, a.CivId));
    }

    // ---- the AI

    [Fact]
    public void AnAiHostProposesAResolutionAndBacksItWithItsDelegates()
    {
        var league = Quiet(c.CivId);
        league.TurnsUntilSession = 1;
        Turn(G);
        var proposal = Single(league);
        Assert.Equal(c.CivId, proposal.Proposer);
        var vote = Assert.Single(proposal.Votes);
        Assert.Equal((c.CivId, LeagueRules.Yes, 2), (vote.Civ, vote.Choice, vote.Count)); // host: 1 civ + 1 host delegate
        Turn(G);
        Assert.Equal(proposal.Resolution, Assert.Single(league.Active).Resolution);
    }

    [Fact]
    public void AnAiOpposesTheProposalsOfItsEnemiesAndBacksFriends()
    {
        var league = Quiet(a.CivId);
        c.GetDiplomacyManager(a)!.DiplomaticStatus = Unciv.Logic.Civilization.Diplomacy.DiplomaticStatus.War;
        league.InSession = true;
        var proposal = new LeagueProposal { Id = 80, Resolution = "Arts Funding", Proposer = a.CivId };
        league.Proposals.Add(proposal);
        league.GetMember(c.CivId)!.Delegates = 3;
        LeagueAi_Vote(c);
        var vote = Assert.Single(proposal.Votes);
        Assert.Equal((LeagueRules.No, 3), (vote.Choice, vote.Count));

        proposal.Votes.Clear();
        c.GetDiplomacyManager(a)!.DiplomaticStatus = Unciv.Logic.Civilization.Diplomacy.DiplomaticStatus.Peace;
        LeagueAi_Vote(c);
        Assert.Empty(proposal.Votes); // no opinion: abstain
        c.GetDiplomacyManager(a)!.SetFlag(Unciv.Logic.Civilization.Diplomacy.DiplomacyFlags.DeclarationOfFriendship, 30);
        league.Proposals.Add(new LeagueProposal { Id = 81, Resolution = "Sciences Funding", Proposer = a.CivId });
        LeagueAi_Vote(c);
        Assert.Equal([2, 1], league.Proposals.Select(it => it.Votes.Sum(v => v.Count))); // 3 delegates over 2 proposals: 2 and 1
    }

    // The AI votes at session start; here it is triggered by starting a session over prepared proposals.
    private void LeagueAi_Vote(CivilizationObject civ)
    {
        var type = typeof(LeagueRules).Assembly.GetType("Unciv.Logic.WorldCongress.LeagueAi")!;
        type.GetMethod("Vote")!.Invoke(null, [G, G.League, civ]);
    }

    // ---- other systems

    [Fact]
    public void WorldReligionRaisesTheTourismOfItsHolyCity()
    {
        var league = Quiet(a.CivId);
        var religion = game.AddReligion(c);
        CityObject holy = c.Cities.First();
        Assert.Equal(0, LeagueRules.HolyCityTourismModifier(holy));
        league.Active.Add(new ActiveResolution { Id = 3, Resolution = LeagueData.WorldReligion, Choice = religion.Name });
        Assert.Equal(0, LeagueRules.HolyCityTourismModifier(holy)); // not a holy city yet
        holy.Religion.ReligionThisIsTheHolyCityOf = religion.Name;
        Assert.Equal(50, LeagueRules.HolyCityTourismModifier(holy));
        Assert.Equal(50, TourismRules.CityTourismModifier(holy));
        Assert.Equal(0, TourismRules.CityTourismModifier(a.Cities.First()));
    }

    [Fact]
    public void TheLeagueSurvivesSaveAndCloneAndIsOmittedWhileUnfounded()
    {
        Assert.DoesNotContain("\"league\"", UncivFiles.GameInfoToString(G, forceZip: false));
        var league = Found();
        Accepted(a, new CommitVotes { ProposalId = Single(league).Id, Choice = a.CivId, Count = 1 });
        league.Active.Add(new ActiveResolution { Id = 9, Resolution = "Arts Funding", Choice = "x", Proposer = a.CivId, Turn = 4 });
        LeagueRules.AddExtraVotes(league, b.CivId, 2);

        string json = JsonSerializer.Serialize(league, UncivJson.DefaultOptions);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<League>(json, UncivJson.DefaultOptions), UncivJson.DefaultOptions));
        Assert.Equal(json, JsonSerializer.Serialize(league.Clone(), UncivJson.DefaultOptions));
        Assert.Equal(json, JsonSerializer.Serialize(G.Clone().League, UncivJson.DefaultOptions));
        Assert.Contains("\"league\"", UncivFiles.GameInfoToString(G, forceZip: false));
    }

    [Fact]
    public void TheLeagueQueryShowsTheViewerItsDelegatesAndProposals()
    {
        var registry = new QueryRegistry(UncivJson.DefaultOptions);
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        LeagueResult Ask(CivilizationObject viewer)
        {
            var response = registry.Execute(registry.ToEnvelope(viewer.CivName, new LeagueQuery()), new QueryContext(G));
            Assert.True(response.Ok, response.Message);
            return JsonSerializer.Deserialize<LeagueResult>(response.Result!.Value.GetRawText(), UncivJson.DefaultOptions)!;
        }

        Assert.False(Ask(a).Founded);
        var league = Found();
        Accepted(a, new CommitVotes { ProposalId = Single(league).Id, Choice = b.CivId, Count = 1 });
        var mine = Ask(a);
        Assert.Equal((true, true, a.CivId, "World Congress Founded"), (mine.Founded, mine.InSession, mine.Host, mine.SessionName));
        Assert.False(mine.CanPropose);
        Assert.Equal([2, 1, 1], mine.Members.Select(it => it.Delegates));
        Assert.Equal([1, 1, 0], mine.Members.Select(it => it.Left));
        var row = Assert.Single(mine.Proposals);
        Assert.Equal([new KeyValuePair<string, int>(c.CivId, 1), new KeyValuePair<string, int>(b.CivId, 1)], row.Tally);
        Assert.Equal([new KeyValuePair<string, int>(b.CivId, 1)], row.YourVotes);
        Assert.Empty(Ask(b).Proposals.Single().YourVotes);

        league.InSession = false;
        league.Proposals.Clear();
        var between = Ask(a);
        Assert.True(between.CanPropose);
        Assert.Contains("Arts Funding", between.Proposable);
        Assert.DoesNotContain("Ban Luxury", between.Proposable); // needs a choice
        Assert.DoesNotContain(LeagueData.DiplomaticVictory, between.Proposable);
        Assert.False(Ask(c).CanPropose);
    }

    [Fact]
    public void TheLeagueTurnRunsWhenTheCivCycleWraps()
    {
        var advance = typeof(GameInfo).GetMethod("AdvancePlayerIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        int turns = G.Turns;
        Assert.Equal(1, advance.Invoke(G, [0]));
        Assert.Null(G.League); // not the end of a round
        Assert.Equal(0, advance.Invoke(G, [G.Civilizations.Count - 1]));
        Assert.NotNull(G.League);
        Assert.Equal(turns + 1, G.Turns);
    }
}
