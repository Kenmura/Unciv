using Unciv.Utils;

namespace Unciv.Core.Tests.Utils;

// Expected values for RoundToInt/FloatToInt/DoubleToInt come from running java.lang.Math.round(float),
// Float.toInt()/Double.toInt() semantics (JVM, which Kotlin delegates to); Mod follows kotlin.Int.mod.
public class KotlinMathTests
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 1)]
    [InlineData(-0.5f, 0)]
    [InlineData(1.5f, 2)]
    [InlineData(2.5f, 3)]
    [InlineData(-1.5f, -1)]
    [InlineData(-2.5f, -2)]
    [InlineData(2.4f, 2)]
    [InlineData(-2.6f, -3)]
    [InlineData(8388607.5f, 8388608)] // 2^23 - 0.5: largest float with a fractional half
    [InlineData(-8388607.5f, -8388607)]
    [InlineData(16777217f, 16777216)] // not representable; input is already 16777216f
    public void RoundToIntIsHalfUp(float value, int expected)
    {
        Assert.Equal(expected, KotlinMath.RoundToInt(value));
    }

    [Fact]
    public void RoundToIntLargestFloatBelowHalfRoundsDown()
    {
        // 0.49999997f + 0.5f rounds up to 1f in float arithmetic; Java's Math.round gives 0
        Assert.Equal(0, KotlinMath.RoundToInt(0.49999997f));
        Assert.Equal(0, KotlinMath.RoundToInt(BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(0.5f) - 1)));
        Assert.Equal(-1, KotlinMath.RoundToInt(-0.50000006f));
    }

    [Fact]
    public void RoundToIntSaturatesAndRejectsNaN()
    {
        Assert.Equal(int.MaxValue, KotlinMath.RoundToInt(3e9f));
        Assert.Equal(int.MinValue, KotlinMath.RoundToInt(-3e9f));
        Assert.Equal(int.MaxValue, KotlinMath.RoundToInt(float.PositiveInfinity));
        Assert.Equal(int.MinValue, KotlinMath.RoundToInt(float.NegativeInfinity));
        Assert.Throws<ArgumentException>(() => KotlinMath.RoundToInt(float.NaN));
    }

    [Theory]
    [InlineData(1.9f, 1)]
    [InlineData(-1.9f, -1)]
    [InlineData(2.5f, 2)]
    [InlineData(1.5f, 1)]
    [InlineData(-0.9f, 0)]
    [InlineData(0f, 0)]
    [InlineData(float.NaN, 0)]
    [InlineData(3e9f, int.MaxValue)]
    [InlineData(-3e9f, int.MinValue)]
    [InlineData(2147483648f, int.MaxValue)]
    [InlineData(-2147483648f, int.MinValue)]
    [InlineData(float.PositiveInfinity, int.MaxValue)]
    [InlineData(float.NegativeInfinity, int.MinValue)]
    public void FloatToIntTruncatesAndSaturates(float value, int expected)
    {
        Assert.Equal(expected, KotlinMath.FloatToInt(value));
    }

    [Theory]
    [InlineData(1.9, 1)]
    [InlineData(-1.9, -1)]
    [InlineData(2.5, 2)]
    [InlineData(double.NaN, 0)]
    [InlineData(2147483647.0, int.MaxValue)]
    [InlineData(2147483647.9, int.MaxValue)]
    [InlineData(2147483648.0, int.MaxValue)]
    [InlineData(-2147483648.0, int.MinValue)]
    [InlineData(-2147483649.0, int.MinValue)]
    [InlineData(1e300, int.MaxValue)]
    [InlineData(double.NegativeInfinity, int.MinValue)]
    public void DoubleToIntTruncatesAndSaturates(double value, int expected)
    {
        Assert.Equal(expected, KotlinMath.DoubleToInt(value));
    }

    [Theory]
    [InlineData(7, 3, 1)]
    [InlineData(-7, 3, 2)]
    [InlineData(7, -3, -2)]
    [InlineData(-7, -3, -1)]
    [InlineData(6, 3, 0)]
    [InlineData(-6, 3, 0)]
    [InlineData(6, -3, 0)]
    [InlineData(0, 5, 0)]
    [InlineData(int.MinValue, 7, 5)]
    [InlineData(int.MinValue, int.MaxValue, int.MaxValue - 1)]
    public void ModTakesSignOfDivisor(int value, int modulus, int expected)
    {
        Assert.Equal(expected, KotlinMath.Mod(value, modulus));
    }

    [Fact]
    public void ModByZeroThrows()
    {
        Assert.Throws<DivideByZeroException>(() => KotlinMath.Mod(1, 0));
    }
}
