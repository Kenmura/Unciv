using Godot;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C096: what the screen is: a touch build (Android or iOS, where the finger is the pointer) and its density. A desktop touchscreen stays a mouse build (its emulated
/// mouse path works and the UI is not enlarged). <c>OsScale</c> (T311) is the desktop OS display scale, which the automatic UI scale honours. The harness replaces <see cref="Current"/> to run a phone profile on a desktop window.
/// </summary>
public sealed record DisplayProfile(bool Touch, float Dpi, float OsScale = 1f)
{
    private static DisplayProfile? current;

    /// <summary>The profile in effect; detected from the platform on first use.</summary>
    public static DisplayProfile Current
    {
        get => current ??= Detect();
        set => current = value;
    }

    /// <summary>Touch on a mobile platform, with the density the display server reports.</summary>
    public static DisplayProfile Detect()
    {
        bool touch = OS.HasFeature("mobile");
        // T311: the OS display scale of a desktop window (Windows and macOS scaling, Wayland fractional scale); 1 where the display server does not report one
        float os = touch ? 1f : DisplayServer.ScreenGetScale(DisplayServer.WindowGetCurrentScreen());
        return new(touch, DisplayServer.ScreenGetDpi(), os >= 1f ? os : 1f);
    }

    /// <summary>True when the touch rules (density scale, 48 dp targets, finger drag threshold) apply and the density is known.</summary>
    public bool UsesDensity => Touch && Dpi > 0f;
}
