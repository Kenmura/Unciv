using System.Globalization;

namespace Unciv.Models;

// Engine-owned stand-in for com.badlogic.gdx.graphics.Color (the members Unciv logic uses) and
// core/src/com/unciv/models/ImmutableColor.kt. Mutable class with value equality on the packed int bits, like libGDX.
// Stripped: premultiplyAlpha, mul(Color), set(Int)/rgba parsing, 5-arg lerp, hsv helpers (add when a ticket needs them).
public class EngineColor : IEquatable<EngineColor>
{
    public static readonly EngineColor White = new ImmutableColor(new EngineColor(1f, 1f, 1f, 1f));
    public static readonly EngineColor Black = new ImmutableColor(new EngineColor(0f, 0f, 0f, 1f));
    public static readonly EngineColor DarkGray = new ImmutableColor(new EngineColor(0x3f / 255f, 0x3f / 255f, 0x3f / 255f, 1f));
    public static readonly EngineColor Firebrick = new ImmutableColor(new EngineColor(0xb2 / 255f, 0x22 / 255f, 0x22 / 255f, 1f));

    public float R { get; protected set; }

    public float G { get; protected set; }

    public float B { get; protected set; }

    public float A { get; protected set; }

    public EngineColor()
    {
    }

    public EngineColor(float r, float g, float b, float a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
        Clamp();
    }

    public EngineColor(EngineColor color)
        : this(color.R, color.G, color.B, color.A)
    {
    }

    public virtual EngineColor Set(EngineColor color) => SetCore(color.R, color.G, color.B, color.A);

    public virtual EngineColor Set(float r, float g, float b, float a) => SetCore(r, g, b, a);

    public virtual EngineColor Add(EngineColor color) => SetCore(R + color.R, G + color.G, B + color.B, A + color.A);

    public virtual EngineColor Sub(EngineColor color) => SetCore(R - color.R, G - color.G, B - color.B, A - color.A);

    public virtual EngineColor Mul(float value) => SetCore(R * value, G * value, B * value, A * value);

    public virtual EngineColor Lerp(EngineColor target, float t) =>
        SetCore(R + t * (target.R - R), G + t * (target.G - G), B + t * (target.B - B), A + t * (target.A - A));

    /// <summary>Mutable copy (also of an <see cref="ImmutableColor"/>).</summary>
    public EngineColor Cpy() => new(this);

    /// <summary>Packed ABGR, as libGDX <c>Color.toIntBits</c>.</summary>
    public int ToIntBits() => ((int)(255 * A) << 24) | ((int)(255 * B) << 16) | ((int)(255 * G) << 8) | (int)(255 * R);

    public bool Equals(EngineColor? other) => other is not null && ToIntBits() == other.ToIntBits();

    public override bool Equals(object? obj) => Equals(obj as EngineColor);

    public override int GetHashCode() => ToIntBits();

    /// <summary>8 hex digits RRGGBBAA, as libGDX <c>Color.toString</c>.</summary>
    public override string ToString()
    {
        int rgba = ((int)(255 * R) << 24) | ((int)(255 * G) << 16) | ((int)(255 * B) << 8) | (int)(255 * A);
        return ((uint)rgba).ToString("x8", CultureInfo.InvariantCulture);
    }

    protected EngineColor SetCore(float r, float g, float b, float a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
        Clamp();
        return this;
    }

    private void Clamp()
    {
        R = Math.Clamp(R, 0f, 1f);
        G = Math.Clamp(G, 0f, 1f);
        B = Math.Clamp(B, 0f, 1f);
        A = Math.Clamp(A, 0f, 1f);
    }
}

/// <summary>Color that throws <see cref="NotSupportedException"/> on every modifying call.</summary>
public sealed class ImmutableColor(EngineColor color) : EngineColor(color)
{
    private static NotSupportedException Error() =>
        new("ImmutableColor cannot be modified - use .cpy() first to create a mutable copy");

    public override EngineColor Set(EngineColor color) => throw Error();

    public override EngineColor Set(float r, float g, float b, float a) => throw Error();

    public override EngineColor Add(EngineColor color) => throw Error();

    public override EngineColor Sub(EngineColor color) => throw Error();

    public override EngineColor Mul(float value) => throw Error();

    public override EngineColor Lerp(EngineColor target, float t) => throw Error();
}
