using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/PerpetualConstruction.kt
// Not ported yet, because they need City, CityConstructions or the Fonts UI constants (T046 onwards): getProductionTooltip, getConversionRate,
// isBuildable and shouldBeDisplayed (StatConversion reads city.cityStats and the civ's EnablesStatProduction uniques).
public abstract class PerpetualConstruction(string name, string description) : IConstruction
{
    public string Name { get; set; } = name;
    public string Description { get; } = description;

    public Counter<string> GetStockpiledResourceRequirements(GameContext state) => Counter.ZERO;

    public Counter<string> GetResourceRequirementsPerTurn(GameContext? state = null) => Counter.ZERO;

    public IReadOnlySet<string> RequiredResources(GameContext? state = null) => EmptySet;

    private static readonly HashSet<string> EmptySet = [];

    public abstract class StatConversion(Stat stat)
        : PerpetualConstruction(stat.ToString(), $"Convert production to [{stat}] at a rate of [rate] to 1")
    {
        public Stat Stat { get; } = stat;
    }

    public sealed class ScienceConstruction() : StatConversion(Stat.Science);
    public sealed class GoldConstruction() : StatConversion(Stat.Gold);
    public sealed class CultureConstruction() : StatConversion(Stat.Culture);
    public sealed class FaithConstruction() : StatConversion(Stat.Faith);
    public sealed class FoodConstruction() : StatConversion(Stat.Food);
    public sealed class IdleConstruction() : PerpetualConstruction("Nothing", "The city will not produce anything.");

    public static readonly StatConversion Science = new ScienceConstruction();
    public static readonly StatConversion Gold = new GoldConstruction();
    public static readonly StatConversion Culture = new CultureConstruction();
    public static readonly StatConversion Faith = new FaithConstruction();
    public static readonly StatConversion Food = new FoodConstruction();
    public static readonly PerpetualConstruction Idle = new IdleConstruction();

    /// <summary>Map of names to instances, in declaration order (Kotlin <c>mapOf</c> is insertion-ordered).</summary>
    public static IReadOnlyDictionary<string, PerpetualConstruction> PerpetualConstructionsMap { get; } =
        new List<PerpetualConstruction> { Science, Gold, Culture, Faith, Food, Idle }.ToDictionary(it => it.Name);

    /// <returns>whether <paramref name="name"/> represents a PerpetualConstruction - note "" is translated to Nothing in the queue so <c>IsNamePerpetual("")==true</c></returns>
    public static bool IsNamePerpetual(string name) => name.Length == 0 || PerpetualConstructionsMap.ContainsKey(name);
}
