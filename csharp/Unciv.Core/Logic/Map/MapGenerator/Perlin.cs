using System;

namespace Unciv.Logic.Map.MapGenerator;

// version 1.1.3
// From https://rosettacode.org/wiki/Perlin_noise#Kotlin
public static class Perlin
{
    private static readonly int[] Permutation =
    [
        151, 160, 137,  91,  90,  15, 131,  13, 201,  95,  96,  53, 194, 233,   7, 225,
        140,  36, 103,  30,  69, 142,   8,  99,  37, 240,  21,  10,  23, 190,   6, 148,
        247, 120, 234,  75,   0,  26, 197,  62,  94, 252, 219, 203, 117,  35,  11,  32,
        57 , 177,  33,  88, 237, 149,  56,  87, 174,  20, 125, 136, 171, 168,  68, 175,
        74 , 165,  71, 134, 139,  48,  27, 166,  77, 146, 158, 231,  83, 111, 229, 122,
        60 , 211, 133, 230, 220, 105,  92,  41,  55,  46, 245,  40, 244, 102, 143,  54,
        65 ,  25,  63, 161,   1, 216,  80,  73, 209,  76, 132, 187, 208,  89,  18, 169,
        200, 196, 135, 130, 116, 188, 159,  86, 164, 100, 109, 198, 173, 186,   3,  64,
        52 , 217, 226, 250, 124, 123,   5, 202,  38, 147, 118, 126, 255,  82,  85, 212,
        207, 206,  59, 227,  47,  16,  58,  17, 182, 189,  28,  42, 223, 183, 170, 213,
        119, 248, 152,   2,  44, 154, 163,  70, 221, 153, 101, 155, 167,  43, 172,   9,
        129,  22,  39, 253,  19,  98, 108, 110,  79, 113, 224, 232, 178, 185, 112, 104,
        218, 246,  97, 228, 251,  34, 242, 193, 238, 210, 144,  12, 191, 179, 162, 241,
        81 ,  51, 145, 235, 249,  14, 239, 107,  49, 192, 214,  31, 181, 199, 106, 157,
        184,  84, 204, 176, 115, 121,  50,  45, 127,   4, 150, 254, 138, 236, 205,  93,
        222, 114,  67,  29,  24,  72, 243, 141, 128, 195,  78,  66, 215,  61, 156, 180
    ];

    private static readonly int[,] Grad3Table =
    {
        { 1, 1, 0 }, { -1, 1, 0 }, { 1, -1, 0 }, { -1, -1, 0 },
        { 1, 0, 1 }, { -1, 0, 1 }, { 1, 0, -1 }, { -1, 0, -1 },
        { 0, 1, 1 }, { 0, -1, 1 }, { 0, 1, -1 }, { 0, -1, -1 },
        { 1, 0, -1 }, { -1, 0, -1 }, { 0, -1, 1 }, { 0, 1, 1 }
    };

    private static readonly int[] P = InitializeP();

    private static int[] InitializeP()
    {
        var p = new int[512];
        for (int i = 0; i < 512; i++)
        {
            p[i] = i < 256 ? Permutation[i] : Permutation[i - 256];
        }
        return p;
    }

    public static double Noise3d(
        double x, double y, double z,
        int nOctaves = 3,
        double persistence = 0.5,
        double lacunarity = 2.0,
        double scale = 10.0)
    {
        double freq = 1.0;
        double amp = 1.0;
        double max = 0.0;
        double total = 0.0;
        for (int i = 0; i < nOctaves; i++)
        {
            total += amp * Noise(x * freq / scale, y * freq / scale, z * freq / scale);
            max += amp;
            freq *= lacunarity;
            amp *= persistence;
        }
        return total / max;
    }

    public static double RidgedNoise3d(
        double x, double y, double z,
        int nOctaves = 3,
        double persistence = 0.5,
        double lacunarity = 2.0,
        double scale = 10.0)
    {
        double freq = 1.0;
        double amp = 1.0;
        double max = 0.0;
        double total = 0.0;
        for (int i = 0; i < nOctaves; i++)
        {
            double value = Noise(
                x * freq / scale,
                y * freq / scale,
                z * freq / scale);
            value = Math.Abs(value);
            total += amp * value;
            max += amp;
            freq *= lacunarity;
            amp *= persistence;
        }
        return total / max;
    }

    public static double Noise(double x, double y, double z)
    {
        // Find unit cube that contains point
        int xi = (int)Math.Floor(x) & 255;
        int yi = (int)Math.Floor(y) & 255;
        int zi = (int)Math.Floor(z) & 255;

        // Find relative x, y, z of point in cube
        double xx = x - Math.Floor(x);
        double yy = y - Math.Floor(y);
        double zz = z - Math.Floor(z);

        // Compute fade curves for each of xx, yy, zz
        double u = Fade(xx);
        double v = Fade(yy);
        double w = Fade(zz);

        // Hash co-ordinates of the 8 cube corners
        // and add blended results from 8 corners of cube

        int a = P[xi] + yi;
        int aa = P[a] + zi;
        int ab = P[a + 1] + zi;
        int b = P[xi + 1] + yi;
        int ba = P[b] + zi;
        int bb = P[b + 1] + zi;

        return Lerp(w,
            Lerp(v,
                Lerp(u, Grad3(P[aa], xx, yy, zz),
                    Grad3(P[ba], xx - 1, yy, zz)
                ),
                Lerp(u, Grad3(P[ab], xx, yy - 1, zz),
                    Grad3(P[bb], xx - 1, yy - 1, zz)
                )
            ),
            Lerp(v,
                Lerp(u, Grad3(P[aa + 1], xx, yy, zz - 1),
                    Grad3(P[ba + 1], xx - 1, yy, zz - 1)
                ),
                Lerp(u, Grad3(P[ab + 1], xx, yy - 1, zz - 1),
                    Grad3(P[bb + 1], xx - 1, yy - 1, zz - 1)
                )
            )
        );
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static double Lerp(double t, double a, double b) => a + t * (b - a);

    private static double Grad3(int hash, double x, double y, double z)
    {
        int h = hash & 15;
        return x * Grad3Table[h, 0] + y * Grad3Table[h, 1] + z * Grad3Table[h, 2];
    }
}
