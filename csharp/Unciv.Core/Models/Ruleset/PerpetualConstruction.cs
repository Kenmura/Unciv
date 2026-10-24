using System.Collections.Frozen;
using Unciv.Logic.City;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Components;
using Unciv.Utils;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/PerpetualConstruction.kt
public abstract class PerpetualConstruction(string name, string description) : IConstruction
{
    public string Name { get; set; } = name;
    public string Description { get; } = description;

    public abstract string GetProductionTooltip(City city, bool withIcon = false);

    public abstract bool IsBuildable(CityConstructions cityConstructions);

    public bool ShouldBeDisplayed(CityConstructions cityConstructions) => IsBuildable(cityConstructions);

    public Counter<string> GetStockpiledResourceRequirements(GameContext state) => Counter.ZERO;

    public Counter<string> GetResourceRequirementsPerTurn(GameContext? state = null) => Counter.ZERO;

    public IReadOnlySet<string> RequiredResources(GameContext? state = null) => EmptySet;

    private static readonly FrozenSet<string> EmptySet = FrozenSet<string>.Empty;

    public abstract class StatConversion(Stat stat)
        : PerpetualConstruction(stat.ToString(), $"Convert production to [{stat}] at a rate of [rate] to 1")
    {
        public Stat Stat { get; } = stat;

        public override string GetProductionTooltip(City city, bool withIcon = false) =>
            $"\r\n{KotlinMath.RoundToInt(city.CityStats.CurrentCityStats.Production / GetConversionRate(city))}{(withIcon ? Stat.Character() : "")}/{Fonts.Turn}";

        public int GetConversionRate(City city) => KotlinMath.RoundToInt(1 / city.CityStats.GetStatConversionRate(Stat));

        public override bool IsBuildable(CityConstructions cityConstructions)
        {
            var city = cityConstructions.City;
            if (Stat == Stat.Faith && !city.Civ.GameInfo.IsReligionEnabled())
                return false;

            var context = city.State;
            return city.Civ.GetMatchingUniques(UniqueType.EnablesStatProduction, context)
                .Any(it => it.Params[0] == Stat.ToString());
        }
    }

    public sealed class ScienceConstruction() : StatConversion(Stat.Science);
    public sealed class GoldConstruction() : StatConversion(Stat.Gold);
    public sealed class CultureConstruction() : StatConversion(Stat.Culture);
    public sealed class FaithConstruction() : StatConversion(Stat.Faith);
    public sealed class FoodConstruction() : StatConversion(Stat.Food);
    public sealed class IdleConstruction() : PerpetualConstruction("Nothing", "The city will not produce anything.")
    {
        public override string GetProductionTooltip(City city, bool withIcon = false) => "";
        public override bool IsBuildable(CityConstructions cityConstructions) => true;
    }

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
