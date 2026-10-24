using Unciv.Utils;

namespace Unciv.Logic.Map.PathingMap;

// Ported from core/src/com/unciv/logic/map/pathingmap/FixedPointMovement.kt (T067). Stripped: the @Pure annotations.
// Kotlin Int arithmetic wraps (the build is unchecked); Float -> Int conversions go through KotlinMath.FloatToInt (saturating, NaN is 0).

/// <summary>
/// Fixed-point movement value (base 30) for pathfinding, avoiding heap allocations.
/// 1.0f movement = 30 internal units. The <see cref="Bits"/> must fit within [0, 16384) for multi-turn movement costs
/// or [0, 512) for single-turn costs when packed into routing nodes (T093).
/// Non-integer Float -> FixedPointMovement rounds to nearest 1/30th via HALF_UP.
/// </summary>
public readonly struct FixedPointMovement : IComparable<FixedPointMovement>, IEquatable<FixedPointMovement>
{
    private const int MoveSpeedBase = 30;

    public static readonly FixedPointMovement FpmZero = new(0);
    public static readonly FixedPointMovement FpmPointFive = new(MoveSpeedBase / 2);
    public static readonly FixedPointMovement FpmOne = new(MoveSpeedBase);

    /// <summary>The internal fixed-point representation (0 to 16384 exclusive).</summary>
    public int Bits { get; }

    private FixedPointMovement(int bits) => Bits = bits;

    public static FixedPointMovement FpmFromFixedPointBits(int bits) => new(bits);

    public static FixedPointMovement FpmFromMovement(int move) => new(unchecked(move * MoveSpeedBase));

    /// <summary>Rounding HALF_UP.</summary>
    public static FixedPointMovement FpmFromMovement(float move)
    {
        var plusOneBit = KotlinMath.FloatToInt(move * (MoveSpeedBase * 2));
        return new FixedPointMovement((plusOneBit >> 1) + (plusOneBit & 1));
    }

    public static FixedPointMovement operator +(FixedPointMovement a, FixedPointMovement other) => new(unchecked(a.Bits + other.Bits));
    public static FixedPointMovement operator +(FixedPointMovement a, int value) => new(unchecked(a.Bits + value * MoveSpeedBase));
    public static FixedPointMovement operator +(FixedPointMovement a, float value) => new(unchecked(a.Bits + FpmFromMovement(value).Bits));
    public static FixedPointMovement operator -(FixedPointMovement a, FixedPointMovement other) => new(unchecked(a.Bits - other.Bits));
    public static FixedPointMovement operator -(FixedPointMovement a, int value) => new(unchecked(a.Bits - value * MoveSpeedBase));
    public static FixedPointMovement operator -(FixedPointMovement a, float value) => new(unchecked(a.Bits - FpmFromMovement(value).Bits));
    public static FixedPointMovement operator *(FixedPointMovement a, FixedPointMovement other) => new(unchecked((int)((long)a.Bits * other.Bits / MoveSpeedBase)));
    public static FixedPointMovement operator *(FixedPointMovement a, int multiplier) => new(unchecked(a.Bits * multiplier));
    public static FixedPointMovement operator *(FixedPointMovement a, float multiplier) => FpmFromMovement(a.Bits * multiplier);
    public static FixedPointMovement operator /(FixedPointMovement a, FixedPointMovement other) => new(unchecked((int)((long)a.Bits * MoveSpeedBase / other.Bits)));
    public static FixedPointMovement operator /(FixedPointMovement a, int divisor) => new(a.Bits / divisor);
    public static FixedPointMovement operator /(FixedPointMovement a, float divisor) => FpmFromMovement(a.Bits / divisor);

    public int CompareTo(FixedPointMovement other) => Bits.CompareTo(other.Bits);
    public int CompareTo(int other) => Bits.CompareTo(unchecked(other * MoveSpeedBase));
    public int CompareTo(float other) => CompareTo(FpmFromMovement(other));

    public static bool operator <(FixedPointMovement a, FixedPointMovement b) => a.CompareTo(b) < 0;
    public static bool operator >(FixedPointMovement a, FixedPointMovement b) => a.CompareTo(b) > 0;
    public static bool operator <=(FixedPointMovement a, FixedPointMovement b) => a.CompareTo(b) <= 0;
    public static bool operator >=(FixedPointMovement a, FixedPointMovement b) => a.CompareTo(b) >= 0;
    public static bool operator <(FixedPointMovement a, int b) => a.CompareTo(b) < 0;
    public static bool operator >(FixedPointMovement a, int b) => a.CompareTo(b) > 0;
    public static bool operator <=(FixedPointMovement a, int b) => a.CompareTo(b) <= 0;
    public static bool operator >=(FixedPointMovement a, int b) => a.CompareTo(b) >= 0;
    public static bool operator <(FixedPointMovement a, float b) => a.CompareTo(b) < 0;
    public static bool operator >(FixedPointMovement a, float b) => a.CompareTo(b) > 0;
    public static bool operator <=(FixedPointMovement a, float b) => a.CompareTo(b) <= 0;
    public static bool operator >=(FixedPointMovement a, float b) => a.CompareTo(b) >= 0;

    public static bool operator ==(FixedPointMovement a, FixedPointMovement b) => a.Bits == b.Bits;
    public static bool operator !=(FixedPointMovement a, FixedPointMovement b) => a.Bits != b.Bits;
    public bool Equals(FixedPointMovement other) => Bits == other.Bits;
    public override bool Equals(object? obj) => obj is FixedPointMovement other && Equals(other);
    public override int GetHashCode() => Bits;

    public float ToFloat() => Bits / 30f;

    public FixedPointMovement CoerceAtMost(FixedPointMovement max) => new(Math.Min(Bits, max.Bits));
    public FixedPointMovement CoerceAtLeast(FixedPointMovement min) => new(Math.Max(Bits, min.Bits));

    /// <exception cref="ArgumentException">Kotlin <c>coerceIn</c>: when <paramref name="min"/> is greater than <paramref name="max"/>.</exception>
    public FixedPointMovement CoerceIn(FixedPointMovement min, FixedPointMovement max)
    {
        if (min.Bits > max.Bits) throw new ArgumentException($"Cannot coerce value to an empty range: maximum {max.Bits} is less than minimum {min.Bits}.");
        return new FixedPointMovement(Bits < min.Bits ? min.Bits : Bits > max.Bits ? max.Bits : Bits);
    }

    public override string ToString() => FormattableString.Invariant($"FPM{{{Bits}/30={ToFloat()}}}");
}
