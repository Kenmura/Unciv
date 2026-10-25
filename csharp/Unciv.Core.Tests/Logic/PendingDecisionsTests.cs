using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Logic;

// T184: PendingDecisions.For mirrors NextTurnAction order and each decision disappears when its state is resolved.
[Collection("RulesetCache")]
public sealed class PendingDecisionsTests : IDisposable
{
    private readonly RulesetClass ruleset;
    private readonly GameInfo game;
    private readonly Civilization babylon;

    public PendingDecisionsTests()
    {
        RulesetCache.Clear();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(root.FullName, "android", "assets", "jsons"))) root = root.Parent!;
        var jsons = Path.Combine(root.FullName, "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
        game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.AI)],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            1L, 1000L);
        babylon = game.GetCivilization("Babylon");
        babylon.PopupAlerts.Clear(); // the intro
        foreach (var unit in babylon.Units.GetCivUnits()) unit.Due = false;
    }

    public void Dispose() => RulesetCache.Clear();

    // ReligionState has a private setter (it is driven by great prophets); tests put the civ straight into the state they need.
    private void SetReligionState(ReligionState state) =>
        typeof(ReligionManager).GetProperty(nameof(ReligionManager.ReligionState))!.SetValue(babylon.ReligionManager, state);

    private static List<DecisionKind> Kinds(Civilization civ) => PendingDecisions.For(civ).Select(it => it.Kind).ToList();

    [Fact]
    public void FreshGameHasNothingToDecide()
    {
        Assert.Empty(PendingDecisions.For(babylon));
        Assert.Empty(PendingDecisions.BlockingFor(babylon));
    }

    [Fact]
    public void FreeGreatPersonBlocksUntilChosen()
    {
        babylon.GreatPeople.FreeGreatPeople = 1;
        var decision = Assert.Single(PendingDecisions.For(babylon));
        Assert.Equal(new Decision(DecisionKind.FreeGreatPerson, "", "choose-free-great-person", true), decision);
        babylon.GreatPeople.FreeGreatPeople = 0;
        Assert.Empty(PendingDecisions.For(babylon));
    }

    [Fact]
    public void CityWithEmptyQueueBlocksUnlessPuppet()
    {
        var settler = babylon.Units.GetCivUnits().First(it => it.Name == "Settler");
        var city = babylon.AddCity(settler.GetTile().Position, settler);
        city.CityConstructions.EditQueue(it => it.Add("Warrior"));
        Assert.DoesNotContain(DecisionKind.CityProduction, Kinds(babylon));

        city.CityConstructions.EditQueue(it => it.Clear());
        Assert.Equal(new Decision(DecisionKind.CityProduction, city.Name, "queue-construction", true, -1), PendingDecisions.For(babylon).Single(it => it.Kind == DecisionKind.CityProduction));

        city.IsPuppet = true;
        Assert.DoesNotContain(DecisionKind.CityProduction, Kinds(babylon));

        city.IsPuppet = false;
        Assert.Contains(DecisionKind.CityProduction, Kinds(babylon));
    }

    [Fact]
    public void TechnologyIsResolvedByTheFreeTechCommandWhenOneIsPending()
    {
        babylon.Tech.FreeTechs = 1;
        var decision = PendingDecisions.For(babylon).Single(it => it.Kind == DecisionKind.Technology);
        Assert.Equal("pick-free-technology", decision.ResolvedBy);
        Assert.True(decision.Blocking);
        babylon.Tech.FreeTechs = 0;
        Assert.DoesNotContain(DecisionKind.Technology, Kinds(babylon));
    }

    [Fact]
    public void PolicyShowsWhileAdoptable()
    {
        babylon.Policies.StoredCulture = 100_000;
        babylon.Policies.ShouldOpenPolicyPicker = true;
        Assert.Contains(new Decision(DecisionKind.Policy, "", "adopt-policy", true), PendingDecisions.For(babylon));
        babylon.Policies.ShouldOpenPolicyPicker = false;
        Assert.DoesNotContain(DecisionKind.Policy, Kinds(babylon));
    }

    [Fact]
    public void MoveSpiesNeedsEspionageEnabled()
    {
        var settler = babylon.Units.GetCivUnits().First(it => it.Name == "Settler");
        babylon.AddCity(settler.GetTile().Position, settler);
        babylon.EspionageManager.AddSpy();
        Assert.DoesNotContain(DecisionKind.MoveSpies, Kinds(babylon));
        game.GameParameters.EspionageEnabled = true;
        Assert.Contains(DecisionKind.MoveSpies, Kinds(babylon));
        babylon.EspionageManager.DismissedShouldMoveSpies = true;
        Assert.DoesNotContain(DecisionKind.MoveSpies, Kinds(babylon));
    }

    [Theory]
    [InlineData(ReligionState.FoundingReligion, DecisionKind.FoundReligion)]
    [InlineData(ReligionState.EnhancingReligion, DecisionKind.EnhanceReligion)]
    public void ReligionStatesAskForBeliefs(ReligionState state, DecisionKind kind)
    {
        SetReligionState(state);
        Assert.Contains(kind, Kinds(babylon));
        SetReligionState(ReligionState.None);
        Assert.DoesNotContain(kind, Kinds(babylon));
    }

    [Fact]
    public void FreeBeliefsAskForAReform()
    {
        babylon.ReligionManager.FreeBeliefs["Follower"] = 1;
        Assert.Contains(DecisionKind.ReformReligion, Kinds(babylon));
        babylon.ReligionManager.FreeBeliefs.Clear();
        Assert.DoesNotContain(DecisionKind.ReformReligion, Kinds(babylon));
    }

    [Fact]
    public void DiplomaticVoteIsPending()
    {
        babylon.AddFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString(), 0);
        Assert.Contains(new Decision(DecisionKind.DiplomaticVote, "", "vote-diplomatic", true), PendingDecisions.For(babylon));
    }

    [Fact]
    public void AutomatedUnitsMustMoveFirstUnlessTheSettingMovesThemAtTurnStart()
    {
        var unit = babylon.Units.GetCivUnits().First();
        unit.CurrentMovement = 2f;
        unit.Action = "moveTo 1,1";
        Assert.Contains(new Decision(DecisionKind.MoveAutomatedUnits, "", "move-automated-units", true), PendingDecisions.For(babylon));

        babylon.HasMovedAutomatedUnits = true;
        Assert.DoesNotContain(DecisionKind.MoveAutomatedUnits, Kinds(babylon));
        babylon.HasMovedAutomatedUnits = false;

        unit.CurrentMovement = 0f;
        Assert.DoesNotContain(DecisionKind.MoveAutomatedUnits, Kinds(babylon));
        unit.CurrentMovement = 2f;

        unit.Action = null;
        Assert.DoesNotContain(DecisionKind.MoveAutomatedUnits, Kinds(babylon));

        unit.Action = "moveTo 1,1";
        game.SetPlayerSettings(babylon.CivId, new PlayerSettings { AutomatedUnitsMoveOnTurnStart = true });
        Assert.DoesNotContain(DecisionKind.MoveAutomatedUnits, Kinds(babylon));
    }

    [Fact]
    public void DueUnitsAreListedButDoNotBlock()
    {
        var unit = babylon.Units.GetCivUnits().First();
        unit.Due = true;
        unit.Action = null;
        var decision = PendingDecisions.For(babylon).Single(it => it.Kind == DecisionKind.DueUnits);
        Assert.False(decision.Blocking);
        foreach (var each in babylon.Units.GetCivUnits()) each.Due = false;
        Assert.DoesNotContain(DecisionKind.DueUnits, Kinds(babylon));
    }

    [Fact]
    public void AlertsSplitIntoChoicesAndInformationAndOnlyConquestBlocks()
    {
        babylon.PopupAlerts.Add(new PopupAlert(AlertType.WonderBuilt, "x"));
        babylon.PopupAlerts.Add(new PopupAlert(AlertType.DiplomaticMarriage, "Greece"));
        babylon.PopupAlerts.Add(new PopupAlert(AlertType.CityConquered, "c"));
        Assert.Equal(
            [
                new Decision(DecisionKind.Alert, "WonderBuilt", "dismiss-alert", false, 0),
                new Decision(DecisionKind.Alert, "DiplomaticMarriage", "answer-alert", false, 1),
                new Decision(DecisionKind.Alert, "CityConquered", "answer-alert", true, 2),
            ],
            PendingDecisions.For(babylon));
        Assert.Single(PendingDecisions.BlockingFor(babylon));
        babylon.PopupAlerts.Clear();
        Assert.Empty(PendingDecisions.For(babylon));
    }

    [Fact]
    public void TradeRequestsAreListedByRequester()
    {
        babylon.TradeRequests.Add(new TradeRequest { RequestingCiv = "Greece", Trade = new Trade() });
        Assert.Equal(new Decision(DecisionKind.TradeRequest, "Greece", "respond-trade-request", false, 0), Assert.Single(PendingDecisions.For(babylon)));
    }

    [Fact]
    public void OrderFollowsNextTurnAction()
    {
        babylon.GreatPeople.FreeGreatPeople = 1;
        babylon.Tech.FreeTechs = 1;
        SetReligionState(ReligionState.FoundingReligion);
        babylon.PopupAlerts.Add(new PopupAlert(AlertType.GoldenAge, ""));
        babylon.TradeRequests.Add(new TradeRequest { RequestingCiv = "Greece", Trade = new Trade() });
        Assert.Equal(
            [DecisionKind.FreeGreatPerson, DecisionKind.Technology, DecisionKind.FoundReligion, DecisionKind.Alert, DecisionKind.TradeRequest],
            Kinds(babylon));
    }

    [Fact]
    public void SpectatorsDecideNothing()
    {
        var spectatorGame = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = [new Player(Constants.Spectator, PlayerType.Human), new Player("Greece", PlayerType.AI), new Player("Rome", PlayerType.AI)],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            1L, 1000L);
        var spectator = spectatorGame.Civilizations.Single(it => it.IsSpectator());
        spectator.PopupAlerts.Add(new PopupAlert(AlertType.GoldenAge, ""));
        Assert.Empty(PendingDecisions.For(spectator));
    }

    [Fact]
    public void QueryDoesNotChangeTheGame()
    {
        babylon.PopupAlerts.Add(new PopupAlert(AlertType.GoldenAge, ""));
        babylon.GreatPeople.FreeGreatPeople = 1;
        var before = UncivFiles.GameInfoToString(game, forceZip: false);
        _ = PendingDecisions.For(babylon);
        Assert.Equal(before, UncivFiles.GameInfoToString(game, forceZip: false));
    }
}
