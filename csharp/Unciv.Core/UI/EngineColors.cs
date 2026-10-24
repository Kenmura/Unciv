
namespace Unciv.Models;

// Engine-owned stand-in for com.badlogic.gdx.graphics.Colors (the default name table, no Unciv additions exist) and Color.valueOf(String).
public static class EngineColors
{
    // Order and values as libGDX Colors.reset()
    private static readonly Dictionary<string, uint> Named = new()
    {
        ["CLEAR"] = 0x00000000,
        ["BLACK"] = 0x000000ff,
        ["WHITE"] = 0xffffffff,
        ["LIGHT_GRAY"] = 0xbfbfbfff,
        ["GRAY"] = 0x7f7f7fff,
        ["DARK_GRAY"] = 0x3f3f3fff,
        ["BLUE"] = 0x0000ffff,
        ["NAVY"] = 0x000080ff,
        ["ROYAL"] = 0x4169e1ff,
        ["SLATE"] = 0x708090ff,
        ["SKY"] = 0x87ceebff,
        ["CYAN"] = 0x00ffffff,
        ["TEAL"] = 0x008080ff,
        ["GREEN"] = 0x00ff00ff,
        ["CHARTREUSE"] = 0x7fff00ff,
        ["LIME"] = 0x32cd32ff,
        ["FOREST"] = 0x228b22ff,
        ["OLIVE"] = 0x6b8e23ff,
        ["YELLOW"] = 0xffff00ff,
        ["GOLD"] = 0xffd700ff,
        ["GOLDENROD"] = 0xdaa520ff,
        ["ORANGE"] = 0xffa500ff,
        ["BROWN"] = 0x8b4513ff,
        ["TAN"] = 0xd2b48cff,
        ["FIREBRICK"] = 0xb22222ff,
        ["RED"] = 0xff0000ff,
        ["SCARLET"] = 0xff341cff,
        ["CORAL"] = 0xff7f50ff,
        ["SALMON"] = 0xfa8072ff,
        ["PINK"] = 0xff69b4ff,
        ["MAGENTA"] = 0xff00ffff,
        ["PURPLE"] = 0xa020f0ff,
        ["VIOLET"] = 0xee82eeff,
        ["MAROON"] = 0xb03060ff,
    };

    /// <summary><c>Colors.get(name)</c>: exact (case-sensitive) name lookup, null if unknown.</summary>
    public static EngineColor? Get(string name) =>
        Named.TryGetValue(name, out var rgba) ? FromRgba8888(rgba) : null;

    /// <summary><c>Color.valueOf(hex)</c>: optional leading '#', 6 digits RRGGBB or 8 digits RRGGBBAA (any other length reads as 6 digits plus opaque alpha).</summary>
    public static EngineColor ValueOf(string hex)
    {
        if (hex[0] == '#') hex = hex[1..];
        // Java parseInt/Character.digit accept any Unicode decimal digit, int.Parse(HexNumber) accepts ASCII only
        static int Digit(char c) =>
            char.IsDigit(c) ? (int)char.GetNumericValue(c)
            : c is >= 'a' and <= 'f' ? c - 'a' + 10
            : c is >= 'A' and <= 'F' ? c - 'A' + 10
            : throw new FormatException($"Invalid hex digit '{c}'");
        float Part(int start) => (Digit(hex[start]) * 16 + Digit(hex[start + 1])) / 255f;
        return new EngineColor(Part(0), Part(2), Part(4), hex.Length != 8 ? 1f : Part(6));
    }

    private static EngineColor FromRgba8888(uint rgba) =>
        new(((rgba >> 24) & 255) / 255f, ((rgba >> 16) & 255) / 255f, ((rgba >> 8) & 255) / 255f, (rgba & 255) / 255f);
}
