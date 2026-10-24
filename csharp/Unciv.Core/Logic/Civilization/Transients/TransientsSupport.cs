using System.Globalization;

namespace Unciv.Logic.Civilization.Transients;

/// <summary>Kotlin <c>String.toInt()/toFloat()/toDouble()</c> and the wrapping <c>sumOf</c> the transients use (T058).</summary>
internal static class TransientsSupport
{
    public static int ToInt(this string text) => int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static float ToFloat(this string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    public static double ToDouble(this string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Kotlin <c>sumOf { Int }</c>: wraps on overflow, where LINQ's <c>Sum</c> throws.</summary>
    public static int SumOf<T>(this IEnumerable<T> source, Func<T, int> selector)
    {
        var sum = 0;
        foreach (var item in source) sum = unchecked(sum + selector(item));
        return sum;
    }
}
