using System.Text;

namespace Unciv.Assets;

/// <summary>Built-in 5x7 bitmap font (A-Z, 0-9, '?') used for icon initials and unit billboards. Original glyph shapes, no font file involved.</summary>
public static class GlyphFont
{
    public const int Width = 5;
    public const int Height = 7;

    private static readonly Dictionary<char, string> GlyphTable = new()
    {
        ['A'] = ".###.#...##...#######...##...##...#",
        ['B'] = "####.#...##...#####.#...##...#####.",
        ['C'] = ".#####....#....#....#....#.....####",
        ['D'] = "####.#...##...##...##...##...#####.",
        ['E'] = "######....#....####.#....#....#####",
        ['F'] = "######....#....####.#....#....#....",
        ['G'] = ".#####....#....#..###...##...#.###.",
        ['H'] = "#...##...##...#######...##...##...#",
        ['I'] = "#####..#....#....#....#....#..#####",
        ['J'] = "..###...#....#....#....#.#..#..##..",
        ['K'] = "#...##..#.#.#..##...#.#..#..#.#...#",
        ['L'] = "#....#....#....#....#....#....#####",
        ['M'] = "#...###.###.#.##.#.##...##...##...#",
        ['N'] = "#...###..##.#.##..###...##...##...#",
        ['O'] = ".###.#...##...##...##...##...#.###.",
        ['P'] = "####.#...##...#####.#....#....#....",
        ['Q'] = ".###.#...##...##...##.#.##..#..##.#",
        ['R'] = "####.#...##...#####.#.#..#..#.#...#",
        ['S'] = ".#####....#.....###.....#....#####.",
        ['T'] = "#####..#....#....#....#....#....#..",
        ['U'] = "#...##...##...##...##...##...#.###.",
        ['V'] = "#...##...##...##...##...#.#.#...#..",
        ['W'] = "#...##...##...##.#.##.#.###.###...#",
        ['X'] = "#...##...#.#.#...#...#.#.#...##...#",
        ['Y'] = "#...##...#.#.#...#....#....#....#..",
        ['Z'] = "#####....#...#...#...#...#....#####",
        ['0'] = ".###.#...##..###.#.###..##...#.###.",
        ['1'] = "..#...##....#....#....#....#...###.",
        ['2'] = ".###.#...#....#...#...#...#...#####",
        ['3'] = "####.....#....#.###.....#....#####.",
        ['4'] = "...#...##..#.#.#..#.#####...#....#.",
        ['5'] = "######....####.....#....##...#.###.",
        ['6'] = ".###.#....#....####.#...##...#.###.",
        ['7'] = "#####....#...#...#...#....#....#...",
        ['8'] = ".###.#...##...#.###.#...##...#.###.",
        ['9'] = ".###.#...##...#.####....#....#.###.",
        ['?'] = ".###.#...#....#...#...#.........#..",
    };

    /// <summary>Rows of '#'/'.' for <paramref name="c"/> (letters case-insensitive; unknown characters draw '?'). Exactly <see cref="Height"/> rows of <see cref="Width"/>.</summary>
    public static string[] Rows(char c)
    {
        var key = char.ToUpperInvariant(c);
        var bits = GlyphTable.TryGetValue(key, out var found) ? found : GlyphTable['?'];
        var rows = new string[Height];
        for (var r = 0; r < Height; r++) rows[r] = bits.Substring(r * Width, Width);
        return rows;
    }

    /// <summary>Up to two characters: the first letters of the first two words, or the first two letters of a single word. ASCII letters and digits only; "?" when none.</summary>
    public static string Initials(string name)
    {
        var words = name.Split([' ', '_', '-', '/', '\'', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsAsciiLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0).ToList();
        var sb = new StringBuilder();
        if (words.Count == 0) return "?";
        if (words.Count == 1) sb.Append(words[0].AsSpan(0, Math.Min(2, words[0].Length)));
        else sb.Append(words[0][0]).Append(words[1][0]);
        return sb.ToString().ToUpperInvariant();
    }

    /// <summary>Paints <paramref name="text"/> centred into an RGBA buffer, each glyph pixel a scale x scale block.</summary>
    public static void Draw(byte[] rgba, int width, int height, string text, Rgb colour, int scale)
    {
        var advance = (Width + 1) * scale;
        var totalWidth = text.Length * advance - scale;
        var left = (width - totalWidth) / 2;
        var top = (height - Height * scale) / 2;
        for (var i = 0; i < text.Length; i++)
        {
            var rows = Rows(text[i]);
            for (var r = 0; r < Height; r++)
            {
                for (var c = 0; c < Width; c++)
                {
                    if (rows[r][c] != '#') continue;
                    for (var sy = 0; sy < scale; sy++)
                    {
                        for (var sx = 0; sx < scale; sx++)
                        {
                            var px = left + i * advance + c * scale + sx;
                            var py = top + r * scale + sy;
                            if (px < 0 || py < 0 || px >= width || py >= height) continue;
                            var o = (py * width + px) * 4;
                            rgba[o] = colour.R; rgba[o + 1] = colour.G; rgba[o + 2] = colour.B; rgba[o + 3] = 255;
                        }
                    }
                }
            }
        }
    }
}
