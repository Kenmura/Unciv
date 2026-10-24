namespace Unciv.Harness;

/// <summary>A decoded image: tightly packed 8-bit RGBA, row-major, top row first. Independent of Godot so the comparison code is testable anywhere.</summary>
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public RgbaImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Image must be at least 1x1");
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException($"Expected {width * height * 4} bytes for {width}x{height} RGBA, got {pixels.Length}", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public static RgbaImage Solid(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = a;
        }
        return new RgbaImage(width, height, pixels);
    }

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    public void SetPixel(int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        int i = (y * Width + x) * 4;
        Pixels[i] = r;
        Pixels[i + 1] = g;
        Pixels[i + 2] = b;
        Pixels[i + 3] = a;
    }
}
