namespace Unciv.Assets;

/// <summary>Raw RGBA8 image (row-major, 4 bytes per pixel).</summary>
public sealed record PixelImage(int Width, int Height, byte[] Rgba);

/// <summary>Deterministic generated textures: terrain noise and 64x64 icons. Pure integer maths on the key's stable hash.</summary>
public static class ProceduralImages
{
    public const int IconSize = 64;

    /// <summary>Grey value noise in 200..255, one independent value per pixel; multiplied onto the albedo so flat colours get a little grain.</summary>
    public static PixelImage Noise(AssetKey key, int size = 16)
    {
        var seed = StableHash.Of(key.ToString());
        var data = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var v = (byte)(200 + StableHash.Mix(seed, x, y) % 56);
                var o = (y * size + x) * 4;
                data[o] = v; data[o + 1] = v; data[o + 2] = v; data[o + 3] = 255;
            }
        }
        return new PixelImage(size, size, data);
    }

    public enum IconShape
    {
        Disc,
        Square,
        Diamond,
        Ring,
        Triangle,
    }

    public static IconShape ShapeFor(AssetKey key) => (IconShape)(StableHash.Of(key.ToString()) / 360 % 5);

    /// <summary>Fill colour for an icon key: the stat palette for stat icons, otherwise a hash-derived hue.</summary>
    public static Rgb IconColour(AssetKey key) =>
        key.Category == RulesetAssetKeys.StatIcon ? PlaceholderPalette.ForStat(key.Name) : key.Category == RulesetAssetKeys.StatusIcon ? PlaceholderPalette.ForStatus(key.Name) : PlaceholderPalette.FromHash(key.Category + "/" + key.Name, 170, 200);

    /// <summary>64x64 icon: coloured shape with a dark outline and the name's initials in a bitmap font. Transparent outside the shape.</summary>
    public static PixelImage Icon(AssetKey key)
    {
        var fill = IconColour(key);
        var shape = key.IsDefault ? IconShape.Square : ShapeFor(key);
        var data = new byte[IconSize * IconSize * 4];
        const int c2 = IconSize - 1; // coordinates doubled around the centre so integer maths is exact
        for (var y = 0; y < IconSize; y++)
        {
            for (var x = 0; x < IconSize; x++)
            {
                var depth = Depth(shape, 2 * x - c2, 2 * y - c2); // >0 inside, larger = deeper
                if (depth <= 0 || (shape == IconShape.Ring && depth > 40)) continue;
                var colour = depth <= 4 ? PlaceholderPalette.IconOutline : fill;
                var o = (y * IconSize + x) * 4;
                data[o] = colour.R; data[o + 1] = colour.G; data[o + 2] = colour.B; data[o + 3] = 255;
            }
        }
        GlyphFont.Draw(data, IconSize, IconSize, GlyphFont.Initials(key.Name), PlaceholderPalette.IconGlyph, scale: 3);
        return new PixelImage(IconSize, IconSize, data);
    }

    /// <summary>Distance-inside-the-shape measure in half-pixel units from the doubled, centred coordinates (sx, sy); 0 or negative means outside. The outline band is depth 1..4.</summary>
    private static int Depth(IconShape shape, int sx, int sy)
    {
        const int half = IconSize - 2; // doubled half-extent of the drawable area
        var dx = Math.Abs(sx);
        var dy = Math.Abs(sy);
        return shape switch
        {
            IconShape.Square => half - Math.Max(dx, dy) + 1,
            IconShape.Diamond => half - (dx + dy) * 2 / 3 + 1,
            IconShape.Triangle => Math.Min((sy + half) / 2 - dx, half - sy) + 1,
            _ => half - ISqrt(dx * dx + dy * dy) + 1,
        };
    }

    /// <summary>Initials on a dark translucent plate with a one-pixel margin, glyph pixels doubled; the unit-block billboard texture.</summary>
    public static PixelImage Billboard(string text)
    {
        const int scale = 2;
        const int margin = 3;
        var width = (text.Length * (GlyphFont.Width + 1) - 1) * scale + 2 * margin;
        var height = GlyphFont.Height * scale + 2 * margin;
        var data = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            data[i * 4] = PlaceholderPalette.IconOutline.R;
            data[i * 4 + 1] = PlaceholderPalette.IconOutline.G;
            data[i * 4 + 2] = PlaceholderPalette.IconOutline.B;
            data[i * 4 + 3] = 210;
        }
        GlyphFont.Draw(data, width, height, text, PlaceholderPalette.IconGlyph, scale);
        return new PixelImage(width, height, data);
    }

    /// <summary>C004: a unit's health bar picture, <paramref name="width"/> x <paramref name="height"/> px: a dark plate with a one-pixel rim and the left <paramref name="fraction"/> of the inside filled with <paramref name="fill"/> (at least one pixel while anything is left).</summary>
    public static PixelImage HealthBar(float fraction, Rgb fill, int width = 40, int height = 6)
    {
        var data = new byte[width * height * 4];
        var inner = width - 2;
        var filled = fraction <= 0f ? 0 : Math.Clamp((int)(fraction * inner + 0.5f), 1, inner);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var rim = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                var colour = rim ? PlaceholderPalette.IconOutline : x - 1 < filled ? fill : PlaceholderPalette.HealthBack;
                var o = (y * width + x) * 4;
                data[o] = colour.R; data[o + 1] = colour.G; data[o + 2] = colour.B; data[o + 3] = 255;
            }
        }
        return new PixelImage(width, height, data);
    }

    private static int ISqrt(int n)
    {
        var x = 0;
        while ((x + 1) * (x + 1) <= n) x++;
        return x;
    }
}
