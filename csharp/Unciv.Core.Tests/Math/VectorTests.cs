using Unciv.Mathematics;

namespace Unciv.Core.Tests.Math;

public class VectorTests
{
    [Fact]
    public void Vector2_ConstructorsAndCpy()
    {
        var v = new Vector2(1.5f, -2f);
        Assert.Equal(1.5f, v.x);
        Assert.Equal(-2f, v.y);
        var c = v.Cpy();
        Assert.NotSame(v, c);
        Assert.Equal(v, c);
        c.x = 9f;
        Assert.Equal(1.5f, v.x);
        Assert.Equal(new Vector2(0f, 0f), new Vector2());
    }

    [Fact]
    public void Vector2_MutatorsReturnThisAndChain()
    {
        var v = new Vector2(1f, 2f);
        Assert.Same(v, v.Add(1f, 1f));
        Assert.Same(v, v.Add(new Vector2(0.5f, 0.5f)));
        Assert.Equal(new Vector2(2.5f, 3.5f), v);
        Assert.Same(v, v.Sub(0.5f, 0.5f));
        Assert.Same(v, v.Sub(new Vector2(1f, 1f)));
        Assert.Equal(new Vector2(1f, 2f), v);
        Assert.Same(v, v.Scl(3f));
        Assert.Equal(new Vector2(3f, 6f), v);
        v.Scl(new Vector2(2f, 0.5f));
        Assert.Equal(new Vector2(6f, 3f), v);
        v.Set(7f, 8f);
        Assert.Equal(new Vector2(7f, 8f), v);
        v.Set(new Vector2(-1f, -2f));
        Assert.Equal(new Vector2(-1f, -2f), v);
        v.SetZero();
        Assert.True(v.IsZero());
    }

    [Fact]
    public void Vector2_LengthDistanceDot()
    {
        var v = new Vector2(3f, 4f);
        Assert.Equal(5f, v.Len());
        Assert.Equal(25f, v.Len2());
        Assert.Equal(11f, v.Dot(new Vector2(1f, 2f)));
        Assert.Equal(2f, v.Crs(new Vector2(1f, 2f)));
        Assert.Equal(5f, new Vector2(0f, 0f).Dst(v));
        Assert.Equal(25f, new Vector2(0f, 0f).Dst2(v));
    }

    [Fact]
    public void Vector2_Nor()
    {
        var v = new Vector2(3f, 4f).Nor();
        Assert.Equal(0.6f, v.x);
        Assert.Equal(0.8f, v.y);
        var zero = new Vector2().Nor();
        Assert.Equal(new Vector2(), zero);
    }

    [Fact]
    public void Vector2_EqualityIsByFloatBits()
    {
        Assert.Equal(new Vector2(1f, 2f), new Vector2(1f, 2f));
        Assert.NotEqual(new Vector2(1f, 2f), new Vector2(2f, 1f));
        Assert.NotEqual(new Vector2(0f, 0f), new Vector2(-0f, 0f));  // floatToIntBits distinguishes -0
        Assert.Equal(new Vector2(float.NaN, 0f), new Vector2(float.NaN, 0f));
        Assert.False(new Vector2(1f, 2f).Equals(null));
        Assert.False(new Vector2(1f, 2f).Equals("x"));
    }

    [Fact]
    public void Vector2_HashCodeMatchesLibGdx()
    {
        // 31 * (31 * 1 + bits(1f)) + bits(2f); bits(1f) = 0x3f800000, bits(2f) = 0x40000000
        int expected = unchecked(31 * (31 + 0x3f800000) + 0x40000000);
        Assert.Equal(expected, new Vector2(1f, 2f).GetHashCode());
        Assert.Equal(new Vector2(float.NaN, 1f).GetHashCode(), new Vector2(-float.NaN, 1f).GetHashCode());
        var set = new HashSet<Vector2> { new(1f, 2f) };
        Assert.Contains(new Vector2(1f, 2f), set);
    }

    [Fact]
    public void Vector2_ZeroXY_ToString()
    {
        Assert.True(Vector2.Zero.IsZero());
        Assert.Equal(new Vector2(1f, 0f), Vector2.X);
        Assert.Equal(new Vector2(0f, 1f), Vector2.Y);
        Assert.Equal("(1,2.5)", new Vector2(1f, 2.5f).ToString());
    }

    [Fact]
    public void Vector2_UsesSinglePrecision()
    {
        // 16777216f + 1f == 16777216f in float arithmetic (would differ in double)
        var v = new Vector2(16777216f, 0f).Add(1f, 0f);
        Assert.Equal(16777216f, v.x);
        // 0.1f * 3 in float is 0.3f (rounded to float), not 0.30000000000000004
        Assert.Equal(0.3f, new Vector2(0.1f, 0f).Scl(3f).x);
    }

    [Fact]
    public void Vector3_BasicsAndMutators()
    {
        var v = new Vector3(1f, 2f, 3f);
        Assert.Equal(new Vector3(1f, 2f, 3f), v.Cpy());
        Assert.NotSame(v, v.Cpy());
        Assert.Same(v, v.Add(1f, 1f, 1f));
        v.Add(new Vector3(1f, 1f, 1f));
        Assert.Equal(new Vector3(3f, 4f, 5f), v);
        v.Sub(1f, 1f, 1f).Sub(new Vector3(1f, 1f, 1f));
        Assert.Equal(new Vector3(1f, 2f, 3f), v);
        v.Scl(2f);
        Assert.Equal(new Vector3(2f, 4f, 6f), v);
        v.Scl(new Vector3(0.5f, 0.5f, 0.5f));
        Assert.Equal(new Vector3(1f, 2f, 3f), v);
        v.Set(4f, 5f, 6f);
        Assert.Equal(new Vector3(4f, 5f, 6f), v);
        v.Set(new Vector3(7f, 8f, 9f));
        Assert.Equal(new Vector3(7f, 8f, 9f), v);
        v.SetZero();
        Assert.True(v.IsZero());
    }

    [Fact]
    public void Vector3_MathAndNor()
    {
        var v = new Vector3(2f, 3f, 6f);
        Assert.Equal(7f, v.Len());
        Assert.Equal(49f, v.Len2());
        Assert.Equal(7f, new Vector3().Dst(v));
        Assert.Equal(49f, new Vector3().Dst2(v));
        Assert.Equal(2f + 6f + 18f, v.Dot(new Vector3(1f, 2f, 3f)));
        Assert.Equal(new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f).Crs(new Vector3(0f, 1f, 0f)));
        Assert.Equal(new Vector3(), new Vector3().Nor());
        Assert.Equal(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 0f).Nor());
        var n = new Vector3(0f, 0f, 5f).Nor();
        Assert.Equal(new Vector3(0f, 0f, 1f), n);
    }

    [Fact]
    public void Vector3_EqualityHashAndToString()
    {
        Assert.Equal(new Vector3(1f, 2f, 3f), new Vector3(1f, 2f, 3f));
        Assert.NotEqual(new Vector3(1f, 2f, 3f), new Vector3(1f, 2f, 4f));
        Assert.NotEqual(new Vector3(0f, 0f, 0f), new Vector3(0f, -0f, 0f));
        int expected = unchecked(31 * (31 * (31 + 0x3f800000) + 0x40000000) + 0x40400000);
        Assert.Equal(expected, new Vector3(1f, 2f, 3f).GetHashCode());
        Assert.Equal("(1,2,3.5)", new Vector3(1f, 2f, 3.5f).ToString());
        Assert.Equal(new Vector3(0f, 0f, 1f), Vector3.Z);
    }
}
