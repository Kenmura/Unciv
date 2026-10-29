using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Tourism;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Trade.Routes;

/// <summary>
/// What a route yields to each end (bnw-spec 4.1.4, <c>GetTradeConnectionValueTimes100</c> @101f96f0). Every value is hundredths, the
/// arithmetic is the engine's integer arithmetic (truncating division) and the stats come in the order of <see cref="TradeYield"/>.
/// Ported from the observed BNW engine, not from Kotlin. Where the spec leaves a term open this file says so on that term.
/// </summary>
public static class TradeRouteYields
{
    private const int ResourceBonusTimes100 = 50;
    private const int RiverBonusPercent = 25;
    private const int SeaBonusPercent = 100;
    private const int DomesticBaseTimes100 = 300;

    /// <summary>The origin side's and the destination side's yields of a (possibly planned) route.</summary>
    public readonly record struct Pair(int[] Origin, int[] Dest);

    /// <summary>Stores the current yields of <paramref name="route"/> (they are recomputed every turn, spec 4.1.4).</summary>
    public static void Recompute(TradeRoute route, CityObject origin, CityObject dest)
    {
        var pair = Compute(origin, dest, route.Domain, route.Kind, route.Id);
        route.OriginYields = [.. pair.Origin];
        route.DestYields = [.. pair.Dest];
    }

    /// <summary>
    /// Yields of a route between the two cities. <paramref name="excludeRouteId"/> is the route itself when it already exists, so that
    /// its own presence does not end the "exclusive connection" bonus.
    /// </summary>
    public static Pair Compute(CityObject origin, CityObject dest, TradeDomain domain, TradeRouteKind kind, int excludeRouteId = 0)
    {
        var originYields = new int[TradeYield.Count];
        var destYields = new int[TradeYield.Count];
        if (origin.Civ == dest.Civ)
        {
            // Domestic: only the destination city gains (@101f96f0 returns 0 for the origin)
            if (kind == TradeRouteKind.Food) destYields[TradeYield.Food] = Domestic(origin.Civ, domain, Stat.Food);
            else if (kind == TradeRouteKind.Production) destYields[TradeYield.Production] = Domestic(origin.Civ, domain, Stat.Production);
            return new Pair(originYields, destYields);
        }

        var originCiv = origin.Civ;
        var destCiv = dest.Civ;
        bool sea = domain == TradeDomain.Sea;
        int domainPercent = sea ? SeaBonusPercent : 0;
        bool anyCityState = originCiv.IsCityState || destCiv.IsCityState;
        var constants = originCiv.GameInfo.Ruleset.ModOptions.Constants;

        // --- origin side
        int originMultiplier = 100 + domainPercent + River(domain, origin);
        int tradeBuildings = 100 + SumCivPercent(originCiv, UniqueType.TradeBuildingModifier);
        int policy = PolicyGold(origin, destCiv, sea);
        int yourBuildings = SumBuildings(origin, sea ? UniqueType.GoldFromSeaTradeRoutes : UniqueType.GoldFromLandTradeRoutes, filtered: true) * tradeBuildings / 100;
        // "Gold to the owner of an international trade route from this city" is whole Gold: x100
        int theirBuildings = anyCityState ? 0 : SumBuildings(origin, UniqueType.TradeRouteOwnerGold, filtered: false) * 100 * tradeBuildings / 100;
        var partnerStats = originCiv.GetMatchingUniques(UniqueType.YieldPerTradePartner).Select(it => it.Stats).ToList();
        int trait = AddTraitYields(originYields, partnerStats);
        int goldSum = constants.InternationalTradeBase + CityGpt(origin, constants.InternationalTradeCityGptDivisor)
            + CityGpt(dest, constants.InternationalTradeCityGptDivisor) + ResourceValue(origin, dest, originCiv)
            + Exclusive(dest, originCiv, excludeRouteId, constants.InternationalTradeExclusiveConnection) + policy + yourBuildings + theirBuildings + trait;
        originYields[TradeYield.Gold] = Math.Max(100, goldSum * originMultiplier / 100);
        // The origin owner's influence over the destination owner adds science (spec 4.3.5, GetInfluenceTradeRouteScienceBonus @1006e720)
        originYields[TradeYield.Science] += ScienceBase(Techs(destCiv) - Techs(originCiv)) * 100 + InfluenceRules.TradeRouteScienceBonusTimes100(originCiv, destCiv);

        // --- destination side
        int destMultiplier = 100 + domainPercent + River(domain, dest);
        int destTradeBuildings = 100 + SumCivPercent(destCiv, UniqueType.TradeBuildingModifier);
        int destBuildings = SumBuildings(dest, sea ? UniqueType.GoldFromSeaTradeRoutes : UniqueType.GoldFromLandTradeRoutes, filtered: true) * destTradeBuildings / 100;
        int destTheirBuildings = anyCityState ? 0 : SumBuildings(dest, UniqueType.TradeRouteTargetGold, filtered: false) * 100 * destTradeBuildings / 100;
        var incomingStats = destCiv.GetMatchingUniques(UniqueType.YieldFromIncomingTradeRoutes).Select(it => it.Stats).ToList();
        int incoming = AddTraitYields(destYields, incomingStats);
        destYields[TradeYield.Gold] = (100 + destBuildings + destTheirBuildings + incoming) * destMultiplier / 100;
        destYields[TradeYield.Science] += ScienceBase(Techs(originCiv) - Techs(destCiv)) * 100;

        return new Pair(originYields, destYields);
    }

    private static int SumCivPercent(CivilizationObject civ, UniqueType type) => civ.GetMatchingUniques(type).Sum(it => it.Params[0].ToInt());

    // Stats of a Morocco-style trait: gold joins the gold sum (returned), every other stat is a yield of its own (x100)
    private static int AddTraitYields(int[] yields, List<Models.StatTypes.Stats> statsList)
    {
        int gold = 0;
        foreach (var stats in statsList)
        {
            foreach (var pair in stats)
            {
                int amount = KotlinMath.RoundToInt(pair.Value * 100f);
                int index = TradeRouteRules.YieldIndex(pair.Key);
                if (index == TradeYield.Gold) gold += amount;
                else if (index >= 0) yields[index] += amount;
            }
        }
        return gold;
    }

    private static int Techs(CivilizationObject civ) => civ.Tech.TechsResearched.Count;

    /// <summary>(@101f8c50) the science a civ earns from a partner ahead of it: half the tech lead rounded up, at least 1; 0 when not behind. The 2.0f is a float.</summary>
    public static int ScienceBase(int techLead) => techLead > 0 ? Math.Max((int)MathF.Ceiling(techLead / 2.0f), 1) : 0;

    /// <summary>(@101f8dd0) the city's gold per turn in hundredths over <paramref name="divisor"/>; 0 where the ruleset has no divisor (G&amp;K).</summary>
    private static int CityGpt(CityObject city, int divisor)
    {
        if (divisor <= 0) return 0;
        // [I] (B020): the city's gold without the trade routes' own gold, so a route's value does not feed back into itself
        int own = 0;
        foreach (var route in TradeRouteRules.Routes(city.Civ.GameInfo))
        {
            if (route.OriginCityId == city.Id) own += route.OriginYields[TradeYield.Gold];
            else if (route.DestCityId == city.Id) own += route.DestYields[TradeYield.Gold];
        }
        return (KotlinMath.RoundToInt(city.CityStats.CurrentCityStats.Gold * 100f) - own) / divisor;
    }

    /// <summary>(@101f9680) +25 when a land route's city sits on a river. [I]: the city of the side being computed.</summary>
    private static int River(TradeDomain domain, CityObject city) =>
        domain == TradeDomain.Land && city.GetCenterTile().IsAdjacentToRiver() ? RiverBonusPercent : 0;

    private static int Exclusive(CityObject dest, CivilizationObject originCiv, int excludeRouteId, int value) =>
        TradeRouteRules.Routes(dest.Civ.GameInfo).Any(it => it.Id != excludeRouteId && it.DestCityId == dest.Id && it.OriginOwner != originCiv.CivId) ? 0 : value;

    /// <summary>
    /// (@101f7c20) the policy gold of an international route: the land or sea column of the origin city's filter, shared ideology (B030: Economic
    /// Union) and city-state routes. The land and sea columns are hundredths of Gold; the other two rows are whole Gold (x100).
    /// </summary>
    private static int PolicyGold(CityObject origin, CivilizationObject destCiv, bool sea)
    {
        int gold = TradeRouteRules.SumForCity(TradeRouteRules.CivUniques(origin, sea ? UniqueType.GoldFromSeaTradeRoutes : UniqueType.GoldFromLandTradeRoutes), origin);
        // B030: GoldFromIdeologyTradeRoutes when both civs follow the same ideology; whole Gold like the city-state row
        string? ideology = origin.Civ.Policies.Ideology;
        if (ideology != null && ideology == destCiv.Policies.Ideology)
            gold += TradeRouteRules.CivUniques(origin, UniqueType.GoldFromIdeologyTradeRoutes).Sum(it => it.Params[0].ToInt()) * 100;
        if (destCiv.IsCityState)
            gold += TradeRouteRules.CivUniques(origin, UniqueType.GoldFromCityStateTradeRoutes).Sum(it => it.Params[0].ToInt()) * 100;
        return gold;
    }

    private static int SumBuildings(CityObject city, UniqueType type, bool filtered) =>
        filtered
            ? TradeRouteRules.SumForCity(TradeRouteRules.BuildingUniques(city, type), city)
            : TradeRouteRules.BuildingUniques(city, type).Sum(it => it.Params[0].ToInt());

    /// <summary>
    /// (@101f8f00) +50 for every strategic or luxury resource the two cities do not both have, times the origin owner's resource modifier
    /// (Portugal). [X] (B020): a city "has" a resource when one of the tiles it owns carries it (local, worked and owned collapse to owned).
    /// </summary>
    private static int ResourceValue(CityObject origin, CityObject dest, CivilizationObject originCiv)
    {
        var mine = ResourcesOf(origin);
        var theirs = ResourcesOf(dest);
        int count = mine.Count(it => !theirs.Contains(it)) + theirs.Count(it => !mine.Contains(it));
        int modifier = originCiv.GetMatchingUniques(UniqueType.TradeRouteResourceModifier).Sum(it => it.Params[0].ToInt());
        return count * ResourceBonusTimes100 * (100 + modifier) / 100;
    }

    private static SortedSet<string> ResourcesOf(CityObject city)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var tile in city.GetTiles())
        {
            var resource = tile.TileResource;
            if (resource is { ResourceType: ResourceType.Strategic or ResourceType.Luxury }) names.Add(resource.Name);
        }
        return names;
    }

    /// <summary>
    /// A domestic route's yield to its destination: (100 + the owner's percent + 100 on the sea) x (speed percent x (300 + the era's bonus) / 100) / 100.
    /// [I] (B020): the engine's speed getters are labelled from usage only; see <see cref="SpeedPercent"/> for what stands in for them.
    /// </summary>
    private static int Domestic(CivilizationObject civ, TradeDomain domain, Stat stat)
    {
        int policy = civ.GetMatchingUniques(UniqueType.DomesticTradeRouteYield).Where(it => it.Params[1] == stat.ToString()).Sum(it => it.Params[0].ToInt());
        var era = civ.GetEra();
        int eraBonus = stat == Stat.Food ? era.TradeRouteFoodBonusTimes100 : era.TradeRouteProductionBonusTimes100;
        int speed = stat == Stat.Food ? GrowthPercent(civ.GameInfo) : SpeedPercent(civ.GameInfo);
        return (100 + policy + (domain == TradeDomain.Sea ? SeaBonusPercent : 0)) * (speed * (DomesticBaseTimes100 + eraBonus) / 100) / 100;
    }

    /// <summary>[X] (B020): the production (train and construct) percent of the game speed: Unciv's <c>productionCostModifier</c> in percent.</summary>
    public static int SpeedPercent(IGameInfoState game) => KotlinMath.RoundToInt(game.Speed.ProductionCostModifier * 100f);

    /// <summary>[X] (B020): the growth percent of the game speed: Unciv has no growth modifier, its general <c>modifier</c> in percent stands in.</summary>
    public static int GrowthPercent(IGameInfoState game) => KotlinMath.RoundToInt(game.Speed.Modifier * 100f);
}
