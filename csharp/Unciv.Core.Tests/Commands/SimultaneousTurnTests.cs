using System.Globalization;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.Empire;
using Unciv.Commands.Log;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Commands;

// T203 (design/SIMULTANEOUS-TURNS.md sections 4 and 5): two scripted humans play a real started game at the same time. Their per-turn scripts are advanced
// in a seeded random interleaving, so the command order is decided by the log and nothing else. The tests prove the properties of section 4: replay equals
// live at every checkpoint (also from a snapshot plus the log tail), the same inputs give the same bytes, and a different interleaving is a different
// but equally valid game. The conflict table of section 5 and the visibility cases are in SimultaneousTurnConflictTests.
[Collection("RulesetCache")]
public sealed class SimultaneousTurnTests : IDisposable
{
    private const string Babylon = "Babylon";
    private const string Greece = "Greece";
    private readonly RulesetClass ruleset;

    public SimultaneousTurnTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private GameSetupInfo Setup(int mapSeed) => new(
        new GameParameters
        {
            BaseRuleset = ruleset.Name,
            NumberOfCityStates = 0,
            VictoryTypes = ruleset.Victories.Keys.ToList(),
            Players = [new Player(Babylon, PlayerType.Human), new Player(Greece, PlayerType.Human), new Player("Rome", PlayerType.AI)],
        },
        new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = mapSeed, MapSize = MapSize.Tiny });

    // A tiny deterministic generator: the interleaving must not depend on System.Random's algorithm.
    internal sealed class Lcg(ulong seed)
    {
        private ulong state = seed * 6364136223846793005UL + 1442695040888963407UL;

        public int Next(int bound)
        {
            state = unchecked(state * 6364136223846793005UL + 1442695040888963407UL);
            return (int)((state >> 33) % (ulong)bound);
        }
    }

    /// <summary>
    /// One human's per-turn script as a state machine that is asked for its next command only when the host schedules it, so what it does depends on the
    /// live state at that moment (what the other human did meanwhile included). A refused command is not retried; a decision it cannot answer is
    /// ended with the server's <see cref="ForceEndTurn"/>, as the single-player ScriptedHuman does.
    /// </summary>
    private sealed class Script(GameSession session, string civId)
    {
        private int plannedTurn = -1;
        private Queue<int> units = new();
        public int Commands { get; private set; }
        public int Rejected { get; private set; }

        private Unciv.Logic.Civilization.Civilization Civ => session.Game.GetCivilization(civId);

        /// <summary>Submits the next command of this human's turn, or returns false when it has nothing to do until the round is resolved.</summary>
        public bool Step()
        {
            var game = session.Game;
            if (!game.IsActive(Civ)) return false;
            if (plannedTurn != game.Turns)
            {
                plannedTurn = game.Turns;
                units = new Queue<int>(Civ.Units.GetCivUnits().Where(it => it.IsIdle()).Select(it => it.Id));
            }
            while (units.Count > 0)
            {
                var unit = Civ.Units.GetUnitById(units.Dequeue());
                if (unit == null || !unit.IsIdle()) continue;
                var actions = UnitActionDescriptor.Describe(unit).Where(it => it.Action.Action != null).ToList();
                var chosen = actions.FirstOrDefault(it => it.Descriptor.ActionType == UnitActionType.FoundCity);
                if (chosen.Descriptor == null && !unit.IsCivilian()) chosen = actions.FirstOrDefault(it => it.Descriptor.ActionType == UnitActionType.Explore);
                if (chosen.Descriptor == null) continue;
                Submit(new ExecuteUnitAction { UnitId = unit.Id, Descriptor = chosen.Descriptor, Confirmed = true });
                return true;
            }
            var blocking = PendingDecisions.BlockingFor(Civ);
            if (blocking.Count > 0)
            {
                if (Answer(blocking[0])) return true;
                session.SubmitAsServer(new ForceEndTurn { CivId = civId });
                Commands++;
                return true;
            }
            Submit(new EndTurn());
            return true;
        }

        private void Submit(GameCommand command)
        {
            Commands++;
            if (!session.Submit(civId, command).Accepted) Rejected++;
        }

        private bool Answer(Decision decision)
        {
            var rules = session.Game.Ruleset;
            switch (decision.Kind)
            {
                case DecisionKind.CityProduction:
                    var city = Civ.Cities.First(it => it.Name == decision.Subject);
                    string[] choices = ["Warrior", "Scout", "Monument", "Worker", "Granary", "Library", "Barracks", "Settler"];
                    return choices.Any(name => Try(new QueueConstruction { CityId = city.Id, Name = name }));
                case DecisionKind.Technology when Civ.Tech.FreeTechs != 0:
                    return rules.Technologies.Keys.Any(name => Try(new PickFreeTechnology { Tech = name }));
                case DecisionKind.Technology:
                    return rules.Technologies.Keys.Any(name => Try(new SetResearchQueue { Techs = [name] }));
                case DecisionKind.Policy:
                    return rules.PolicyBranches.Keys.Concat(rules.Policies.Keys).Any(name => Try(new AdoptPolicy { Policy = name }));
                case DecisionKind.MoveAutomatedUnits:
                    return Try(new MoveAutomatedUnits());
                default:
                    return false;
            }
        }

        private bool Try(GameCommand command)
        {
            Commands++;
            return session.Submit(civId, command).Accepted;
        }
    }

    private sealed record Live(GameSession Session, StringWriter Text, CommandLogWriter Writer, CommandLogHeader Header, Script[] Humans)
    {
        public int Commands => Humans.Sum(it => it.Commands);
    }

    private Live Start(long gameSeed, int mapSeed)
    {
        var setup = Setup(mapSeed);
        var header = CommandLogHeader.Create(setup, gameSeed, 1000L) with { TurnMode = CommandLogHeader.SimultaneousTurns };
        var game = GameStarter.StartNewGame(setup, gameSeed, 1000L);
        game.StartSimultaneousTurns();
        var session = new GameSession(game) { Clock = ScriptedHuman.DeterministicClock(2000L) };
        var text = new StringWriter(CultureInfo.InvariantCulture);
        var writer = new CommandLogWriter(text, header);
        writer.WriteCheckpoint(0, game.Turns, ReplayVerifier.FullHash(game));
        return new Live(session, text, writer, header, [new Script(session, Babylon), new Script(session, Greece)]);
    }

    private static void Checkpoint(Live live) =>
        live.Writer.WriteCheckpoint(live.Session.LastSeq, live.Session.Game.Turns, ReplayVerifier.FullHash(live.Session.Game));

    private static GameCommand StepCommand(ResolveStep step) => step switch
    {
        ResolveStep.ProcessEndTurn s => new ProcessEndTurn { CivId = s.CivId },
        ResolveStep.RunAiTurn s => new RunAiTurn { CivId = s.CivId },
        ResolveStep.StartNextTurn => new StartNextTurn(),
        ResolveStep.BeginTurn s => new BeginTurn { CivId = s.CivId },
        _ => throw new InvalidOperationException(step.ToString()),
    };

    /// <summary>Plays rounds until <paramref name="untilTurn"/> is the game's turn, one command of a randomly chosen human at a time.</summary>
    private static void Play(Live live, ulong interleaving, int untilTurn)
    {
        var rng = new Lcg(interleaving);
        var session = live.Session;
        var game = session.Game;
        for (var guard = 0; game.Turns < untilTurn && game.VictoryData == null; guard++)
        {
            Assert.True(guard < 20000, "the scripted humans stopped making progress");
            if (game.TurnState!.Phase == SimultaneousPhase.Resolving)
            {
                while (game.NextResolveStep() is { } step)
                {
                    var result = session.SubmitAsServer(StepCommand(step));
                    Assert.True(result.Accepted, result.Validation.Code + ": " + result.Validation.Message);
                }
                live.Writer.Sync(session);
                Checkpoint(live);
                continue;
            }
            var first = rng.Next(live.Humans.Length);
            var stepped = live.Humans[first].Step() || live.Humans[1 - first].Step();
            live.Writer.Sync(session);
            if (!stepped) break; // every human is defeated or ended: nothing left that a script can do
            if (session.LastSeq % 7 == 0) Checkpoint(live); // checkpoints inside a round too: the interleaving itself is replayed and compared
        }
        live.Writer.Sync(session);
    }

    private static List<ulong> Checkpoints(CommandLog log) => log.Lines.OfType<CommandLogLine.Checkpoint>().Select(it => it.FullHash).ToList();

    private static string Save(GameInfo game) => UncivFiles.GameInfoToString(game, forceZip: false);

    private Live Played(long gameSeed, int mapSeed, ulong interleaving, int turns)
    {
        var live = Start(gameSeed, mapSeed);
        Play(live, interleaving, live.Session.Game.Turns + turns);
        return live;
    }

    [Theory]
    [InlineData(1L, 3, 11UL)]
    [InlineData(77L, 11, 12UL)]
    public void ReplayEqualsLiveForASeededInterleavingOfTwoHumansOver60Turns(long gameSeed, int mapSeed, ulong interleaving)
    {
        var live = Played(gameSeed, mapSeed, interleaving, 60);
        var game = live.Session.Game;
        Assert.Equal(60, game.Turns); // a new game starts at turn 0
        var log = CommandLogReader.Read(live.Text.ToString());
        Assert.Equal(CommandLogHeader.SimultaneousTurns, log.Header.TurnMode);

        // the run really is a two-human run: both founded cities and acted, the log interleaves their actors, and the checkpoints are mostly distinct states
        Assert.All([Babylon, Greece], civ => Assert.NotEmpty(game.GetCivilization(civ).Cities));
        var actors = log.Lines.OfType<CommandLogLine.Command>().Select(it => it.Envelope.ActorId).ToList();
        Assert.Contains(Babylon, actors);
        Assert.Contains(Greece, actors);
        Assert.Contains(CommandContext.ServerActor, actors);
        Assert.True(Switches(actors) > 100, "the two humans' commands must really alternate within a round");
        Assert.True(Checkpoints(log).Distinct().Count() > 60 * 2);
        Assert.Contains(log.Lines, it => it is CommandLogLine.Command { Envelope.Type: "execute-unit-action" });

        var result = ReplayVerifier.Verify(log);
        Assert.True(result.Matches, $"diverged at seq {result.DivergedAtSeq} after {result.CheckpointsVerified} of {Checkpoints(log).Count} checkpoints");
        Assert.Equal(Checkpoints(log).Count, result.CheckpointsVerified);
        Assert.Equal(live.Session.LastSeq, result.LastSeq);
        Assert.Equal(ReplayVerifier.FullHash(game), result.FinalHash);
        Assert.Equal(Save(game), Save(result.Game)); // not only the hash: the same bytes
    }

    private static int Switches(List<string> actors)
    {
        var humans = actors.Where(it => it != CommandContext.ServerActor).ToList();
        return humans.Zip(humans.Skip(1)).Count(it => it.First != it.Second);
    }

    [Fact]
    public void TheSameInputsTwiceGiveTheSameLogByteForByteAndAnotherInterleavingGivesAnotherValidGame()
    {
        var first = Played(5L, 8, 3UL, 15).Text.ToString();
        Assert.Equal(first, Played(5L, 8, 3UL, 15).Text.ToString());
        Assert.DoesNotContain('\r', first);
        Assert.All(first.TrimEnd('\n').Split('\n'), line => Assert.StartsWith("{", line, StringComparison.Ordinal));

        // The order is data: a different interleaving of the same scripts is a different log, and it replays just as well.
        var other = Played(5L, 8, 4UL, 15);
        Assert.NotEqual(first, other.Text.ToString());
        var result = ReplayVerifier.Verify(CommandLogReader.Read(other.Text.ToString()));
        Assert.True(result.Matches);
        Assert.Equal(ReplayVerifier.FullHash(other.Session.Game), result.FinalHash);
        // the interleaving shows in the log: one human's commands come at different seqs
        Assert.NotEqual(
            SeqsOf(CommandLogReader.Read(first), Greece, "end-turn"),
            SeqsOf(CommandLogReader.Read(other.Text.ToString()), Greece, "end-turn"));
    }

    private static List<long> SeqsOf(CommandLog log, string actor, string type) =>
        log.Lines.OfType<CommandLogLine.Command>().Where(it => it.Envelope.ActorId == actor && it.Envelope.Type == type).Select(it => it.Envelope.Seq).ToList();

    [Fact]
    public void ASnapshotMidRoundPlusTheLogTailReproducesTheFinalHashAndEveryLaterCheckpoint()
    {
        var live = Start(9L, 5);
        Play(live, 21UL, 12);
        // stop in the middle of a round: one human has ended, the other is still playing
        var game = live.Session.Game;
        var babylon = game.GetCivilization(Babylon);
        for (var guard = 0; !game.HasEndedTurn(babylon); guard++)
        {
            Assert.True(guard < 1000 && live.Humans[0].Step());
            live.Writer.Sync(live.Session);
        }
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState!.Phase);
        Assert.False(game.HasEndedTurn(game.GetCivilization(Greece)));
        live.Writer.Sync(live.Session);
        Checkpoint(live);
        var snapshotSeq = live.Session.LastSeq;
        var saved = Save(live.Session.Game);
        Play(live, 22UL, 30);

        var log = CommandLogReader.Read(live.Text.ToString());
        var later = log.Lines.OfType<CommandLogLine.Checkpoint>().Count(it => it.Seq > snapshotSeq);
        Assert.True(later > 18);
        var result = ReplayVerifier.VerifyTail(UncivFiles.GameInfoFromString(saved), snapshotSeq, log);
        Assert.True(result.Matches, "diverged at " + result.DivergedAtSeq);
        Assert.Equal(later + 1, result.CheckpointsVerified); // the snapshot's own checkpoint and every later one
        Assert.Equal(ReplayVerifier.FullHash(live.Session.Game), result.FinalHash);
        Assert.Equal(live.Session.LastSeq, result.LastSeq);
    }

    [Fact]
    public void AHeaderNamingAnUnknownTurnModeIsRefusedAndASimultaneousOneStartsSimultaneousTurns()
    {
        var live = Played(5L, 8, 3UL, 2);
        var log = CommandLogReader.Read(live.Text.ToString());
        Assert.Equal(SimultaneousPhase.Playing, ReplayVerifier.StartGame(log.Header).TurnState!.Phase);
        Assert.Equal(TurnMode.Simultaneous, ReplayVerifier.StartGame(log.Header).TurnMode);
        Assert.Throws<CommandLogException>(() => ReplayVerifier.StartGame(log.Header with { TurnMode = "Lockstep" }));
        // a log played simultaneously cannot be replayed as sequential: the first EndTurn would be refused
        Assert.Equal(TurnMode.Sequential, ReplayVerifier.StartGame(log.Header with { TurnMode = CommandLogHeader.SequentialTurns }).TurnMode);
    }
}
