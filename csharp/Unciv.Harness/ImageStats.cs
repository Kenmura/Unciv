namespace Unciv.Harness;

/// <summary>Cheap "did anything render" checks for a captured frame.</summary>
public sealed record ImageStats(int Width, int Height, double NonBackgroundFraction, int DistinctColors)
{
    /// <summary>Background is taken from the top-left pixel; a pixel counts as foreground when any channel differs by more than <paramref name="tolerance"/>.</summary>
    public static ImageStats Analyze(RgbaImage image, int tolerance = 2)
    {
        var (br, bg, bb, ba) = image.GetPixel(0, 0);
        long foreground = 0;
        var colors = new HashSet<int>();   // membership only, never iterated
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                var (r, g, b, a) = image.GetPixel(x, y);
                if (Math.Abs(r - br) > tolerance || Math.Abs(g - bg) > tolerance || Math.Abs(b - bb) > tolerance || Math.Abs(a - ba) > tolerance)
                    foreground++;
                colors.Add((r >> 3 << 10) | (g >> 3 << 5) | (b >> 3));   // 5 bits per channel
            }
        return new ImageStats(image.Width, image.Height, (double)foreground / ((long)image.Width * image.Height), colors.Count);
    }

    public bool IsBlank => NonBackgroundFraction == 0 || DistinctColors <= 1;
}
