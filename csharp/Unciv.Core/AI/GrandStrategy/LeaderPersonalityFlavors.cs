using Unciv.AI.Data;
using Unciv.Utils;

namespace Unciv.AI.GrandStrategy;

/// <summary>
/// A leader's personality flavors and active flavor modifiers (§2 in SPEC.md).
/// Each leader has a personality value per flavor (range 0..20, default 5).
/// </summary>
public sealed class LeaderPersonalityFlavors
{
    public const int DefaultFlavorValue = 5;
    public const int PersonalityFlavorMinValue = 0;
    public const int PersonalityFlavorMaxValue = 20;
    public const int FlavorMinValue = -1000;
    public const int FlavorMaxValue = 1000;
    public const int DefaultRandomizationRange = 2;

    private readonly OrderedDictionary<string, int> flavors = new();

    public LeaderPersonalityFlavors() { }

    public LeaderPersonalityFlavors(IEnumerable<KeyValuePair<string, int>> initialFlavors)
    {
        foreach (var (flavor, value) in initialFlavors)
        {
            SetFlavor(flavor, value);
        }
    }

    public int this[string flavor]
    {
        get => flavors.TryGetValue(flavor, out int value) ? value : DefaultFlavorValue;
        set => SetFlavor(flavor, value);
    }

    public int GetFlavor(string flavor) => this[flavor];

    public void SetFlavor(string flavor, int value)
    {
        flavors[flavor] = Math.Clamp(value, PersonalityFlavorMinValue, PersonalityFlavorMaxValue);
    }

    public bool HasFlavor(string flavor) => flavors.ContainsKey(flavor);

    public IEnumerable<KeyValuePair<string, int>> AllFlavors => flavors;

    public LeaderPersonalityFlavors Clone()
    {
        var clone = new LeaderPersonalityFlavors();
        foreach (var (k, v) in flavors)
        {
            clone.flavors[k] = v;
        }
        return clone;
    }

    /// <summary>
    /// Randomizes set personality flavors by ±range using the engine's deterministic RNG,
    /// clamping the result to [0..20] (§2 in SPEC.md).
    /// </summary>
    public void Randomize(GameRandom random, int range = DefaultRandomizationRange)
    {
        ArgumentNullException.ThrowIfNull(random);
        foreach (string key in flavors.Keys.ToList())
        {
            int delta = random.NextInt(-range, range + 1);
            SetFlavor(key, flavors[key] + delta);
        }
    }

    /// <summary>
    /// Computes the effective flavor value when <paramref name="activeStrategy"/> is active,
    /// taking the base personality flavor and applying modifiers from AIGrandStrategy_FlavorMods.
    /// </summary>
    public int GetModifiedFlavor(string flavor, GrandStrategy activeStrategy, GrandStrategyFlavors? flavorsData = null)
    {
        int baseValue = this[flavor];
        if (activeStrategy == GrandStrategy.None) return baseValue;

        flavorsData ??= GrandStrategyFlavors.LoadEmbedded();
        int mod = flavorsData.Get(GrandStrategyFlavors.FlavorModsTable, activeStrategy.ToStrategyName(), flavor);
        return Math.Clamp(baseValue + mod, FlavorMinValue, FlavorMaxValue);
    }

    /// <summary>
    /// Returns a map of all flavors with grand strategy modifiers applied.
    /// </summary>
    public OrderedDictionary<string, int> GetEffectiveFlavors(GrandStrategy activeStrategy, GrandStrategyFlavors? flavorsData = null)
    {
        flavorsData ??= GrandStrategyFlavors.LoadEmbedded();
        var result = new OrderedDictionary<string, int>();

        foreach (var (k, _) in flavors)
        {
            result[k] = GetModifiedFlavor(k, activeStrategy, flavorsData);
        }

        if (activeStrategy != GrandStrategy.None)
        {
            foreach (var entry in flavorsData.Entries(GrandStrategyFlavors.FlavorModsTable, activeStrategy.ToStrategyName()))
            {
                if (!result.ContainsKey(entry.Key))
                {
                    result[entry.Key] = GetModifiedFlavor(entry.Key, activeStrategy, flavorsData);
                }
            }
        }

        return result;
    }
}
