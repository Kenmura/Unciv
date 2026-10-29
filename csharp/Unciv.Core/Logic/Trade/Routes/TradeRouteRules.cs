using Unciv.Commands;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Trade.Routes;

/// <summary>Why a route cannot be established: a stable <see cref="CommandErrors"/> code plus text for people.</summary>
public readonly record struct RouteRefusal(string Code, string Message);

/// <summary>A route that passed <see cref="TradeRouteRules.Check"/>: the cities, the domain and the path the unit would walk.</summary>
public sealed record RoutePlan(CityObject Origin, CityObject Dest, TradeDomain Domain, TradeRouteKind Kind, IReadOnlyList<HexCoord> Path);

/// <summary>
/// Trade routes of Brave New World (bnw-spec 4.1, ticket B020): the gate, slots, range, path finding and the checks and effects of
/// establishing a route. Yields are in <see cref="TradeRouteYields"/>, the per-turn lifecycle and plunder in <see cref="TradeRouteTurns"/>.
/// Not in Kotlin. Everything is switched on by the ruleset's ModOptions unique <see cref="UniqueType.EnablesInternationalTradeRoutes"/>,
/// never by the ruleset name; a game without the unique never creates a route and none of this code runs for it.
/// </summary>
public static class TradeRouteRules
{
    /// <summary>Civs remember an ended route this many turns (bnw-spec 4.1.6 item 5, @101f7e40).</summary>
    public const int RouteMemoryTurns = 30;

    /// <summary>Base path length of a sea / land route before techs, traits and buildings (bnw-spec 4.1.3, @101f9e00).</summary>
    public const int BaseSeaRange = 20;
    public const int BaseLandRange = 10;

    /// <summary>[X] (B020): a route lasts about this many turns at Standard speed (spec 4.1.6 item 2; the engine formula is still [?]).</summary>
    public const int StandardRouteTurns = 30;

    public static bool IsEnabled(IGameInfoState game) =>
        ((IHasUniques)game.Ruleset.ModOptions).HasUnique(UniqueType.EnablesInternationalTradeRoutes);

    public static IReadOnlyList<TradeRoute> Routes(IGameInfoState game) => game.TradeRoutes ?? (IReadOnlyList<TradeRoute>)[];

    public static CityObject? FindCity(IGameInfoState game, string cityId) =>
        game.GetCities().FirstOrDefault(it => it.Id == cityId);

    public static TradeDomain DomainOf(MapUnit unit) => unit.BaseUnit.IsWaterUnit ? TradeDomain.Sea : TradeDomain.Land;

    // ---- slots and range

    /// <summary>
    /// Trade route slots (@101fa000): (techs' and buildings' flat slots) x (100 + percent) / 100, truncating; none without a capital.
    /// Venice's double is a +100% row.
    /// </summary>
    public static int Slots(CivilizationObject civ)
    {
        if (civ.GetCapital(firstCityIfNoCapital: false) == null) return 0;
        int flat = civ.GetMatchingUniques(UniqueType.TradeRouteSlots).Sum(it => it.Params[0].ToInt());
        int percent = civ.GetMatchingUniques(UniqueType.TradeRouteSlotsPercent).Sum(it => it.Params[0].ToInt());
        return Math.Max(0, flat * (100 + percent) / 100);
    }

    /// <summary>Slots in use (@101f7330): every route whose origin owner is the civ, domestic ones included [K].</summary>
    public static int SlotsUsed(CivilizationObject civ) => Routes(civ.GameInfo).Count(it => it.OriginOwner == civ.CivId);

    /// <summary>
    /// The longest path (in steps) a trade unit of <paramref name="domain"/> may walk from <paramref name="origin"/> (@101f9e00):
    /// (base + techs' and trait's flat range) x (100 + the origin city's range percent) / 100.
    /// </summary>
    public static int Range(CivilizationObject civ, CityObject origin, TradeDomain domain)
    {
        bool sea = domain == TradeDomain.Sea;
        int range = sea ? BaseSeaRange : BaseLandRange;
        range += civ.GetMatchingUniques(sea ? UniqueType.SeaTradeRouteRangeFlat : UniqueType.LandTradeRouteRangeFlat).Sum(it => it.Params[0].ToInt());
        var percentType = sea ? UniqueType.SeaTradeRouteRange : UniqueType.LandTradeRouteRange;
        int percent = SumForCity(BuildingUniques(origin, percentType), origin) + SumForCity(CivUniques(origin, percentType), origin);
        return Math.Max(0, range * (100 + percent) / 100);
    }

    // ---- uniques of a city, split by where they come from (the buildings of the city versus policies, traits and so on)

    /// <summary>Uniques of the buildings built in <paramref name="city"/>, whether or not they name "in this city" (a Market has no city filter).</summary>
    internal static IEnumerable<UniqueObject> BuildingUniques(CityObject city, UniqueType type) =>
        city.CityConstructions.BuiltBuildingUniqueMap.GetUniques(type).Where(it => it.ConditionalsApply(city.State));

    /// <summary>Uniques of the owner that do not come from a building (policies, nation, techs, ...): buildings are read through <see cref="BuildingUniques"/>.</summary>
    internal static IEnumerable<UniqueObject> CivUniques(CityObject city, UniqueType type) =>
        city.Civ.GetMatchingUniques(type, city.State).Where(it => it.SourceObjectType is not (UniqueTarget.Building or UniqueTarget.Wonder));

    /// <summary>Sum of the first parameter of uniques whose second parameter is a city filter matching <paramref name="city"/>.</summary>
    internal static int SumForCity(IEnumerable<UniqueObject> uniques, CityObject city) =>
        uniques.Where(it => city.MatchesFilter(it.Params[1])).Sum(it => it.Params[0].ToInt());

    // ---- path

    /// <summary>
    /// The shortest path from <paramref name="from"/> to <paramref name="to"/> for a trade unit of <paramref name="domain"/>, both ends
    /// included, or null when none is at most <paramref name="maxSteps"/> steps long. [X] (B020): land units cross passable land, sea units
    /// passable water, borders, fog and units are ignored (trade units ignore them in Civ V), and terrain movement cost is not counted:
    /// the range is a step count (spec 4.1.3). Breadth first in the fixed neighbour order, so ties are always broken the same way.
    /// </summary>
    public static List<HexCoord>? FindPath(Tile from, Tile to, TradeDomain domain, int maxSteps)
    {
        if (from == to || maxSteps < 1) return null;
        var parent = new Dictionary<HexCoord, HexCoord> { [from.Position] = from.Position };
        var frontier = new List<Tile> { from };
        for (int depth = 1; depth <= maxSteps && frontier.Count != 0; depth++)
        {
            var next = new List<Tile>();
            foreach (var tile in frontier)
            {
                foreach (var neighbor in tile.Neighbors)
                {
                    if (parent.ContainsKey(neighbor.Position)) continue;
                    if (neighbor != to && !CanWalk(neighbor, domain)) continue;
                    parent[neighbor.Position] = tile.Position;
                    if (neighbor == to) return Unwind(parent, from.Position, to.Position);
                    next.Add(neighbor);
                }
            }
            frontier = next;
        }
        return null;
    }

    private static bool CanWalk(Tile tile, TradeDomain domain) =>
        !tile.IsImpassible() && (domain == TradeDomain.Sea ? tile.IsWater : tile.IsLand);

    private static List<HexCoord> Unwind(Dictionary<HexCoord, HexCoord> parent, HexCoord start, HexCoord end)
    {
        var path = new List<HexCoord> { end };
        while (path[^1] != start) path.Add(parent[path[^1]]);
        path.Reverse();
        return path;
    }

    // ---- checks

    /// <summary>The unit's own conditions for starting a route (bnw-spec 4.1.6 step 1): a trade unit with movement left, in one of its owner's cities.</summary>
    public static RouteRefusal? CheckUnitReady(MapUnit unit, out CityObject? origin)
    {
        origin = null;
        if (!IsEnabled(unit.Civ.GameInfo)) return new RouteRefusal(CommandErrors.NotAllowed, "Trade routes are not enabled in this ruleset");
        if (!unit.HasUnique(UniqueType.CanEstablishTradeRoutes))
            return new RouteRefusal(CommandErrors.NotAllowed, $"{unit.Name} cannot establish trade routes");
        if (unit.BaseUnit.IsAirUnit()) return new RouteRefusal(CommandErrors.NotAllowed, "Air units cannot establish trade routes");
        if (!unit.HasMovement()) return new RouteRefusal(CommandErrors.NotAllowed, "The unit has no movement left");
        var tile = unit.GetTile();
        origin = tile.IsCityCenter() ? tile.GetCity() : null;
        if (origin == null || origin.Civ != unit.Civ)
        {
            origin = null;
            return new RouteRefusal(CommandErrors.NotAllowed, "The unit must be in one of your cities");
        }
        return null;
    }

    /// <summary><see cref="CheckUnitReady"/>, then <see cref="Check"/> for the target.</summary>
    public static RouteRefusal? CheckUnit(MapUnit unit, CityObject dest, TradeRouteKind kind, out RoutePlan? plan)
    {
        plan = null;
        if (CheckUnitReady(unit, out var origin) is { } refusal) return refusal;
        return Check(unit.Civ, origin.NotNull(), dest, DomainOf(unit), kind, out plan);
    }

    /// <summary>
    /// Can <paramref name="civ"/> open a route from <paramref name="origin"/> to <paramref name="dest"/> now: not a duplicate, a free slot,
    /// owners that meet and are not at war, a domestic route only with the enabling building, a path within range (bnw-spec 4.1.6 step 1).
    /// Embargoes belong to the World Congress (B060).
    /// </summary>
    public static RouteRefusal? Check(CivilizationObject civ, CityObject origin, CityObject dest, TradeDomain domain, TradeRouteKind kind, out RoutePlan? plan, bool requireSlot = true)
    {
        plan = null;
        var game = civ.GameInfo;
        if (!IsEnabled(game)) return new RouteRefusal(CommandErrors.NotAllowed, "Trade routes are not enabled in this ruleset");
        if (origin.Civ != civ) return new RouteRefusal(CommandErrors.NotYourCity, $"{origin.Name} is not yours");
        if (!Enum.IsDefined(kind)) return new RouteRefusal(CommandErrors.InvalidTarget, "Unknown route kind");
        if (dest == origin) return new RouteRefusal(CommandErrors.InvalidTarget, "A route needs two different cities");
        if (dest.Civ.IsBarbarian) return new RouteRefusal(CommandErrors.InvalidTarget, "Barbarians do not trade");
        if (dest.Civ != civ)
        {
            if (kind != TradeRouteKind.International)
                return new RouteRefusal(CommandErrors.InvalidTarget, "Only international routes go to another civilization");
            if (!civ.Knows(dest.Civ)) return new RouteRefusal(CommandErrors.InvalidTarget, $"You have not met the owner of {dest.Name}");
            if (civ.IsAtWarWith(dest.Civ)) return new RouteRefusal(CommandErrors.NotAllowed, "You are at war with the owner of that city");
        }
        else
        {
            if (kind == TradeRouteKind.International)
                return new RouteRefusal(CommandErrors.InvalidTarget, "A route between your own cities is a domestic route");
            string stat = kind == TradeRouteKind.Food ? "Food" : "Production"; // kind is Food or Production here
            if (!BuildingUniques(origin, UniqueType.AllowsDomesticTradeRoutes).Any(it => it.Params[0] == stat))
                return new RouteRefusal(CommandErrors.NotAllowed, $"{origin.Name} has no building for domestic {stat} routes");
        }
        if (!dest.GetCenterTile().IsExplored(civ)) return new RouteRefusal(CommandErrors.InvalidTarget, $"{dest.Name} is not explored");
        if (Routes(game).Any(it => it.OriginCityId == origin.Id && it.DestCityId == dest.Id))
            return new RouteRefusal(CommandErrors.NotAllowed, "There is already a route between these cities");
        if (requireSlot && SlotsUsed(civ) >= Slots(civ)) return new RouteRefusal(CommandErrors.NotAllowed, "No free trade route slot");
        int range = Range(civ, origin, domain);
        var path = FindPath(origin.GetCenterTile(), dest.GetCenterTile(), domain, range);
        if (path == null) return new RouteRefusal(CommandErrors.InvalidTarget, $"{dest.Name} is out of range or cannot be reached");
        plan = new RoutePlan(origin, dest, domain, kind, path);
        return null;
    }

    // ---- trade units (B006): build restrictions and rebasing

    /// <summary>Trade units the civ owns right now: each one is a slot spoken for, whether or not it has started its route yet.</summary>
    public static int TradeUnitsAlive(CivilizationObject civ) =>
        civ.Units.GetCivUnits().Count(it => it.HasUnique(UniqueType.CanEstablishTradeRoutes));

    /// <summary>
    /// Slots neither used by a route nor held by a trade unit that is still on the map. A trade unit cannot be built when this is not
    /// positive (the "NO_EXTRA_SLOTS" text key of @10152ed0; counting the idle units is [X], B006).
    /// </summary>
    public static int SlotsFree(CivilizationObject civ) => Slots(civ) - SlotsUsed(civ) - TradeUnitsAlive(civ);

    /// <summary>
    /// Could a trade unit of <paramref name="domain"/> built in <paramref name="origin"/> be sent anywhere, slots aside: some other city within
    /// range that <see cref="Check"/> accepts for an international route, or for a domestic one when the city has the enabling building
    /// (the "NONE_OF_TYPE_LAND/SEA" text keys of @10152ed0, B006). Cities farther away by air than the range are skipped without a path search.
    /// </summary>
    public static bool HasTarget(CivilizationObject civ, CityObject origin, TradeDomain domain)
    {
        int range = Range(civ, origin, domain);
        var from = origin.GetCenterTile();
        foreach (var dest in civ.GameInfo.GetCities())
        {
            if (dest == origin || from.AerialDistanceTo(dest.GetCenterTile()) > range) continue;
            if (dest.Civ != civ)
            {
                if (Check(civ, origin, dest, domain, TradeRouteKind.International, out _, requireSlot: false) == null) return true;
            }
            else if (Check(civ, origin, dest, domain, TradeRouteKind.Food, out _, requireSlot: false) == null
                || Check(civ, origin, dest, domain, TradeRouteKind.Production, out _, requireSlot: false) == null) return true;
        }
        return false;
    }

    /// <summary>
    /// Why <paramref name="unit"/> (standing in one of its owner's cities) cannot move its home to <paramref name="target"/> (spec 4.1.2,
    /// <c>ChangeTradeUnitHome</c>): another own city that the unit's domain can serve from here, within the range of its current city.
    /// A unit that has started a route is not on the map, so only idle units can rebase. The move uses up the unit's movement. [X] (B006).
    /// </summary>
    public static RouteRefusal? CheckRebase(MapUnit unit, CityObject target)
    {
        if (CheckUnitReady(unit, out var origin) is { } refusal) return refusal;
        var civ = unit.Civ;
        if (target.Civ != civ) return new RouteRefusal(CommandErrors.NotYourCity, $"{target.Name} is not yours");
        if (target == origin) return new RouteRefusal(CommandErrors.InvalidTarget, "The unit is already based in that city");
        var domain = DomainOf(unit);
        if (domain == TradeDomain.Sea && !target.IsNaval())
            return new RouteRefusal(CommandErrors.InvalidTarget, $"{target.Name} is not a port");
        if (FindPath(origin.NotNull().GetCenterTile(), target.GetCenterTile(), domain, Range(civ, origin, domain)) == null)
            return new RouteRefusal(CommandErrors.InvalidTarget, $"{target.Name} is out of range or cannot be reached");
        if (!unit.Movement.CanMoveTo(target.GetCenterTile()))
            return new RouteRefusal(CommandErrors.InvalidTarget, $"The unit cannot be placed in {target.Name}");
        return null;
    }

    /// <summary>Moves the idle trade unit to its new home; <see cref="CheckRebase"/> must have passed.</summary>
    public static void Rebase(MapUnit unit, CityObject target)
    {
        unit.RemoveFromTile();
        unit.PutInTile(target.GetCenterTile());
        unit.CurrentMovement = 0f;
    }

    // ---- establishing

    /// <summary>
    /// Opens the route of <paramref name="plan"/> with <paramref name="unit"/>, which leaves the map for good (see <see cref="TradeRoute"/>).
    /// <see cref="CheckUnit"/> must have passed. Circuits [X] (B020): the route lasts <see cref="StandardRouteTurns"/> turns scaled by the
    /// game speed's production percent, in whole round trips of at least one.
    /// </summary>
    public static TradeRoute Establish(MapUnit unit, RoutePlan plan)
    {
        var game = unit.Civ.GameInfo;
        int stepsPerTurn = StepsPerTurn(unit.BaseUnit.Movement);
        int roundTrip = (plan.Path.Count - 1) * 2;
        int turnsPerCircuit = Math.Max(1, (roundTrip + stepsPerTurn - 1) / stepsPerTurn);
        int targetTurns = Math.Max(1, StandardRouteTurns * TradeRouteYields.SpeedPercent(game) / 100);
        int circuits = Math.Max(1, (targetTurns + turnsPerCircuit - 1) / turnsPerCircuit);

        var route = new TradeRoute
        {
            Id = unchecked(++game.LastTradeRouteId),
            OriginCityId = plan.Origin.Id,
            DestCityId = plan.Dest.Id,
            Origin = plan.Origin.GetCenterTile().Position,
            Dest = plan.Dest.GetCenterTile().Position,
            OriginOwner = plan.Origin.Civ.CivId,
            DestOwner = plan.Dest.Civ.CivId,
            Domain = plan.Domain,
            Kind = plan.Kind,
            UnitId = unit.Id,
            UnitName = unit.Name,
            Path = [.. plan.Path],
            UnitPathIndex = 0,
            MovingForward = true,
            CircuitsToComplete = circuits,
            TurnRouteComplete = unchecked(game.Turns + circuits * turnsPerCircuit),
        };
        TradeRouteYields.Recompute(route, plan.Origin, plan.Dest);
        unit.Destroy();
        (game.TradeRoutes ??= []).Add(route);

        plan.Origin.Civ.UpdateStatsForNextTurn();
        if (plan.Dest.Civ != plan.Origin.Civ) plan.Dest.Civ.UpdateStatsForNextTurn();
        TradeRouteTurns.Announce(game, route);
        return route;
    }

    /// <summary>Path steps a unit with <paramref name="movement"/> points walks per turn: one point is one step, at least one.</summary>
    public static int StepsPerTurn(int movement) => Math.Max(1, movement);

    internal static int YieldIndex(Stat stat) => stat switch
    {
        Stat.Food => TradeYield.Food,
        Stat.Production => TradeYield.Production,
        Stat.Gold => TradeYield.Gold,
        Stat.Science => TradeYield.Science,
        Stat.Culture => TradeYield.Culture,
        Stat.Faith => TradeYield.Faith,
        _ => -1,
    };
}
