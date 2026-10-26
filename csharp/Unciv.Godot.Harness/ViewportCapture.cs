using Godot;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>Turns what a viewport rendered into files and engine-neutral images.</summary>
public static class ViewportCapture
{
    /// <summary>Reads the viewport's last rendered frame and returns it as RGBA8 (call after at least one frame has been drawn).</summary>
    public static RgbaImage Capture(Viewport viewport)
    {
        using var image = viewport.GetTexture().GetImage();
        return ToRgba(image);
    }

    /// <summary>Captures the viewport, writes it to <paramref name="path"/> as PNG and returns the pixels.</summary>
    public static RgbaImage CaptureViewportPng(Viewport viewport, string path)
    {
        using var image = viewport.GetTexture().GetImage()
            ?? throw new IOException($"The viewport ({viewport.GetVisibleRect().Size}) rendered no image for {path}: it may exceed the GPU texture limit");
        var error = image.SavePng(path);
        if (error != Error.Ok) throw new IOException($"Could not write {path}: {error}");
        return ToRgba(image);
    }

    public static RgbaImage LoadPng(string path)
    {
        using var image = Image.LoadFromFile(path);
        if (image == null) throw new IOException($"Could not read {path}");
        return ToRgba(image);
    }

    private static RgbaImage ToRgba(Image source)
    {
        if (source.IsCompressed()) source.Decompress();
        if (source.GetFormat() != Image.Format.Rgba8) source.Convert(Image.Format.Rgba8);
        return new RgbaImage(source.GetWidth(), source.GetHeight(), source.GetData());
    }
}
