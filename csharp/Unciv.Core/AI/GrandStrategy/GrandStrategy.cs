namespace Unciv.AI.GrandStrategy;

/// <summary>
/// Civ 5 grand strategy types (§3.1 in SPEC.md).
/// The four strategies are identical in base, G&amp;K, and BNW.
/// </summary>
public enum GrandStrategy
{
    None = 0,
    Conquest = 1,
    Culture = 2,
    UnitedNations = 3,
    Spaceship = 4,
}

public static class GrandStrategyExtensions
{
    public const string ConquestName = "AIGRANDSTRATEGY_CONQUEST";
    public const string CultureName = "AIGRANDSTRATEGY_CULTURE";
    public const string UnitedNationsName = "AIGRANDSTRATEGY_UNITED_NATIONS";
    public const string SpaceshipName = "AIGRANDSTRATEGY_SPACESHIP";

    /// <summary>
    /// The four active grand strategies in canonical evaluation order (§3.2 in SPEC.md).
    /// </summary>
    public static readonly IReadOnlyList<GrandStrategy> All =
    [
        GrandStrategy.Conquest,
        GrandStrategy.Culture,
        GrandStrategy.UnitedNations,
        GrandStrategy.Spaceship,
    ];

    public static string ToStrategyName(this GrandStrategy strategy) => strategy switch
    {
        GrandStrategy.Conquest => ConquestName,
        GrandStrategy.Culture => CultureName,
        GrandStrategy.UnitedNations => UnitedNationsName,
        GrandStrategy.Spaceship => SpaceshipName,
        _ => "NONE",
    };

    public static GrandStrategy ParseStrategy(string name) => name switch
    {
        ConquestName => GrandStrategy.Conquest,
        CultureName => GrandStrategy.Culture,
        UnitedNationsName => GrandStrategy.UnitedNations,
        SpaceshipName => GrandStrategy.Spaceship,
        "NONE" or "" => GrandStrategy.None,
        _ => throw new ArgumentException($"Unknown grand strategy name: '{name}'", nameof(name)),
    };

    public static bool TryParseStrategy(string name, out GrandStrategy strategy)
    {
        switch (name)
        {
            case ConquestName:
                strategy = GrandStrategy.Conquest;
                return true;
            case CultureName:
                strategy = GrandStrategy.Culture;
                return true;
            case UnitedNationsName:
                strategy = GrandStrategy.UnitedNations;
                return true;
            case SpaceshipName:
                strategy = GrandStrategy.Spaceship;
                return true;
            case "NONE" or "":
                strategy = GrandStrategy.None;
                return true;
            default:
                strategy = GrandStrategy.None;
                return false;
        }
    }
}
