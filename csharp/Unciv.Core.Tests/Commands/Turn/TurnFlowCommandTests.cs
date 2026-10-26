using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Commands.Turn;

// T187: the turn commands on a real started game (Babylon human, Greece and Rome AI, barbarians). NextTurn == EndTurn + RunAiTurn* + BeginTurn is the acceptance test.
[Collection("RulesetCache")]
public sealed class TurnFlowCommandTests : IDisposable
{
    private const long Now = 5000L;
    private readonly RulesetClass ruleset;

    public TurnFlowCommandTests()
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

    private GameInfo NewGame() => GameStarter.StartNewGame(
        new GameSetupInfo(
            new GameParameters
            {
                BaseRuleset = ruleset.Name,
                NumberOfCityStates = 0,
                VictoryTypes = ruleset.Victories.Keys.ToList(),
                Players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.AI), new Player("Rome", PlayerType.AI)],
            },
            new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
        1L, 1000L);

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    private static GameSession Session(GameInfo game, Func<long>? clock = null) => new(game) { Clock = clock ?? (() => Now) };

    private static void Ok(CommandResult result) => Assert.True(result.Accepted, result.Validation.Message);

    private static void RunAutoPlayers(GameSession session)
    {
        while (session.Game.TurnPhase is TurnPhase.AutoPlayers(var next))
            Ok(session.SubmitAsServer(new RunAiTurn { CivId = next.CivId }));
    }

    private static void Rejected(CommandResult result, string code)
    {
        Assert.False(result.Accepted);
        Assert.Equal(code, result.Validation.Code);
    }

    [Fact]
    public void CommandsAreRegisteredAndRoundTripThroughJson()
    {
        var names = GameCommandRegistry.Scan().ToDictionary(it => it.Name, it => it.CommandClass);
        Assert.Equal(typeof(EndTurn), names["end-turn"]);
        Assert.Equal(typeof(ForceEndTurn), names["force-end-turn"]);
        Assert.Equal(typeof(RunAiTurn), names["run-ai-turn"]);
        Assert.Equal(typeof(BeginTurn), names["begin-turn"]);
        Assert.Equal(typeof(MoveAutomatedUnits), names["move-automated-units"]);
        Assert.Equal(typeof(SetPlayerSettings), names["set-player-settings"]);
        Assert.Equal(typeof(AckAlert), names["ack-alert"]);
        Assert.Equal(typeof(RespondToAlert), names["respond-to-alert"]);
        Assert.Equal(typeof(DismissPolicyPicker), names["dismiss-policy-picker"]);
        Assert.Equal(typeof(DismissMoveSpies), names["dismiss-move-spies"]);
        Assert.Equal(typeof(Resign), names["resign"]);
        Assert.Equal(typeof(OneMoreTurn), names["one-more-turn"]);

        var ack = JsonSerializer.Deserialize<AckAlert>("""{"index":2,"alertType":"WonderBuilt","value":"x"}""", UncivJson.DefaultOptions)!;
        Assert.Equal((2, AlertType.WonderBuilt, "x"), (ack.Index, ack.AlertType, ack.Value));
    }

    [Fact]
    public void CommandsReproduceNextTurnExactly()
    {
        var viaNextTurn = NewGame();
        viaNextTurn.NextTurn(Now);

        var game = NewGame();
        var session = Session(game);
        Ok(session.Submit("Babylon", new EndTurn()));
        Assert.IsType<TurnPhase.AutoPlayers>(game.TurnPhase);
        RunAutoPlayers(session);
        Assert.Equal(new TurnPhase.AwaitingInput(game.GetCivilization("Babylon")), game.TurnPhase);
        Ok(session.SubmitAsServer(new BeginTurn { CivId = "Babylon" }));

        Assert.Equal(Hash(viaNextTurn), Hash(game));
        Assert.Equal(Now, game.CurrentTurnStartTime);
        Assert.Equal(5, session.Log.Count); // end, Greece, Rome, barbarians, begin
    }

    [Fact]
    public void ClockStampsTheLoggedTimeAndDiscardsTheClientsValue()
    {
        var game = NewGame();
        var session = Session(game, () => 7000L);
        var envelope = new CommandEnvelope(99, "end-turn", "Babylon", JsonDocument.Parse("""{"Now":1,"now":2}""").RootElement.Clone());
        Ok(session.Submit(envelope));
        Assert.Equal(7000L, session.Log[0].Payload.GetProperty("now").GetInt64());
        Assert.False(session.Log[0].Payload.TryGetProperty("Now", out _));
        Assert.Equal(6, game.GetCivilization("Babylon").TotalTurnTimeSeconds); // (7000 - 1000) / 1000

        var duplicate = new CommandEnvelope(0, "resign", "Greece", JsonDocument.Parse("""{"now":1,"now":2}""").RootElement.Clone());
        Rejected(session.Submit(duplicate), CommandErrors.NotHuman); // stamping must not throw on duplicate keys
        var notAnObject = new CommandEnvelope(0, "end-turn", "Babylon", JsonDocument.Parse("[]").RootElement.Clone());
        Rejected(session.Submit(notAnObject), CommandCodes.MalformedCommand);
        var other = new CommandEnvelope(0, "set-player-settings", "Babylon", JsonDocument.Parse("""{"now":5}""").RootElement.Clone());
        Ok(session.Submit(other));
        Assert.Equal(5L, session.Log[^1].Payload.GetProperty("now").GetInt64()); // untouched: not a timed command
    }

    [Fact]
    public void EndTurnIsRefusedTwiceWhilePendingAndToTheWrongPlayer()
    {
        var game = NewGame();
        var session = Session(game);
        var babylon = game.GetCivilization("Babylon");
        Rejected(session.Submit("Greece", new EndTurn()), CommandErrors.NotHuman);
        Rejected(session.SubmitAsServer(new EndTurn()), CommandErrors.NotHuman);

        babylon.GreatPeople.FreeGreatPeople = 1;
        var blocked = session.Submit("Babylon", new EndTurn());
        Rejected(blocked, CommandErrors.PendingDecisions);
        Assert.Contains("FreeGreatPerson", blocked.Validation.Message);
        babylon.GreatPeople.FreeGreatPeople = 0;

        Ok(session.Submit("Babylon", new EndTurn()));
        // The guard of T184: the cycle must not advance a second time before the next BeginTurn.
        Rejected(session.Submit("Babylon", new EndTurn()), CommandErrors.NotYourTurn);
    }

    [Fact]
    public void EndTurnRefusesATimeBeforeTheTurnStarted()
    {
        var game = NewGame();
        var session = Session(game, () => 10L);
        Rejected(session.Submit("Babylon", new EndTurn()), CommandErrors.NotAllowed);
    }

    [Fact]
    public void ServerCommandsAreServerOnlyAndStateDependent()
    {
        var game = NewGame();
        var session = Session(game);
        Rejected(session.Submit("Babylon", new RunAiTurn { CivId = "Greece" }), CommandErrors.NotAllowed);
        Rejected(session.Submit("Babylon", new BeginTurn { CivId = "Babylon" }), CommandErrors.NotAllowed);
        Rejected(session.Submit("Babylon", new ForceEndTurn { CivId = "Babylon" }), CommandErrors.NotAllowed);
        Rejected(session.SubmitAsServer(new RunAiTurn { CivId = "Greece" }), CommandErrors.NotAllowed); // nobody ended yet
        Rejected(session.SubmitAsServer(new RunAiTurn { CivId = "Nobody" }), CommandErrors.UnknownCiv);
        Rejected(session.SubmitAsServer(new BeginTurn { CivId = "Babylon" }), CommandErrors.NotAllowed); // already begun
        Rejected(session.SubmitAsServer(new ForceEndTurn { CivId = "Greece" }), CommandErrors.NotHuman);
        Rejected(session.SubmitAsServer(new ForceEndTurn { CivId = "Nobody" }), CommandErrors.UnknownCiv);

        Ok(session.Submit("Babylon", new EndTurn()));
        Rejected(session.SubmitAsServer(new RunAiTurn { CivId = "Rome" }), CommandErrors.NotAllowed); // Greece is next
        Rejected(session.SubmitAsServer(new BeginTurn { CivId = "Babylon" }), CommandErrors.NotAllowed); // auto civs left
        Rejected(session.SubmitAsServer(new ForceEndTurn { CivId = "Babylon" }), CommandErrors.NotYourTurn);
        Ok(session.SubmitAsServer(new RunAiTurn { CivId = "Greece" }));
    }

    [Fact]
    public void ForceEndTurnSkipsThePendingCheck()
    {
        var game = NewGame();
        var session = Session(game);
        game.GetCivilization("Babylon").GreatPeople.FreeGreatPeople = 1;
        Rejected(session.Submit("Babylon", new EndTurn()), CommandErrors.PendingDecisions);
        Ok(session.SubmitAsServer(new ForceEndTurn { CivId = "Babylon" }));
        Assert.IsType<TurnPhase.AutoPlayers>(game.TurnPhase);
        Assert.Equal(1, game.GetCivilization("Babylon").GreatPeople.FreeGreatPeople); // nothing cleared
    }

    [Fact]
    public void VictoryStopsPlayCommandsUntilOneMoreTurn()
    {
        var game = NewGame();
        var session = Session(game);
        Rejected(session.Submit("Babylon", new OneMoreTurn()), CommandErrors.NotAllowed);
        game.VictoryData = new VictoryData { WinningCiv = "Babylon", VictoryType = "Scientific", VictoryTurn = 1 };
        Rejected(session.Submit("Babylon", new EndTurn()), CommandErrors.GameOver);
        Rejected(session.Submit("Greece", new OneMoreTurn()), CommandErrors.NotHuman);
        Ok(session.Submit("Babylon", new OneMoreTurn()));
        Assert.True(game.OneMoreTurnMode);
        Rejected(session.Submit("Babylon", new OneMoreTurn()), CommandErrors.NotAllowed);
        Ok(session.Submit("Babylon", new EndTurn()));
    }

    [Fact]
    public void ResignMakesTheCivAnAiAndMovesTheTurnOn()
    {
        var game = NewGame();
        var session = Session(game);
        var babylon = game.GetCivilization("Babylon");
        babylon.PlayerId = "player-1";
        babylon.TurnsPlayedAsHuman = 4;
        Rejected(session.Submit("Greece", new Resign()), CommandErrors.NotHuman);
        Ok(session.Submit("Babylon", new Resign()));

        Assert.True(babylon.IsAI());
        Assert.Equal("", babylon.PlayerId);
        Assert.Equal(0, babylon.TurnsPlayedAsHuman);
        Assert.Equal(new TurnPhase.AutoPlayers(babylon), game.TurnPhase);
        Assert.All(game.Civilizations.Where(it => it.IsHuman()), it => Assert.Contains(it.Notifications, n => n.Text.Contains("resigned")));
        Rejected(session.Submit("Babylon", new Resign()), CommandErrors.NotHuman);
    }

    [Fact]
    public void ResignOutOfTurnJustChangesTheController()
    {
        var game = NewGame();
        var session = Session(game);
        var babylon = game.GetCivilization("Babylon");
        Ok(session.Submit("Babylon", new EndTurn()));
        Ok(session.Submit("Babylon", new Resign()));
        Assert.True(babylon.IsAI());
        Assert.IsType<TurnPhase.AutoPlayers>(game.TurnPhase);
    }

    [Fact]
    public void MoveAutomatedUnitsMovesOnceAndHonoursTheSetting()
    {
        var game = NewGame();
        var session = Session(game);
        var babylon = game.GetCivilization("Babylon");
        babylon.HasMovedAutomatedUnits = false;
        Rejected(session.Submit("Babylon", new MoveAutomatedUnits()), CommandErrors.NotAllowed); // nothing automated yet
        var unit = babylon.Units.GetCivUnits().First(it => it.CurrentMovement > 0);
        unit.Action = "Explore";
        Ok(session.Submit("Babylon", new MoveAutomatedUnits()));
        Assert.True(babylon.HasMovedAutomatedUnits);
        Rejected(session.Submit("Babylon", new MoveAutomatedUnits()), CommandErrors.NotAllowed);

        babylon.HasMovedAutomatedUnits = false;
        Ok(session.Submit("Babylon", new SetPlayerSettings { AutomatedUnitsMoveOnTurnStart = true }));
        Rejected(session.Submit("Babylon", new MoveAutomatedUnits()), CommandErrors.NotAllowed);
    }

    [Fact]
    public void SetPlayerSettingsChangesOnlyTheGivenMembers()
    {
        var game = NewGame();
        var session = Session(game);
        var defaults = PlayerSettings.Default;
        Ok(session.Submit("Babylon", new SetPlayerSettings
        {
            AutoAssignCityProduction = true,
            AutoAssignSpecialistsInNewCities = false,
            AutomatedUnitsMoveOnTurnStart = true,
            CitiesAutoBombardAtEndOfTurn = true,
            NotificationsLogMaxTurns = 9,
            AutomatedUnitsChoosePromotions = true,
            AutoBuildingRoads = false,
            AutomatedWorkersReplaceImprovements = false,
            StopAutomatedWorkersRemoveVegetation = true,
            HumanConquestPrompt = true,
        }));
        var all = game.GetPlayerSettings("Babylon");
        Assert.Equal(
            (true, false, true, true, 9, true, false, false, true, true),
            (all.AutoAssignCityProduction, all.AutoAssignSpecialistsInNewCities, all.AutomatedUnitsMoveOnTurnStart, all.CitiesAutoBombardAtEndOfTurn,
                all.NotificationsLogMaxTurns, all.AutomatedUnitsChoosePromotions, all.AutoBuildingRoads, all.AutomatedWorkersReplaceImprovements,
                all.StopAutomatedWorkersRemoveVegetation, all.HumanConquestPrompt));

        Ok(session.Submit("Babylon", new SetPlayerSettings { AutoBuildingRoads = true }));
        var partial = game.GetPlayerSettings("Babylon");
        Assert.Equal(all with { AutoBuildingRoads = true }, partial);
        Assert.Equal(defaults, game.GetPlayerSettings("Greece"));

        Rejected(session.Submit("Babylon", new SetPlayerSettings { NotificationsLogMaxTurns = -1 }), CommandErrors.InvalidTarget);
        Rejected(session.Submit("Greece", new SetPlayerSettings()), CommandErrors.NotHuman);
        Rejected(session.Submit("Nobody", new SetPlayerSettings()), CommandErrors.UnknownCiv);
    }

    [Fact]
    public void DismissCommandsClearTheSavedFlags()
    {
        var game = NewGame();
        var session = Session(game);
        var babylon = game.GetCivilization("Babylon");
        Rejected(session.Submit("Babylon", new DismissPolicyPicker()), CommandErrors.StaleReference);
        babylon.Policies.ShouldOpenPolicyPicker = true;
        Ok(session.Submit("Babylon", new DismissPolicyPicker()));
        Assert.False(babylon.Policies.ShouldOpenPolicyPicker);

        Rejected(session.Submit("Babylon", new DismissMoveSpies()), CommandErrors.StaleReference);
    }

    [Fact]
    public void ADefeatedSoleHumanStillEndsTheTurnSoTheGameGoesOn()
    {
        // C024: the defeat screen offers "Keep watching"; with every human defeated the first one controls the turn (Kotlin), so its End turn must work.
        var game = NewGame();
        var session = Session(game);
        var babylon = game.GetCivilization("Babylon");
        babylon.HasEverOwnedOriginalCapital = true;
        babylon.Cities = [];
        Assert.True(babylon.IsDefeated());
        Ok(session.Submit("Babylon", new EndTurn()));
    }
}
