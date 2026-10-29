using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unit;

// core/src/com/unciv/models/ruleset/unit/BaseUnitCost.kt
public class BaseUnitCost(BaseUnit baseUnit)
{
    private readonly BaseUnit baseUnit = baseUnit;

    public BaseUnit BaseUnit => baseUnit;

    public int GetProductionCost(IConstructionCivilization civInfo, IConstructionCity? city)
    {
        var self = (IHasUniques)baseUnit;
        var productionCost = (float)baseUnit.Cost;

        var stateForConditionals = city?.State ?? civInfo.State;
        foreach (var unique in self.GetMatchingUniques(UniqueType.CostIncreasesPerCity, stateForConditionals))
            productionCost += civInfo.Cities.Count * ParseInt(unique.Params[0]);

        foreach (var unique in self.GetMatchingUniques(UniqueType.CostIncreasesWhenBuilt, stateForConditionals))
            productionCost += civInfo.GetBuiltItemsWithIncreasingCost(baseUnit.Name) * ParseInt(unique.Params[0]);

        foreach (var unique in self.GetMatchingUniques(UniqueType.CostPercentageChange, stateForConditionals))
            productionCost *= unique.Params[0].ToPercent();

        productionCost *= civInfo.IsCityState
            ? 1.5f
            : civInfo.IsHuman()
                ? civInfo.GetDifficulty().UnitCostModifier
                : civInfo.GameInfo.GetDifficulty().AiUnitCostModifier;

        productionCost *= civInfo.GameInfo.Speed.ProductionCostModifier;
        return KotlinMath.FloatToInt(productionCost);
    }

    /// <summary>Brave New World (B011, RELIGION_MIN_FAITH_FIRST_GREAT_PERSON): the price of the first Great Person bought with Faith.</summary>
    public const int FaithGreatPersonBasePrice = 1000;

    /// <summary>Brave New World (B011, RELIGION_FAITH_DELTA_NEXT_GREAT_PERSON): what each Great Person of the same kind already bought adds.</summary>
    public const int FaithGreatPersonPriceIncrease = 500;

    /// <summary>
    /// Brave New World (B011, bnw-spec/SPEC.md 4.5): does <c>Can purchase all Great People with Faith</c> (a belief, with its own conditionals such as
    /// <c>starting from the [Industrial era]</c>) let the city buy this unit with Faith? Great Prophets keep their own purchase.
    /// </summary>
    public bool IsFaithPurchasableGreatPerson(IConstructionCity city, Stat stat) =>
        stat == Stat.Faith && baseUnit.IsGreatPerson && !baseUnit.IsGreatPersonOfType("Faith")
        && city.GetMatchingUniques(UniqueType.FaithPurchaseAllGreatPeople, city.State).Any();

    /// <summary>Contains only unit-specific uniques that allow purchasing with stat</summary>
    public bool CanBePurchasedWithStat(IConstructionCity city, Stat stat)
    {
        var conditionalState = city.State;
        var statName = stat.ToString();

        if (city.GetMatchingUniques(UniqueType.BuyUnitsIncreasingCost, conditionalState)
            .Any(it =>
                it.Params[2] == statName
                && baseUnit.MatchesFilter(it.Params[0], conditionalState)
                && city.MatchesFilter(it.Params[3], null)))
            return true;

        if (city.GetMatchingUniques(UniqueType.BuyUnitsByProductionCost, conditionalState)
            .Any(it => it.Params[1] == statName && baseUnit.MatchesFilter(it.Params[0], conditionalState)))
            return true;

        if (city.GetMatchingUniques(UniqueType.BuyUnitsWithStat, conditionalState)
            .Any(it =>
                it.Params[1] == statName
                && baseUnit.MatchesFilter(it.Params[0], conditionalState)
                && city.MatchesFilter(it.Params[2], null)))
            return true;

        if (city.GetMatchingUniques(UniqueType.BuyUnitsForAmountStat, conditionalState)
            .Any(it =>
                it.Params[2] == statName
                && baseUnit.MatchesFilter(it.Params[0], conditionalState)
                && city.MatchesFilter(it.Params[3], null)))
            return true;

        return IsFaithPurchasableGreatPerson(city, stat);
    }

    public int? GetStatBuyCost(IConstructionCity city, Stat stat)
    {
        var baseBuyCost = ((INonPerpetualConstruction)baseUnit).GetBaseBuyCost(city, stat);
        if (baseBuyCost == null) return null;
        double cost = baseBuyCost.Value;
        var conditionalState = city.State;

        city.ForEachMatchingUnique(UniqueType.BuyUnitsDiscount, null, unique =>
        {
            if (stat.ToString() == unique.Params[0] && baseUnit.MatchesFilter(unique.Params[1], conditionalState))
                cost *= unique.Params[2].ToPercent();
        });
        city.ForEachMatchingUnique(UniqueType.BuyItemsDiscount, null, unique =>
        {
            if (stat.ToString() == unique.Params[0])
                cost *= unique.Params[1].ToPercent();
        });

        return KotlinMath.DoubleToInt(cost / 10f) * 10;
    }

    public IEnumerable<float> GetBaseBuyCosts(IConstructionCity city, Stat stat)
    {
        var conditionalState = city.State;
        var statName = stat.ToString();
        var self = (INonPerpetualConstruction)baseUnit;

        foreach (var unique in city.GetMatchingUniques(UniqueType.BuyUnitsIncreasingCost, conditionalState)
                     .Where(it =>
                         it.Params[2] == statName
                         && baseUnit.MatchesFilter(it.Params[0], conditionalState)
                         && city.MatchesFilter(it.Params[3], null)))
        {
            yield return self.GetCostForConstructionsIncreasingInPrice(
                ParseInt(unique.Params[1]),
                ParseInt(unique.Params[4]),
                city.Civ.GetBoughtItemsWithIncreasingPrice(baseUnit.Name)) * city.Civ.GameInfo.Speed.GetStatCostModifier(stat);
        }

        foreach (var unique in city.GetMatchingUniques(UniqueType.BuyUnitsByProductionCost, conditionalState)
                     .Where(it => it.Params[1] == statName && baseUnit.MatchesFilter(it.Params[0], conditionalState)))
            yield return (float)(GetProductionCost(city.Civ, city) * ParseInt(unique.Params[2]));

        if (city.GetMatchingUniques(UniqueType.BuyUnitsWithStat, conditionalState)
            .Any(it =>
                it.Params[1] == statName
                && baseUnit.MatchesFilter(it.Params[0], conditionalState)
                && city.MatchesFilter(it.Params[2], null)))
            yield return city.Civ.GetEra().BaseUnitBuyCost * city.Civ.GameInfo.Speed.GetStatCostModifier(stat);

        foreach (var unique in city.GetMatchingUniques(UniqueType.BuyUnitsForAmountStat, conditionalState)
                     .Where(it =>
                         it.Params[2] == statName
                         && baseUnit.MatchesFilter(it.Params[0], conditionalState)
                         && city.MatchesFilter(it.Params[3], null)))
            yield return ParseInt(unique.Params[1]) * city.Civ.GameInfo.Speed.GetStatCostModifier(stat);

        if (IsFaithPurchasableGreatPerson(city, stat))
            yield return self.GetCostForConstructionsIncreasingInPrice(FaithGreatPersonBasePrice, FaithGreatPersonPriceIncrease,
                city.Civ.GetBoughtItemsWithIncreasingPrice(baseUnit.Name)) * city.Civ.GameInfo.Speed.GetStatCostModifier(stat);
    }

    /// <summary>Kotlin <c>String.toInt()</c></summary>
    private static int ParseInt(string text) => int.Parse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture);
}
