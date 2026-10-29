using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Assets;

public class TurnBarModelTests
{
    private static readonly TurnRosterEntry[] Roster = [new("Rome", "Alice", true), new("Greece", "Bob", true)];

    private static TurnStatusDto Status(string phase = "Playing", long? deadline = null, params string[] ended) =>
        new() { Ended = [.. ended], Phase = phase, Deadline = deadline };

    [Fact]
    public void NoRosterMeansNoBar() => Assert.Null(TurnBarModel.Of(Status(), [], "Rome", 0));

    [Fact]
    public void RowsFollowTheEndedList()
    {
        var bar = TurnBarModel.Of(Status(ended: "Greece"), Roster, "Rome", 0)!;
        Assert.Equal([("Rome", false, true), ("Greece", true, false)], bar.Rows.Select(it => (it.CivId, it.Ended, it.IsYou)));
        Assert.Equal("Waiting for Alice", bar.Headline);
        Assert.False(bar.Resolving);
    }

    [Fact]
    public void HeadlineNamesEveryoneWaitedForAndMarksOfflineOnes()
    {
        var roster = new TurnRosterEntry[] { new("Rome", "Alice", true), new("Greece", "Bob", false) };
        Assert.Equal("Waiting for Alice, Bob (offline)", TurnBarModel.Of(Status(), roster, "Rome", 0)!.Headline);
        Assert.False(TurnBarModel.Of(Status(), roster, "Rome", 0)!.Rows[1].Connected);
    }

    [Fact]
    public void EveryoneEndedAndResolvingHeadlines()
    {
        Assert.Equal(TurnBarModel.EveryoneEnded, TurnBarModel.Of(Status(ended: ["Rome", "Greece"]), Roster, "Rome", 0)!.Headline);
        var resolving = TurnBarModel.Of(Status("Resolving", null, "Rome", "Greece"), Roster, "Rome", 0)!;
        Assert.Equal((TurnBarModel.ResolvingHeadline, true), (resolving.Headline, resolving.Resolving));
    }

    [Theory]
    [InlineData(65_000L, 0L, "1:05")]
    [InlineData(65_001L, 0L, "1:06")]
    [InlineData(1_000L, 5_000L, "0:00")]
    [InlineData(600_000L, 0L, "10:00")]
    public void CountdownRoundsUpAndNeverGoesNegative(long deadline, long now, string expected) => Assert.Equal(expected, TurnBarModel.Countdown(deadline, now));

    [Fact]
    public void NoDeadlineMeansNoCountdown() => Assert.Null(TurnBarModel.Of(Status(), Roster, "Rome", 0)!.Countdown);

    [Fact]
    public void ToastsDescribeOtherHumansOnly()
    {
        string? Toast(GameEvent e) => TurnBarModel.Toast(e, Roster, "Rome");
        Assert.Equal("Bob ended their turn", Toast(new PlayerEndedTurn("Greece", 3)));
        Assert.Equal("Bob is playing again", Toast(new PlayerUndidEndTurn("Greece", 3)));
        Assert.Equal("Bob moved a Warrior", Toast(new UnitMoved("Greece", 4, "Warrior", [])));
        Assert.Equal("Bob founded Athens", Toast(new CityFounded("Greece", "c1", "Athens", new HexCoord(1, 1))));
        Assert.Equal("Bob declared war on Egypt", Toast(new WarDeclared("Greece", "Egypt")));
        Assert.Equal("Bob made peace with Egypt", Toast(new PeaceMade("Greece", "Egypt")));
        Assert.Null(Toast(new PlayerEndedTurn("Rome", 3))); // the viewer's own action
        Assert.Null(Toast(new PlayerEndedTurn("Egypt", 3))); // not a human of the roster
        Assert.Null(Toast(new TurnResolving(3)));
    }
}
