using Unciv.AI.Data;
using Unciv.AI.Homeland;
using Unciv.Utils;

namespace Unciv.Core.Tests.AI.Homeland;

public class HomelandAITests
{
    private static readonly GlobalAIDefines Defines = GlobalAIDefines.LoadEmbedded();
    private const string Prefix = "AI_HOMELAND_MOVE_PRIORITY_";

    private static HomelandMoveQueue NewQueue(AIEdition e = AIEdition.BNW) => new(Defines, e);

    [Fact]
    public void EveryPriorityDefineMapsToAnEnumMemberAndBack()
    {
        var defineNames = Defines.Names.Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n != Prefix + "RANDOMNESS").ToList();
        var memberNames = Enum.GetValues<HomelandMovePriority>().Select(m => m.ToDefineName()).ToList();
        Assert.Equal(defineNames, memberNames);
    }

    [Fact]
    public void QueueWithoutRandomnessFollowsBasePriority()
    {
        var queue = NewQueue().Build();
        Assert.Equal(HomelandMovePriority.AddSpaceshipPart, queue[0].Move);
        Assert.Equal(200, queue[0].Priority);
        Assert.Equal(HomelandMovePriority.Patrol, queue[^1].Move);
        Assert.Equal(0, queue[^1].Priority);
        Assert.Equal(queue.OrderByDescending(e => e.Priority).Select(e => e.Move), queue.Select(e => e.Move));
        // ties keep data-file order: Archaeologist, TradeUnit, Treasure are all 100
        var moves = queue.Select(e => e.Move).ToList();
        Assert.True(moves.IndexOf(HomelandMovePriority.Archaeologist) < moves.IndexOf(HomelandMovePriority.TradeUnit));
        Assert.True(moves.IndexOf(HomelandMovePriority.TradeUnit) < moves.IndexOf(HomelandMovePriority.Treasure));
        Assert.True(moves.IndexOf(HomelandMovePriority.Worker) < moves.IndexOf(HomelandMovePriority.Garrison));
    }

    [Fact]
    public void QueueFiltersByEdition()
    {
        Assert.DoesNotContain(NewQueue(AIEdition.Base).Build(), e => e.Move == HomelandMovePriority.TradeUnit);
        Assert.Contains(NewQueue(AIEdition.BNW).Build(), e => e.Move == HomelandMovePriority.TradeUnit);
        Assert.DoesNotContain(NewQueue(AIEdition.BNW).Build(), e => e.Move == HomelandMovePriority.ArtistGoldenAge);
        Assert.Contains(NewQueue(AIEdition.Base).Build(), e => e.Move == HomelandMovePriority.ArtistGoldenAge);
    }

    [Fact]
    public void RandomJitterIsSeededAndBounded()
    {
        var a = NewQueue().Build(new GameRandom(42));
        var b = NewQueue().Build(new GameRandom(42));
        Assert.Equal(a, b);
        var plain = NewQueue().Build().ToDictionary(e => e.Move, e => e.Priority);
        foreach (var e in a)
            Assert.InRange(e.Priority - plain[e.Move], 0, 9);
        Assert.NotEqual(NewQueue().Build(new GameRandom(1)), NewQueue().Build(new GameRandom(2)));
    }

    [Fact]
    public void RunVisitsMovesInQueueOrder()
    {
        var queue = NewQueue().Build();
        var seen = new List<HomelandMovePriority>();
        HomelandMoveQueue.Run(queue, seen.Add);
        Assert.Equal(queue.Select(e => e.Move), seen);
        Assert.Throws<ArgumentNullException>(() => HomelandMoveQueue.Run(queue, null!));
        Assert.Throws<ArgumentNullException>(() => HomelandMoveQueue.Run(null!, _ => { }));
    }

    [Fact]
    public void TunablesComeFromDefines()
    {
        var q = NewQueue();
        Assert.Equal(4, q.MaxDefensiveMoveTurns);
        Assert.Equal(5, q.MaxUpgradeMoveTurns);
        Assert.Equal(10, q.GreatPersonTurnsToWait);
        Assert.Equal(8, q.EstimateTurnsDistance);
        Assert.Throws<ArgumentNullException>(() => new HomelandMoveQueue(null!, AIEdition.BNW));
    }
}
