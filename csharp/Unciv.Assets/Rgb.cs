namespace Unciv.Assets;

/// <summary>8-bit colour. All colours of the client originate in <see cref="PlaceholderPalette"/> as these.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>Integer-only HSV conversion (hue 0..359, saturation and value 0..255), so results never depend on float rounding.</summary>
    public static Rgb FromHsv(int hue, int saturation, int value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 255);
        value = Math.Clamp(value, 0, 255);
        var region = hue / 60;
        var remainder = (hue % 60) * 255 / 60;
        var p = value * (255 - saturation) / 255;
        var q = value * (255 - saturation * remainder / 255) / 255;
        var t = value * (255 - saturation * (255 - remainder) / 255) / 255;
        return region switch
        {
            0 => new Rgb((byte)value, (byte)t, (byte)p),
            1 => new Rgb((byte)q, (byte)value, (byte)p),
            2 => new Rgb((byte)p, (byte)value, (byte)t),
            3 => new Rgb((byte)p, (byte)q, (byte)value),
            4 => new Rgb((byte)t, (byte)p, (byte)value),
            _ => new Rgb((byte)value, (byte)p, (byte)q),
        };
    }

    /// <summary>Scales each channel by <paramref name="percent"/>/100, clamped (percent below 100 darkens, above lightens).</summary>
    public Rgb Scale(int percent) => new(
        (byte)Math.Clamp(R * percent / 100, 0, 255),
        (byte)Math.Clamp(G * percent / 100, 0, 255),
        (byte)Math.Clamp(B * percent / 100, 0, 255));
}
