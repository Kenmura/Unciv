namespace Unciv.GodotClient.Settings;

/// <summary>C028: the Godot-free parts of the options screen: value ranges, the sound level curve, the pack list and the edge-pan / scroll maths.</summary>
public static class OptionsModel
{
    public const float MinScrollSpeed = 0.25f;
    public const float MaxScrollSpeed = 4f;
    public const float UiScaleStep = 0.25f;
    public const float MinUiScale = 0.5f;
    public const float MaxUiScale = 4f;
    public const string DefaultPack = "Default";

    /// <summary>Edge-pan trigger band in pixels, and the map speed at full scroll speed in zoom-sizes per second.</summary>
    public const float EdgeMarginPixels = 16f;
    public const float PanPerSecond = 1.2f;

    /// <summary>The window modes, as stored. The first is the default.</summary>
    public static readonly IReadOnlyList<string> WindowModes = ["Windowed", "Fullscreen", "Borderless"];

    /// <summary>The audio bus level for a 0..1 volume: silent at 0, otherwise 20*log10 of the volume (so 1 is 0 dB).</summary>
    public static float VolumeDb(float volume) =>
        !(volume > 0f) ? -80f : (float)Math.Max(-80.0, 20.0 * Math.Log10(Math.Min(1f, volume)));

    /// <summary>The next UI scale after pressing bigger (+1) or smaller (-1), kept inside the allowed range.</summary>
    public static float StepUiScale(float current, int direction) =>
        Math.Clamp(current + direction * UiScaleStep, MinUiScale, MaxUiScale);

    /// <summary>The pack names to offer: the default (no pack) first, then every directory name that is a plain name (the always-on bundled icon pack excepted), sorted ordinally.</summary>
    public static IReadOnlyList<string> PackNames(IEnumerable<string> directoryNames) =>
        [DefaultPack, .. directoryNames.Where(IsPlainName).Where(n => n != DefaultPack && n != Unciv.Assets.AssetResolver.BundledPack).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>The pack handed to the asset registry: null for the default.</summary>
    public static string? RegistryPack(string setting) => setting == DefaultPack || string.IsNullOrWhiteSpace(setting) ? null : setting;

    private static bool IsPlainName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name != "." && name != ".." && name.IndexOfAny(['/', '\\']) < 0;

    /// <summary>
    /// The map direction the mouse asks for when it rests near a window edge: x -1 (left) to 1 (right), y -1 (up) to 1 (down), ramping linearly from 0 at
    /// the edge of the band to 1 at the window border. Zero when disabled, when the window is degenerate, or when the mouse is outside the window.
    /// </summary>
    public static (float X, float Y) EdgePanDirection(bool enabled, float mouseX, float mouseY, float width, float height)
    {
        if (!enabled || !(width > 0f) || !(height > 0f)) return (0f, 0f);
        if (mouseX < 0f || mouseY < 0f || mouseX > width || mouseY > height) return (0f, 0f);
        return (Ramp(mouseX, width), Ramp(mouseY, height));

        static float Ramp(float position, float extent)
        {
            float band = Math.Min(EdgeMarginPixels, extent / 2f);
            if (position < band) return -(band - position) / band;
            if (position > extent - band) return (position - (extent - band)) / band;
            return 0f;
        }
    }

    /// <summary>World distance the map moves in <paramref name="seconds"/> at the given scroll speed and zoom size.</summary>
    public static float PanDistance(float scrollSpeed, float zoom, double seconds) =>
        (float)(PanPerSecond * Math.Clamp(scrollSpeed, MinScrollSpeed, MaxScrollSpeed) * zoom * seconds);
}
