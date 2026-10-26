using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Empire;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Files;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using PolicyObject = Unciv.Models.Ruleset.Policy;

namespace Unciv.Core.Tests.Commands.Empire;

// T192: research, policy, religion, great person, espionage and world congress commands. a is the current player, b a second human.
[Collection("RulesetCache")]
public sealed class EmpireCommandTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject a;
    private readonly CivilizationObject b;
    private readonly CityObject city;
    private readonly GameSession session;

    public EmpireCommandTests()
    {
        testGame.Ruleset.ModOptions.Uniques.Add("Can convert gold to science with sliders");
        testGame.MakeHexagonalMap(9);
        a = testGame.AddCiv(isPlayer: true);
        b = testGame.AddCiv(isPlayer: true);
        testGame.AddUnit("Warrior", b, testGame.GetTile(8, 0));
        city = testGame.AddCity(a, testGame.GetTile(0, 0), initialPopulation: 2);
        testGame.GameInfo.CurrentPlayerCiv = a;
        testGame.GameInfo.CurrentPlayer = a.CivId;
        session = new GameSession(testGame.GameInfo);
    }

    private ulong Hash() => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(testGame.GameInfo, forceZip: false)));

    private void Ok(GameCommand command)
    {
        var result = session.Submit(a.CivId, command);
        Assert.True(result.Accepted, result.Validation.Message);
    }

    private void AssertRejected(GameCommand command, string code, string? actor = null)
    {
        ulong before = Hash();
        int logged = session.Log.Count;
        var result = session.Submit(actor ?? a.CivId, command);
        Assert.False(result.Accepted);
        Assert.Equal(code, result.Validation.Code);
        Assert.Equal(before, Hash());
        Assert.Equal(logged, session.Log.Count);
    }

    [Fact]
    public void CommandsAreRegisteredAndRoundTripThroughJson()
    {
        var names = GameCommandRegistry.Scan().ToDictionary(it => it.Name, it => it.CommandClass);
        Assert.Equal(typeof(SetResearchQueue), names["set-research-queue"]);
        Assert.Equal(typeof(PickFreeTechnology), names["pick-free-technology"]);
        Assert.Equal(typeof(DebugAddTechnology), names["debug-add-technology"]);
        Assert.Equal(typeof(AdoptPolicy), names["adopt-policy"]);
        Assert.Equal(typeof(FoundPantheon), names["found-pantheon"]);
        Assert.Equal(typeof(ChooseBeliefs), names["choose-beliefs"]);
        Assert.Equal(typeof(FoundReligion), names["found-religion"]);
        Assert.Equal(typeof(ChooseFreeGreatPerson), names["choose-free-great-person"]);
        Assert.Equal(typeof(MoveSpy), names["move-spy"]);
        Assert.Equal(typeof(VoteDiplomatic), names["vote-diplomatic"]);
        Assert.Equal(typeof(AckDiplomaticVoteResult), names["ack-diplomatic-vote-result"]);
        Assert.Equal(typeof(SetGoldToScience), names["set-gold-to-science"]);

        var queue = JsonSerializer.Deserialize<SetResearchQueue>("""{"techs":["Agriculture","Pottery"]}""", UncivJson.DefaultOptions)!;
        Assert.Equal(["Agriculture", "Pottery"], queue.Techs);
        var vote = JsonSerializer.Deserialize<VoteDiplomatic>("""{"civId":null}""", UncivJson.DefaultOptions)!;
        Assert.Null(vote.CivId);
        var spy = JsonSerializer.Deserialize<MoveSpy>("""{"spy":"Alice","cityId":"c1"}""", UncivJson.DefaultOptions)!;
        Assert.Equal(("Alice", "c1"), (spy.Spy, spy.CityId));
    }

    [Fact]
    public void EveryCommandNeedsTheActiveActor()
    {
        var commands = new GameCommand[]
        {
            new SetResearchQueue { Techs = ["Agriculture"] }, new PickFreeTechnology { Tech = "Agriculture" },
            new AdoptPolicy { Policy = "Tradition" }, new FoundPantheon(), new ChooseBeliefs(), new FoundReligion(),
            new ChooseFreeGreatPerson(), new MoveSpy(), new VoteDiplomatic(), new AckDiplomaticVoteResult(), new SetGoldToScience(),
        };
        foreach (var command in commands)
        {
            AssertRejected(command, CommandErrors.NotYourTurn, b.CivId);
            AssertRejected(command, CommandErrors.UnknownCiv, "Atlantis");
        }
    }

    // --- research

    [Fact]
    public void SetResearchQueueNeedsPrerequisitesInOrderAndCommitsTheQueue()
    {
        AssertRejected(new SetResearchQueue(), CommandErrors.InvalidTarget);
        AssertRejected(new SetResearchQueue { Techs = ["Nonsense"] }, CommandErrors.InvalidTarget);
        AssertRejected(new SetResearchQueue { Techs = ["Pottery", "Pottery"] }, CommandErrors.InvalidTarget);
        AssertRejected(new SetResearchQueue { Techs = ["Calendar", "Pottery"] }, CommandErrors.NotAllowed); // Calendar needs Pottery first
        AssertRejected(new SetResearchQueue { Techs = ["Calendar"] }, CommandErrors.NotAllowed);
        AssertRejected(new SetResearchQueue { Techs = ["Agriculture"] }, CommandErrors.NotAllowed); // researched already
        Ok(new SetResearchQueue { Techs = ["Pottery", "Calendar"] });
        Assert.Equal(["Pottery", "Calendar"], a.Tech.TechsToResearch);
        Ok(new SetResearchQueue { Techs = ["Mining"] });
        Assert.Equal(["Mining"], a.Tech.TechsToResearch);
    }

    [Fact]
    public void SetResearchQueueCompletesAResearchThatAlreadyHasEnoughScience()
    {
        a.Tech.TechsInProgress["Pottery"] = a.Tech.CostOfTech("Pottery");
        Ok(new SetResearchQueue { Techs = ["Pottery", "Calendar"] });
        Assert.True(a.Tech.IsResearched("Pottery"));
    }

    [Fact]
    public void FreeTechnologyModeBlocksTheQueueAndPicksOnlyResearchableTechs()
    {
        AssertRejected(new PickFreeTechnology { Tech = "Pottery" }, CommandErrors.NotAllowed);
        a.Tech.FreeTechs = 1;
        AssertRejected(new SetResearchQueue { Techs = ["Pottery"] }, CommandErrors.NotAllowed);
        AssertRejected(new PickFreeTechnology { Tech = "Calendar" }, CommandErrors.NotAllowed);
        AssertRejected(new PickFreeTechnology { Tech = "Nonsense" }, CommandErrors.InvalidTarget);
        Ok(new PickFreeTechnology { Tech = "Pottery" });
        Assert.True(a.Tech.IsResearched("Pottery"));
        Assert.Equal(0, a.Tech.FreeTechs);
        AssertRejected(new PickFreeTechnology { Tech = "Mining" }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void DebugAddTechnologyNeedsGodMode()
    {
        AssertRejected(new DebugAddTechnology { Tech = "Pottery" }, CommandErrors.NotAllowed);
        testGame.GameInfo.GameParameters.GodMode = true;
        AssertRejected(new DebugAddTechnology { Tech = "Nonsense" }, CommandErrors.InvalidTarget);
        a.Tech.FreeTechs = 1;
        AssertRejected(new DebugAddTechnology { Tech = "Pottery" }, CommandErrors.NotAllowed); // free-tech mode
        a.Tech.FreeTechs = 0;
        Ok(new DebugAddTechnology { Tech = "Pottery" });
        Assert.True(a.Tech.IsResearched("Pottery"));
        AssertRejected(new DebugAddTechnology { Tech = "Pottery" }, CommandErrors.NotAllowed);
    }

    // --- policies

    [Fact]
    public void AdoptPolicySpendsCultureOrAFreePolicyAndRejectsWhatThePickerDisables()
    {
        AssertRejected(new AdoptPolicy { Policy = "Nonsense" }, CommandErrors.InvalidTarget);
        AssertRejected(new AdoptPolicy { Policy = "Tradition" }, CommandErrors.NotAllowed); // no culture
        a.Policies.StoredCulture = 1000;
        AssertRejected(new AdoptPolicy { Policy = "Legalism" }, CommandErrors.NotAllowed); // prerequisite missing
        int cost = a.Policies.GetCultureNeededForNextPolicy();
        Ok(new AdoptPolicy { Policy = "Tradition" }); // a branch
        Assert.True(a.Policies.IsAdopted("Tradition"));
        Assert.Equal(1000 - cost, a.Policies.StoredCulture);
        AssertRejected(new AdoptPolicy { Policy = "Tradition" }, CommandErrors.NotAllowed); // twice

        a.Policies.StoredCulture = 0;
        a.Policies.FreePolicies = 1;
        Ok(new AdoptPolicy { Policy = "Aristocracy" });
        Assert.Equal(0, a.Policies.FreePolicies);
        Assert.Equal(0, a.Policies.StoredCulture);
    }

    [Fact]
    public void AdoptPolicyRejectsBranchCompletionPolicies()
    {
        a.Policies.FreePolicies = 5;
        var complete = testGame.Ruleset.Policies.Values.First(it => it.PolicyBranchType == PolicyObject.PolicyBranchTypes.BranchComplete);
        AssertRejected(new AdoptPolicy { Policy = complete.Name }, CommandErrors.NotAllowed);
    }

    // --- religion

    private Belief FirstFreePantheon() =>
        testGame.Ruleset.Beliefs.Values.First(it => it.Type == BeliefType.Pantheon && a.ReligionManager.GetReligionWithBelief(it) == null);

    [Fact]
    public void FoundPantheonPaysFaithAndTakesAnUnownedPantheonBelief()
    {
        var belief = FirstFreePantheon();
        AssertRejected(new FoundPantheon { Belief = belief.Name }, CommandErrors.NotAllowed); // no faith
        a.ReligionManager.StoredFaith = 1000;
        AssertRejected(new FoundPantheon { Belief = "Nonsense" }, CommandErrors.InvalidTarget);
        var founder = testGame.Ruleset.Beliefs.Values.First(it => it.Type == BeliefType.Founder);
        AssertRejected(new FoundPantheon { Belief = founder.Name }, CommandErrors.InvalidTarget);

        int cost = a.ReligionManager.FaithForPantheon();
        Ok(new FoundPantheon { Belief = belief.Name });
        Assert.Equal(ReligionState.Pantheon, a.ReligionManager.ReligionState);
        Assert.True(a.ReligionManager.Religion!.HasBelief(belief.Name));
        Assert.Equal(1000 - cost, a.ReligionManager.StoredFaith);
        Assert.False(a.ReligionManager.CanFoundOrExpandPantheon());
        AssertRejected(new FoundPantheon { Belief = belief.Name }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void AnotherCivCannotTakeABeliefThatIsOwned()
    {
        var belief = FirstFreePantheon();
        a.ReligionManager.StoredFaith = 1000;
        Ok(new FoundPantheon { Belief = belief.Name });
        testGame.GameInfo.CurrentPlayerCiv = b;
        testGame.GameInfo.CurrentPlayer = b.CivId;
        b.ReligionManager.StoredFaith = 1000;
        var result = session.Submit(b.CivId, new FoundPantheon { Belief = belief.Name });
        Assert.False(result.Accepted);
        Assert.Equal(CommandErrors.NotAllowed, result.Validation.Code);
    }

    private void StartFoundingReligion()
    {
        a.ReligionManager.StoredFaith = 1000;
        Ok(new FoundPantheon { Belief = FirstFreePantheon().Name });
        var prophet = testGame.AddUnit("Great Prophet", a, city.GetCenterTile());
        a.ReligionManager.FoundReligion(prophet);
        Assert.Equal(ReligionState.FoundingReligion, a.ReligionManager.ReligionState);
    }

    [Fact]
    public void FoundReligionValidatesIconNameAndBeliefSlots()
    {
        StartFoundingReligion();
        var slots = a.ReligionManager.GetBeliefsToChooseAtFounding();
        var good = new List<string>();
        foreach (var entry in slots)
        {
            for (int i = 0; i < entry.Value; i++)
            {
                var type = entry.Key == BeliefType.Any ? BeliefType.Follower : entry.Key;
                good.Add(testGame.Ruleset.Beliefs.Values
                    .First(it => it.Type == type && !good.Contains(it.Name) && a.ReligionManager.GetReligionWithBelief(it) == null).Name);
            }
        }
        AssertRejected(new ChooseBeliefs { Beliefs = good }, CommandErrors.NotAllowed); // founding is FoundReligion's job
        AssertRejected(new FoundReligion { ReligionName = "Nonsense", DisplayName = "Nonsense", Beliefs = good }, CommandErrors.InvalidTarget);
        AssertRejected(new FoundReligion { ReligionName = "Islam", DisplayName = "", Beliefs = good }, CommandErrors.InvalidTarget);
        AssertRejected(new FoundReligion { ReligionName = "Islam", DisplayName = "Buddhism", Beliefs = good }, CommandErrors.InvalidTarget);
        AssertRejected(new FoundReligion { ReligionName = "Islam", DisplayName = "[x] has [y]", Beliefs = good }, CommandErrors.InvalidTarget);
        AssertRejected(new FoundReligion { ReligionName = "Islam", DisplayName = new string('x', 33), Beliefs = good }, CommandErrors.InvalidTarget);
        AssertRejected(new FoundReligion { ReligionName = "Islam", DisplayName = "Islam", Beliefs = [.. good.Skip(1)] }, CommandErrors.InvalidTarget);
        AssertRejected(new FoundReligion { ReligionName = "Islam", DisplayName = "Islam", Beliefs = ["Nonsense", .. good.Skip(1)] }, CommandErrors.InvalidTarget);
        AssertRejected(new FoundReligion { ReligionName = "Islam", DisplayName = "Islam", Beliefs = [.. good.AsEnumerable().Reverse()] }, CommandErrors.InvalidTarget);

        Ok(new FoundReligion { ReligionName = "Islam", DisplayName = "My Faith", Beliefs = good });
        var religion = a.ReligionManager.Religion!;
        Assert.Equal("Islam", religion.Name);
        Assert.Equal("My Faith", religion.DisplayName);
        Assert.Equal(ReligionState.Religion, a.ReligionManager.ReligionState);
        foreach (var name in good) Assert.True(religion.HasBelief(name));
        Assert.Equal("Islam", city.Religion.ReligionThisIsTheHolyCityOf);
        AssertRejected(new FoundReligion { ReligionName = "Judaism", DisplayName = "Judaism", Beliefs = good }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void ChooseBeliefsUsesFreeBeliefsOfAFoundedReligion()
    {
        StartFoundingReligion();
        var names = new List<string>();
        foreach (var entry in a.ReligionManager.GetBeliefsToChooseAtFounding())
            for (int i = 0; i < entry.Value; i++)
            {
                var type = entry.Key == BeliefType.Any ? BeliefType.Follower : entry.Key;
                names.Add(testGame.Ruleset.Beliefs.Values.First(it => it.Type == type && !names.Contains(it.Name)
                    && a.ReligionManager.GetReligionWithBelief(it) == null).Name);
            }
        Ok(new FoundReligion { ReligionName = "Islam", DisplayName = "Islam", Beliefs = names });

        AssertRejected(new ChooseBeliefs(), CommandErrors.NotAllowed); // nothing to choose
        a.ReligionManager.FreeBeliefs["Follower"] = 1;
        var extra = testGame.Ruleset.Beliefs.Values.First(it => it.Type == BeliefType.Follower && !names.Contains(it.Name));
        AssertRejected(new ChooseBeliefs { Beliefs = [] }, CommandErrors.InvalidTarget);
        AssertRejected(new ChooseBeliefs { Beliefs = [names[0]] }, CommandErrors.InvalidTarget); // wrong type or owned
        var founder = testGame.Ruleset.Beliefs.Values.First(it => it.Type == BeliefType.Founder && !names.Contains(it.Name));
        AssertRejected(new ChooseBeliefs { Beliefs = [founder.Name] }, CommandErrors.InvalidTarget);
        Ok(new ChooseBeliefs { Beliefs = [extra.Name] });
        Assert.True(a.ReligionManager.Religion!.HasBelief(extra.Name));
        Assert.False(a.ReligionManager.HasFreeBeliefs());

        a.ReligionManager.FreeBeliefs["Any"] = 2;
        var anyBelief = testGame.Ruleset.Beliefs.Values.First(it => it.Type == BeliefType.Follower && !a.ReligionManager.Religion.HasBelief(it.Name)
            && a.ReligionManager.GetReligionWithBelief(it) == null);
        AssertRejected(new ChooseBeliefs { Beliefs = [anyBelief.Name, anyBelief.Name] }, CommandErrors.NotAllowed); // twice
        AssertRejected(new ChooseBeliefs { Beliefs = [anyBelief.Name, extra.Name] }, CommandErrors.NotAllowed); // already held
    }

    // --- great person

    [Fact]
    public void ChooseFreeGreatPersonGrantsTheUnitFromTheOptions()
    {
        AssertRejected(new ChooseFreeGreatPerson { Unit = "Great Scientist" }, CommandErrors.NotAllowed);
        a.GreatPeople.FreeGreatPeople = 1;
        AssertRejected(new ChooseFreeGreatPerson { Unit = "Warrior" }, CommandErrors.NotAllowed);
        int units = a.Units.GetCivUnits().Count();
        Ok(new ChooseFreeGreatPerson { Unit = "Great Scientist" });
        Assert.Equal(0, a.GreatPeople.FreeGreatPeople);
        Assert.Equal(units + 1, a.Units.GetCivUnits().Count());
        AssertRejected(new ChooseFreeGreatPerson { Unit = "Great Scientist" }, CommandErrors.NotAllowed);
    }

    // --- espionage

    private Unciv.Models.Spy SpyWithWorld(out CityObject foreign)
    {
        testGame.GameInfo.GameParameters.EspionageEnabled = true;
        foreign = testGame.AddCity(b, testGame.GetTile(6, 0));
        var spy = a.EspionageManager.AddSpy();
        return spy;
    }

    [Fact]
    public void MoveSpyNeedsEspionageAnExploredCityAndAFreeSlot()
    {
        var spy = SpyWithWorld(out var foreign);
        testGame.GameInfo.GameParameters.EspionageEnabled = false;
        AssertRejected(new MoveSpy { Spy = spy.Name, CityId = foreign.Id }, CommandErrors.NotAllowed);
        testGame.GameInfo.GameParameters.EspionageEnabled = true;
        AssertRejected(new MoveSpy { Spy = "Nobody", CityId = foreign.Id }, CommandErrors.InvalidTarget);
        AssertRejected(new MoveSpy { Spy = spy.Name, CityId = "nope" }, CommandErrors.UnknownCity);
        AssertRejected(new MoveSpy { Spy = spy.Name, CityId = null }, CommandErrors.NotAllowed); // already in the hideout
        AssertRejected(new MoveSpy { Spy = spy.Name, CityId = foreign.Id }, CommandErrors.InvalidTarget); // not explored

        foreign.GetCenterTile().SetExplored(a, true);
        Ok(new MoveSpy { Spy = spy.Name, CityId = foreign.Id });
        Assert.Same(foreign, spy.GetCityOrNull());
        Assert.Equal(Unciv.Models.SpyAction.Moving, spy.Action);
        AssertRejected(new MoveSpy { Spy = spy.Name, CityId = foreign.Id }, CommandErrors.NotAllowed); // already there

        var second = a.EspionageManager.AddSpy();
        var dead = a.EspionageManager.AddSpy();
        dead.SetAction(Unciv.Models.SpyAction.Dead);
        AssertRejected(new MoveSpy { Spy = dead.Name, CityId = null }, CommandErrors.NotAllowed);
        AssertRejected(new MoveSpy { Spy = second.Name, CityId = foreign.Id }, CommandErrors.NotAllowed); // occupied

        Ok(new MoveSpy { Spy = spy.Name, CityId = null });
        Assert.Null(spy.GetCityOrNull());
        Assert.True(spy.IsIdle());
    }

    // --- world congress

    [Fact]
    public void VoteDiplomaticRecordsAVoteOrAbstention()
    {
        AssertRejected(new VoteDiplomatic { CivId = b.CivId }, CommandErrors.NotAllowed); // no vote open
        a.AddFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString(), 0);
        AssertRejected(new VoteDiplomatic { CivId = b.CivId }, CommandErrors.InvalidTarget); // not met
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        AssertRejected(new VoteDiplomatic { CivId = "Atlantis" }, CommandErrors.InvalidTarget);
        AssertRejected(new VoteDiplomatic { CivId = a.CivId }, CommandErrors.InvalidTarget);
        Ok(new VoteDiplomatic { CivId = b.CivId });
        Assert.Equal(b.CivId, testGame.GameInfo.DiplomaticVictoryVotesCast[a.CivId]);
        Assert.False(a.MayVoteForDiplomaticVictory());
        AssertRejected(new VoteDiplomatic { CivId = null }, CommandErrors.NotAllowed); // already voted
    }

    [Fact]
    public void VoteDiplomaticCanAbstain()
    {
        a.AddFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString(), 0);
        Ok(new VoteDiplomatic { CivId = null });
        Assert.True(testGame.GameInfo.DiplomaticVictoryVotesCast.ContainsKey(a.CivId));
        Assert.Null(testGame.GameInfo.DiplomaticVictoryVotesCast[a.CivId]);
    }

    // --- vote result (C053)

    [Fact]
    public void AckDiplomaticVoteResultClearsTheResultAndNeedsOne()
    {
        AssertRejected(new AckDiplomaticVoteResult(), CommandErrors.StaleReference); // nothing to acknowledge
        a.AddFlag(CivFlags.ShowDiplomaticVotingResults.ToString(), 1); // not due yet
        AssertRejected(new AckDiplomaticVoteResult(), CommandErrors.StaleReference);
        a.AddFlag(CivFlags.ShowDiplomaticVotingResults.ToString(), 0);
        Assert.True(a.ShouldShowDiplomaticVotingResults());
        Ok(new AckDiplomaticVoteResult());
        Assert.False(a.ShouldShowDiplomaticVotingResults());
        Assert.Equal(-1, a.FlagsCountdown[CivFlags.ShowDiplomaticVotingResults.ToString()]);
        Assert.False(b.FlagsCountdown.ContainsKey(CivFlags.ShowDiplomaticVotingResults.ToString()));
        AssertRejected(new AckDiplomaticVoteResult(), CommandErrors.StaleReference); // only once
    }

    [Fact]
    public void AckDiplomaticVoteResultIsInTheLogAndReplaysToTheSameState()
    {
        a.AddFlag(CivFlags.ShowDiplomaticVotingResults.ToString(), 0);
        var twin = new EmpireCommandTests();
        twin.a.AddFlag(CivFlags.ShowDiplomaticVotingResults.ToString(), 0);
        Assert.Equal(Hash(), twin.Hash());
        Ok(new AckDiplomaticVoteResult());
        var logged = Assert.Single(session.Log);
        Assert.Equal("ack-diplomatic-vote-result", logged.Type);
        Assert.NotEqual(Hash(), twin.Hash());
        twin.session.Replay(JsonSerializer.Deserialize<CommandEnvelope>(JsonSerializer.Serialize(logged, UncivJson.DefaultOptions), UncivJson.DefaultOptions)!);
        Assert.Equal(Hash(), twin.Hash());
    }

    // --- gold to science

    [Fact]
    public void SetGoldToScienceStoresAWholePercentStep()
    {
        AssertRejected(new SetGoldToScience { Percent = 35 }, CommandErrors.InvalidTarget);
        AssertRejected(new SetGoldToScience { Percent = -10 }, CommandErrors.InvalidTarget);
        AssertRejected(new SetGoldToScience { Percent = 110 }, CommandErrors.InvalidTarget);
        Ok(new SetGoldToScience { Percent = 30 });
        Assert.Equal(0.3f, a.Tech.GoldPercentConvertedToScience);
        Ok(new SetGoldToScience { Percent = 100 });
        Assert.Equal(1f, a.Tech.GoldPercentConvertedToScience);
    }

    [Fact]
    public void SetGoldToScienceNeedsTheRulesetOption()
    {
        var plain = new TestGame();
        plain.MakeHexagonalMap(3);
        var civ = plain.AddCiv(isPlayer: true);
        plain.AddCity(civ, plain.GetTile(0, 0));
        plain.GameInfo.CurrentPlayerCiv = civ;
        plain.GameInfo.CurrentPlayer = civ.CivId;
        var result = new GameSession(plain.GameInfo).Submit(civ.CivId, new SetGoldToScience { Percent = 10 });
        Assert.False(result.Accepted);
        Assert.Equal(CommandErrors.NotAllowed, result.Validation.Code);
    }
}
