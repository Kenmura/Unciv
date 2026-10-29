namespace Unciv.Harness;

/// <summary>Tolerances for comparing a captured frame to its golden image.</summary>
/// <param name="MaxHashDistance">Largest allowed Hamming distance between the 64-bit perceptual hashes (layout change detector).</param>
/// <param name="MaxMeanAbsoluteDifference">Largest allowed mean per-channel difference, 0..1 (overall shading/colour drift).</param>
/// <param name="PixelTolerance">A pixel counts as different when any channel differs by more than this (0..255).</param>
/// <param name="MaxDifferingPixelFraction">Largest allowed fraction of different pixels, 0..1.</param>
public sealed record GoldenTolerance(int MaxHashDistance = 6, double MaxMeanAbsoluteDifference = 0.02, int PixelTolerance = 16, double MaxDifferingPixelFraction = 0.05)
{
    /// <summary>
    /// Cross-renderer profile (T246): the goldens were recorded with Fedora's Mesa llvmpipe, the ubuntu-24.04 runner's differs slightly in
    /// blending/dithering. Measured over all 221 captures of the first green CI run against the Fedora goldens: perceptual-hash distance at most 9,
    /// mean difference at most 0.0191, at most 8.3% of the pixels off by more than 16. The limits sit between that drift and what a wrong screen
    /// scores (the CI self-test <c>run-scenario.sh --expect-golden-failure</c> compares every capture with an unrelated image: hash 19-30, mean
    /// 0.045-0.121): hash 12, mean 0.04, per-pixel 32, fraction 0.15. Selected with <c>HARNESS_GOLDEN_TOLERANCE=renderer</c>; the default stays strict
    /// so the developer machine keeps its precise check.
    /// </summary>
    public static GoldenTolerance Renderer { get; } = new(MaxHashDistance: 12, MaxMeanAbsoluteDifference: 0.04, PixelTolerance: 32, MaxDifferingPixelFraction: 0.15);

    /// <summary>The profile named by <paramref name="name"/> (<c>strict</c>, the default, or <c>renderer</c>); null for an unknown name.</summary>
    public static GoldenTolerance? FromName(string? name) => string.IsNullOrEmpty(name) || name == "strict" ? new GoldenTolerance() : name == "renderer" ? Renderer : null;
}

public sealed record GoldenResult(bool Passed, int HashDistance, double MeanAbsoluteDifference, double DifferingPixelFraction, string Message);

public static class GoldenComparer
{
    public static GoldenResult Compare(RgbaImage actual, RgbaImage golden, GoldenTolerance? tolerance = null)
    {
        tolerance ??= new GoldenTolerance();
        if (actual.Width != golden.Width || actual.Height != golden.Height)
            return new GoldenResult(false, 64, 1, 1, $"Size mismatch: actual {actual.Width}x{actual.Height}, golden {golden.Width}x{golden.Height}");

        int hashDistance = PerceptualHash.HammingDistance(PerceptualHash.Compute(actual), PerceptualHash.Compute(golden));
        long channelDifference = 0;
        long differingPixels = 0;
        for (int i = 0; i < actual.Pixels.Length; i += 4)
        {
            int worst = 0;
            for (int c = 0; c < 4; c++)
            {
                int d = Math.Abs(actual.Pixels[i + c] - golden.Pixels[i + c]);
                channelDifference += d;
                worst = Math.Max(worst, d);
            }
            if (worst > tolerance.PixelTolerance) differingPixels++;
        }
        double meanDifference = channelDifference / (255.0 * actual.Pixels.Length);
        double differingFraction = (double)differingPixels / ((long)actual.Width * actual.Height);

        var failures = new List<string>();
        if (hashDistance > tolerance.MaxHashDistance) failures.Add($"hash distance {hashDistance} > {tolerance.MaxHashDistance}");
        if (meanDifference > tolerance.MaxMeanAbsoluteDifference) failures.Add($"mean difference {meanDifference:F4} > {tolerance.MaxMeanAbsoluteDifference:F4}");
        if (differingFraction > tolerance.MaxDifferingPixelFraction) failures.Add($"differing pixels {differingFraction:P2} > {tolerance.MaxDifferingPixelFraction:P2}");

        string message = failures.Count == 0 ? "match" : "differs from golden: " + string.Join("; ", failures);
        return new GoldenResult(failures.Count == 0, hashDistance, meanDifference, differingFraction, message);
    }
}
