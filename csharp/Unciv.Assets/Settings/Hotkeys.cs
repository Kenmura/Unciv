using Unciv.Assets;

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

    /// <summary>C078: the Kotlin <c>NextTurnAlternate</c> (Space); <see cref="EndTurn"/> now holds the Kotlin <c>NextTurn</c> default, N.</summary>
    EndTurnAlternate,

    /// <summary>C078: opens the empire overview on its last tab (Kotlin <c>EmpireOverview</c>, E).</summary>
    OpenOverview,

    /// <summary>C078: the overview's Cities tab (Kotlin <c>EmpireOverviewTrades</c>, F2, the economic info).</summary>
    OverviewCities,

    /// <summary>C078: the overview's Units tab (Kotlin <c>EmpireOverviewUnits</c>, F3).</summary>
    OverviewUnits,

    /// <summary>C078: the overview's Politics tab (Kotlin <c>EmpireOverviewPolitics</c>, F4).</summary>
    OverviewPolitics,

    /// <summary>C078: the overview's Stats tab (Kotlin <c>EmpireOverviewStats</c>, F9).</summary>
    OverviewStats,

    /// <summary>C078: the overview's Resources tab (Kotlin <c>EmpireOverviewResources</c>, F10).</summary>
    OverviewResources,

    /// <summary>C078: the victory status page (Kotlin <c>VictoryScreen</c>, F8).</summary>
    OpenVictory,

    /// <summary>C078: the Save entry of the game menu (Kotlin <c>SaveGame</c>, Ctrl+S).</summary>
    SaveGame,

    /// <summary>C078: the Load entry of the game menu (Kotlin <c>LoadGame</c>, Ctrl+L).</summary>
    LoadGame,

    /// <summary>C078: the Options entry of the game menu (Kotlin <c>Options</c>, Ctrl+O).</summary>
    OpenOptions,

    /// <summary>C078: centres the camera on the capital (Kotlin <c>ViewCapitalCity</c>, Home).</summary>
    ViewCapital,

    /// <summary>C078: toggles the resource icons on the map (Kotlin <c>ToggleResourceDisplay</c>, Ctrl+R).</summary>
    ToggleResources,

    /// <summary>C078: toggles the yield icons on the map (Kotlin <c>ToggleYieldDisplay</c>, Ctrl+Y).</summary>
    ToggleYields,

    /// <summary>C089: opens the map marker editor on the tile under the pointer (the marker list when the pointer is off the map). Unciv has no such key; Ctrl+M because plain M is Automate.</summary>
    PlaceMarker,

    /// <summary>C090: pings the tile under the pointer for the allies of a hosted game (Look here). Unciv has no such key.</summary>
    PingMap,

    // C078: the unit actions. Each is named like the engine's UnitActionType (so a menu entry finds its key by name); DisbandUnit is the panel's own Disband entry.
    // Their keys are the Kotlin KeyboardBinding.UnitActions defaults. Several actions share a key (they are never on offer together, as in Kotlin).
    Automate,
    StopAutomation,
    StopMovement,
    Sleep,
    SleepUntilHealed,
    Fortify,
    FortifyUntilHealed,
    Explore,
    StopExploration,
    Upgrade,
    Transform,
    Pillage,
    Paradrop,
    AirSweep,
    SetUp,
    FoundCity,
    Repair,
    CreateImprovement,
    HurryResearch,
    HurryWonder,
    HurryBuilding,
    ConductTradeMission,
    FoundReligion,
    TriggerUnique,
    SpreadReligion,
    RemoveHeresy,
    EnhanceReligion,
    Skip,
    AddInCapital,
    DisbandUnit,
}

/// <summary>
/// C028: key bindings as text ("N", "Space", "Ctrl+Z"), kept Godot-free so the rules are testable. The settings store only the bindings that differ from
/// <see cref="Defaults"/>. A key text is canonical when <see cref="Normalize"/> returns it unchanged. One key belongs to one action: rebinding to a key
/// another action holds swaps the two (C078: that holds among the game screen's actions; the unit actions are a second scope, see <see cref="IsUnitAction"/>). The arrow keys always pan the map in addition to the bound pan keys (they are not rebindable).
/// </summary>
public static class Hotkeys
{
    private static readonly string[] ModifierOrder = ["Ctrl", "Alt", "Shift"];

    /// <summary>The default key of every action.</summary>
    public static readonly IReadOnlyDictionary<HotkeyAction, string> Defaults = new Dictionary<HotkeyAction, string>
    {
        // C078, deliberate deviation from the earlier Space/N/T/P/F defaults (owner decision: the Kotlin defaults). Where Kotlin has no key for an action it is F4/Ctrl+D here.
        [HotkeyAction.EndTurn] = "N",
        [HotkeyAction.EndTurnAlternate] = "Space",
        [HotkeyAction.NextUnit] = "Period",
        [HotkeyAction.OpenTech] = "F6",
        [HotkeyAction.OpenPolicies] = "F5",
        [HotkeyAction.OpenDiplomacy] = "Ctrl+D",
        [HotkeyAction.OpenOverview] = "E",
        [HotkeyAction.OverviewCities] = "F2",
        [HotkeyAction.OverviewUnits] = "F3",
        [HotkeyAction.OverviewPolitics] = "F4",
        [HotkeyAction.OverviewStats] = "F9",
        [HotkeyAction.OverviewResources] = "F10",
        [HotkeyAction.OpenVictory] = "F8",
        [HotkeyAction.SaveGame] = "Ctrl+S",
        [HotkeyAction.LoadGame] = "Ctrl+L",
        [HotkeyAction.OpenOptions] = "Ctrl+O",
        [HotkeyAction.ViewCapital] = "Home",
        [HotkeyAction.ToggleResources] = "Ctrl+R",
        [HotkeyAction.ToggleYields] = "Ctrl+Y",
        [HotkeyAction.PlaceMarker] = "Ctrl+M",
        [HotkeyAction.PingMap] = "Ctrl+P",
        [HotkeyAction.Automate] = "M",
        [HotkeyAction.StopAutomation] = "M",
        [HotkeyAction.StopMovement] = "M",
        [HotkeyAction.Sleep] = "F",
        [HotkeyAction.SleepUntilHealed] = "H",
        [HotkeyAction.Fortify] = "F",
        [HotkeyAction.FortifyUntilHealed] = "H",
        [HotkeyAction.Explore] = "X",
        [HotkeyAction.StopExploration] = "X",
        [HotkeyAction.Upgrade] = "U",
        [HotkeyAction.Transform] = "K",
        [HotkeyAction.Pillage] = "P",
        [HotkeyAction.Paradrop] = "P",
        [HotkeyAction.AirSweep] = "A",
        [HotkeyAction.SetUp] = "T",
        [HotkeyAction.FoundCity] = "C",
        [HotkeyAction.Repair] = "R",
        [HotkeyAction.CreateImprovement] = "I",
        [HotkeyAction.HurryResearch] = "G",
        [HotkeyAction.HurryWonder] = "G",
        [HotkeyAction.HurryBuilding] = "G",
        [HotkeyAction.ConductTradeMission] = "G",
        [HotkeyAction.FoundReligion] = "G",
        [HotkeyAction.TriggerUnique] = "G",
        [HotkeyAction.SpreadReligion] = "G",
        [HotkeyAction.RemoveHeresy] = "H",
        [HotkeyAction.EnhanceReligion] = "G",
        [HotkeyAction.Skip] = "Z",
        [HotkeyAction.AddInCapital] = "G",
        [HotkeyAction.DisbandUnit] = "Delete",
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

    /// <summary>
    /// C078: the unit actions form their own scope (Kotlin <c>Category.UnitActions</c>): they may share a key with each other (the menu offers at most one of them
    /// for a unit) and with a game screen action (the game screen action wins a press; <see cref="Rebind"/> refuses to bind a unit action to such a key).
    /// </summary>
    public static bool IsUnitAction(HotkeyAction action) => action >= HotkeyAction.Automate;

    /// <summary>The hotkey of a unit panel entry (an engine action by its type's name, or Disband), or null when it has none (improvements, promotions, actions Kotlin gives no key).</summary>
    public static HotkeyAction? ForEntry(MenuEntry entry)
    {
        if (entry.Kind == MenuEntryKind.Disband) return HotkeyAction.DisbandUnit;
        if (entry.Kind != MenuEntryKind.Engine || entry.Action == null) return null;
        return Enum.TryParse<HotkeyAction>(entry.Action.Descriptor.ActionType.ToString(), out var action) && IsUnitAction(action) ? action : null;
    }

    /// <summary>The unit actions bound to a key press (canonical or not), in enum order; empty when none.</summary>
    public static IReadOnlyList<HotkeyAction> ResolveUnit(IReadOnlyDictionary<string, string> bindings, string? keyText)
    {
        if (Normalize(keyText) is not { } key) return [];
        return [.. Enum.GetValues<HotkeyAction>().Where(a => IsUnitAction(a) && KeyFor(bindings, a) == key)];
    }

    /// <summary>The binding map made safe: unknown actions, unusable keys and keys equal to the default are dropped, and so is a screen action's binding onto a key another screen action holds (a hand-edited file cannot create ambiguity).</summary>
    public static SortedDictionary<string, string> Sanitize(IDictionary<string, string>? raw)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (raw == null) return result;
        var effective = new Dictionary<HotkeyAction, string>(Defaults);
        var overridden = new List<HotkeyAction>();
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            if (!raw.TryGetValue(action.ToString(), out var value) || Normalize(value) is not { } key) continue;
            effective[action] = key;
            overridden.Add(action);
        }
        // while two screen actions share a key, the last bound one among them goes back to its default (a dropped binding can free or take a key, hence the loop); a swap that
        // Rebind made has no duplicate in the end and passes through, and an older file written against the pre-C078 defaults keeps its other bindings
        while (true)
        {
            var clashing = overridden.Where(a => !IsUnitAction(a) && effective.Any(it => it.Key != a && !IsUnitAction(it.Key) && it.Value == effective[a])).ToList();
            if (clashing.Count == 0) break;
            effective[clashing[^1]] = Defaults[clashing[^1]];
            overridden.Remove(clashing[^1]);
        }
        return FromEffective(effective);
    }

    /// <summary>The key currently bound to <paramref name="action"/>.</summary>
    public static string KeyFor(IReadOnlyDictionary<string, string> bindings, HotkeyAction action) =>
        bindings.TryGetValue(action.ToString(), out var text) && Normalize(text) is { } key ? key : Defaults[action];

    /// <summary>The game screen action a key press triggers (canonical or not), or null; the unit actions are <see cref="ResolveUnit"/>.</summary>
    public static HotkeyAction? Resolve(IReadOnlyDictionary<string, string> bindings, string? keyText)
    {
        if (Normalize(keyText) is not { } key) return null;
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            if (!IsUnitAction(action) && KeyFor(bindings, action) == key) return action;
        }
        return null;
    }

    /// <summary>
    /// The bindings after binding <paramref name="action"/> to <paramref name="keyText"/>, or null when the key is not usable. When another action holds
    /// the key, that action takes this action's old key (a swap), so no game screen key ever has two actions. <paramref name="swapped"/> names the other action.
    /// C078: a unit action is never swapped (unit actions may share keys), but it is refused a key a game screen action holds; a game screen action that takes a key held by unit
    /// actions only shadows them (the unit actions keep the key and lose to the screen action).
    /// </summary>
    public static SortedDictionary<string, string>? Rebind(IReadOnlyDictionary<string, string> bindings, HotkeyAction action, string? keyText, out HotkeyAction? swapped)
    {
        swapped = null;
        if (Normalize(keyText) is not { } key) return null;
        var effective = Enum.GetValues<HotkeyAction>().ToDictionary(a => a, a => KeyFor(bindings, a));
        var previous = effective[action];
        bool unit = IsUnitAction(action);
        if (unit && key != Defaults[action] && effective.Any(it => !IsUnitAction(it.Key) && it.Value == key)) return null;
        foreach (var other in Enum.GetValues<HotkeyAction>())
        {
            if (!unit && !IsUnitAction(other) && other != action && effective[other] == key)
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
