using Unciv.Commands;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;
using CityObject = Unciv.Logic.City.City;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Trade.Routes;

/// <summary>
/// Exotic goods (bnw-spec 4.12, ticket B006): a unit with the unique <c>Carries [n] exotic goods</c> (the Portuguese Nau) sells one load
/// next to a city of another civilization that its owner has met and is not at war with, for gold and experience. Not in Kotlin.
/// Gold and experience lie between the ruleset's <c>exoticGoodsGoldMin/Max</c> and <c>exoticGoodsXpMin/Max</c> (100..400 and 10..30 in BNW),
/// larger the farther the city is from the seller's capital [K]. The scale is [X] (B006): linear in the air distance, full at
/// <see cref="FullDistance"/> tiles, the minimum for a civ without a capital. Everything is gated by the trade-route ModOptions unique.
/// </summary>
public static class ExoticGoods
{
    /// <summary>[X] (B006): distance from the capital, in tiles, at which a load fetches the maximum.</summary>
    public const int FullDistance = 40;

    public static int LoadsCarried(MapUnit unit) =>
        unit.GetMatchingUniques(UniqueType.ExoticGoodsCarried).Sum(it => it.Params[0].ToInt());

    public static int LoadsLeft(MapUnit unit) => Math.Max(0, LoadsCarried(unit) - unit.ExoticGoodsSold);

    /// <summary>Gold of a load sold to <paramref name="target"/> by a unit of <paramref name="unit"/>'s owner.</summary>
    public static int GoldFor(MapUnit unit, CityObject target)
    {
        var c = unit.Civ.GameInfo.Ruleset.ModOptions.Constants;
        return Scale(unit, target, c.ExoticGoodsGoldMin, c.ExoticGoodsGoldMax);
    }

    /// <summary>Experience of a load sold to <paramref name="target"/>.</summary>
    public static int XpFor(MapUnit unit, CityObject target)
    {
        var c = unit.Civ.GameInfo.Ruleset.ModOptions.Constants;
        return Scale(unit, target, c.ExoticGoodsXpMin, c.ExoticGoodsXpMax);
    }

    private static int Scale(MapUnit unit, CityObject target, int min, int max)
    {
        var capital = unit.Civ.GetCapital(firstCityIfNoCapital: false);
        if (capital == null || max <= min) return min;
        int distance = Math.Min(capital.GetCenterTile().AerialDistanceTo(target.GetCenterTile()), FullDistance);
        return min + (max - min) * distance / FullDistance;
    }

    /// <summary>Why <paramref name="unit"/> cannot sell a load to <paramref name="target"/> now, or null.</summary>
    public static RouteRefusal? Check(MapUnit unit, CityObject target)
    {
        var civ = unit.Civ;
        if (!TradeRouteRules.IsEnabled(civ.GameInfo)) return new RouteRefusal(CommandErrors.NotAllowed, "Exotic goods are not enabled in this ruleset");
        if (LoadsLeft(unit) <= 0) return new RouteRefusal(CommandErrors.NotAllowed, $"{unit.Name} has no exotic goods to sell");
        if (!unit.HasMovement()) return new RouteRefusal(CommandErrors.NotAllowed, "The unit has no movement left");
        var owner = target.Civ;
        if (owner == civ || owner.IsBarbarian) return new RouteRefusal(CommandErrors.InvalidTarget, "Exotic goods are sold to another civilization");
        if (!civ.Knows(owner)) return new RouteRefusal(CommandErrors.InvalidTarget, $"You have not met the owner of {target.Name}");
        if (civ.IsAtWarWith(owner)) return new RouteRefusal(CommandErrors.NotAllowed, "You are at war with the owner of that city");
        if (unit.GetTile().AerialDistanceTo(target.GetCenterTile()) != 1)
            return new RouteRefusal(CommandErrors.InvalidTarget, $"The unit must be next to {target.Name}");
        return null;
    }

    /// <summary>Sells one load; <see cref="Check"/> must have passed. The unit stays but has no movement left.</summary>
    public static (int Gold, int Xp) Sell(MapUnit unit, CityObject target)
    {
        int gold = GoldFor(unit, target);
        int xp = XpFor(unit, target);
        unit.ExoticGoodsSold = unchecked(unit.ExoticGoodsSold + 1);
        unit.Civ.AddGold(gold);
        unit.Promotions.XP = unchecked(unit.Promotions.XP + xp);
        unit.CurrentMovement = 0f;
        unit.Civ.AddNotification($"Your [{unit.Name}] sold exotic goods in [{target.Name}] for [{gold}] Gold",
            target.Location.ToHexCoord(), NotificationCategory.Trade, NotificationIcon.Trade);
        return (gold, xp);
    }
}
