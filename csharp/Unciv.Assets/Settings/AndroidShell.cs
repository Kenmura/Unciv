namespace Unciv.GodotClient.Settings;

/// <summary>C099: the Godot-free decisions of the Android shell: what the back button does, the insets the safe area and the virtual keyboard leave, and which options a platform shows.</summary>
public static class AndroidShell
{
    /// <summary>What a system back request does: close a popup or pop a screen when there is one, otherwise leave the app (the main menu is the bottom screen).</summary>
    public static BackOutcome BackOutcome(bool handledByScreens) => handledByScreens ? Settings.BackOutcome.Handled : Settings.BackOutcome.Quit;

    /// <summary>
    /// The margins (left, top, right, bottom, in pixels) that keep content inside the safe area (x, y, width, height) of a window <paramref name="windowWidth"/> by
    /// <paramref name="windowHeight"/>, with the bottom raised by the virtual keyboard height. A safe area that is empty or outside the window (not reported) gives no inset.
    /// </summary>
    public static Insets ComputeInsets(int windowWidth, int windowHeight, int safeX, int safeY, int safeWidth, int safeHeight, int keyboardHeight)
    {
        int keyboard = Math.Clamp(keyboardHeight, 0, Math.Max(0, windowHeight));
        if (safeWidth <= 0 || safeHeight <= 0 || windowWidth <= 0 || windowHeight <= 0) return new Insets(0, 0, 0, keyboard);
        int left = Math.Clamp(safeX, 0, windowWidth);
        int top = Math.Clamp(safeY, 0, windowHeight);
        int right = Math.Clamp(windowWidth - (safeX + safeWidth), 0, windowWidth);
        int bottom = Math.Clamp(windowHeight - (safeY + safeHeight), 0, windowHeight);
        return new Insets(left, top, right, Math.Max(bottom, keyboard));
    }

    /// <summary>Hosting a game needs a listening socket and a desktop-style window; a touch build hides it and the window mode picker.</summary>
    public static bool ShowsHostGame(bool touch) => !touch;

    public static bool ShowsWindowMode(bool touch) => !touch;
}

public enum BackOutcome
{
    Handled,
    Quit,
}

/// <summary>Pixel margins of the safe area.</summary>
public readonly record struct Insets(int Left, int Top, int Right, int Bottom)
{
    public bool IsZero => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}
