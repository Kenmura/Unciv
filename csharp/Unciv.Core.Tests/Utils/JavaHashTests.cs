using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Core.Tests.Utils;

// Golden values come from the JVM: String/Long/Float/Double/Boolean.hashCode, the compiled HexCoord data class,
// and com.unciv.utils.RandomnessKt (hashOf, withHash, pseudoRandomUuid) taken from the installed Unciv.jar,
// with Random(seed) from kotlin-stdlib 2.3.0.
public class JavaHashTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 97)]
    [InlineData("Hello", 69609650)]
    [InlineData("createNewCamp", -203756635)]
    [InlineData("QuestManager", 511475851)]
    [InlineData("City.Id", -1844922242)]
    [InlineData("ünï©ode €", 532555056)]
    [InlineData("😀x", 54959989)] // surrogate pair: hashed per UTF-16 code unit
    [InlineData("The quick brown fox jumps over the lazy dog", -609428141)]
    public void StringHashCodeMatchesJvm(string value, int expected)
    {
        Assert.Equal(expected, JavaHash.HashCode(value));
    }

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(1L, 1)]
    [InlineData(-1L, 0)]
    [InlineData(long.MinValue, int.MinValue)]
    [InlineData(long.MaxValue, int.MinValue)]
    [InlineData(4294967296L, 1)]
    [InlineData(81985529216486895L, -2004318072)]
    public void LongHashCodeMatchesJvm(long value, int expected)
    {
        Assert.Equal(expected, JavaHash.HashCode(value));
    }

    [Theory]
    [InlineData(0x00000000, 0)]
    [InlineData(unchecked((int)0x80000000), int.MinValue)] // -0f differs from 0f
    [InlineData(0x3fc00000, 1069547520)] // 1.5f
    [InlineData(0x7fc00000, 2143289344)] // NaN
    [InlineData(0x7fc00001, 2143289344)] // another NaN payload: canonicalized by floatToIntBits
    [InlineData(0x7f7fffff, 2139095039)] // Float.MAX_VALUE
    [InlineData(unchecked((int)0xff800000), -8388608)] // -Infinity
    [InlineData(0x3dcccccd, 1036831949)] // 0.1f
    [InlineData(unchecked((int)0xc0200000), -1071644672)] // -2.5f
    public void FloatHashCodeMatchesJvm(int bits, int expected)
    {
        Assert.Equal(expected, JavaHash.HashCode(BitConverter.Int32BitsToSingle(bits)));
    }

    [Theory]
    [InlineData(0x0000000000000000L, 0)]
    [InlineData(long.MinValue, int.MinValue)] // -0.0
    [InlineData(0x3ff8000000000000L, 1073217536)] // 1.5
    [InlineData(0x7ff8000000000000L, 2146959360)] // NaN
    [InlineData(0x7ff8000000000001L, 2146959360)] // another NaN payload
    [InlineData(0x7fefffffffffffffL, -2146435072)] // Double.MAX_VALUE
    [InlineData(0x3fb999999999999aL, -1507852285)] // 0.1
    public void DoubleHashCodeMatchesJvm(long bits, int expected)
    {
        Assert.Equal(expected, JavaHash.HashCode(BitConverter.Int64BitsToDouble(bits)));
    }

    [Fact]
    public void BooleanAndIntHashCodes()
    {
        Assert.Equal(1231, JavaHash.HashCode(true));
        Assert.Equal(1237, JavaHash.HashCode(false));
        Assert.Equal(-5, JavaHash.HashCode(-5));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 31)]
    [InlineData(0, 1, 1)]
    [InlineData(3, -4, 89)]
    [InlineData(-7, 12, -205)]
    [InlineData(100, 100, 3200)]
    [InlineData(-1, -1, -32)]
    [InlineData(int.MaxValue, int.MinValue, -31)]
    public void HexCoordHashCodeMatchesKotlinDataClass(int x, int y, int expected)
    {
        Assert.Equal(expected, HexCoord.Of(x, y).GetHashCode());
        Assert.Equal(HexCoord.Of(x, y).GetHashCode(), HexCoord.Of(x, y).GetHashCode());
    }

    [Fact]
    public void HashOfAndWithHashMatchKotlin()
    {
        Assert.Equal(0, Randomness.HashOf());
        Assert.Equal(7, Randomness.HashOf(7));
        Assert.Equal(1758370431, Randomness.HashOf(JavaHash.HashCode("createNewCamp"), 31, HexCoord.Of(3, -4).GetHashCode()));
        Assert.Equal(65403, Randomness.HashOf(int.MaxValue, -5, int.MinValue, 99999));
        Assert.Equal(2147483622, int.MaxValue.WithHash(5)); // wraps like Kotlin Int
        Assert.Equal(-97, (-3).WithHash(-4));
    }

    [Fact]
    public void StateBasedRandomMatchesKotlinFormula()
    {
        // Random(hashOf(caller.hashCode(), seed, stateHash)), default seed 31
        var camp = Randomness.StateBasedRandom("createNewCamp", 31, HexCoord.Of(3, -4).GetHashCode());
        Assert.Equal(1427059229, camp.NextInt());
        Assert.Equal(68, camp.NextInt(100));
        Assert.Equal(0.07902527522541192, camp.NextDouble());

        var quest = Randomness.StateBasedRandom("QuestManager", 31, 0);
        Assert.Equal(1542899963, quest.NextInt());
        Assert.Equal(89, quest.NextInt(100));
        Assert.Equal(0.011285070070891079, quest.NextDouble());

        var city = Randomness.StateBasedRandom("City.Id", 3, 0);
        Assert.Equal(-283134962, city.NextInt());
        Assert.Equal(90, city.NextInt(100));
        Assert.Equal(0.22494225372038956, city.NextDouble());
    }

    [Fact]
    public void PseudoRandomUuidMatchesKotlinAndJavaFormatting()
    {
        var rng = Randomness.StateBasedRandom("City.Id", 3, 0);
        Assert.Equal("ef1fb40e-3ae9-450d-b995-d0d81cded490", Randomness.PseudoRandomUuid(rng).ToString());
        Assert.Equal("62ed969a-c739-4bdd-933b-b29d081f281f", Randomness.PseudoRandomUuid(rng).ToString());
    }

    [Fact]
    public void UuidToStringPadsLikeJava()
    {
        Assert.Equal("00000000-0000-0000-0000-000000000000", new JavaUuid(0, 0).ToString());
        Assert.Equal("00000001-0002-0003-0004-000000000005", new JavaUuid(0x0000000100020003L, 0x0004000000000005L).ToString());
        Assert.Equal("ffffffff-ffff-ffff-ffff-ffffffffffff", new JavaUuid(-1, -1).ToString());
    }
}
