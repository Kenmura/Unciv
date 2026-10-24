namespace Unciv.Models.StatTypes;

// Namespace is not `Unciv.Models.Stats`: a namespace named like the `Stats` class would make every
// `Stats` type reference elsewhere under Unciv.Models fail to resolve.
//
// Stripped (UI/audio deps, restore in a later ticket): notificationIcon, purchaseSound, character (Fonts), color.
public enum Stat
{
    Production,
    Food,
    Gold,
    Science,
    Culture,
    Happiness,
    Faith,
}

/// <summary>Kotlin `Stat` enum members that do not map onto a plain C# enum (companion object, lazy properties).</summary>
public static class StatExtensions
{
    public static readonly IReadOnlySet<Stat> StatsUsableToBuy =
        new HashSet<Stat> { Stat.Gold, Stat.Food, Stat.Science, Stat.Culture, Stat.Faith };

    public static readonly IReadOnlySet<Stat> StatsWithCivWideField =
        new HashSet<Stat> { Stat.Gold, Stat.Science, Stat.Culture, Stat.Faith, Stat.Happiness };

    private static readonly Dictionary<string, Stat> valuesAsMap = BuildValuesAsMap();

    private static Dictionary<string, Stat> BuildValuesAsMap()
    {
        var map = new Dictionary<string, Stat>();
        foreach (var stat in Enum.GetValues<Stat>()) map[stat.ToString()] = stat;
        return map;
    }

    public static bool IsCityWide(this Stat stat) => !StatsWithCivWideField.Contains(stat);

    /// <summary>All values in declaration order (Kotlin `Stat.entries`).</summary>
    public static IReadOnlyList<Stat> Entries { get; } = Enum.GetValues<Stat>();

    /// <summary>Kotlin `Stat.safeValueOf`: exact, case-sensitive name match or null.</summary>
    public static Stat? SafeValueOf(string name) => valuesAsMap.TryGetValue(name, out var stat) ? stat : null;

    public static bool IsStat(string name) => valuesAsMap.ContainsKey(name);

    public static IReadOnlyCollection<string> Names() => valuesAsMap.Keys;

    /// <summary>Kotlin `Stat.valueOf`: throws for unknown names.</summary>
    public static Stat ValueOf(string name) =>
        valuesAsMap.TryGetValue(name, out var stat)
            ? stat
            : throw new ArgumentException($"No enum constant Stat.{name}");
}
