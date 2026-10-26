using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Commands.Turn;

// T226: the multiplayer game-over and forced-end rules: who may switch a won game to one more turn, what a defeated human sees, and how a forced end of
// turn settles the blocking decisions a human left open.
[Collection("RulesetCache")]
public sealed class MultiplayerGameEndTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public MultiplayerGameEndTests()
    {
        RulesetCache.Clear();
        var jsons = Path.Combine(RepoRoot(), "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "android", "assets", "jsons"))) return dir.FullName;
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private GameInfo NewGame(bool simultaneous = true, string? baseRuleset = null)
    {
        var rules = baseRuleset == null ? ruleset : RulesetCache.Get(baseRuleset).NotNull();
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = rules.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = rules.Victories.Keys.ToList(),
                    Players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.Human), new Player("Rome", PlayerType.AI)],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            1L, 1000L);
        if (simultaneous) game.StartSimultaneousTurns();
        return game;
    }

    private static GameSession Session(GameInfo game)
    {
        var now = 5000L;
        return new GameSession(game) { Clock = () => now += 1000 };
    }

    private static string Save(GameInfo game) => UncivFiles.GameInfoToString(game, forceZip: false);

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(Save(game)));

    private static void Won(GameInfo game) => game.VictoryData = new VictoryData { WinningCiv = "Greece", VictoryType = "Scientific", VictoryTurn = 1 };

    private static void Defeat(GameInfo game, string civId)
    {
        foreach (var unit in game.GetCivilization(civId).Units.GetCivUnits().ToList()) unit.Destroy();
        Assert.True(game.GetCivilization(civId).IsDefeated());
    }

    private static void Rejected(GameSession session, string actor, GameCommand command, string code)
    {
        var (hash, count) = (Hash(session.Game), session.Log.Count);
        var result = actor == CommandContext.ServerActor ? session.SubmitAsServer(command) : session.Submit(actor, command);
        Assert.False(result.Accepted);
        Assert.Equal(code, result.Validation.Code);
        Assert.Equal((hash, count), (Hash(session.Game), session.Log.Count)); // a rejection is pure
    }

    // ----- One More Turn authority -----

    [Fact]
    public void WithoutAHostCivOnlyTheServerMaySwitchASimultaneousGameToOneMoreTurn()
    {
        var game = NewGame();
        var session = Session(game);
        Won(game);
        Rejected(session, "Babylon", new OneMoreTurn(), CommandErrors.NotAllowed);
        Rejected(session, "Greece", new OneMoreTurn(), CommandErrors.NotAllowed);
        Assert.False(game.OneMoreTurnMode);
        Assert.True(session.SubmitAsServer(new OneMoreTurn()).Accepted);
        Assert.True(game.OneMoreTurnMode);
        Rejected(session, CommandContext.ServerActor, new OneMoreTurn(), CommandErrors.NotAllowed); // nothing left to continue past
    }

    [Fact]
    public void OnlyTheHostHumanMayContinueAndNotWithoutAVictory()
    {
        var game = NewGame();
        var session = Session(game);
        game.TurnState!.HostCivId = "Greece";
        Rejected(session, "Greece", new OneMoreTurn(), CommandErrors.NotAllowed); // no victory yet
        Won(game);
        Rejected(session, "Babylon", new OneMoreTurn(), CommandErrors.NotAllowed);
        Rejected(session, "Rome", new OneMoreTurn(), CommandErrors.NotHuman);
        Assert.True(session.Submit("Greece", new OneMoreTurn()).Accepted);
        Assert.True(game.OneMoreTurnMode);
    }

    [Fact]
    public void ADefeatedHostCannotContinueTheGameButTheServerCan()
    {
        var game = NewGame();
        var session = Session(game);
        game.TurnState!.HostCivId = "Greece";
        Won(game);
        Defeat(game, "Greece");
        Rejected(session, "Greece", new OneMoreTurn(), CommandErrors.NotAllowed);
        Assert.True(session.SubmitAsServer(new OneMoreTurn()).Accepted);
    }

    [Fact]
    public void ASequentialGameKeepsTheKotlinRuleAnyHumanMayContinue()
    {
        var game = NewGame(simultaneous: false);
        var session = Session(game);
        Won(game);
        Rejected(session, CommandContext.ServerActor, new OneMoreTurn(), CommandErrors.NotHuman);
        Assert.True(session.Submit("Greece", new OneMoreTurn()).Accepted);
    }

    // ----- defeated humans -----

    [Fact]
    public void ADefeatedHumanSeesOnlyItsOwnViewByDefaultAndTheWholeMapWhenTheLobbyAllowsIt()
    {
        var game = NewGame();
        game.Turns = 1; // vision of a defeated civ is not granted during setup
        var greece = game.GetCivilization("Greece");
        var unexplored = game.TileMap.Values.First(it => !it.IsExplored(greece));
        Defeat(game, "Greece");
        Assert.False(greece.HasSpectatorVision());
        greece.RevealMapWhenDefeated();
        Assert.False(unexplored.IsExplored(greece));

        game.TurnState!.DefeatedSeeFullMap = true;
        Assert.True(greece.HasSpectatorVision());
        Assert.False(game.GetCivilization("Babylon").HasSpectatorVision());
        greece.RevealMapWhenDefeated();
        Assert.True(unexplored.IsExplored(greece));
    }

    [Fact]
    public void TheFullMapIsOnlyForDefeatedHumansOfAStartedGameAndSequentialGamesAreUntouched()
    {
        var game = NewGame();
        game.TurnState!.DefeatedSeeFullMap = true;
        Defeat(game, "Rome");
        Defeat(game, "Greece");
        Assert.False(game.GetCivilization("Rome").HasSpectatorVision()); // an AI is not a spectator
        Assert.False(game.GetCivilization("Greece").HasSpectatorVision()); // setup: Turns is 0
        game.Turns = 1;
        Assert.True(game.GetCivilization("Greece").HasSpectatorVision());
        game.TurnState.Mode = TurnMode.Sequential;
        Assert.False(game.GetCivilization("Greece").HasSpectatorVision());
    }

    [Fact]
    public void TheLobbySettingsSurviveCloneAndSaveLoad()
    {
        var game = NewGame();
        game.TurnState!.HostCivId = "Babylon";
        game.TurnState.DefeatedSeeFullMap = true;
        var clone = game.TurnState.Clone();
        Assert.Equal(("Babylon", true), (clone.HostCivId, clone.DefeatedSeeFullMap));
        var loaded = UncivFiles.GameInfoFromString(Save(game));
        Assert.Equal(("Babylon", true), (loaded.TurnState!.HostCivId, loaded.TurnState.DefeatedSeeFullMap));
        Assert.DoesNotContain("hostCiv", Save(NewGame())); // a default game's save is unchanged
    }

    // ----- ForceEndTurn settles blocking decisions -----

    private static Unciv.Logic.Civilization.Civilization Found(GameInfo game, string civId)
    {
        var civ = game.GetCivilization(civId);
        var settler = civ.Units.GetCivUnits().First(it => it.Name == "Settler");
        civ.AddCity(settler.GetTile().Position, settler);
        return civ;
    }

    private static IEnumerable<DecisionAutoResolved> Resolved(CommandResult result) => result.Events.OfType<DecisionAutoResolved>();

    [Fact]
    public void ForceEndTurnSettlesProductionFreeGreatPersonAndTechLikeTheAi()
    {
        var game = NewGame();
        var session = Session(game);
        var babylon = Found(game, "Babylon");
        var city = babylon.Cities.Single();
        city.CityConstructions.EditQueue(it => it.Clear());
        babylon.GreatPeople.FreeGreatPeople = 1;
        babylon.Tech.FreeTechs = 1;
        Assert.Equal(3, PendingDecisions.BlockingFor(babylon).Count);

        var result = session.SubmitAsServer(new ForceEndTurn { CivId = "Babylon" });
        Assert.True(result.Accepted, result.Validation.Message);
        Assert.Empty(PendingDecisions.BlockingFor(babylon));
        Assert.NotEmpty(city.CityConstructions.CurrentConstructionName());
        Assert.Equal(0, babylon.GreatPeople.FreeGreatPeople);
        Assert.Equal(0, babylon.Tech.FreeTechs);
        var kinds = Resolved(result).Select(it => it.Kind).ToList();
        Assert.Equal([nameof(DecisionKind.FreeGreatPerson), nameof(DecisionKind.CityProduction), nameof(DecisionKind.Technology)], kinds);
        Assert.All(Resolved(result), it => Assert.Equal("Babylon", it.CivId));
        Assert.Contains(new PlayerEndedTurn("Babylon", game.Turns), result.Events); // and the turn is ended
        Assert.Equal(city.CityConstructions.CurrentConstructionName(), Resolved(result).Single(it => it.Kind == nameof(DecisionKind.CityProduction)).Resolution);
    }

    [Fact]
    public void ForceEndTurnPicksAConqueredCityTreatmentAndTheLogReplaysToTheSameState()
    {
        ulong Run(out List<DecisionAutoResolved> events)
        {
            var game = NewGame();
            var session = Session(game);
            var greece = Found(game, "Greece");
            var rome = game.GetCivilization("Rome");
            var city = greece.Cities.Single();
            city.CityConstructions.EditQueue(it => it.Add("Warrior"));
            greece.PopupAlerts.Add(new PopupAlert(AlertType.CityConquered, city.Id));
            Assert.Contains(PendingDecisions.BlockingFor(greece), it => it.Kind == DecisionKind.Alert);
            var result = session.SubmitAsServer(new ForceEndTurn { CivId = "Greece" });
            Assert.True(result.Accepted, result.Validation.Message);
            events = Resolved(result).ToList();
            Assert.DoesNotContain(greece.PopupAlerts, it => it.Type == AlertType.CityConquered);
            Assert.True(city.IsPuppet);
            Assert.NotNull(rome);
            return Hash(game);
        }

        var first = Run(out var firstEvents);
        var second = Run(out var secondEvents);
        Assert.Equal(first, second);
        Assert.Equal(firstEvents, secondEvents);
        var only = Assert.Single(firstEvents, it => it.Kind == nameof(DecisionKind.Alert));
        Assert.Equal((nameof(DecisionKind.Alert), nameof(AlertType.CityConquered), "puppet"), (only.Kind, only.Subject, only.Resolution));
    }

    [Fact]
    public void ForceEndTurnLeavesWhatCannotBeSettledAndDismissesIdleSpies()
    {
        var game = NewGame();
        var session = Session(game);
        game.GameParameters.EspionageEnabled = true;
        var babylon = Found(game, "Babylon");
        babylon.GreatPeople.FreeGreatPeople = 1;
        babylon.Cities.Single().CityConstructions.EditQueue(it => it.Add("Warrior"));
        babylon.EspionageManager.AddSpy();
        Assert.Contains(DecisionKind.MoveSpies, PendingDecisions.BlockingFor(babylon).Select(it => it.Kind));
        var result = session.SubmitAsServer(new ForceEndTurn { CivId = "Babylon" });
        Assert.True(result.Accepted);
        Assert.True(babylon.EspionageManager.DismissedShouldMoveSpies);
        Assert.DoesNotContain(DecisionKind.MoveSpies, PendingDecisions.BlockingFor(babylon).Select(it => it.Kind));
    }

    [Fact]
    public void ForceEndTurnAdoptsAPolicyWhenOneIsPending()
    {
        var game = NewGame();
        var session = Session(game);
        var babylon = Found(game, "Babylon");
        babylon.Cities.Single().CityConstructions.EditQueue(it => it.Add("Warrior"));
        babylon.Policies.StoredCulture = 100_000;
        babylon.Policies.ShouldOpenPolicyPicker = true;
        Assert.Contains(DecisionKind.Policy, PendingDecisions.BlockingFor(babylon).Select(it => it.Kind));
        var result = session.SubmitAsServer(new ForceEndTurn { CivId = "Babylon" });
        Assert.True(result.Accepted);
        Assert.DoesNotContain(DecisionKind.Policy, PendingDecisions.BlockingFor(babylon).Select(it => it.Kind));
        Assert.Contains(Resolved(result), it => it.Kind == nameof(DecisionKind.Policy));
        Assert.NotEmpty(babylon.Policies.AdoptedPolicies);
    }

    [Fact]
    public void ForceEndTurnVotesAndFoundsAPantheon()
    {
        var game = NewGame(baseRuleset: "Civ V - Gods & Kings");
        var session = Session(game);
        var babylon = Found(game, "Babylon");
        babylon.Cities.Single().CityConstructions.EditQueue(it => it.Add("Warrior"));
        babylon.AddFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString(), 0);
        babylon.ReligionManager.StoredFaith = 100_000;
        var kinds = PendingDecisions.BlockingFor(babylon).Select(it => it.Kind).ToList();
        Assert.Contains(DecisionKind.DiplomaticVote, kinds);
        Assert.Contains(DecisionKind.Pantheon, kinds);
        var result = session.SubmitAsServer(new ForceEndTurn { CivId = "Babylon" });
        Assert.True(result.Accepted, result.Validation.Message);
        var resolved = Resolved(result).Select(it => it.Kind).ToList();
        Assert.Contains(nameof(DecisionKind.DiplomaticVote), resolved);
        Assert.Contains(nameof(DecisionKind.Pantheon), resolved);
        Assert.Equal(ReligionState.Pantheon, babylon.ReligionManager.ReligionState);
        Assert.DoesNotContain(DecisionKind.DiplomaticVote, PendingDecisions.BlockingFor(babylon).Select(it => it.Kind));
    }

    [Fact]
    public void AnAlertThatCannotBeAnsweredStaysPendingAndDoesNotStopTheForcedEnd()
    {
        var game = NewGame();
        var session = Session(game);
        var greece = Found(game, "Greece");
        greece.Cities.Single().CityConstructions.EditQueue(it => it.Add("Warrior"));
        greece.PopupAlerts.Add(new PopupAlert(AlertType.CityConquered, "no such city"));
        var result = session.SubmitAsServer(new ForceEndTurn { CivId = "Greece" });
        Assert.True(result.Accepted, result.Validation.Message);
        Assert.DoesNotContain(Resolved(result), it => it.Kind == nameof(DecisionKind.Alert));
        Assert.Contains(greece.PopupAlerts, it => it.Type == AlertType.CityConquered);
        Assert.Contains(new PlayerEndedTurn("Greece", game.Turns), result.Events);
    }
}
