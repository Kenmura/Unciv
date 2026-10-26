using Unciv.Assets;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Assets;

public sealed class NextTurnProgressModelTests
{
    [Fact]
    public void Begin_ShowsTheStartingHeadingAndAnEmptyBar()
    {
        var view = NextTurnProgressModel.Begin();
        Assert.Equal(NextTurnProgressModel.Starting, view.Heading);
        Assert.Equal("", view.Detail);
        Assert.Equal(0.0, view.Fraction);
    }

    [Fact]
    public void Of_NamesTheCivAndItsPositionOneBased()
    {
        var view = NextTurnProgressModel.Of(new TurnProgress(2, 8, "Rome"), id => id.ToUpperInvariant());
        Assert.Equal(NextTurnProgressModel.Heading, view.Heading);
        Assert.Equal("ROME - 3 of 8", view.Detail);
        Assert.Equal(0.25, view.Fraction);
    }

    [Theory]
    [InlineData(0, 0, "X - 1 of 1", 0.0)]
    [InlineData(9, 3, "X - 3 of 3", 1.0)]
    [InlineData(-4, 3, "X - 1 of 3", 0.0)]
    public void Of_ClampsOddCounts(int played, int total, string detail, double fraction)
    {
        var view = NextTurnProgressModel.Of(new TurnProgress(played, total, "X"), id => id);
        Assert.Equal(detail, view.Detail);
        Assert.Equal(fraction, view.Fraction);
    }
}
