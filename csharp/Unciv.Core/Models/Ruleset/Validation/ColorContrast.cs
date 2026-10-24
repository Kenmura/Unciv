namespace Unciv.Models.Ruleset.Validation;

// Ported from core/src/com/unciv/ui/components/extensions/Scene2dExtensions.kt (getRelativeLuminance, getContrastRatio): all defined by
// https://www.w3.org/TR/WCAG20/. Kept internal here because only the validator needs them.
internal static class ColorContrast
{
    /// <summary>Kotlin <c>Color.getRelativeLuminance</c>. The channel is a Float compared and combined with Double literals, so the arithmetic is Double.</summary>
    public static double GetRelativeLuminance(EngineColor color)
    {
        static double GetRelativeChannelLuminance(float channel) =>
            channel < 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

        double r = GetRelativeChannelLuminance(color.R);
        double g = GetRelativeChannelLuminance(color.G);
        double b = GetRelativeChannelLuminance(color.B);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>Kotlin <c>getContrastRatio</c>; the ratio can range from 1 to 21.</summary>
    public static double GetContrastRatio(EngineColor color1, EngineColor color2)
    {
        double innerColorLuminance = GetRelativeLuminance(color1);
        double outerColorLuminance = GetRelativeLuminance(color2);

        return innerColorLuminance > outerColorLuminance
            ? (innerColorLuminance + 0.05) / (outerColorLuminance + 0.05)
            : (outerColorLuminance + 0.05) / (innerColorLuminance + 0.05);
    }
}
