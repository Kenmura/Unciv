using System.Numerics;
using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

public class PinchTrackerTests
{
    [Fact]
    public void OneFingerIsNeverAPinch()
    {
        var t = new PinchTracker();
        Assert.False(t.Down(0, new Vector2(10, 10)));
        Assert.Null(t.Move(0, new Vector2(50, 50)));
        Assert.False(t.Active);
    }

    [Fact]
    public void SecondFingerStartsAPinchAndSpreadingZoomsIn()
    {
        var t = new PinchTracker();
        t.Down(0, new Vector2(100, 100));
        Assert.True(t.Down(1, new Vector2(200, 100)));
        var move = t.Move(1, new Vector2(300, 100));
        Assert.NotNull(move);
        Assert.Equal(2f, move!.Value.ZoomFactor, 3);
        Assert.Equal(new Vector2(50, 0), move.Value.MidpointDelta);
        Assert.Equal(new Vector2(200, 100), move.Value.Midpoint);
    }

    [Fact]
    public void MovingBothFingersTogetherPansWithoutZoom()
    {
        var t = new PinchTracker();
        t.Down(0, new Vector2(100, 100));
        t.Down(1, new Vector2(200, 100));
        var first = t.Move(0, new Vector2(100, 150));
        var move = t.Move(1, new Vector2(200, 150));
        Assert.Equal(1f, first!.Value.ZoomFactor * move!.Value.ZoomFactor, 3); // the factors of the two half steps cancel
        Assert.Equal(new Vector2(0, 50), first.Value.MidpointDelta + move.Value.MidpointDelta);
    }

    [Fact]
    public void LiftingAFingerEndsThePinchAndANewLandingRestartsIt()
    {
        var t = new PinchTracker();
        t.Down(0, new Vector2(0, 0));
        t.Down(1, new Vector2(100, 0));
        t.Up(1);
        Assert.False(t.Active);
        Assert.Null(t.Move(0, new Vector2(30, 30)));
        Assert.True(t.Down(2, new Vector2(130, 30)));
        Assert.Equal(1f, t.Move(2, new Vector2(130, 30))!.Value.ZoomFactor, 3);
    }

    [Fact]
    public void ThreeFingersAndUnknownFingersYieldNothing()
    {
        var t = new PinchTracker();
        t.Down(0, new Vector2(0, 0));
        t.Down(1, new Vector2(10, 0));
        Assert.False(t.Down(2, new Vector2(20, 0)));
        Assert.Null(t.Move(1, new Vector2(15, 0)));
        Assert.Null(t.Move(9, new Vector2(15, 0)));
    }

    [Fact]
    public void FingersOnTheSamePointDoNotDivideByZero()
    {
        var t = new PinchTracker();
        t.Down(0, new Vector2(5, 5));
        t.Down(1, new Vector2(5, 5));
        Assert.Equal(1f, t.Move(1, new Vector2(50, 5))!.Value.ZoomFactor);
    }
}
