using Unciv.AI;
using Unciv.AI.Operations;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.AI.Operations;

// T235 review round 2: pins the exact edges of the sanitizer (valid values at the limit survive untouched, one past the limit is clamped or dropped),
// which the behavioural tests of CorruptCiv5AiStateTests do not reach (mutating the limits there survived).
[Collection("RulesetCache")]
public sealed class Civ5AiStateSanitizerBoundaryTests : IDisposable
{
    private readonly GameInfo game;
    private readonly Civilization civ;

    public Civ5AiStateSanitizerBoundaryTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        RulesetClass ruleset = RulesetCache.GetVanillaRuleset();
        var setup = new GameSetupInfo(
            new GameParameters
            {
                BaseRuleset = ruleset.Name,
                NumberOfCityStates = 0,
                VictoryTypes = ruleset.Victories.Keys.ToList(),
                Players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.AI)],
            },
            new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 5, MapSize = MapSize.Tiny });
        game = GameStarter.StartNewGame(setup, 9L, 1000L);
        civ = game.Civilizations.First(c => c.IsAI() && !c.IsBarbarian);
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException();
    }

    private OperationRecord Record(int id, Action<OperationRecord>? change = null)
    {
        var r = new OperationRecord
        {
            Id = id,
            Type = OperationType.BasicCityAttack,
            Target = game.TileMap.TileList[0].Position,
            Muster = game.TileMap.TileList[1].Position,
            Slots = [ArmyRole.Melee],
            Filled = [null],
            MaxRecruitTurns = 5,
            State = OperationState.Recruiting,
            StartTurn = game.Turns,
        };
        change?.Invoke(r);
        return r;
    }

    private Civ5AiState Sanitized(Civ5AiState state)
    {
        civ.Civ5Ai = state;
        Civ5AiStateSanitizer.Sanitize(state, civ);
        return state;
    }

    [Theory]
    [InlineData(OperationState.Recruiting)]
    [InlineData(OperationState.Gathering)]
    [InlineData(OperationState.Moving)]
    [InlineData(OperationState.AtTarget)]
    public void EveryLiveStateIsKept(OperationState state) =>
        Assert.Single(Sanitized(new Civ5AiState { Operations = [Record(1, r => r.State = state)] }).Operations);

    [Theory]
    [InlineData(OperationState.Succeeded)]
    [InlineData(OperationState.Aborted)]
    public void FinishedStatesAreDropped(OperationState state) =>
        Assert.Empty(Sanitized(new Civ5AiState { Operations = [Record(1, r => r.State = state)] }).Operations);

    [Fact]
    public void IdsBelowOneAreDroppedAndOneIsKept() =>
        Assert.Equal([1], Sanitized(new Civ5AiState { Operations = [Record(0), Record(-1), Record(1)] }).Operations.Select(o => o.Id));

    [Fact]
    public void SixteenSlotsAreKeptAndSeventeenDropped()
    {
        OperationRecord Slots(int id, int n) => Record(id, r => { r.Slots = [.. Enumerable.Repeat(ArmyRole.Melee, n)]; r.Filled = [.. Enumerable.Repeat((int?)null, n)]; });
        Assert.Equal([1], Sanitized(new Civ5AiState { Operations = [Slots(1, Civ5AiStateSanitizer.MaxSlots), Slots(2, Civ5AiStateSanitizer.MaxSlots + 1)] }).Operations.Select(o => o.Id));
    }

    [Fact]
    public void AFilledListLongerThanTheSlotsIsDropped() =>
        Assert.Empty(Sanitized(new Civ5AiState { Operations = [Record(1, r => r.Filled = [null, null])] }).Operations);

    [Fact]
    public void CountersAreKeptAtTheirLimitsAndClampedOnePast()
    {
        int turn = game.Turns;
        int max = Civ5AiStateSanitizer.MaxCounter;
        var s = Sanitized(new Civ5AiState
        {
            TurnsActive = 100_000,
            Operations =
            [
                Record(1, r => { r.MaxRecruitTurns = max; r.TurnsRecruiting = 0; r.StartTurn = turn - max; }),
                Record(2, r => { r.MaxRecruitTurns = max + 1; r.TurnsRecruiting = -1; r.StartTurn = turn - max - 1; }),
                Record(3, r => { r.MaxRecruitTurns = 0; r.TurnsRecruiting = max; r.StartTurn = turn; }),
                Record(4, r => { r.MaxRecruitTurns = -1; r.TurnsRecruiting = max + 1; r.StartTurn = turn + 1; }),
            ],
        });
        Assert.Equal(100_000, s.TurnsActive);
        Assert.Equal([(max, 0, turn - max), (max, 0, turn - max), (0, max, turn), (0, max, turn)], s.Operations.Select(o => (o.MaxRecruitTurns, o.TurnsRecruiting, o.StartTurn)));
        Assert.Equal(100_000, Sanitized(new Civ5AiState { TurnsActive = 100_001 }).TurnsActive);
        Assert.Equal(0, Sanitized(new Civ5AiState { TurnsActive = -1 }).TurnsActive);
    }

    [Fact]
    public void NextOperationIdIsAtLeastOneAndAboveTheLargestId()
    {
        Assert.Equal(1, Sanitized(new Civ5AiState { NextOperationId = 0 }).NextOperationId);
        Assert.Equal(1, Sanitized(new Civ5AiState { NextOperationId = 1 }).NextOperationId);
        Assert.Equal(5, Sanitized(new Civ5AiState { NextOperationId = 5, Operations = [Record(2)] }).NextOperationId);
        Assert.Equal(int.MaxValue - Civ5AiStateSanitizer.MaxCounter, Sanitized(new Civ5AiState { NextOperationId = int.MaxValue }).NextOperationId);
    }
}
