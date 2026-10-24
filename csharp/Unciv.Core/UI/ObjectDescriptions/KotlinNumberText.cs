using System.Globalization;

namespace Unciv.UI.ObjectDescriptions;

// Kotlin string templates print numbers with Float.toString() / Float.toInt(), which differ from the C# defaults.

internal static class KotlinNumberText
{
    /// <summary>Kotlin <c>Float.toString()</c> (Java <c>Float.toString</c>): shortest round-trip digits, at least one fraction digit, "E" notation outside 1e-3 until 1e7.</summary>
    public static string FloatToString(float value)
    {
        if (float.IsNaN(value)) return "NaN";
        if (float.IsPositiveInfinity(value)) return "Infinity";
        if (float.IsNegativeInfinity(value)) return "-Infinity";
        if (value == 0f) return float.IsNegative(value) ? "-0.0" : "0.0";

        // "R" gives the shortest round-trip digits; take them apart and lay them out the Java way
        var roundTrip = value.ToString("R", CultureInfo.InvariantCulture);
        var negative = roundTrip.StartsWith('-');
        if (negative) roundTrip = roundTrip[1..];

        // Split into digit string and decimal exponent (value = 0.d1d2... * 10^pointPos)
        var exponent = 0;
        var ePos = roundTrip.IndexOf('E');
        if (ePos >= 0)
        {
            exponent = int.Parse(roundTrip[(ePos + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            roundTrip = roundTrip[..ePos];
        }
        var dot = roundTrip.IndexOf('.');
        var intPart = dot < 0 ? roundTrip : roundTrip[..dot];
        var fracPart = dot < 0 ? "" : roundTrip[(dot + 1)..];
        var digits = (intPart + fracPart).TrimStart('0');
        var leadingZeros = (intPart + fracPart).Length - digits.Length;
        var pointPos = intPart.Length - leadingZeros + exponent;
        digits = digits.TrimEnd('0');
        if (digits.Length == 0) digits = "0";

        var abs = Math.Abs(value);
        string result;
        if (abs >= 1e-3f && abs < 1e7f)
        {
            if (pointPos <= 0)
                result = "0." + new string('0', -pointPos) + digits;
            else if (digits.Length <= pointPos)
                result = digits + new string('0', pointPos - digits.Length) + ".0";
            else
                result = digits[..pointPos] + "." + digits[pointPos..];
        }
        else
        {
            var mantissa = digits[..1] + "." + (digits.Length > 1 ? digits[1..] : "0");
            result = mantissa + "E" + (pointPos - 1).ToString(CultureInfo.InvariantCulture);
        }
        return negative ? "-" + result : result;
    }

    /// <summary>Kotlin <c>Float.toInt()</c>: truncates toward zero, saturates, NaN is 0.</summary>
    public static int FloatToInt(float value)
    {
        if (float.IsNaN(value)) return 0;
        if (value >= int.MaxValue) return int.MaxValue;
        if (value <= int.MinValue) return int.MinValue;
        return (int)value;
    }

    public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
