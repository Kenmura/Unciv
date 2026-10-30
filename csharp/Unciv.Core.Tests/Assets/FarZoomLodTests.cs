using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

/// <summary>T287: where the far-zoom draw-call savings switch on.</summary>
public sealed class FarZoomLodTests
{
    [Theory]
    [InlineData(4f, false)]
    [InlineData(14f, false)] // the default view keeps its yield icons
    [InlineData(23.99f, false)]
    [InlineData(FarZoomLod.HideYieldsZoom, true)]
    [InlineData(40f, true)]
    [InlineData(float.NaN, false)]
    public void YieldsHideFromTheThreshold(float zoom, bool hidden) => Assert.Equal(hidden, FarZoomLod.HideYields(zoom));

    [Theory]
    [InlineData(4f, false)]
    [InlineData(14f, false)] // the default view and the goldens draw the prop nodes
    [InlineData(21.99f, false)]
    [InlineData(FarZoomLod.MergePropsZoom, true)]
    [InlineData(40f, true)]
    [InlineData(float.NaN, false)]
    public void PropsMergeFromTheThreshold(float zoom, bool merged) => Assert.Equal(merged, FarZoomLod.MergeProps(zoom));

    [Fact]
    public void TheDefaultAndNearestViewsAreNeverFar()
    {
        Assert.False(FarZoomLod.HideYields(14f));
        Assert.False(FarZoomLod.MergeProps(14f));
        Assert.True(FarZoomLod.MergeProps(FarZoomLod.HideYieldsZoom), "the yields go no later than the props merge");
    }
}
