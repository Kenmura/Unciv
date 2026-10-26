using Godot;

namespace Unciv.GodotClient.Settings;

/// <summary>C028: the bridge between Godot key events and the Godot-free key texts of <see cref="Hotkeys"/>.</summary>
public static class HotkeyInput
{
    /// <summary>The canonical key text of a key event ("N", "Ctrl+Shift+Z"), or null for a modifier-only press or a key that cannot be bound.</summary>
    public static string? TextOf(InputEventKey key)
    {
        var code = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (code is Key.None or Key.Ctrl or Key.Shift or Key.Alt or Key.Meta) return null;
        var name = OS.GetKeycodeString(code);
        var modifiers = (key.CtrlPressed ? "Ctrl+" : "") + (key.AltPressed ? "Alt+" : "") + (key.ShiftPressed ? "Shift+" : "");
        return Hotkeys.Normalize(modifiers + name);
    }

    /// <summary>The key (without modifiers) a key text names, or null.</summary>
    public static Key? KeyOf(string keyText)
    {
        var name = keyText[(keyText.LastIndexOf('+') + 1)..];
        var code = OS.FindKeycodeFromString(name);
        return code == Key.None ? null : code;
    }

    /// <summary>Whether the key of <paramref name="keyText"/> is held right now (modifiers are ignored: this is for held keys such as map panning).</summary>
    public static bool IsHeld(string keyText) => KeyOf(keyText) is { } code && Input.IsKeyPressed(code);
}
