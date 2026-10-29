using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.WorldCongress;
using Unciv.Models.Metadata;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.WorldCongress;

// B018 (bnw-spec/SPEC.md 4.7.4 step 4 and 4.7.6): the diplomatic victory vote of the United Nations session, on the real generated BNW ruleset.
// A and B are humans, C an AI; the UN session is open with the Diplomatic Victory proposal (no delegates committed yet); with three civs and
// no city-states the votes needed are 8 (floor(1.443 ln 3 + 7)).
[Collection("RulesetCache")]
public sealed class LeagueVictoryTests
{
    private readonly TestGame game = new(BaseRuleset.Civ_V_BNW);
    private readonly CivilizationObject a, b, c;
    private GameInfo G => game.GameInfo;
    private readonly League league;
    private readonly LeagueProposal vote;

    public LeagueVictoryTests()
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
        a.Tech.Era = game.Ruleset.Eras["Atomic era"];
        G.GameParameters.VictoryTypes.Add("Diplomatic");
        LeagueRules.ProcessTurn(G);
        league = G.League!;
        vote = league.Proposals.Single(it => it.Resolution == LeagueData.DiplomaticVictory);
        vote.Votes.Clear();
        league.Proposals.RemoveAll(it => it != vote);
    }

    private void Commit(CivilizationObject voter, CivilizationObject candidate, int count) =>
        vote.Votes.Add(new LeagueVote { Civ = voter.CivId, Choice = candidate.CivId, Count = count });

    private static readonly LeagueSession UnitedNations = LeagueData.Sessions[3];

    [Theory]
    [InlineData(8f, 16f, 40)] // 10.0 + 30.67, the spec example (66 possible)
    [InlineData(3f, 0f, 8)] // 8.585, no city-states: the city-state part is 0
    [InlineData(3f, 4f, 17)] // 8.585 + 8.454
    [InlineData(8f, 0f, 10)]
    [InlineData(7.5f, 0f, 9)] // one defeated civ counts half: 9.9075
    [InlineData(2f, 0.5f, 8)] // a lone defeated city-state: ln is negative, the part is 0
    [InlineData(1f, 0f, 6)] // 7 is cut to all delegates: 4 + 2 host
    public void VotesNeededFollowTheFormulaOfTheSpec(float civs, float cityStates, int expected) =>
        Assert.Equal(expected, LeagueRules.VotesNeeded(civs, cityStates, UnitedNations));

    [Fact]
    public void VotesNeededAreCutToTheMinimumAndToWhatTheMembersHave()
    {
        // first session: 1 delegate per civ + 1 host: 8 would be 4 of 4 possible
        Assert.Equal(4, LeagueRules.VotesNeeded(3f, 0f, LeagueData.Sessions[0]));
        Assert.Equal(6, LeagueRules.VotesNeeded(1.5f, 0f, UnitedNations)); // 1.443 ln 1.5 + 7 = 7.58 -> 7, but 4 * 1 + 2 = 6 delegates exist
    }

    [Fact]
    public void TheGameCountsLivingCivsAndCityStatesFullAndDefeatedOnesHalf()
    {
        Assert.Equal(8, LeagueRules.VotesNeeded(G, league));
        var states = new List<CivilizationObject>();
        for (int i = 0; i < 3; i++)
        {
            var state = game.AddCiv(cityStateType: "Cultured");
            game.AddCity(state, game.GetTile(i, 4));
            states.Add(state);
        }
        Assert.Equal(12, LeagueRules.VotesNeeded(G, league)); // 3 civs and 3 city-states: 8.585 + 3.84
        foreach (var city in states[0].Cities.ToList()) city.DestroyCity(overrideSafeties: true);
        Assert.False(states[0].IsAlive());
        Assert.Equal(9, LeagueRules.VotesNeeded(G, league)); // 2.5 city-states: 8.585 + 0.92
    }

    [Fact]
    public void ACivWithTheVotesNeededWinsTheDiplomaticVictory()
    {
        Commit(a, a, 8);
        Commit(b, b, 4);
        LeagueRules.ProcessTurn(G);

        Assert.True(a.VictoryManager.HasEverWonDiplomaticVote);
        Assert.False(b.VictoryManager.HasEverWonDiplomaticVote);
        var won = G.VictoryData.NotNull();
        Assert.Equal((a.CivId, "Diplomatic"), (won.WinningCiv, won.VictoryType));
        Assert.True(G.CheckForVictory());
        Assert.Contains(a.Notifications, it => it.Text == "You have been elected world leader!");
        Assert.Contains(b.Notifications, it => it.Text.Contains("has been elected world leader"));
        Assert.Equal(0, league.GetMember(a.CivId)!.ExtraVotes);
    }

    [Fact]
    public void OneDelegateFewerFailsAndGivesTheLeaderTheBonus()
    {
        Commit(a, a, 7);
        Commit(b, b, 4);
        LeagueRules.ProcessTurn(G);

        Assert.Null(G.VictoryData);
        Assert.False(a.VictoryManager.HasEverWonDiplomaticVote);
        Assert.Equal([2, 0, 0], league.Members.Select(it => it.ExtraVotes));
        Assert.Contains(a.Notifications, it => it.Text.Contains("8 delegates are needed"));
        Assert.False(league.InSession);
        Assert.Equal(4 + 2 + 2, LeagueRules.StartingVotes(G, league, a));
    }

    [Fact]
    public void ATieForTheMostVotesWinsNothingAndBothLeadersGetTheBonus()
    {
        Commit(a, a, 8);
        Commit(b, b, 8);
        league.GetMember(b.CivId)!.ExtraVotes = 1;
        LeagueRules.ProcessTurn(G);

        Assert.Null(G.VictoryData);
        Assert.Equal([2, 3, 0], league.Members.Select(it => it.ExtraVotes));
        Assert.Contains(a.Notifications, it => it.Text.Contains("tied"));
    }

    [Fact]
    public void NoVoteAtAllGivesNoBonus()
    {
        LeagueRules.ProcessTurn(G);
        Assert.Null(G.VictoryData);
        Assert.Equal([0, 0, 0], league.Members.Select(it => it.ExtraVotes));
        Assert.Contains(a.Notifications, it => it.Text.Contains("nobody voted"));
    }

    [Fact]
    public void TheDelegatesOfACivThatDiedDuringTheSessionDoNotCount()
    {
        Commit(a, a, 1);
        Commit(b, a, 8); // 9 for A while B lives
        foreach (var city in b.Cities.ToList()) city.DestroyCity(overrideSafeties: true);
        Assert.False(b.IsAlive());
        LeagueRules.ProcessTurn(G);

        Assert.Null(G.VictoryData);
        Assert.Equal(2, league.GetMember(a.CivId)!.ExtraVotes);
    }

    [Fact]
    public void ADeadCandidateCannotWin()
    {
        Commit(a, c, 4);
        Commit(b, c, 4);
        foreach (var city in c.Cities.ToList()) city.DestroyCity(overrideSafeties: true);
        LeagueRules.ProcessTurn(G);
        Assert.Null(G.VictoryData);
        Assert.False(c.VictoryManager.HasEverWonDiplomaticVote);
    }

    [Fact]
    public void AGameWithoutTheDiplomaticVictoryEnabledHasNoWinner()
    {
        G.GameParameters.VictoryTypes.Clear();
        Commit(a, a, 8);
        LeagueRules.ProcessTurn(G);
        Assert.Null(G.VictoryData);
        Assert.False(a.VictoryManager.HasEverWonDiplomaticVote);
        Assert.Contains(a.Notifications, it => it.Text.Contains("not enabled"));
        Assert.DoesNotContain(a.Notifications, it => it.Text.Contains("elected world leader"));
    }

    [Fact]
    public void ADefeatedMajorCivCountsHalfInTheVotesNeeded()
    {
        Assert.Equal(8, LeagueRules.VotesNeeded(G, league));
        foreach (var city in c.Cities.ToList()) city.DestroyCity(overrideSafeties: true);
        Assert.Equal(8, LeagueRules.VotesNeeded(2.5f, 0f, UnitedNations)); // 8.32
        Assert.Equal(8, LeagueRules.VotesNeeded(G, league));
    }

    [Fact]
    public void TheVictoryFlagSurvivesClone()
    {
        Commit(a, a, 8);
        LeagueRules.ProcessTurn(G);
        var copy = G.Clone();
        Assert.Equal(a.CivId, copy.VictoryData.NotNull().WinningCiv);
        Assert.True(copy.GetCivilization(a.CivId).VictoryManager.HasEverWonDiplomaticVote);
    }
}
