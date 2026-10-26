using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Diplomacy;
using Unciv.Commands.Events;
using Unciv.Commands.Turn;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Commands.Turn;

// T202: the commands of simultaneous turns on a real started game (Babylon and Greece human, Rome AI, barbarians): EndTurn / UndoEndTurn / ForceEndTurn
// marking, the RequireActive gate and its codes, the server step commands against NextResolveStep, SetController, Resign and AutoPlayTurn in a round,
// replay and save/load equality, and a seeded fuzz of command interleavings that checks the invariants after every command.
[Collection("RulesetCache")]
public sealed class SimultaneousCommandTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public SimultaneousCommandTests()
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

    private GameInfo NewGame(long seed = 1L, bool simultaneous = true, params Player[] players)
    {
        if (players.Length == 0) players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.Human), new Player("Rome", PlayerType.AI)];
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = players.ToList(),
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            seed, 1000L);
        if (simultaneous) game.StartSimultaneousTurns();
        return game;
    }

    private static GameSession Session(GameInfo game) => new(game) { Clock = ScriptedClock() };

    private static Func<long> ScriptedClock()
    {
        var now = 5000L;
        return () => now += 1000;
    }

    private static string Save(GameInfo game) => UncivFiles.GameInfoToString(game, forceZip: false);

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(Save(game)));

    private static void Ok(CommandResult result) => Assert.True(result.Accepted, result.Validation.Code + ": " + result.Validation.Message);

    private static void Rejected(GameSession session, CommandResult result, string code)
    {
        Assert.False(result.Accepted);
        Assert.Equal(code, result.Validation.Code);
        Assert.False(string.IsNullOrEmpty(result.Validation.Message));
    }

    // A rejection must change nothing: state hash, log and sequence number are the same afterwards (T164 purity rule).
    private static void RejectsWithoutEffect(GameSession session, Func<CommandResult> submit, string code)
    {
        var (hash, count, seq) = (Hash(session.Game), session.Log.Count, session.LastSeq);
        Rejected(session, submit(), code);
        Assert.Equal((hash, count, seq), (Hash(session.Game), session.Log.Count, session.LastSeq));
    }

    // A new game's humans own no city yet (their settlers are not founded), so nothing blocks EndTurn; Settle documents and asserts that.
    private static void Settle(GameInfo game, string civId) => Assert.Empty(PendingDecisions.BlockingFor(game.GetCivilization(civId)));

    // A blocking decision that does not depend on cities: a free great person to choose.
    private static void Block(GameInfo game, string civId) => game.GetCivilization(civId).GreatPeople.FreeGreatPeople = 1;

    private static GameCommand StepCommand(ResolveStep step) => step switch
    {
        ResolveStep.ProcessEndTurn s => new ProcessEndTurn { CivId = s.CivId },
        ResolveStep.RunAiTurn s => new RunAiTurn { CivId = s.CivId },
        ResolveStep.StartNextTurn => new StartNextTurn(),
        ResolveStep.BeginTurn s => new BeginTurn { CivId = s.CivId },
        _ => throw new InvalidOperationException(step.ToString()),
    };

    // The host's resolve pump: submits the command NextResolveStep names until the round is over.
    private static List<ResolveStep> Pump(GameSession session, List<GameEvent>? events = null)
    {
        var steps = new List<ResolveStep>();
        while (session.Game.NextResolveStep() is { } step)
        {
            var result = session.SubmitAsServer(StepCommand(step));
            Ok(result);
            events?.AddRange(result.Events);
            steps.Add(step);
        }
        return steps;
    }

    private static void EndBoth(GameSession session)
    {
        Settle(session.Game, "Babylon");
        Settle(session.Game, "Greece");
        Ok(session.Submit("Babylon", new EndTurn()));
        Ok(session.Submit("Greece", new EndTurn()));
    }

    private static GameCommand Probe() => new DismissPolicyPicker();

    // ----- registration and wire -----

    [Fact]
    public void NewCommandsAreRegisteredAndRoundTripThroughJson()
    {
        var names = GameCommandRegistry.Scan().ToDictionary(it => it.Name, it => it.CommandClass);
        Assert.Equal(typeof(UndoEndTurn), names["undo-end-turn"]);
        Assert.Equal(typeof(ProcessEndTurn), names["process-end-turn"]);
        Assert.Equal(typeof(StartNextTurn), names["start-next-turn"]);
        Assert.Equal(typeof(SetController), names["set-controller"]);

        var command = JsonSerializer.Deserialize<SetController>("""{"civId":"Greece","controller":"Human","playerId":"key-1"}""", UncivJson.DefaultOptions)!;
        Assert.Equal(("Greece", PlayerType.Human, "key-1"), (command.CivId, command.Controller, command.PlayerId));
        var back = JsonSerializer.Deserialize<SetController>(JsonSerializer.Serialize(command, UncivJson.DefaultOptions), UncivJson.DefaultOptions)!;
        Assert.Equal(("Greece", PlayerType.Human, "key-1"), (back.CivId, back.Controller, back.PlayerId));
        Assert.Equal(PlayerType.AI, new SetController().Controller);

        Assert.Equal("turn-resolving", CommandErrors.TurnResolving);
        Assert.Equal("already-ended", CommandErrors.AlreadyEnded);
        Assert.Equal("out-of-sequence", CommandErrors.OutOfSequence);
    }

    // ----- RequireActive -----

    [Fact]
    public void BothHumansAreActiveAtOnceAndOthersAreNotPlayers()
    {
        var game = NewGame();
        game.GetCivilization("Babylon").Policies.ShouldOpenPolicyPicker = true;
        game.GetCivilization("Greece").Policies.ShouldOpenPolicyPicker = true;
        var session = Session(game);
        Assert.Equal("Babylon", game.CurrentPlayer);
        Ok(session.Submit("Greece", Probe())); // not the nominal current player, still active
        Ok(session.Submit("Babylon", Probe()));
        Rejected(session, session.Submit("Rome", Probe()), CommandErrors.NotHuman);
        Rejected(session, session.Submit("Nobody", Probe()), CommandErrors.UnknownCiv);
        Rejected(session, session.SubmitAsServer(Probe()), CommandErrors.NotHuman);
    }

    [Fact]
    public void AnEndedHumanIsRefusedPlayCommandsButMayStillAnswerRequests()
    {
        var game = NewGame();
        var session = Session(game);
        Ok(session.SubmitAsServer(new ForceEndTurn { CivId = "Babylon" }));
        var babylon = game.GetCivilization("Babylon");
        babylon.Policies.ShouldOpenPolicyPicker = true;
        RejectsWithoutEffect(session, () => session.Submit("Babylon", Probe()), CommandErrors.AlreadyEnded);
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new MoveAutomatedUnits()), CommandErrors.AlreadyEnded);

        // answering requests does not spend the turn: dismiss / respond reach their own validation and are not turned away as ended
        babylon.TradeRequests.Add(new TradeRequest("Greece", new Trade()));
        Ok(session.Submit("Babylon", new DismissTradeRequest { CivId = "Greece" }));
        Rejected(session, session.Submit("Babylon", new DismissTradeRequest { CivId = "Greece" }), CommandErrors.StaleReference);
        Rejected(session, session.Submit("Babylon", new RespondToTradeRequest { CivId = "Greece", Accept = true }), CommandErrors.StaleReference);
        Assert.NotEqual(CommandErrors.AlreadyEnded, session.Submit("Babylon", new RespondToDemand { CivId = "Greece", Demand = "x", Accept = true }).Validation.Code);
        Assert.NotEqual(CommandErrors.AlreadyEnded, session.Submit("Babylon", new RespondToFriendship { CivId = "Greece", Accept = true }).Validation.Code);

        // acknowledging an alert is not tied to the turn at all
        babylon.PopupAlerts.Add(new PopupAlert(AlertType.WonderBuilt, "Pyramids"));
        Ok(session.Submit("Babylon", new AckAlert { Index = babylon.PopupAlerts.Count - 1, AlertType = AlertType.WonderBuilt, Value = "Pyramids" }));
    }

    [Fact]
    public void WhileResolvingEveryPlayerCommandIsRefusedAsTurnResolving()
    {
        var game = NewGame();
        var session = Session(game);
        EndBoth(session);
        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState!.Phase);
        game.GetCivilization("Babylon").Policies.ShouldOpenPolicyPicker = true;
        RejectsWithoutEffect(session, () => session.Submit("Babylon", Probe()), CommandErrors.TurnResolving);
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new UndoEndTurn()), CommandErrors.TurnResolving);
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new EndTurn()), CommandErrors.TurnResolving);
        RejectsWithoutEffect(session, () => session.Submit("Greece", new DismissTradeRequest { CivId = "Babylon" }), CommandErrors.TurnResolving);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new ForceEndTurn { CivId = "Babylon" }), CommandErrors.TurnResolving);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new SetController { CivId = "Rome", Controller = PlayerType.Human }), CommandErrors.TurnResolving);
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new Resign { Now = 9000 }), CommandErrors.TurnResolving);
    }

    [Fact]
    public void ANonStartedOrInactiveHumanIsNotYourTurn()
    {
        var game = NewGame();
        var session = Session(game);
        var greece = game.GetCivilization("Greece");
        game.SetHumanController(greece, false, ""); // hand over without the command: Greece is AI now, the human check answers first
        Rejected(session, session.Submit("Greece", Probe()), CommandErrors.NotHuman);

        // a human the round does not wait for (turn never begun, e.g. a save edited by hand) is told it is not playing
        var rome = game.GetCivilization("Rome");
        rome.PlayerType = PlayerType.Human;
        Assert.False(game.IsTurnStarted(rome));
        Rejected(session, session.Submit("Rome", Probe()), CommandErrors.NotYourTurn);
        Rejected(session, session.Submit("Rome", new EndTurn()), CommandErrors.NotYourTurn);
    }

    [Fact]
    public void GameOverComesBeforeEverythingAndOneMoreTurnLiftsIt()
    {
        var game = NewGame();
        var session = Session(game);
        game.VictoryData = new VictoryData { WinningCiv = "Greece", VictoryType = "Scientific", VictoryTurn = 1 };
        Rejected(session, session.Submit("Babylon", new EndTurn()), CommandErrors.GameOver);
        Ok(session.Submit("Babylon", new OneMoreTurn()));
        Settle(game, "Babylon");
        Ok(session.Submit("Babylon", new EndTurn()));
    }

    [Fact]
    public void ADefeatedHumanCannotActAndOnlyTheSoleSurvivorEndsTheTurn()
    {
        var game = NewGame();
        var session = Session(game);
        var greece = game.GetCivilization("Greece");
        foreach (var unit in greece.Units.GetCivUnits().ToList()) unit.Destroy();
        Assert.True(greece.IsDefeated());
        Rejected(session, session.Submit("Greece", Probe()), CommandErrors.NotAllowed);
        Rejected(session, session.Submit("Greece", new EndTurn()), CommandErrors.NotYourTurn); // Babylon is alive, so the round does not wait for Greece
        Rejected(session, session.Submit("Greece", new UndoEndTurn()), CommandErrors.NotYourTurn);
        Rejected(session, session.SubmitAsServer(new ForceEndTurn { CivId = "Greece" }), CommandErrors.NotYourTurn);

        // with every human defeated the first one controls the cycle and ends the turn through RequireActive(allowDefeated: true)
        foreach (var unit in game.GetCivilization("Babylon").Units.GetCivUnits().ToList()) unit.Destroy();
        game.GetCivilization("Babylon").Cities = [];
        Assert.True(game.GetCivilization("Babylon").IsDefeated());
        Ok(session.Submit("Babylon", new EndTurn()));
        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState!.Phase);
        Assert.Equal(new ResolveStep.ProcessEndTurn("Babylon"), game.NextResolveStep());
    }

    // ----- EndTurn / UndoEndTurn / ForceEndTurn -----

    [Fact]
    public void EndTurnMarksTheCivAndTheLastOneStartsResolving()
    {
        var game = NewGame();
        var session = Session(game);
        var turn = game.Turns;
        Settle(game, "Babylon");
        Settle(game, "Greece");
        var first = session.Submit("Babylon", new EndTurn());
        Ok(first);
        Assert.Equal([new PlayerEndedTurn("Babylon", turn)], first.Events);
        Assert.Equal(["Babylon"], game.TurnState!.Ended);
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState.Phase);
        Assert.True(game.IsTurnStarted(game.GetCivilization("Babylon"))); // still started until its ProcessEndTurn
        Assert.Null(game.NextResolveStep());

        var second = session.Submit("Greece", new EndTurn());
        Ok(second);
        Assert.Equal([new PlayerEndedTurn("Greece", turn), new TurnResolving(turn)], second.Events);
        Assert.Equal(["Babylon", "Greece"], game.TurnState.Ended);
        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState.Phase);
        Assert.Equal(new ResolveStep.ProcessEndTurn("Babylon"), game.NextResolveStep());
        Assert.Equal(turn, game.Turns); // nothing advanced yet
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new EndTurn()), CommandErrors.TurnResolving);
    }

    [Fact]
    public void EndTurnTwiceIsAlreadyEndedAndPendingDecisionsStillBlock()
    {
        var game = NewGame();
        var session = Session(game);
        Block(game, "Babylon");
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new EndTurn()), CommandErrors.PendingDecisions);
        game.GetCivilization("Babylon").GreatPeople.FreeGreatPeople = 0;
        Ok(session.Submit("Babylon", new EndTurn()));
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new EndTurn()), CommandErrors.AlreadyEnded);
        Assert.Equal(["Babylon"], game.TurnState!.Ended);
    }

    [Fact]
    public void EndTurnNeedsNoClockAndStaysATimedCommandInTheLog()
    {
        var game = NewGame();
        var session = new GameSession(game); // no Clock: the payload's Now (0) is before the turn start, which a simultaneous EndTurn does not read
        Settle(game, "Babylon");
        Ok(session.Submit("Babylon", new EndTurn()));
        Ok(session.SubmitAsServer(new ForceEndTurn { CivId = "Greece" })); // likewise
        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState!.Phase);
    }

    [Fact]
    public void UndoEndTurnTakesTheMarkBackWhilePlaying()
    {
        var game = NewGame();
        var session = Session(game);
        var turn = game.Turns;
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new UndoEndTurn()), CommandErrors.NotAllowed); // has not ended
        Settle(game, "Babylon");
        Ok(session.Submit("Babylon", new EndTurn()));
        var undo = session.Submit("Babylon", new UndoEndTurn());
        Ok(undo);
        Assert.Equal([new PlayerUndidEndTurn("Babylon", turn)], undo.Events);
        Assert.Empty(game.TurnState!.Ended);
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new UndoEndTurn()), CommandErrors.NotAllowed);
        game.GetCivilization("Babylon").Policies.ShouldOpenPolicyPicker = true;
        Ok(session.Submit("Babylon", Probe())); // acts again
        Ok(session.Submit("Babylon", new EndTurn()));

        // the order of the marks is the order they were (re)made
        Ok(session.SubmitAsServer(new ForceEndTurn { CivId = "Greece" }));
        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState.Phase);
        Assert.Equal(["Babylon", "Greece"], game.TurnState.Ended);
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new UndoEndTurn()), CommandErrors.TurnResolving);
    }

    [Fact]
    public void UndoEndTurnRefusesNonPlayersAndSequentialGames()
    {
        var game = NewGame();
        var session = Session(game);
        Rejected(session, session.Submit("Rome", new UndoEndTurn()), CommandErrors.NotHuman);
        Rejected(session, session.SubmitAsServer(new UndoEndTurn()), CommandErrors.NotHuman);
        Rejected(session, session.Submit("Nobody", new UndoEndTurn()), CommandErrors.UnknownCiv);

        var sequential = NewGame(simultaneous: false);
        var sequentialSession = Session(sequential);
        RejectsWithoutEffect(sequentialSession, () => sequentialSession.Submit("Babylon", new UndoEndTurn()), CommandErrors.NotAllowed);
        RejectsWithoutEffect(sequentialSession, () => sequentialSession.Submit("Greece", new UndoEndTurn()), CommandErrors.NotYourTurn);
    }

    [Fact]
    public void ForceEndTurnSkipsPendingDecisionsAndIsServerOnly()
    {
        var game = NewGame();
        var session = Session(game);
        var turn = game.Turns;
        Rejected(session, session.Submit("Babylon", new ForceEndTurn { CivId = "Greece" }), CommandErrors.NotAllowed);
        Rejected(session, session.SubmitAsServer(new ForceEndTurn { CivId = "Rome" }), CommandErrors.NotHuman);
        Rejected(session, session.SubmitAsServer(new ForceEndTurn { CivId = "Nobody" }), CommandErrors.UnknownCiv);
        Block(game, "Greece");
        Assert.NotEmpty(PendingDecisions.BlockingFor(game.GetCivilization("Greece")));
        var result = session.SubmitAsServer(new ForceEndTurn { CivId = "Greece" });
        Ok(result);
        Assert.Equal([new PlayerEndedTurn("Greece", turn)], result.Events);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new ForceEndTurn { CivId = "Greece" }), CommandErrors.AlreadyEnded);
        var last = session.SubmitAsServer(new ForceEndTurn { CivId = "Babylon" });
        Ok(last);
        Assert.Contains(new TurnResolving(turn), last.Events);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new ForceEndTurn { CivId = "Babylon" }), CommandErrors.TurnResolving);
    }

    // ----- server step commands -----

    [Fact]
    public void ARoundResolvesThroughTheStepCommandsInTheSequenceOfTheDesign()
    {
        var game = NewGame();
        var session = Session(game);
        var turn = game.Turns;
        EndBoth(session);
        var events = new List<GameEvent>();
        var steps = Pump(session, events);

        var barbarians = game.Civilizations.First(it => it.IsBarbarian).CivId;
        var ai = game.Civilizations.Where(it => it.IsAI()).Select(it => it.CivId).ToList();
        Assert.Equal(
            [
                new ResolveStep.ProcessEndTurn("Babylon"), new ResolveStep.ProcessEndTurn("Greece"),
                .. ai.Select(it => (ResolveStep)new ResolveStep.RunAiTurn(it)),
                new ResolveStep.StartNextTurn(),
                new ResolveStep.BeginTurn("Babylon"), new ResolveStep.BeginTurn("Greece"),
            ],
            steps);
        Assert.Contains(barbarians, ai);
        Assert.Equal(turn + 1, game.Turns);
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState!.Phase);
        Assert.Empty(game.TurnState.Ended);
        Assert.Equal(["Babylon", "Greece"], game.TurnState.Started);
        Assert.Null(game.NextResolveStep());
        Assert.Equal(1, events.OfType<TurnStarted>().Count(it => it.CivId == "Babylon"));
        Assert.Equal(1, events.OfType<TurnEnded>().Count(it => it.CivId == "Greece"));
        Assert.Equal(2, session.Log.Count(it => it.Type == "end-turn"));
        Assert.Contains(session.Log, it => it.Type == "start-next-turn");
        Assert.Contains(session.Log, it => it.Type == "process-end-turn" && it.Payload.GetProperty("now").GetInt64() > 0);
        Assert.Equal(game.CurrentTurnStartTime, session.Log.Last(it => it.Type == "begin-turn").Payload.GetProperty("now").GetInt64());

        // and the next round plays again
        Settle(game, "Babylon");
        Ok(session.SubmitAsServer(new ForceEndTurn { CivId = "Babylon" }));
    }

    [Fact]
    public void StepCommandsAreOnlyValidForTheStepNextResolveStepNames()
    {
        var game = NewGame();
        var session = Session(game);

        // Playing: no step at all
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new ProcessEndTurn { CivId = "Babylon" }), CommandErrors.OutOfSequence);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new RunAiTurn { CivId = "Rome" }), CommandErrors.OutOfSequence);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new StartNextTurn()), CommandErrors.OutOfSequence);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new BeginTurn { CivId = "Babylon" }), CommandErrors.OutOfSequence);

        EndBoth(session);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new ProcessEndTurn { CivId = "Greece" }), CommandErrors.OutOfSequence); // Babylon is first
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new RunAiTurn { CivId = "Rome" }), CommandErrors.OutOfSequence);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new StartNextTurn()), CommandErrors.OutOfSequence);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new BeginTurn { CivId = "Babylon" }), CommandErrors.OutOfSequence);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new ProcessEndTurn { CivId = "Nobody" }), CommandErrors.UnknownCiv);
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new ProcessEndTurn { CivId = "Babylon" }), CommandErrors.NotAllowed);
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new StartNextTurn()), CommandErrors.NotAllowed);
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new RunAiTurn { CivId = "Rome" }), CommandErrors.NotAllowed);
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new BeginTurn { CivId = "Babylon" }), CommandErrors.NotAllowed);

        Ok(session.SubmitAsServer(new ProcessEndTurn { CivId = "Babylon" }));
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new ProcessEndTurn { CivId = "Babylon" }), CommandErrors.OutOfSequence); // done once
        Ok(session.SubmitAsServer(new ProcessEndTurn { CivId = "Greece" }));
        while (game.NextResolveStep() is ResolveStep.RunAiTurn ai)
        {
            RejectsWithoutEffect(session, () => session.SubmitAsServer(new StartNextTurn()), CommandErrors.OutOfSequence);
            Ok(session.SubmitAsServer(new RunAiTurn { CivId = ai.CivId }));
        }
        Ok(session.SubmitAsServer(new StartNextTurn()));
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new StartNextTurn()), CommandErrors.OutOfSequence);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new BeginTurn { CivId = "Greece" }), CommandErrors.OutOfSequence); // Babylon first
        Ok(session.SubmitAsServer(new BeginTurn { CivId = "Babylon" }));
        Ok(session.SubmitAsServer(new BeginTurn { CivId = "Greece" }));
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState!.Phase);
    }

    [Fact]
    public void StepCommandsCheckTheClockAndSequentialGamesRefuseThem()
    {
        var game = NewGame();
        var session = Session(game);
        EndBoth(session);
        // Session.Clock stamps a later time, so the check is exercised without it:
        var raw = new GameSession(game);
        RejectsWithoutEffect(raw, () => raw.SubmitAsServer(new ProcessEndTurn { CivId = "Babylon", Now = 10 }), CommandErrors.NotAllowed);
        Ok(raw.SubmitAsServer(new ProcessEndTurn { CivId = "Babylon", Now = 9000 }));
        Ok(raw.SubmitAsServer(new ProcessEndTurn { CivId = "Greece", Now = 9000 }));
        while (game.NextResolveStep() is ResolveStep.RunAiTurn ai) Ok(raw.SubmitAsServer(new RunAiTurn { CivId = ai.CivId }));
        Ok(raw.SubmitAsServer(new StartNextTurn()));
        RejectsWithoutEffect(raw, () => raw.SubmitAsServer(new BeginTurn { CivId = "Babylon", Now = 10 }), CommandErrors.NotAllowed);
        Ok(raw.SubmitAsServer(new BeginTurn { CivId = "Babylon", Now = 12000 }));
        Assert.Equal(12000L, game.CurrentTurnStartTime);

        var sequential = NewGame(simultaneous: false);
        var sequentialSession = Session(sequential);
        RejectsWithoutEffect(sequentialSession, () => sequentialSession.SubmitAsServer(new ProcessEndTurn { CivId = "Babylon" }), CommandErrors.NotAllowed);
        RejectsWithoutEffect(sequentialSession, () => sequentialSession.SubmitAsServer(new StartNextTurn()), CommandErrors.NotAllowed);
        // the T187 commands keep their sequential meaning
        RejectsWithoutEffect(sequentialSession, () => sequentialSession.SubmitAsServer(new RunAiTurn { CivId = "Rome" }), CommandErrors.NotAllowed);
        RejectsWithoutEffect(sequentialSession, () => sequentialSession.SubmitAsServer(new BeginTurn { CivId = "Babylon" }), CommandErrors.NotAllowed);
    }

    [Fact]
    public void ALogOfARoundReplaysToTheSameStateAndTheSameEvents()
    {
        var live = NewGame();
        var session = Session(live);
        var startTurn = live.Turns;
        var rounds = 0;
        while (rounds < 3)
        {
            Settle(live, "Babylon");
            Settle(live, "Greece");
            Ok(session.Submit("Babylon", new EndTurn()));
            Ok(rounds == 1 ? session.SubmitAsServer(new ForceEndTurn { CivId = "Greece" }) : session.Submit("Greece", new EndTurn()));
            Pump(session);
            rounds++;
        }
        Assert.Equal(startTurn + 3, live.Turns);

        var follower = NewGame();
        var replay = new GameSession(follower);
        foreach (var envelope in session.Log) replay.Replay(envelope);
        Assert.Equal(Hash(live), Hash(follower));
        Assert.Equal(session.LastSeq, replay.LastSeq);
    }

    [Fact]
    public void ASnapshotBetweenAnyTwoCommandsOfARoundLoadsToTheLiveState()
    {
        var game = NewGame();
        var session = Session(game);
        Settle(game, "Babylon");
        Settle(game, "Greece");
        var loadedAt = new List<string>();
        void CheckLoad(string label)
        {
            var loaded = UncivFiles.GameInfoFromString(Save(game));
            Assert.True(Hash(loaded) == Hash(game), "after " + label);
            Assert.Equal(game.NextResolveStep(), loaded.NextResolveStep());
            Assert.Equal(game.TurnState!.Ended, loaded.TurnState!.Ended);
            loadedAt.Add(label);
        }
        Ok(session.Submit("Babylon", new EndTurn()));
        CheckLoad("end babylon");
        Ok(session.Submit("Babylon", new UndoEndTurn()));
        CheckLoad("undo");
        Ok(session.Submit("Babylon", new EndTurn()));
        Ok(session.SubmitAsServer(new ForceEndTurn { CivId = "Greece" }));
        CheckLoad("resolving");
        while (game.NextResolveStep() is { } step)
        {
            Ok(session.SubmitAsServer(StepCommand(step)));
            CheckLoad(step.ToString());
        }
        Assert.True(loadedAt.Count > 6);
    }

    // ----- SetController -----

    [Fact]
    public void LeavingHandsTheCivToTheAiAndTheRestOfTheRoundWaitsForTheOthers()
    {
        var game = NewGame();
        var session = Session(game);
        var turn = game.Turns;
        Settle(game, "Babylon");
        Ok(session.Submit("Babylon", new EndTurn()));
        var greece = game.GetCivilization("Greece");
        greece.PlayerId = "key-greece";

        var leave = session.SubmitAsServer(new SetController { CivId = "Greece", Controller = PlayerType.AI });
        Ok(leave);
        Assert.Equal([new ControllerChanged("Greece", false), new TurnResolving(turn)], leave.Events); // Greece was the last one the round waited for
        Assert.True(greece.IsAI());
        Assert.Equal("", greece.PlayerId);
        Assert.Equal(["Babylon"], game.TurnState!.Ended);
        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState.Phase);

        var events = new List<GameEvent>();
        var steps = Pump(session, events);
        Assert.Contains(new ResolveStep.RunAiTurn("Greece"), steps);
        Assert.DoesNotContain(steps, it => it is ResolveStep.ProcessEndTurn { CivId: "Greece" });
        Assert.DoesNotContain(steps, it => it is ResolveStep.BeginTurn { CivId: "Greece" });
        Assert.Equal(0, events.OfType<TurnStarted>().Count(it => it.CivId == "Greece")); // its begun turn is not started a second time
        Assert.Equal(1, events.OfType<TurnEnded>().Count(it => it.CivId == "Greece"));
        Assert.Equal(turn + 1, game.Turns);
    }

    [Fact]
    public void LeavingWhileTheOthersHaveNotEndedKeepsPlayingAndClearsTheEndMark()
    {
        var game = NewGame();
        var session = Session(game);
        Settle(game, "Greece");
        Ok(session.Submit("Greece", new EndTurn()));
        var result = session.SubmitAsServer(new SetController { CivId = "Greece", Controller = PlayerType.AI });
        Ok(result);
        Assert.Equal([new ControllerChanged("Greece", false)], result.Events);
        Assert.Empty(game.TurnState!.Ended);
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState.Phase);
        Assert.Equal("Babylon", game.CurrentPlayer);
        // the single remaining human now ends the round alone
        Settle(game, "Babylon");
        var end = session.Submit("Babylon", new EndTurn());
        Ok(end);
        Assert.Contains(end.Events, it => it is TurnResolving);
    }

    [Fact]
    public void ALeaverCannotBeTheLastHumanAndAutoPlayEnds()
    {
        var game = NewGame();
        var session = Session(game);
        game.GameParameters.GodMode = false;
        Ok(session.SubmitAsServer(new SetController { CivId = "Babylon", Controller = PlayerType.AI }));
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new SetController { CivId = "Greece", Controller = PlayerType.AI }), CommandErrors.NotAllowed);
        Assert.True(game.GetCivilization("Greece").IsHuman());
        Assert.Equal("Greece", game.CurrentPlayer); // the nominal cursor follows the lowest-index active human

        var autoGame = NewGame();
        autoGame.TurnState!.AutoPlayAllowed = true;
        var autoSession = Session(autoGame);
        Ok(autoSession.Submit("Babylon", new StartAutoPlay { Turns = 5 }));
        Assert.True(autoGame.ClientState.AutoPlay!.IsAutoPlaying());
        Ok(autoSession.SubmitAsServer(new SetController { CivId = "Babylon", Controller = PlayerType.AI }));
        Assert.False(autoGame.ClientState.AutoPlay.IsAutoPlaying());
    }

    [Fact]
    public void JoiningBeginsTheCivsTurnSoItCanActAndTheRoundWaitsForIt()
    {
        var game = NewGame();
        var session = Session(game);
        var rome = game.GetCivilization("Rome");
        Assert.False(game.IsTurnStarted(rome));
        Settle(game, "Babylon");
        Settle(game, "Greece");
        Ok(session.Submit("Babylon", new EndTurn()));

        var join = session.SubmitAsServer(new SetController { CivId = "Rome", Controller = PlayerType.Human, PlayerId = "key-rome" });
        Ok(join);
        Assert.Contains(new ControllerChanged("Rome", true), join.Events);
        Assert.Contains(join.Events, it => it is TurnStarted { CivId: "Rome" });
        Assert.True(rome.IsHuman());
        Assert.Equal("key-rome", rome.PlayerId);
        Assert.True(game.IsTurnStarted(rome));
        Assert.True(game.IsActive(rome));
        Assert.Equal(["Babylon", "Greece", "Rome"], game.TurnState!.Started);

        Ok(session.Submit("Greece", new EndTurn()));
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState.Phase); // Rome still has to end
        Settle(game, "Rome");
        rome.Policies.ShouldOpenPolicyPicker = true;
        Ok(session.Submit("Rome", Probe()));
        var end = session.Submit("Rome", new EndTurn());
        Ok(end);
        Assert.Contains(end.Events, it => it is TurnResolving);
        var steps = Pump(session);
        Assert.Contains(new ResolveStep.ProcessEndTurn("Rome"), steps);
        Assert.Contains(new ResolveStep.BeginTurn("Rome"), steps);
        Assert.DoesNotContain(steps, it => it is ResolveStep.RunAiTurn { CivId: "Rome" });
    }

    [Fact]
    public void ReclaimingACivWhoseTurnAlreadyBeganDoesNotStartItAgain()
    {
        var game = NewGame();
        var session = Session(game);
        Ok(session.SubmitAsServer(new SetController { CivId = "Greece", Controller = PlayerType.AI }));
        var back = session.SubmitAsServer(new SetController { CivId = "Greece", Controller = PlayerType.Human, PlayerId = "key-2" });
        Ok(back);
        Assert.DoesNotContain(back.Events, it => it is TurnStarted);
        Assert.True(game.IsActive(game.GetCivilization("Greece")));
        Assert.Equal("key-2", game.GetCivilization("Greece").PlayerId);
    }

    [Fact]
    public void AKeylessJoinIsAllowedAndTheLastResignDoesNotStartAnEmptyRound()
    {
        var game = NewGame();
        var session = Session(game);
        Ok(session.SubmitAsServer(new SetController { CivId = "Rome", Controller = PlayerType.Human })); // hotseat-style: no key, although other civs have none either
        Assert.True(game.GetCivilization("Rome").IsHuman());
        Ok(session.Submit("Rome", new Resign()));
        Ok(session.Submit("Greece", new Resign()));
        var last = session.Submit("Babylon", new Resign());
        Ok(last);
        Assert.DoesNotContain(last.Events, it => it is TurnResolving); // nobody is left to wait for or to end the round
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState!.Phase);
        Assert.DoesNotContain(game.Civilizations, it => it.IsHuman());
    }

    [Fact]
    public void SetControllerValidation()
    {
        var game = NewGame();
        var session = Session(game);
        game.GetCivilization("Babylon").PlayerId = "key-b";
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new SetController { CivId = "Greece" }), CommandErrors.NotAllowed);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new SetController { CivId = "Nobody" }), CommandErrors.UnknownCiv);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new SetController { CivId = "Rome", Controller = PlayerType.AI }), CommandErrors.NotAllowed); // already
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new SetController { CivId = "Greece", Controller = PlayerType.Human }), CommandErrors.NotAllowed); // already
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new SetController { CivId = "Rome", Controller = (PlayerType)77 }), CommandErrors.InvalidTarget);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new SetController { CivId = "Barbarians", Controller = PlayerType.Human }), CommandErrors.NotAllowed);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new SetController { CivId = "Rome", Controller = PlayerType.Human, PlayerId = "key-b" }), CommandErrors.NotAllowed); // key in use
        var rome = game.GetCivilization("Rome");
        foreach (var unit in rome.Units.GetCivUnits().ToList()) unit.Destroy();
        Assert.True(rome.IsDefeated());
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new SetController { CivId = "Rome", Controller = PlayerType.Human }), CommandErrors.NotAllowed);

        var sequential = NewGame(simultaneous: false);
        var sequentialSession = Session(sequential);
        RejectsWithoutEffect(sequentialSession, () => sequentialSession.SubmitAsServer(new SetController { CivId = "Greece", Controller = PlayerType.AI }), CommandErrors.NotAllowed);
    }

    [Fact]
    public void ASpectatorOrCityStateCannotChangeHands()
    {
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 1,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.Human), new Player("Spectator", PlayerType.Human)],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            1L, 1000L);
        game.StartSimultaneousTurns();
        var session = Session(game);
        var spectator = game.Civilizations.First(it => it.IsSpectator());
        var cityState = game.Civilizations.First(it => it.IsCityState);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new SetController { CivId = spectator.CivId, Controller = PlayerType.AI }), CommandErrors.NotAllowed);
        RejectsWithoutEffect(session, () => session.SubmitAsServer(new SetController { CivId = cityState.CivId, Controller = PlayerType.Human }), CommandErrors.NotAllowed);
        // a spectator is no player: it cannot act and the round does not wait for it
        Rejected(session, session.Submit(spectator.CivId, Probe()), CommandErrors.NotHuman);
        Rejected(session, session.Submit(spectator.CivId, new UndoEndTurn()), CommandErrors.NotHuman);
        Settle(game, "Babylon");
        Settle(game, "Greece");
        Ok(session.Submit("Babylon", new EndTurn()));
        Ok(session.Submit("Greece", new EndTurn()));
        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState!.Phase);
    }

    // ----- Resign and AutoPlayTurn in a round -----

    [Fact]
    public void ResigningInARoundHandsTheCivToTheAiWithoutTouchingTheSequentialCycle()
    {
        var game = NewGame();
        var session = Session(game);
        var turn = game.Turns;
        Settle(game, "Babylon");
        Ok(session.Submit("Babylon", new EndTurn()));
        var greece = game.GetCivilization("Greece");
        greece.PlayerId = "key-greece";
        greece.TurnsPlayedAsHuman = 4;
        var result = session.Submit("Greece", new Resign());
        Ok(result);
        Assert.Contains(new ControllerChanged("Greece", false), result.Events);
        Assert.Contains(new TurnResolving(turn), result.Events);
        Assert.True(greece.IsAI());
        Assert.Equal(("", 0), (greece.PlayerId, greece.TurnsPlayedAsHuman));
        Assert.All(game.Civilizations.Where(it => it.IsHuman()), civ => Assert.Contains(civ.Notifications, n => n.Text.Contains("resigned", StringComparison.Ordinal)));
        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState!.Phase);
        Assert.Equal(new ResolveStep.ProcessEndTurn("Babylon"), game.NextResolveStep()); // and the sequential cycle was not touched
        Pump(session);
        Assert.Equal(turn + 1, game.Turns);
    }

    [Fact]
    public void AResignThatDoesNotCompleteTheRoundOnlyChangesTheController()
    {
        var game = NewGame();
        var session = Session(game);
        Settle(game, "Greece");
        Ok(session.Submit("Greece", new EndTurn()));
        var result = session.Submit("Greece", new Resign());
        Ok(result);
        Assert.DoesNotContain(result.Events, it => it is TurnResolving);
        Assert.Empty(game.TurnState!.Ended);
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState.Phase);
        Rejected(session, session.Submit("Greece", new Resign()), CommandErrors.NotHuman); // an AI civ cannot resign again
    }

    [Fact]
    public void AutoPlayTurnEndsTheCivInASimultaneousRound()
    {
        var game = NewGame();
        game.TurnState!.AutoPlayAllowed = true;
        var session = Session(game);
        Ok(session.Submit("Babylon", new StartAutoPlay { Turns = 2 }));
        var result = session.Submit("Babylon", new AutoPlayTurn());
        Ok(result);
        Assert.Contains(result.Events, it => it is PlayerEndedTurn { CivId: "Babylon" });
        Assert.True(game.HasEndedTurn(game.GetCivilization("Babylon")));
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState.Phase);
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new AutoPlayTurn()), CommandErrors.AlreadyEnded);
        Ok(session.SubmitAsServer(new ForceEndTurn { CivId = "Greece" }));
        Pump(session);
        Ok(session.Submit("Babylon", new AutoPlayTurn())); // the second of its two turns
        Assert.Equal(0, ((AutoPlayState)game.ClientState.AutoPlay!).TurnsToAutoPlay);
    }

    // The clock is not read by a simultaneous AutoPlayTurn either (ProcessEndTurn carries the time).
    [Fact]
    public void AutoPlayTurnNeedsNoClockInASimultaneousRound()
    {
        var game = NewGame();
        game.TurnState!.AutoPlayAllowed = true;
        var session = new GameSession(game); // no Clock: Now (0) is before the turn start
        Ok(session.Submit("Babylon", new StartAutoPlay { Turns = 1 }));
        Ok(session.Submit("Babylon", new AutoPlayTurn()));
        Assert.True(game.HasEndedTurn(game.GetCivilization("Babylon")));
    }

    // An answer to a decision (annex, raze, war) must not land between the steps of a resolving round.
    [Fact]
    public void RespondToAlertIsRefusedWhileTheRoundResolves()
    {
        var game = NewGame();
        var session = Session(game);
        game.GetCivilization("Babylon").PopupAlerts.Add(new PopupAlert(AlertType.Denounced, "Greece"));
        Ok(session.Submit("Babylon", new EndTurn()));
        Ok(session.Submit("Greece", new EndTurn()));
        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState!.Phase);
        RejectsWithoutEffect(session, () => session.Submit("Babylon", new RespondToAlert { Index = 0, AlertType = AlertType.Denounced, Value = "Greece", Choice = "war" }), CommandErrors.TurnResolving);
    }

    // ----- engine pieces -----

    [Fact]
    public void TheEngineGuardsItsOwnTransitions()
    {
        var game = NewGame();
        var babylon = game.GetCivilization("Babylon");
        var greece = game.GetCivilization("Greece");
        Assert.False(game.BeginResolvingIfAllEnded());
        game.EndHumanTurn(babylon);
        Assert.False(game.BeginResolvingIfAllEnded());
        Assert.True(game.HasEndedTurn(babylon));
        Assert.False(game.HasEndedTurn(greece));
        game.EndHumanTurn(greece);
        Assert.True(game.BeginResolvingIfAllEnded());
        Assert.False(game.BeginResolvingIfAllEnded()); // already resolving
        Assert.Throws<InvalidOperationException>(() => game.SetHumanController(greece, false, ""));
        Assert.Throws<InvalidOperationException>(() => game.UndoEndHumanTurn(babylon));

        var sequential = NewGame(simultaneous: false);
        Assert.False(sequential.HasEndedTurn(sequential.GetCivilization("Babylon")));
        Assert.False(sequential.BeginResolvingIfAllEnded());
        Assert.Throws<InvalidOperationException>(() => sequential.SetHumanController(sequential.GetCivilization("Greece"), false, ""));
    }

    // ----- T202 review additions -----
    // A defeated human of an earlier round is an auto civ and its turn has not begun; when the last living human then leaves, it becomes the one the round
    // waits for, so the hand-over must begin its turn, or nobody could ever end the round.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheLastLivingHumanLeavingDoesNotStallTheRoundBehindADefeatedHuman(bool resign)
    {
        var game = NewGame();
        var session = Session(game);
        var greece = game.GetCivilization("Greece");
        foreach (var unit in greece.Units.GetCivUnits().ToList()) unit.Destroy();
        Assert.True(greece.IsDefeated());
        Ok(session.Submit("Babylon", new EndTurn()));
        Pump(session);
        Assert.False(game.IsTurnStarted(greece));

        Ok(resign ? session.Submit("Babylon", new Resign()) : session.SubmitAsServer(new SetController { CivId = "Babylon", Controller = PlayerType.AI }));
        Assert.True(game.IsTurnStarted(greece));
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState!.Phase);
        var turn = game.Turns;
        Ok(session.Submit("Greece", new EndTurn()));
        Pump(session);
        Assert.Equal(turn + 1, game.Turns);
        Assert.Equal(game.Civilizations.Where(it => game.IsTurnStarted(it)).Select(it => it.CivId).ToList(), game.TurnState.Started);
    }

    // The resolved state is a function of the log order only where the order matters: two humans ending in either order, with or without a take-back, resolve identically.
    [Fact]
    public void TheOrderInWhichHumansEndDoesNotChangeTheResolvedState()
    {
        ulong Run(bool reverse, bool undoCycle)
        {
            var game = NewGame();
            var session = new GameSession(game) { Clock = () => 9000L };
            string[] order = reverse ? ["Greece", "Babylon"] : ["Babylon", "Greece"];
            if (undoCycle)
            {
                Ok(session.Submit(order[0], new EndTurn()));
                Ok(session.Submit(order[0], new UndoEndTurn()));
            }
            foreach (var civ in order) Ok(session.Submit(civ, new EndTurn()));
            Pump(session);
            return Hash(game);
        }

        var expected = Run(false, false);
        Assert.Equal(expected, Run(true, false));
        Assert.Equal(expected, Run(false, true));
        Assert.Equal(expected, Run(true, true));
    }

    // Ending, taking back and ending again spends nothing: the turn counters and the end-of-turn work happen once per round, in the resolve steps.
    [Fact]
    public void EndOfRoundWorkHappensOncePerRoundNoMatterHowOftenTheTurnWasEnded()
    {
        var game = NewGame();
        var session = Session(game);
        var babylon = game.GetCivilization("Babylon");
        var played = babylon.TurnsPlayedAsHuman;
        var turn = game.Turns;
        Ok(session.Submit("Babylon", new EndTurn()));
        Ok(session.Submit("Babylon", new UndoEndTurn()));
        Ok(session.Submit("Babylon", new EndTurn()));
        Ok(session.Submit("Greece", new EndTurn()));
        Assert.Equal((played, turn), (babylon.TurnsPlayedAsHuman, game.Turns));
        var events = new List<GameEvent>();
        Pump(session, events);
        Assert.Equal((played + 1, turn + 1), (babylon.TurnsPlayedAsHuman, game.Turns));
        Assert.Equal(1, events.OfType<TurnEnded>().Count(it => it.CivId == "Babylon"));
    }

    // A human handed to the AI with a decision still open is played by step 2 without a second StartTurn, and the round resolves.
    [Fact]
    public void LeavingWithAPendingDecisionStillResolvesTheRoundAndTheCivCanBeReclaimed()
    {
        var game = NewGame();
        var session = Session(game);
        Block(game, "Babylon");
        Ok(session.SubmitAsServer(new SetController { CivId = "Babylon", Controller = PlayerType.AI }));
        Ok(session.Submit("Greece", new EndTurn()));
        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState!.Phase);
        Pump(session);
        Ok(session.SubmitAsServer(new SetController { CivId = "Babylon", Controller = PlayerType.Human, PlayerId = "k" }));
        Assert.True(game.IsActive(game.GetCivilization("Babylon")));
        Assert.Equal(game.Civilizations.Where(it => game.IsTurnStarted(it)).Select(it => it.CivId).ToList(), game.TurnState.Started);
    }

    // ----- property / fuzz -----

    // A tiny deterministic generator (the fuzz must not depend on System.Random's algorithm).
    private sealed class Lcg(ulong seed)
    {
        private ulong state = seed * 6364136223846793005UL + 1442695040888963407UL;

        public int Next(int bound)
        {
            state = unchecked(state * 6364136223846793005UL + 1442695040888963407UL);
            return (int)((state >> 33) % (ulong)bound);
        }
    }

    private static readonly string[] FuzzCivs = ["Babylon", "Greece", "Rome"];

    private static CommandResult FuzzStep(GameSession session, Lcg rng, bool settle)
    {
        var game = session.Game;
        var civ = FuzzCivs[rng.Next(FuzzCivs.Length)];
        if (settle && civ != "Rome") Settle(game, civ);
        switch (rng.Next(9))
        {
            case 0:
            case 1:
                return session.Submit(civ, new EndTurn());
            case 2:
                return session.Submit(civ, new UndoEndTurn());
            case 3:
                return session.SubmitAsServer(new ForceEndTurn { CivId = civ });
            case 4:
                return session.SubmitAsServer(new SetController { CivId = civ, Controller = rng.Next(2) == 0 ? PlayerType.Human : PlayerType.AI, PlayerId = "key-" + civ });
            case 5:
                return session.Submit(civ, new Resign());
            case 6:
                return session.SubmitAsServer(StepGuess(game, rng));
            case 7:
                return session.Submit(civ, Probe());
            default:
                return session.Submit(civ, new AckAlert { Index = 0, AlertType = AlertType.WonderBuilt, Value = "x" });
        }
    }

    // Mostly the correct step (so rounds complete), sometimes a deliberately wrong one.
    private static GameCommand StepGuess(GameInfo game, Lcg rng)
    {
        var step = game.NextResolveStep();
        if (step == null || rng.Next(4) == 0) return rng.Next(2) == 0 ? new StartNextTurn() : new RunAiTurn { CivId = FuzzCivs[rng.Next(FuzzCivs.Length)] };
        return StepCommand(step);
    }

    private static void CheckInvariants(GameInfo game, string where)
    {
        var state = game.TurnState!;
        var step = game.NextResolveStep();
        Assert.Equal(step, game.NextResolveStep()); // pure
        if (state.Phase == SimultaneousPhase.Resolving)
        {
            Assert.NotNull(step);
            return;
        }
        Assert.Null(step);
        var active = game.Civilizations.Where(game.IsActive).ToList();
        // Playing never rests with everybody ended: the command that completed the set started the resolution
        var waiting = game.Civilizations.Where(it => it.IsHuman() && !it.IsSpectator() && !it.IsDefeated()).ToList();
        if (waiting.Count > 0) Assert.False(waiting.All(it => game.HasEndedTurn(it)), where + ": everybody ended but the round is still Playing");
        Assert.All(state.Ended, id => Assert.True(game.GetCivilization(id).IsHuman(), where + ": " + id + " ended but is not human"));
        Assert.Equal(state.Ended.Count, state.Ended.Distinct().Count());
        Assert.All(active, civ => Assert.True(civ.IsHuman() && game.IsTurnStarted(civ) && !game.HasEndedTurn(civ)));
        Assert.Equal(game.Civilizations.Where(it => game.IsTurnStarted(it)).Select(it => it.CivId).ToList(), state.Started);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    public void RandomInterleavingsKeepTheInvariantsRejectCleanlyAndReplayToTheSameState(ulong seed)
    {
        var game = NewGame(seed: (long)seed);
        var session = Session(game);
        var rng = new Lcg(seed);
        var outcomes = new HashSet<string>();
        var rounds = 0;
        for (var i = 0; i < 90 && rounds < 4; i++)
        {
            var turnBefore = game.Turns;
            var hash = Hash(game);
            var count = session.Log.Count;
            var result = FuzzStep(session, rng, settle: i % 3 == 0);
            outcomes.Add(result.Accepted ? "ok" : result.Validation.Code);
            if (!result.Accepted)
            {
                Assert.Equal(hash, Hash(game)); // purity: a rejected command changes nothing
                Assert.Equal(count, session.Log.Count);
                Assert.False(string.IsNullOrEmpty(result.Validation.Message));
            }
            else
            {
                Assert.Equal(count + 1, session.Log.Count);
            }
            if (game.TurnState!.Phase == SimultaneousPhase.Resolving && rng.Next(3) != 0)
            {
                Pump(session);
                rounds++;
                Assert.Equal(turnBefore + 1, game.Turns);
            }
            CheckInvariants(game, $"seed {seed} command {i}");
        }

        // the log replays to the same state, and the final state survives a save and load
        var follower = NewGame(seed: (long)seed);
        var replay = new GameSession(follower);
        foreach (var envelope in session.Log) replay.Replay(envelope);
        Assert.Equal(Hash(game), Hash(follower));
        Assert.Equal(Hash(game), Hash(UncivFiles.GameInfoFromString(Save(game))));
        Assert.Contains("ok", outcomes);
    }
}
