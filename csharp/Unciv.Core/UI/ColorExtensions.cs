using Unciv.Models;

namespace Unciv.UI.Components.Extensions;

// core/src/com/unciv/ui/components/extensions/Scene2dExtensions.kt (colorFromRGB only; colorFromHex was not requested)
public static class ColorExtensions
{
    /// <summary>Create a new <see cref="EngineColor"/> from r/g/b given as Integers in the range 0..255.</summary>
    public static EngineColor ColorFromRGB(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    /// <summary>Create a new <see cref="EngineColor"/> from a 3-element list of Integers in the range 0..255.</summary>
    public static EngineColor ColorFromRGB(IReadOnlyList<int> rgb) => ColorFromRGB(rgb[0], rgb[1], rgb[2]);
}
