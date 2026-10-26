namespace Unciv.GodotClient.Settings;

/// <summary>The rebindable keyboard actions of the game screen (C028).</summary>
public enum HotkeyAction
{
    EndTurn,
    NextUnit,
    OpenTech,
    OpenPolicies,
    OpenDiplomacy,
    PanUp,
    PanDown,
    PanLeft,
    PanRight,

    /// <summary>C046: opens the developer console (Kotlin <c>KeyboardBinding.DeveloperConsole</c>, the backtick).</summary>
    OpenConsole,

    /// <summary>C055: opens (or closes) the Civilopedia (Kotlin <c>KeyboardBinding.Civilopedia</c>, F1).</summary>
    OpenPedia,
}

/// <summary>
/// C028: key bindings as text ("N", "Space", "Ctrl+Z"), kept Godot-free so the rules are testable. The settings store only the bindings that differ from
/// <see cref="Defaults"/>. A key text is canonical when <see cref="Normalize"/> returns it unchanged. One key belongs to one action: rebinding to a key
/// another action holds swaps the two. The arrow keys always pan the map in addition to the bound pan keys (they are not rebindable).
/// </summary>
public static class Hotkeys
{
    private static readonly string[] ModifierOrder = ["Ctrl", "Alt", "Shift"];

    /// <summary>The default key of every action.</summary>
    public static readonly IReadOnlyDictionary<HotkeyAction, string> Defaults = new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.EndTurn] = "Space",
        [HotkeyAction.NextUnit] = "N",
        [HotkeyAction.OpenTech] = "T",
        [HotkeyAction.OpenPolicies] = "P",
        [HotkeyAction.OpenDiplomacy] = "F",
        [HotkeyAction.PanUp] = "W",
        [HotkeyAction.PanDown] = "S",
        [HotkeyAction.PanLeft] = "A",
        [HotkeyAction.PanRight] = "D",
        [HotkeyAction.OpenConsole] = "Quoteleft",
        [HotkeyAction.OpenPedia] = "F1",
    };

    /// <summary>Keys that can never be bound (they close screens or are the always-on arrow pan keys).</summary>
    private static readonly HashSet<string> Reserved = new(["Escape", "Left", "Right", "Up", "Down"], StringComparer.Ordinal);

    /// <summary>The canonical text of a key, or null when it is empty, only a modifier, reserved or malformed. Key names compare case-insensitively; a single letter is upper-cased.</summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        // "Ctrl++" style (the plus key itself) is not supported: an empty part makes the text invalid.
        if (parts.Any(string.IsNullOrEmpty)) return null;
        var key = parts[^1];
        if (ModifierOrder.Any(m => string.Equals(m, key, StringComparison.OrdinalIgnoreCase))) return null;
        var modifiers = new List<string>();
        foreach (var part in parts[..^1])
        {
            var modifier = ModifierOrder.FirstOrDefault(m => string.Equals(m, part, StringComparison.OrdinalIgnoreCase));
            if (modifier == null || modifiers.Contains(modifier)) return null;
            modifiers.Add(modifier);
        }
        if (!key.All(c => char.IsLetterOrDigit(c) || c == '_')) return null;
        key = key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key[1..].ToLowerInvariant();
        if (Reserved.Contains(key)) return null;
        return string.Join('+', ModifierOrder.Where(modifiers.Contains).Append(key));
    }

    /// <summary>The binding map made safe: unknown actions, unusable keys and keys equal to the default are dropped. If two actions would end up on one key the whole map is reset to the defaults (a hand-edited file cannot create ambiguity).</summary>
    public static SortedDictionary<string, string> Sanitize(IDictionary<string, string>? raw)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (raw == null) return result;
        var effective = new Dictionary<HotkeyAction, string>(Defaults);
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            if (raw.TryGetValue(action.ToString(), out var value) && Normalize(value) is { } key) effective[action] = key;
        }
        if (effective.Values.Distinct(StringComparer.Ordinal).Count() != effective.Count) return result;
        return FromEffective(effective);
    }

    /// <summary>The key currently bound to <paramref name="action"/>.</summary>
    public static string KeyFor(IReadOnlyDictionary<string, string> bindings, HotkeyAction action) =>
        bindings.TryGetValue(action.ToString(), out var text) && Normalize(text) is { } key ? key : Defaults[action];

    /// <summary>The action a key press triggers (canonical or not), or null.</summary>
    public static HotkeyAction? Resolve(IReadOnlyDictionary<string, string> bindings, string? keyText)
    {
        if (Normalize(keyText) is not { } key) return null;
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            if (KeyFor(bindings, action) == key) return action;
        }
        return null;
    }

    /// <summary>
    /// The bindings after binding <paramref name="action"/> to <paramref name="keyText"/>, or null when the key is not usable. When another action holds
    /// the key, that action takes this action's old key (a swap), so no key ever has two actions. <paramref name="swapped"/> names the other action.
    /// </summary>
    public static SortedDictionary<string, string>? Rebind(IReadOnlyDictionary<string, string> bindings, HotkeyAction action, string? keyText, out HotkeyAction? swapped)
    {
        swapped = null;
        if (Normalize(keyText) is not { } key) return null;
        var effective = Enum.GetValues<HotkeyAction>().ToDictionary(a => a, a => KeyFor(bindings, a));
        var previous = effective[action];
        foreach (var other in Enum.GetValues<HotkeyAction>())
        {
            if (other != action && effective[other] == key)
            {
                effective[other] = previous;
                swapped = other;
            }
        }
        effective[action] = key;
        return FromEffective(effective);
    }

    /// <summary>The bindings after resetting every action to its default key.</summary>
    public static SortedDictionary<string, string> ResetAll() => [];

    private static SortedDictionary<string, string> FromEffective(Dictionary<HotkeyAction, string> effective)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (action, key) in effective)
        {
            if (key != Defaults[action]) result[action.ToString()] = key;
        }
        return result;
    }
}
