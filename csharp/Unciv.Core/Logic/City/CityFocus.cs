using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Logic.City;

// Ported from core/src/com/unciv/logic/city/CityFocus.kt (T060).
//
// Kotlin CityFocus is an enum with constructor properties and per-value overrides; C# keeps the enum and puts the members in CityFocusExtensions.
// Stripped: the UI-only `binding` (KeyboardBinding) and the KDoc references to Automation / CitizenManagementTable.
// Serialized by name (IsPartOfGameInfoSerialization): City stores it as the string `cityAIFocus`.

/// <summary>
/// Controls automatic worker-to-tile assignment. Order matters for building the CitizenManagementTable.
/// </summary>
public enum CityFocus
{
    NoFocus,
    /// <summary>Never clears existing assignments, only assigns new population using default focus, implemented in <c>City.ReassignPopulation</c></summary>
    Manual,
    FoodFocus,
    ProductionFocus,
    GoldFocus,
    ScienceFocus,
    CultureFocus,
    HappinessFocus,
    FaithFocus,
    GoldGrowthFocus,
    ProductionGrowthFocus,
    //GreatPersonFocus
}

public static class CityFocusExtensions
{
    /// <summary>Kotlin <c>CityFocus.entries</c>, in declaration order.</summary>
    public static IReadOnlyList<CityFocus> Entries { get; } = Enum.GetValues<CityFocus>();

    /// <summary>Display label, formatted for tr().</summary>
    public static string Label(this CityFocus focus) => focus switch
    {
        CityFocus.NoFocus => "Default",
        CityFocus.Manual => "Manual",
        CityFocus.GoldGrowthFocus => $"{Stat.Gold.Character()} {Stat.Food.Character()}",
        CityFocus.ProductionGrowthFocus => $"{Stat.Production.Character()} {Stat.Food.Character()}",
        _ => focus.FocusStat().NotNull().Character().ToString(),
    };

    /// <summary>Whether to show or hide in CityScreen's CitizenManagementTable</summary>
    public static bool TableEnabled(this CityFocus focus) => focus != CityFocus.HappinessFocus;

    /// <summary>Which stat the default <see cref="GetStatMultiplier"/> emphasizes - unused if that is overridden w/o calling super</summary>
    public static Stat? FocusStat(this CityFocus focus) => focus switch
    {
        CityFocus.FoodFocus => Stat.Food,
        CityFocus.ProductionFocus => Stat.Production,
        CityFocus.GoldFocus => Stat.Gold,
        CityFocus.ScienceFocus => Stat.Science,
        CityFocus.CultureFocus => Stat.Culture,
        CityFocus.HappinessFocus => Stat.Happiness,
        CityFocus.FaithFocus => Stat.Faith,
        _ => null,
    };

    public static float GetStatMultiplier(this CityFocus focus, Stat stat)
    {
        switch (focus)
        {
            case CityFocus.NoFocus:
            case CityFocus.Manual:
                return 1f;
            case CityFocus.GoldGrowthFocus:
                return stat switch { Stat.Gold => 2f, Stat.Food => 1.5f, _ => 1f };
            case CityFocus.ProductionGrowthFocus:
                return stat switch { Stat.Production => 2f, Stat.Food => 1.5f, _ => 1f };
            default:
                // on ties, prefer the Focus
                return focus.FocusStat() == stat ? 3.05f : 1f;
        }
    }

    private static readonly Dictionary<CityFocus, List<Stat>> StatValuesCache = [];

    /// <summary>Kotlin <c>statValuesForFocus</c> (a lazy property).</summary>
    public static IReadOnlyList<Stat> StatValuesForFocus(this CityFocus focus)
    {
        lock (StatValuesCache)
        {
            if (!StatValuesCache.TryGetValue(focus, out var values))
            {
                values = StatExtensions.Entries.Where(it => focus.GetStatMultiplier(it) != 1f).ToList();
                StatValuesCache[focus] = values;
            }
            return values;
        }
    }

    /// <summary>Kotlin <c>CityFocus.safeValueOf(stat)</c>.</summary>
    public static CityFocus SafeValueOf(Stat stat) => Entries.FirstOrDefault(it => it.FocusStat() == stat, CityFocus.NoFocus);

    /// <summary>Set used in Automation. All non-Food Focuses, so targets 0 Surplus Food</summary>
    public static IReadOnlySet<CityFocus> ZeroFoodFocuses { get; } = new LinkedHashSet<CityFocus>(
    [
        CityFocus.CultureFocus, CityFocus.FaithFocus, CityFocus.GoldFocus,
        CityFocus.HappinessFocus, CityFocus.ProductionFocus, CityFocus.ScienceFocus,
    ]);
}
