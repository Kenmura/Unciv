using System.Globalization;

namespace Unciv.Mathematics;

// Port of the members of libGDX com.badlogic.gdx.math.Vector2 used by Unciv logic code.
//
// Deliberately a mutable *class* (as in libGDX) with equality by value, compared on the raw float bits
// (Java's NumberUtils.floatToIntBits): 0f != -0f, NaN == NaN. Namespace is not `Unciv.Math` because that
// would shadow System.Math for every namespace under Unciv.
public class Vector2 : IEquatable<Vector2>
{
    public static readonly Vector2 X = new(1f, 0f);
    public static readonly Vector2 Y = new(0f, 1f);
    public static readonly Vector2 Zero = new(0f, 0f);

    public float x;
    public float y;

    public Vector2() { }

    public Vector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }

    public Vector2(Vector2 v)
    {
        x = v.x;
        y = v.y;
    }

    public Vector2 Cpy() => new(this);

    public Vector2 Set(float x, float y)
    {
        this.x = x;
        this.y = y;
        return this;
    }

    public Vector2 Set(Vector2 v) => Set(v.x, v.y);

    public Vector2 SetZero()
    {
        x = 0f;
        y = 0f;
        return this;
    }

    public Vector2 Add(Vector2 v) => Add(v.x, v.y);

    public Vector2 Add(float x, float y)
    {
        this.x += x;
        this.y += y;
        return this;
    }

    public Vector2 Sub(Vector2 v) => Sub(v.x, v.y);

    public Vector2 Sub(float x, float y)
    {
        this.x -= x;
        this.y -= y;
        return this;
    }

    public Vector2 Scl(float scalar)
    {
        x *= scalar;
        y *= scalar;
        return this;
    }

    public Vector2 Scl(Vector2 v) => Scl(v.x, v.y);

    public Vector2 Scl(float x, float y)
    {
        this.x *= x;
        this.y *= y;
        return this;
    }

    public float Len() => (float)Math.Sqrt(x * x + y * y);

    public float Len2() => x * x + y * y;

    public Vector2 Nor()
    {
        float len = Len();
        if (len != 0f)
        {
            x /= len;
            y /= len;
        }
        return this;
    }

    public float Dot(Vector2 v) => x * v.x + y * v.y;

    public float Dot(float ox, float oy) => x * ox + y * oy;

    public float Crs(Vector2 v) => x * v.y - y * v.x;

    public float Dst(Vector2 v)
    {
        float xd = v.x - x;
        float yd = v.y - y;
        return (float)Math.Sqrt(xd * xd + yd * yd);
    }

    public float Dst2(Vector2 v)
    {
        float xd = v.x - x;
        float yd = v.y - y;
        return xd * xd + yd * yd;
    }

    public bool IsZero() => x == 0f && y == 0f;

    public bool Equals(Vector2? other) =>
        other != null && FloatToIntBits(x) == FloatToIntBits(other.x) && FloatToIntBits(y) == FloatToIntBits(other.y);

    public override bool Equals(object? obj) => obj is Vector2 v && Equals(v);

    // libGDX: prime = 31; result = 1; result = prime * result + floatToIntBits(x); ... y
    public override int GetHashCode()
    {
        unchecked
        {
            int result = 1;
            result = 31 * result + FloatToIntBits(x);
            result = 31 * result + FloatToIntBits(y);
            return result;
        }
    }

    // Java's floatToIntBits canonicalizes every NaN to 0x7fc00000
    private static int FloatToIntBits(float f) => float.IsNaN(f) ? 0x7fc00000 : BitConverter.SingleToInt32Bits(f);

    // libGDX gives "(x,y)" using Java float formatting; here invariant-culture shortest round-trip (e.g. "1" not "1.0").
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({x},{y})");
}
