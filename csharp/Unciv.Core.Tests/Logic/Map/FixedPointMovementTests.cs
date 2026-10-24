using Unciv.Logic.Map.PathingMap;
using Unciv.Utils;

namespace Unciv.Core.Tests.Logic.Map;

// T067: port of tests/src/com/unciv/logic/map/FixedPointMovementTest.kt (every test, same names and values).
public class FixedPointMovementTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(50, 50)]
    [InlineData(-1, -1)]
    [InlineData(-5, -5)]
    [InlineData(-50, -50)]
    public void TestRoundtripIntegerMovements(int movement, int expected) =>
        Assert.Equal(expected, KotlinMath.RoundToInt(FixedPointMovement.FpmFromMovement(movement).ToFloat()));

    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1.5f, 1.5f)]
    [InlineData(2.5f, 2.5f)]
    [InlineData(5.5f, 5.5f)]
    [InlineData(10.5f, 10.5f)]
    [InlineData(-0.5f, -0.5f)]
    [InlineData(-1.5f, -1.5f)]
    [InlineData(-10.5f, -10.5f)]
    public void TestRoundtripHalfMovements(float movement, float expected) =>
        Assert.Equal(expected, FixedPointMovement.FpmFromMovement(movement).ToFloat());

    [Theory]
    [InlineData(1.4166666f, 1.4333333f)] // 42.5/30 is halfway between 42/30 and 43/30: rounds UP
    [InlineData(0.48333332f, 0.5f)] // 14.5/30 is halfway: rounds UP to 15/30
    [InlineData(0.4833333f, 0.46666667f)] // just below the midpoint: rounds DOWN
    public void TestHalfUpRoundingEdgeCases(float input, float expected) =>
        Assert.Equal(expected, FixedPointMovement.FpmFromMovement(input).ToFloat());

    [Fact]
    public void TestArithmeticMaintainsRoundingForHalves()
    {
        var half = FixedPointMovement.FpmFromMovement(0.5f);
        var oneAndHalf = FixedPointMovement.FpmFromMovement(1.5f);
        Assert.Equal(2.0f, (half + oneAndHalf).ToFloat(), 0.0001f);
        Assert.Equal(1.0f, (half * 2).ToFloat(), 0.0001f);
        Assert.Equal(0.5f, (oneAndHalf / 3).ToFloat(), 0.0001f);
    }

    [Theory]
    [InlineData(0.5f, 0.5f, 0)]
    [InlineData(1.0f, 0.5f, 1)]
    [InlineData(0.5f, 1.0f, -1)]
    [InlineData(-1.0f, 1.0f, -1)]
    [InlineData(-1.0f, -0.5f, -1)]
    public void TestComparisonWithFixedPointValues(float first, float second, int expected) =>
        Assert.Equal(expected, FixedPointMovement.FpmFromMovement(first).CompareTo(FixedPointMovement.FpmFromMovement(second)));

    [Theory]
    [InlineData(0.5f, 0, 1)]
    [InlineData(1.0f, 1, 0)]
    [InlineData(0.5f, 1, -1)]
    [InlineData(2.0f, 1, 1)]
    public void TestComparisonWithInt(float first, int second, int expected) =>
        Assert.Equal(expected, FixedPointMovement.FpmFromMovement(first).CompareTo(second));

    [Theory]
    [InlineData(0.5f, 0.5f, 0)]
    [InlineData(1.0f, 0.5f, 1)]
    [InlineData(0.5f, 1.0f, -1)]
    [InlineData(-0.5f, -0.5f, 0)]
    public void TestComparisonWithFloat(float first, float second, int expected) =>
        Assert.Equal(expected, FixedPointMovement.FpmFromMovement(first).CompareTo(second));

    [Fact]
    public void TestOperatorPlusPreservesValues()
    {
        Assert.Equal(2.0f, (FixedPointMovement.FpmFromMovement(1.0f) + 1).ToFloat());
        Assert.Equal(1.0f, (FixedPointMovement.FpmFromMovement(0.5f) + 0.5f).ToFloat());
        Assert.Equal(2.5f, (FixedPointMovement.FpmFromMovement(1.5f) + 1).ToFloat());
        Assert.Equal(9f, (FixedPointMovement.FpmFromMovement(4.2f) + FixedPointMovement.FpmFromMovement(4.8f)).ToFloat());
    }

    [Fact]
    public void TestCoercionMethods()
    {
        var min = FixedPointMovement.FpmFromMovement(1.0f);
        var max = FixedPointMovement.FpmFromMovement(5.0f);
        var value = FixedPointMovement.FpmFromMovement(3.0f);

        Assert.Equal(3.0f, value.CoerceIn(min, max).ToFloat(), 0.0001f);
        Assert.Equal(1.0f, FixedPointMovement.FpmFromMovement(0.5f).CoerceAtLeast(min).ToFloat(), 0.0001f);
        Assert.Equal(5.0f, FixedPointMovement.FpmFromMovement(6.0f).CoerceAtMost(max).ToFloat(), 0.0001f);
        Assert.Throws<ArgumentException>(() => value.CoerceIn(max, min));
    }

    [Theory]
    [InlineData(1.0f, 2.0f, 0.5f)]
    [InlineData(2.0f, 2.0f, 1.0f)]
    [InlineData(3.0f, 2.0f, 1.5f)]
    [InlineData(0.1f, 3.0f, 0.033333335f)]
    public void TestDivisionKnownValues(float dividend, float divisor, float expected) =>
        Assert.Equal(expected, (FixedPointMovement.FpmFromMovement(dividend) / FixedPointMovement.FpmFromMovement(divisor)).ToFloat());

    [Theory]
    [InlineData(1.0f, 2.0f, 2.0f)]
    [InlineData(1.5f, 1.6666667f, 2.5f)]
    [InlineData(6.0f, 7.0f, 42.0f)]
    [InlineData(0.03333333f, 3f, 0.1f)]
    public void TestMultiplicationKnownValues(float a, float b, float expected) =>
        Assert.Equal(expected, (FixedPointMovement.FpmFromMovement(a) * FixedPointMovement.FpmFromMovement(b)).ToFloat());

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(29)]
    [InlineData(31)]
    [InlineData(61)]
    [InlineData(127)]
    [InlineData(257)]
    [InlineData(509)]
    [InlineData(1021)]
    [InlineData(2047)]
    [InlineData(4093)]
    [InlineData(8191)]
    public void TestRepresentativePrimeBitsFloatRoundtrip(int bits) =>
        Assert.Equal(bits, FixedPointMovement.FpmFromMovement(FixedPointMovement.FpmFromFixedPointBits(bits).ToFloat()).Bits);

    [Fact]
    public void ToStringFormatsBitsAndFloat()
    {
        Assert.Equal("FPM{45/30=1.5}", FixedPointMovement.FpmFromMovement(1.5f).ToString());
        Assert.Equal("FPM{30/30=1.0}", FixedPointMovement.FpmOne.ToString());
        Assert.Equal("FPM{0/30=0.0}", FixedPointMovement.FpmZero.ToString());
    }

    [Fact]
    public void ArithmeticWrapsLikeKotlinInt()
    {
        var big = FixedPointMovement.FpmFromFixedPointBits(int.MaxValue);
        Assert.Equal(int.MinValue, (big + FixedPointMovement.FpmFromFixedPointBits(1)).Bits);
        Assert.Equal(0, FixedPointMovement.FpmFromMovement(float.NaN).Bits); // Kotlin NaN.toInt() == 0
    }

    [Fact]
    public void MixedTypeOperatorsMatchKotlin()
    {
        var one = FixedPointMovement.FpmOne;
        var half = FixedPointMovement.FpmPointFive;
        Assert.Equal(60, (one + 1).Bits);
        Assert.Equal(0, (one - 1).Bits);
        Assert.Equal(15, (one - 0.5f).Bits);
        Assert.Equal(45, (one + 0.5f).Bits);
        Assert.Equal(15, (one - half).Bits);
        Assert.Equal(90, (one * 3).Bits);
        Assert.Equal(1350, (one * 1.5f).Bits); // Kotlin: fpmFromMovement(bits * multiplier)
        Assert.Equal(15, (half * one).Bits);
        Assert.Equal(60, (one / half).Bits);
        Assert.Equal(10, (one / 3).Bits);
        Assert.Equal(450, (one / 2f).Bits);
        Assert.True(one > half && half < one && one >= FixedPointMovement.FpmFromMovement(1) && half <= one);
        Assert.True(one > 0 && one < 2 && one >= 1 && one <= 1);
        Assert.True(one > 0.5f && one < 1.5f && one >= 1f && one <= 1f);
        Assert.True(one == FixedPointMovement.FpmFromMovement(1) && one != half);
        Assert.True(one.Equals((object)FixedPointMovement.FpmFromMovement(1.0f)));
        Assert.False(one.Equals("1"));
        Assert.Equal(30, one.GetHashCode());
    }
}
