using System.Numerics;

namespace Unciv.Harness;

/// <summary>
/// 64-bit difference hash: the image is box-averaged down to 9x8 luminance cells and each bit says whether a cell is
/// brighter than its right neighbour. Robust to the small shading differences between software rasterizers, sensitive to
/// layout/geometry changes. Integer arithmetic only, so the same pixels always give the same hash.
/// </summary>
public static class PerceptualHash
{
    private const int HashColumns = 9;
    private const int HashRows = 8;

    public static ulong Compute(RgbaImage image)
    {
        var cells = new int[HashRows, HashColumns];
        for (int row = 0; row < HashRows; row++)
        {
            int y0 = row * image.Height / HashRows;
            int y1 = Math.Max(y0 + 1, (row + 1) * image.Height / HashRows);
            for (int column = 0; column < HashColumns; column++)
            {
                int x0 = column * image.Width / HashColumns;
                int x1 = Math.Max(x0 + 1, (column + 1) * image.Width / HashColumns);
                long sum = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                        sum += Luminance(image, x, y);
                cells[row, column] = (int)(sum / ((long)(y1 - y0) * (x1 - x0)));
            }
        }

        ulong hash = 0;
        for (int row = 0; row < HashRows; row++)
            for (int column = 0; column < HashColumns - 1; column++)
            {
                hash <<= 1;
                if (cells[row, column] > cells[row, column + 1]) hash |= 1;
            }
        return hash;
    }

    public static int HammingDistance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    private static int Luminance(RgbaImage image, int x, int y)
    {
        var (r, g, b, _) = image.GetPixel(x, y);
        return (299 * r + 587 * g + 114 * b) / 1000;
    }
}
