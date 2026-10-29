using Godot;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C096: what the screen is: a touch build (Android or iOS, where the finger is the pointer) and its density. A desktop touchscreen stays a mouse build (its emulated
/// mouse path works and the UI is not enlarged). The harness replaces <see cref="Current"/> to run a phone profile on a desktop window.
/// </summary>
public sealed record DisplayProfile(bool Touch, float Dpi)
{
    private static DisplayProfile? current;

    /// <summary>The profile in effect; detected from the platform on first use.</summary>
    public static DisplayProfile Current
    {
        get => current ??= Detect();
        set => current = value;
    }

    /// <summary>Touch on a mobile platform, with the density the display server reports.</summary>
    public static DisplayProfile Detect() => new(OS.HasFeature("mobile"), DisplayServer.ScreenGetDpi());

    /// <summary>True when the touch rules (density scale, 48 dp targets, finger drag threshold) apply and the density is known.</summary>
    public bool UsesDensity => Touch && Dpi > 0f;
}
