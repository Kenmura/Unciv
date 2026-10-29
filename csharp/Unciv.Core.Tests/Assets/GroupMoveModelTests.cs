using Unciv.Assets;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unit;
using Unciv.Net.Visibility;
using Unciv.Queries.Units;

namespace Unciv.Core.Tests.Assets;

public class GroupMoveModelTests
{
    // A line of tiles (x, 0): a path is the next tile toward the goal; tiles in Refuse refuse a move, tiles in Occupied cannot be a goal.
    private sealed class Line : IGroupMoveEnvironment
    {
        public Dictionary<int, HexCoord> Units { get; } = [];
        public Dictionary<int, float> Moves { get; } = [];
        public HashSet<HexCoord> Occupied { get; } = [];
        public HashSet<HexCoord> Refuse { get; } = [];
        public List<(int Id, HexCoord To)> Sent { get; } = [];
        public HashSet<int> Civilians { get; } = [];
        public int MaxStep { get; set; } = 2;

        public HexCoord? Position(int unitId) => Units.TryGetValue(unitId, out var at) ? at : null;

        public float MovesLeft(int unitId) => Moves.GetValueOrDefault(unitId, 2f);

        public Task<PathPreviewResult?> PathAsync(int unitId, HexCoord to)
        {
            var at = Units[unitId];
            if (Occupied.Contains(to) && to != at && !Civilians.Contains(unitId)) return Task.FromResult<PathPreviewResult?>(new PathPreviewResult(unitId, new TilePos(to.x, to.y), false, false, 0, null, []));
            int distance = HexMath.GetDistance(at, to);
            int turns = (distance + MaxStep - 1) / MaxStep;
            var step = distance <= MaxStep ? to : new HexCoord(at.x + System.Math.Sign(to.x - at.x) * MaxStep, at.y);
            return Task.FromResult<PathPreviewResult?>(new PathPreviewResult(unitId, new TilePos(to.x, to.y), true, true, turns, new TilePos(step.x, step.y), []));
        }

        public Task<SubmitResult> MoveAsync(int unitId, HexCoord to)
        {
            Sent.Add((unitId, to));
            if (Refuse.Contains(to)) return Task.FromResult(new SubmitResult(false, "blocked", []));
            Occupied.Remove(Units[unitId]);
            Units[unitId] = to;
            Occupied.Add(to);
            return Task.FromResult(new SubmitResult(true, "", []));
        }
    }

    private static Line Start(params (int Id, int X)[] units)
    {
        var line = new Line();
        foreach (var (id, x) in units)
        {
            line.Units[id] = new HexCoord(x, 0);
            line.Occupied.Add(new HexCoord(x, 0));
        }
        return line;
    }

    [Fact]
    public void ToggleAddsAndRemovesKeepingOrder()
    {
        Assert.Equal([1, 2], GroupMoveModel.Toggle([1], 2));
        Assert.Equal([1, 3], GroupMoveModel.Toggle([1, 2, 3], 2));
        Assert.Empty(GroupMoveModel.Toggle([1], 1));
    }

    [Fact]
    public async Task EveryUnitGetsItsOwnMoveCommandFrontUnitFirst()
    {
        var line = Start((1, 0), (2, 3), (3, 1));
        var report = await GroupMoveModel.MoveGroupAsync(line, [1, 2, 3], [], new HexCoord(6, 0));
        Assert.Equal([2, 3, 1], line.Sent.Select(it => it.Id));
        Assert.All(report.Outcomes, it => Assert.Equal(GroupMoveStatus.Moved, it.Status));
        Assert.Equal(new HexCoord(5, 0), line.Units[2]);
    }

    [Fact]
    public async Task SameInputsSendTheSameCommands()
    {
        var a = Start((5, 0), (4, 0));
        var b = Start((5, 0), (4, 0));
        await GroupMoveModel.MoveGroupAsync(a, [5, 4], [], new HexCoord(4, 0));
        await GroupMoveModel.MoveGroupAsync(b, [5, 4], [], new HexCoord(4, 0));
        Assert.Equal(a.Sent, b.Sent);
    }

    [Fact]
    public async Task RefusedStepIsRepathedOnceThenBlocked()
    {
        var line = Start((1, 0));
        line.Refuse.Add(new HexCoord(2, 0));
        var outcome = await GroupMoveModel.MoveOneAsync(line, 1, new HexCoord(9, 0));
        Assert.Equal(GroupMoveStatus.Blocked, outcome.Status);
        Assert.Equal("blocked", outcome.Reason);
        Assert.Equal(2, line.Sent.Count(it => it.To == new HexCoord(2, 0)));
    }

    [Fact]
    public async Task TargetHeldByAMateFallsBackToATileAroundIt()
    {
        var line = Start((1, 0), (2, 1));
        var target = new HexCoord(2, 0);
        line.Occupied.Add(target);
        var outcome = await GroupMoveModel.MoveOneAsync(line, 1, target);
        Assert.Equal(GroupMoveStatus.Moved, outcome.Status);
        Assert.Equal(1, HexMath.GetDistance(line.Units[1], target));
        Assert.NotEqual(target, line.Units[1]);
    }

    [Fact]
    public async Task UnitNextToAHeldTargetHasArrivedAndGoneOrExhaustedUnitsAreReported()
    {
        var line = Start((1, 1), (2, 5), (3, 8));
        var target = new HexCoord(2, 0);
        line.Occupied.Add(target);
        Assert.Equal(GroupMoveStatus.Arrived, (await GroupMoveModel.MoveOneAsync(line, 1, target)).Status);
        line.Moves[2] = 0f;
        Assert.Equal("No movement left", (await GroupMoveModel.MoveOneAsync(line, 2, target)).Reason);
        Assert.Equal(GroupMoveStatus.Arrived, (await GroupMoveModel.MoveOneAsync(line, 3, new HexCoord(8, 0))).Status);
        Assert.Equal("The unit is gone", (await GroupMoveModel.MoveOneAsync(line, 99, target)).Reason);
    }

    [Fact]
    public async Task UnreachableTargetIsBlockedWhenFarAway()
    {
        var line = Start((1, 6));
        var target = new HexCoord(0, 0);
        line.Occupied.Add(target);
        foreach (var ring in HexMath.GetHexCoordsAtDistance(target, 1, 1, false)) line.Occupied.Add(ring);
        var outcome = await GroupMoveModel.MoveOneAsync(line, 1, target);
        Assert.Equal(GroupMoveStatus.Blocked, outcome.Status);
        Assert.Empty(line.Sent);
    }

    [Fact]
    public async Task EscortMovesBothUnitsToTheSameTileAsTwoCommands()
    {
        var line = Start((1, 0), (2, 0));
        line.Civilians.Add(2); // a civilian may share a tile with a military unit
        var pair = new EscortPair(CivilianId: 2, MilitaryId: 1);
        var report = await GroupMoveModel.MoveGroupAsync(line, [1, 2], [pair], new HexCoord(8, 0));
        Assert.Equal("", Assert.Single(report.Outcomes).Reason);
        Assert.Equal([(1, new HexCoord(2, 0)), (2, new HexCoord(2, 0))], line.Sent);
    }

    [Fact]
    public async Task EscortStepCases()
    {
        var line = Start((1, 0), (2, 0));
        var pair = new EscortPair(2, 1);
        Assert.Equal(GroupMoveStatus.Arrived, (await GroupMoveModel.EscortStepAsync(line, pair, new HexCoord(0, 0))).Status);

        line.Moves[2] = 0f;
        Assert.Equal("No movement left", (await GroupMoveModel.EscortStepAsync(line, pair, new HexCoord(5, 0))).Reason);
        line.Moves[2] = 2f;

        line.Refuse.Add(new HexCoord(2, 0));
        var refused = await GroupMoveModel.EscortStepAsync(line, pair, new HexCoord(5, 0));
        Assert.Equal(GroupMoveStatus.Blocked, refused.Status);
        Assert.Equal("blocked", refused.Reason);
        Assert.Equal(new HexCoord(0, 0), line.Units[2]);

        Assert.Equal("The pair is broken", (await GroupMoveModel.EscortStepAsync(line, new EscortPair(7, 1), new HexCoord(5, 0))).Reason);
    }

    [Fact]
    public async Task EscortHoldsStillWhenNoStepIsSharedThisTurn()
    {
        var line = Start((1, 0), (2, 9));
        var outcome = await GroupMoveModel.EscortStepAsync(line, new EscortPair(2, 1), new HexCoord(12, 0));
        Assert.Equal(GroupMoveStatus.Blocked, outcome.Status);
        Assert.Equal("The pair cannot keep together this turn", outcome.Reason);
        Assert.Empty(line.Sent);
    }

    [Fact]
    public async Task EscortWithAnUnreachableTargetIsBlocked()
    {
        var line = Start((1, 0), (2, 0));
        var target = new HexCoord(5, 0);
        line.Occupied.Add(target);
        Assert.Equal("Cannot reach that tile", (await GroupMoveModel.EscortStepAsync(line, new EscortPair(2, 1), target)).Reason);
    }

    private static UnitDto Dto(int id, string name, int x, int y) =>
        new() { Id = id, Name = name, Owner = "A", X = x, Y = y, HealthBucket = 4, IsEmbarked = false, IsFortified = false };

    [Fact]
    public void OfferNeedsOneCivilianAndOneMilitaryUnitNextToEachOther()
    {
        BaseUnit? Lookup(string name) => name switch
        {
            "Warrior" => new BaseUnit { Name = "Warrior", Strength = 6 },
            "Worker" => new BaseUnit { Name = "Worker" },
            _ => null,
        };
        var offer = GroupMoveModel.Offer([Dto(1, "Worker", 0, 0), Dto(2, "Warrior", 1, 0)], Lookup);
        Assert.Equal(new EscortPair(1, 2), offer);
        Assert.Equal(new EscortPair(1, 2), GroupMoveModel.Offer([Dto(2, "Warrior", 1, 0), Dto(1, "Worker", 0, 0)], Lookup));
        Assert.Null(GroupMoveModel.Offer([Dto(1, "Worker", 0, 0), Dto(2, "Warrior", 3, 0)], Lookup));
        Assert.Null(GroupMoveModel.Offer([Dto(1, "Warrior", 0, 0), Dto(2, "Warrior", 1, 0)], Lookup));
        Assert.Null(GroupMoveModel.Offer([Dto(1, "Worker", 0, 0), Dto(2, "Ghost", 1, 0)], Lookup));
        Assert.Null(GroupMoveModel.Offer([Dto(1, "Worker", 0, 0)], Lookup));
    }

    [Fact]
    public void EntryHintAndSummaryWords()
    {
        Assert.Equal(GroupMoveModel.EscortLabel, GroupMoveModel.Entry(false).Label);
        Assert.Equal(GroupMoveModel.StopEscortLabel, GroupMoveModel.Entry(true).Label);
        Assert.Equal(MenuEntryKind.Group, GroupMoveModel.Entry(false).Kind);
        Assert.Equal("", GroupMoveModel.Hint(1, false));
        Assert.Contains("Group of 3", GroupMoveModel.Hint(3, false), StringComparison.Ordinal);
        Assert.Contains("Escort pair", GroupMoveModel.Hint(2, true), StringComparison.Ordinal);
        var report = new GroupMoveReport([new(1, GroupMoveStatus.Moved), new(2, GroupMoveStatus.Arrived), new(3, GroupMoveStatus.Blocked, "No movement left")]);
        Assert.Equal("Group move: 1 moved, 1 already there; blocked: U3 (No movement left)", GroupMoveModel.Summary(report, id => "U" + id));
        Assert.Equal("Group move: 0 moved", GroupMoveModel.Summary(new GroupMoveReport([]), _ => ""));
    }
}
