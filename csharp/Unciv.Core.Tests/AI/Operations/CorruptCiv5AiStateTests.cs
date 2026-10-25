using System.Text.Json.Nodes;
using Unciv.AI;
using Unciv.AI.Operations;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using GrandStrategyKind = Unciv.AI.GrandStrategy.GrandStrategy;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.AI.Operations;

// T235 review fix: a save is untrusted input. Whatever its "civ5Ai" records hold, loading it and playing on must never throw, and the AI must carry on.
[Collection("RulesetCache")]
public sealed class CorruptCiv5AiStateTests : IDisposable
{
    private static readonly HexCoord OffMap = new(9999, -9999);
    private readonly RulesetClass ruleset;

    public CorruptCiv5AiStateTests()
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

    private GameInfo StartAiGame()
    {
        var setup = new GameSetupInfo(
            new GameParameters
            {
                BaseRuleset = ruleset.Name,
                NumberOfCityStates = 0,
                VictoryTypes = ruleset.Victories.Keys.ToList(),
                Players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.AI), new Player("Rome", PlayerType.AI)],
            },
            new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 5, MapSize = MapSize.Tiny });
        var game = GameStarter.StartNewGame(setup, 9L, 1000L);
        game.SimulateUntilWin = true;   // the human seat is played by the AI too
        Play(game, 3);
        return game;
    }

    private static void Play(GameInfo game, int turns)
    {
        for (int i = 0; i < turns; i++)
        {
            game.SimulateMaxTurns = game.Turns + 1;
            game.NextTurn(2000L);
        }
    }

    private static OperationRecord Valid(GameInfo game, int id = 1) => new()
    {
        Id = id,
        Type = OperationType.BasicCityAttack,
        Target = game.TileMap.TileList[0].Position,
        Muster = game.TileMap.TileList[1].Position,
        Slots = [ArmyRole.Melee, ArmyRole.Ranged],
        Filled = [null, null],
        MaxRecruitTurns = 5,
        State = OperationState.Recruiting,
    };

    private static void Mutate(GameInfo game, Action<Civ5AiState, GameInfo> corrupt)
    {
        foreach (var civ in game.Civilizations.Where(c => c.IsAI() && !c.IsBarbarian))
        {
            civ.Civ5Ai ??= new Civ5AiState();
            corrupt(civ.Civ5Ai, game);
        }
    }

    // Edits the saved JSON of each AI civ's state, as a hand-edited save file would be, and loads it back into the civ.
    private static void MutateJson(GameInfo game, Action<JsonObject> corrupt)
    {
        foreach (var civ in game.Civilizations.Where(c => c.IsAI() && !c.IsBarbarian))
        {
            var node = JsonNode.Parse(UncivJson.Serialize(civ.Civ5Ai ?? new Civ5AiState()))!.AsObject();
            corrupt(node);
            civ.Civ5Ai = UncivJson.Deserialize<Civ5AiState>(node.ToJsonString());
        }
    }

    private static void Reload(ref GameInfo game) => game = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(game, forceZip: false));

    private void PlaysOnAfter(Action<Civ5AiState, GameInfo> corrupt)
    {
        var game = StartAiGame();
        Mutate(game, corrupt);
        Reload(ref game);
        PlayAndCheck(game);
    }

    private static void PlayAndCheck(GameInfo game)
    {
        int turn = game.Turns;
        Play(game, 3);
        Assert.Equal(turn + 3, game.Turns);
        foreach (var civ in game.Civilizations.Where(c => c.IsAI() && !c.IsBarbarian))
        {
            var state = civ.Civ5Ai!;
            Assert.All(state.Operations, o => Assert.True(state.NextOperationId > o.Id));
            Assert.All(state.Operations, o => Assert.True(game.TileMap.Contains(o.Target) && game.TileMap.Contains(o.Muster) && o.Slots.Count == o.Filled.Count));
        }
    }

    [Fact]
    public void AnOperationTargetOutsideTheMapIsDropped() =>
        PlaysOnAfter((s, g) => s.Operations = [Valid(g).With(r => r.Target = OffMap), Valid(g, 2)]);

    [Fact]
    public void AnOperationMusterOutsideTheMapIsDropped() =>
        PlaysOnAfter((s, g) => s.Operations = [Valid(g).With(r => r.Muster = OffMap)]);

    [Fact]
    public void AnOperationWithoutSlotsIsDropped() =>
        PlaysOnAfter((s, g) => s.Operations = [Valid(g).With(r => { r.Slots = []; r.Filled = []; })]);

    [Fact]
    public void AnOperationWithMismatchedSlotListsIsDropped() =>
        PlaysOnAfter((s, g) => s.Operations = [Valid(g).With(r => r.Filled = [null])]);

    [Fact]
    public void ANullOperationsListInTheSaveIsTreatedAsEmpty()
    {
        var game = StartAiGame();
        MutateJson(game, o => o["operations"] = null);
        Reload(ref game);
        PlayAndCheck(game);
    }

    [Fact]
    public void ANullBlockedTargetsListInTheSaveIsTreatedAsEmpty()
    {
        var game = StartAiGame();
        MutateJson(game, o => o["blockedTargets"] = null);
        Reload(ref game);
        PlayAndCheck(game);
    }

    [Fact]
    public void NullEntriesAndNullListsInsideARecordAreDropped()
    {
        var game = StartAiGame();
        MutateJson(game, o =>
        {
            var good = JsonNode.Parse(UncivJson.Serialize(Valid(game)))!.AsObject();
            var noSlots = JsonNode.Parse(UncivJson.Serialize(Valid(game, 2)))!.AsObject();
            noSlots["slots"] = null;
            var noFilled = JsonNode.Parse(UncivJson.Serialize(Valid(game, 3)))!.AsObject();
            noFilled["filled"] = null;
            o["operations"] = new JsonArray([(JsonNode?)null, good, noSlots, noFilled]);
            o["blockedTargets"] = new JsonArray([(JsonNode?)null]);
        });
        Reload(ref game);
        PlayAndCheck(game);
    }

    [Fact]
    public void ABlockedTargetOutsideTheMapIsDropped() =>
        PlaysOnAfter((s, _) => s.BlockedTargets = [new BlockedTarget { Position = OffMap, UntilTurn = 1_000_000 }]);

    [Fact]
    public void ADuplicateOperationIdKeepsOnlyTheFirst() =>
        PlaysOnAfter((s, g) => s.Operations = [Valid(g), Valid(g), Valid(g)]);

    [Fact]
    public void UnitsThatDoNotExistOrAreHeldTwiceLeaveTheirSlots() =>
        PlaysOnAfter((s, g) => s.Operations = [Valid(g).With(r => r.Filled = [424242, 424242]), Valid(g, 2).With(r => { r.State = OperationState.Moving; r.Filled = [-5, int.MinValue]; })]);

    [Fact]
    public void UndefinedEnumValuesAreDropped() =>
        PlaysOnAfter((s, g) =>
        {
            s.CurrentStrategy = (GrandStrategyKind)99;
            s.Operations =
            [
                Valid(g, 1).With(r => r.Type = (OperationType)500),
                Valid(g, 2).With(r => r.State = (OperationState)(-1)),
                Valid(g, 3).With(r => r.State = OperationState.Succeeded),
                Valid(g, 4).With(r => r.Slots = [(ArmyRole)7, ArmyRole.Melee]),
            ];
        });

    [Fact]
    public void NegativeAndHugeCountersAreClamped() =>
        PlaysOnAfter((s, g) =>
        {
            s.TurnsActive = int.MinValue;
            s.NextOperationId = int.MinValue;
            s.Operations =
            [
                Valid(g, 1).With(r => { r.MaxRecruitTurns = int.MaxValue; r.TurnsRecruiting = int.MaxValue; r.StartTurn = int.MinValue; }),
                Valid(g, 2).With(r => { r.MaxRecruitTurns = int.MinValue; r.TurnsRecruiting = int.MinValue; r.StartTurn = int.MaxValue; }),
                Valid(g, -3),
                Valid(g, 0),
                Valid(g, int.MaxValue),
            ];
        });

    [Fact]
    public void AHugeTurnsActiveAndNextIdAreClamped() =>
        PlaysOnAfter((s, g) =>
        {
            s.TurnsActive = int.MaxValue;
            s.NextOperationId = int.MaxValue;
            s.Operations = [Valid(g, 7)];
            s.BlockedTargets = [new BlockedTarget { Position = g.TileMap.TileList[0].Position, UntilTurn = int.MaxValue }];
        });

    [Fact]
    public void AnAbsurdNumberOfSlotsIsDropped() =>
        PlaysOnAfter((s, g) => s.Operations = [Valid(g).With(r => { r.Slots = [.. Enumerable.Repeat(ArmyRole.Melee, 100_000)]; r.Filled = [.. Enumerable.Repeat((int?)null, 100_000)]; })]);

    [Fact]
    public void NextOperationIdMovesPastTheLargestSavedId()
    {
        var game = StartAiGame();
        Mutate(game, (s, g) => { s.NextOperationId = 1; s.Operations = [Valid(g, 41)]; });
        var civ = game.Civilizations.First(c => c.IsAI() && !c.IsBarbarian);
        Civ5AiStateSanitizer_Run(civ);
        Assert.Equal(42, civ.Civ5Ai!.NextOperationId);
        Assert.Single(civ.Civ5Ai.Operations);
    }

    [Fact]
    public void SanitizingKeepsOnlyRecordsThatCanRun()
    {
        var game = StartAiGame();
        var civ = game.Civilizations.First(c => c.IsAI() && !c.IsBarbarian);
        int unitId = civ.Units.GetCivUnits().First().Id;
        civ.Civ5Ai = new Civ5AiState
        {
            CurrentStrategy = (GrandStrategyKind)99,
            TurnsActive = -4,
            BlockedTargets = [new BlockedTarget { Position = OffMap }, new BlockedTarget { Position = game.TileMap.TileList[0].Position, UntilTurn = int.MaxValue }],
            Operations =
            [
                Valid(game, 1),
                Valid(game, 1),                                              // duplicate id
                Valid(game, 2).With(r => r.Muster = OffMap),
                Valid(game, 3).With(r => r.Target = OffMap),
                Valid(game, 4).With(r => r.Slots = []),
                Valid(game, 5).With(r => r.Filled = [null]),
                Valid(game, 6).With(r => r.Type = (OperationType)500),
                Valid(game, 7).With(r => r.State = OperationState.Aborted),
                Valid(game, 8).With(r => r.Slots = [(ArmyRole)9, ArmyRole.Melee]),
                Valid(game, 9).With(r => r.Slots = [.. Enumerable.Repeat(ArmyRole.Melee, 100_000)]),
                Valid(game, 10).With(r => { r.Filled = [unitId, 424242]; r.TurnsRecruiting = int.MaxValue; r.MaxRecruitTurns = -1; r.StartTurn = int.MaxValue; }),
                Valid(game, 11),                                             // dropped by the cap of two running operations (T237)
                Valid(game, -2),
                Valid(game, int.MaxValue),
            ],
        };
        Civ5AiStateSanitizer.Sanitize(civ.Civ5Ai, civ);
        var state = civ.Civ5Ai;
        Assert.Equal(GrandStrategyKind.None, state.CurrentStrategy);
        Assert.Equal(0, state.TurnsActive);
        var blocked = Assert.Single(state.BlockedTargets);
        Assert.True(blocked.UntilTurn <= game.Turns + OperationsAdapter.BlockedTurns);
        Assert.Equal([1, 10], state.Operations.Select(o => o.Id));
        var ten = state.Operations[1];
        Assert.Equal([unitId, (int?)null], ten.Filled);
        Assert.InRange(ten.TurnsRecruiting, 0, Civ5AiStateSanitizer.MaxCounter);
        Assert.Equal(0, ten.MaxRecruitTurns);
        Assert.InRange(ten.StartTurn, game.Turns - Civ5AiStateSanitizer.MaxCounter, game.Turns);
        Assert.Equal(11, state.NextOperationId);
    }

    [Fact]
    public void AUnitHeldByOneOperationLeavesTheSlotOfTheOther()
    {
        var game = StartAiGame();
        var civ = game.Civilizations.First(c => c.IsAI() && !c.IsBarbarian);
        int unitId = civ.Units.GetCivUnits().First().Id;
        civ.Civ5Ai = new Civ5AiState { Operations = [Valid(game, 1).With(r => r.Filled = [unitId, null]), Valid(game, 2).With(r => r.Filled = [unitId, null])] };
        Civ5AiStateSanitizer.Sanitize(civ.Civ5Ai, civ);
        Assert.Equal([unitId, (int?)null], civ.Civ5Ai.Operations[0].Filled);
        Assert.Equal([(int?)null, null], civ.Civ5Ai.Operations[1].Filled);
    }

    [Fact]
    public void AValidSavedStateIsLeftAlone()
    {
        var game = StartAiGame();
        var civ = game.Civilizations.First(c => c.IsAI() && !c.IsBarbarian);
        civ.Civ5Ai = new Civ5AiState { CurrentStrategy = GrandStrategyKind.Culture, TurnsActive = 4, NextOperationId = 9, Operations = [Valid(game, 8)] };
        var before = UncivJson.Serialize(civ.Civ5Ai);
        Civ5AiStateSanitizer_Run(civ);
        Assert.Equal(before, UncivJson.Serialize(civ.Civ5Ai));
    }

    private static void Civ5AiStateSanitizer_Run(Civilization civ) => Civ5AiStateSanitizer.Sanitize(civ.Civ5Ai!, civ);
}

internal static class RecordExtensions
{
    public static OperationRecord With(this OperationRecord record, Action<OperationRecord> change)
    {
        change(record);
        return record;
    }
}
