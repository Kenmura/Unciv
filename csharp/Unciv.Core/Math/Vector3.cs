using System.Globalization;

namespace Unciv.Mathematics;

// Port of the members of libGDX com.badlogic.gdx.math.Vector3 used by Unciv logic code.
// Mutable class with value equality on raw float bits, as in libGDX (see Vector2).
public class Vector3 : IEquatable<Vector3>
{
    public static readonly Vector3 X = new(1f, 0f, 0f);
    public static readonly Vector3 Y = new(0f, 1f, 0f);
    public static readonly Vector3 Z = new(0f, 0f, 1f);
    public static readonly Vector3 Zero = new(0f, 0f, 0f);

    public float x;
    public float y;
    public float z;

    public Vector3() { }

    public Vector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public Vector3(Vector3 v)
    {
        x = v.x;
        y = v.y;
        z = v.z;
    }

    public Vector3 Cpy() => new(this);

    public Vector3 Set(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        return this;
    }

    public Vector3 Set(Vector3 v) => Set(v.x, v.y, v.z);

    public Vector3 SetZero()
    {
        x = 0f;
        y = 0f;
        z = 0f;
        return this;
    }

    public Vector3 Add(Vector3 v) => Add(v.x, v.y, v.z);

    public Vector3 Add(float x, float y, float z) => Set(this.x + x, this.y + y, this.z + z);

    public Vector3 Sub(Vector3 v) => Sub(v.x, v.y, v.z);

    public Vector3 Sub(float x, float y, float z) => Set(this.x - x, this.y - y, this.z - z);

    public Vector3 Scl(float scalar) => Set(x * scalar, y * scalar, z * scalar);

    public Vector3 Scl(Vector3 v) => Set(x * v.x, y * v.y, z * v.z);

    public float Len() => (float)Math.Sqrt(x * x + y * y + z * z);

    public float Len2() => x * x + y * y + z * z;

    public Vector3 Nor()
    {
        float len2 = Len2();
        if (len2 == 0f || len2 == 1f) return this;
        return Scl(1f / (float)Math.Sqrt(len2));
    }

    public float Dot(Vector3 v) => x * v.x + y * v.y + z * v.z;

    public Vector3 Crs(Vector3 v) => Set(y * v.z - z * v.y, z * v.x - x * v.z, x * v.y - y * v.x);

    public float Dst(Vector3 v)
    {
        float a = v.x - x;
        float b = v.y - y;
        float c = v.z - z;
        return (float)Math.Sqrt(a * a + b * b + c * c);
    }

    public float Dst2(Vector3 v)
    {
        float a = v.x - x;
        float b = v.y - y;
        float c = v.z - z;
        return a * a + b * b + c * c;
    }

    public bool IsZero() => x == 0f && y == 0f && z == 0f;

    public bool Equals(Vector3? other) =>
        other != null && FloatToIntBits(x) == FloatToIntBits(other.x)
                      && FloatToIntBits(y) == FloatToIntBits(other.y)
                      && FloatToIntBits(z) == FloatToIntBits(other.z);

    public override bool Equals(object? obj) => obj is Vector3 v && Equals(v);

    public override int GetHashCode()
    {
        unchecked
        {
            int result = 1;
            result = 31 * result + FloatToIntBits(x);
            result = 31 * result + FloatToIntBits(y);
            result = 31 * result + FloatToIntBits(z);
            return result;
        }
    }

    // Java's floatToIntBits canonicalizes every NaN to 0x7fc00000
    private static int FloatToIntBits(float f) => float.IsNaN(f) ? 0x7fc00000 : BitConverter.SingleToInt32Bits(f);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({x},{y},{z})");
}
