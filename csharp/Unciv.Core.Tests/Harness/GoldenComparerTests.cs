using Unciv.Harness;

namespace Unciv.Core.Tests.Harness;

public class GoldenComparerTests
{
    // Dark background with a bright rectangle, like a tile on a sea-coloured clear colour.
    private static RgbaImage Scene(int width = 64, int height = 48, int left = 16, int top = 12, int size = 24, byte shade = 200)
    {
        var image = RgbaImage.Solid(width, height, 20, 30, 60);
        for (int y = top; y < top + size; y++)
            for (int x = left; x < left + size; x++)
                image.SetPixel(x, y, shade, (byte)(shade - 40), 40);
        return image;
    }

    [Fact]
    public void IdenticalImagesMatch()
    {
        var result = GoldenComparer.Compare(Scene(), Scene());
        Assert.True(result.Passed, result.Message);
        Assert.Equal(0, result.HashDistance);
        Assert.Equal(0, result.MeanAbsoluteDifference);
        Assert.Equal(0, result.DifferingPixelFraction);
    }

    [Fact]
    public void SmallShadingDriftIsTolerated()
    {
        var result = GoldenComparer.Compare(Scene(shade: 205), Scene(shade: 200));
        Assert.True(result.Passed, result.Message);
        Assert.Equal(0, result.DifferingPixelFraction);
    }

    [Fact]
    public void MovedObjectIsDetected()
    {
        var result = GoldenComparer.Compare(Scene(left: 36), Scene(left: 4));
        Assert.False(result.Passed);
        Assert.True(result.DifferingPixelFraction > 0.05);
        Assert.StartsWith("differs from golden", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingObjectIsDetected()
    {
        var result = GoldenComparer.Compare(RgbaImage.Solid(64, 48, 20, 30, 60), Scene());
        Assert.False(result.Passed);
    }

    [Fact]
    public void SizeMismatchFails()
    {
        var result = GoldenComparer.Compare(Scene(32, 24, 8, 6, 12), Scene());
        Assert.False(result.Passed);
        Assert.Contains("Size mismatch", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TolerancesAreConfigurable()
    {
        var strict = new GoldenTolerance(MaxHashDistance: 0, MaxMeanAbsoluteDifference: 0, PixelTolerance: 0, MaxDifferingPixelFraction: 0);
        Assert.False(GoldenComparer.Compare(Scene(shade: 205), Scene(shade: 200), strict).Passed);
        var lax = new GoldenTolerance(MaxHashDistance: 64, MaxMeanAbsoluteDifference: 1, PixelTolerance: 255, MaxDifferingPixelFraction: 1);
        Assert.True(GoldenComparer.Compare(Scene(left: 36), Scene(left: 4), lax).Passed);
    }

    [Fact]
    public void RendererProfileToleratesBlendingDriftButStillRejectsWrongScreens()
    {
        var drifted = Scene(shade: 240);
        Assert.False(GoldenComparer.Compare(drifted, Scene(shade: 200)).Passed);
        Assert.True(GoldenComparer.Compare(drifted, Scene(shade: 200), GoldenTolerance.Renderer).Passed);
        Assert.False(GoldenComparer.Compare(RgbaImage.Solid(64, 48, 200, 160, 40), Scene(), GoldenTolerance.Renderer).Passed);
        Assert.False(GoldenComparer.Compare(Scene(left: 36), Scene(left: 4), GoldenTolerance.Renderer).Passed);
    }

    [Fact]
    public void ProfilesAreSelectedByName()
    {
        Assert.Equal(new GoldenTolerance(), GoldenTolerance.FromName(null));
        Assert.Equal(new GoldenTolerance(), GoldenTolerance.FromName("strict"));
        Assert.Same(GoldenTolerance.Renderer, GoldenTolerance.FromName("renderer"));
        Assert.Null(GoldenTolerance.FromName("loose"));
    }

    [Fact]
    public void PerceptualHashIsStableAndSensitiveToLayout()
    {
        ulong hash = PerceptualHash.Compute(Scene());
        Assert.Equal(hash, PerceptualHash.Compute(Scene()));
        Assert.NotEqual(hash, PerceptualHash.Compute(Scene(left: 36)));
        Assert.Equal(0, PerceptualHash.HammingDistance(hash, hash));
        Assert.Equal(64, PerceptualHash.HammingDistance(0UL, ulong.MaxValue));
        Assert.Equal(0UL, PerceptualHash.Compute(RgbaImage.Solid(10, 10, 5, 5, 5)));
    }

    [Fact]
    public void ImageStatsSeparateBlankFromRendered()
    {
        var blank = ImageStats.Analyze(RgbaImage.Solid(32, 32, 10, 20, 30));
        Assert.True(blank.IsBlank);
        Assert.Equal(1, blank.DistinctColors);

        var rendered = ImageStats.Analyze(Scene());
        Assert.False(rendered.IsBlank);
        Assert.Equal(24 * 24 / (64.0 * 48), rendered.NonBackgroundFraction, 6);
        Assert.Equal(2, rendered.DistinctColors);
    }

    [Fact]
    public void RgbaImageValidatesItsBuffer()
    {
        Assert.Throws<ArgumentException>(() => new RgbaImage(2, 2, new byte[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RgbaImage(0, 2, []));
        var image = RgbaImage.Solid(2, 1, 1, 2, 3, 4);
        Assert.Equal(((byte)1, (byte)2, (byte)3, (byte)4), image.GetPixel(1, 0));
    }
}
