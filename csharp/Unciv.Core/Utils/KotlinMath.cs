namespace Unciv.Utils;

// Numeric conversions where .NET and Kotlin/JVM disagree. Use these wherever the Kotlin source calls
// roundToInt(), toInt() on a Float/Double, or Int.mod(); never the bare .NET equivalents.
public static class KotlinMath
{
    // kotlin.math.roundToInt(Float): half-up (java.lang.Math.round), saturating, NaN throws.
    // Done without `value + 0.5f` because that float addition itself rounds (0.49999997f + 0.5f == 1f).
    public static int RoundToInt(float value)
    {
        if (float.IsNaN(value)) throw new ArgumentException("Cannot round NaN value.", nameof(value));
        float floor = MathF.Floor(value);
        // value - floor is exact for floats; when |value| >= 2^23 it is 0
        float rounded = value - floor >= 0.5f ? floor + 1f : floor;
        return FloatToInt(rounded);
    }

    // kotlin.math.roundToInt(Double): half-up (java.lang.Math.round), saturating, NaN throws.
    public static int RoundToInt(double value)
    {
        if (double.IsNaN(value)) throw new ArgumentException("Cannot round NaN value.", nameof(value));
        double floor = Math.Floor(value);
        // value - floor is exact for doubles; when |value| >= 2^52 it is 0
        double rounded = value - floor >= 0.5 ? floor + 1.0 : floor;
        return DoubleToInt(rounded);
    }

    // Kotlin Float.toInt(): truncates toward zero, saturates at Int.MIN/MAX, NaN becomes 0. (C# `(int)` is unspecified there.)
    public static int FloatToInt(float value)
    {
        if (float.IsNaN(value)) return 0;
        if (value >= 2147483648f) return int.MaxValue;
        if (value <= -2147483648f) return int.MinValue;
        return (int)value;
    }

    // Kotlin Double.toLong(): truncates toward zero, saturates at Long.MIN/MAX, NaN becomes 0.
    public static long DoubleToLong(double value)
    {
        if (double.IsNaN(value)) return 0;
        if (value >= 9223372036854775807.0) return long.MaxValue;
        if (value <= -9223372036854775808.0) return long.MinValue;
        return (long)value;
    }

    // Kotlin Float.pow(Float): computed in double and rounded to float (kotlin.math.pow delegates to Math.pow).
    public static float Pow(float value, float exponent) => (float)Math.Pow(value, exponent);

    // Kotlin Double.toInt(): same rules as FloatToInt.
    public static int DoubleToInt(double value)
    {
        if (double.IsNaN(value)) return 0;
        if (value >= 2147483647.0) return int.MaxValue;
        if (value <= -2147483648.0) return int.MinValue;
        return (int)value;
    }

    // Kotlin Int.mod(Int): result has the sign of the divisor (C# `%` has the sign of the dividend).
    public static int Mod(int value, int modulus)
    {
        int r = value % modulus;
        return r != 0 && (r ^ modulus) < 0 ? r + modulus : r;
    }
}
