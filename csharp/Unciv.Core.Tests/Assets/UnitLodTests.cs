using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

/// <summary>C085: when unit figures draw fully, reduced or not at all.</summary>
public sealed class UnitLodTests
{
    [Theory]
    [InlineData(4f, false)]
    [InlineData(14f, false)] // the default view keeps every figure
    [InlineData(21.99f, false)]
    [InlineData(UnitLod.FarZoom, true)]
    [InlineData(40f, true)]
    [InlineData(float.NaN, false)]
    public void FarFromTheThreshold(float zoom, bool far) => Assert.Equal(far, UnitLod.IsFar(zoom));

    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(4, false, 4)]
    [InlineData(4, true, 1)]
    [InlineData(1, true, 1)]
    [InlineData(0, true, 0)]
    [InlineData(-2, false, 0)]
    public void FarFormationsDrawTheLeadOnly(int count, bool far, int drawn) => Assert.Equal(drawn, UnitLod.FiguresDrawn(count, far));

    [Fact]
    public void UnitsOutsideTheFootprintAreNotDrawnButTheEdgeIs()
    {
        var view = new GroundRect(0f, 0f, 10f, 10f);
        Assert.True(UnitLod.IsDrawn(5f, 5f, view));
        Assert.True(UnitLod.IsDrawn(-UnitLod.ViewMargin, 10f + UnitLod.ViewMargin, view)); // the margin is inclusive
        Assert.False(UnitLod.IsDrawn(-UnitLod.ViewMargin - 0.01f, 5f, view));
        Assert.False(UnitLod.IsDrawn(5f, 10f + UnitLod.ViewMargin + 0.01f, view));
        Assert.True(UnitLod.IsDrawn(1000f, -1000f, null)); // no footprint: draw everything
    }
}
