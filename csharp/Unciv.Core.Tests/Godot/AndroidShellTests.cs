using Unciv.GodotClient.Settings;

namespace Unciv.Tests.Godot;

// C099: the Godot-free Android shell decisions.
public sealed class AndroidShellTests
{
    [Fact]
    public void BackIsHandledWhileAScreenOrPopupIsOpenAndQuitsOtherwise()
    {
        Assert.Equal(BackOutcome.Handled, AndroidShell.BackOutcome(true));
        Assert.Equal(BackOutcome.Quit, AndroidShell.BackOutcome(false));
    }

    [Fact]
    public void CutoutAndGestureBarBecomeMargins()
    {
        var insets = AndroidShell.ComputeInsets(2400, 1080, 100, 0, 2200, 1040, 0);
        Assert.Equal(new Insets(100, 0, 100, 40), insets);
        Assert.False(insets.IsZero);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 2400, 1080)]
    public void AnUnreportedOrFullSafeAreaGivesNoInset(int x, int y, int w, int h) => Assert.True(AndroidShell.ComputeInsets(2400, 1080, x, y, w, h, 0).IsZero);

    [Fact]
    public void KeyboardRaisesTheBottomOnlyWhenHigherThanTheGestureBar()
    {
        Assert.Equal(500, AndroidShell.ComputeInsets(2400, 1080, 0, 0, 2400, 1040, 500).Bottom);
        Assert.Equal(40, AndroidShell.ComputeInsets(2400, 1080, 0, 0, 2400, 1040, 10).Bottom);
        Assert.Equal(300, AndroidShell.ComputeInsets(2400, 1080, 0, 0, 0, 0, 300).Bottom);
        Assert.Equal(1080, AndroidShell.ComputeInsets(2400, 1080, 0, 0, 2400, 1080, 9999).Bottom);
        Assert.Equal(0, AndroidShell.ComputeInsets(2400, 1080, 0, 0, 2400, 1080, -5).Bottom);
    }

    [Fact]
    public void SafeAreaOutsideTheWindowIsClamped()
    {
        var insets = AndroidShell.ComputeInsets(1000, 500, -50, -20, 2000, 900, 0);
        Assert.Equal(new Insets(0, 0, 0, 0), insets);
        Assert.Equal(new Insets(1000, 500, 0, 0), AndroidShell.ComputeInsets(1000, 500, 5000, 5000, 100, 100, 0));
        Assert.True(AndroidShell.ComputeInsets(0, 0, 0, 0, 10, 10, 0).IsZero);
    }

    [Fact]
    public void HostAndWindowModeAreHiddenOnTouchBuilds()
    {
        Assert.False(AndroidShell.ShowsHostGame(true));
        Assert.True(AndroidShell.ShowsHostGame(false));
        Assert.False(AndroidShell.ShowsWindowMode(true));
        Assert.True(AndroidShell.ShowsWindowMode(false));
    }
}
