using Unciv.Logic;
using Unciv.Logic.Trade.Routes;
using Unciv.View;

namespace Unciv.Queries.Empire;

// RouteId: The route (ids are monotonic)
// OriginCityId, OriginCityName, DestCityId, DestCityName: The two ends
// OriginCiv, DestCiv: The owners (the other one only when the viewer has met it, else empty)
// Domain, Kind: Land or Sea, International / Food / Production
// TurnsLeft: Turns until the route is due to end (0 when due)
// CircuitsCompleted, CircuitsToComplete: Round trips done and wanted
// YourYields: What the route gives the viewer, hundredths in the order Food, Production, Gold, Science, Culture, Faith
public sealed record TradeRouteRow(int RouteId, string OriginCityId, string OriginCityName, string DestCityId, string DestCityName,
    string OriginCiv, string DestCiv, TradeDomain Domain, TradeRouteKind Kind, int TurnsLeft, int CircuitsCompleted, int CircuitsToComplete,
    IReadOnlyList<int> YourYields);

// Slots, Used: The viewer's trade route slots and the ones in use
// Yours: Routes the viewer started, in creation order
// ToYou: International routes of others that end in the viewer's cities, in creation order
public sealed record TradeRoutesResult(int Slots, int Used, IReadOnlyList<TradeRouteRow> Yours, IReadOnlyList<TradeRouteRow> ToYou);

// B020 (bnw-spec 2.4): the viewer's trade routes, yours and to you. Where the trade unit is on its way is not part of it.
[QueryType("empire.TradeRoutes")]
public sealed record TradeRoutesQuery : IQuery<TradeRoutesResult>
{
    public string Type => "empire.TradeRoutes";

    public TradeRoutesResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        var game = civ.GameInfo;
        if (!TradeRouteRules.IsEnabled(game) || civ.IsSpectator())
            throw new QueryException(EmpireQueryCodes.NotAvailable, "Trade routes are not available");

        var routes = TradeRouteRules.Routes(game);
        TradeRouteRow Row(TradeRoute route, bool yours)
        {
            var origin = TradeRouteRules.FindCity(game, route.OriginCityId);
            var dest = TradeRouteRules.FindCity(game, route.DestCityId);
            string Owner(string id) => id == civ.CivId || civ.Knows(id) ? game.GetCivilization(id).CivName : "";
            // a city the viewer has not explored is not named (only the destination of a route to you can be unexplored)
            bool originKnown = yours || origin != null && origin.GetCenterTile().IsExplored(civ);
            return new TradeRouteRow(route.Id, originKnown ? route.OriginCityId : "", originKnown ? origin?.Name ?? "" : "", route.DestCityId, dest?.Name ?? "", Owner(route.OriginOwner),
                Owner(route.DestOwner), route.Domain, route.Kind, Math.Max(0, route.TurnRouteComplete - game.Turns), route.CircuitsCompleted,
                route.CircuitsToComplete, yours ? route.OriginYields : route.DestYields);
        }
        return new TradeRoutesResult(TradeRouteRules.Slots(civ), TradeRouteRules.SlotsUsed(civ),
            routes.Where(it => it.OriginOwner == civ.CivId).Select(it => Row(it, true)).ToList(),
            routes.Where(it => it.DestOwner == civ.CivId && it.OriginOwner != civ.CivId).Select(it => Row(it, false)).ToList());
    }
}
