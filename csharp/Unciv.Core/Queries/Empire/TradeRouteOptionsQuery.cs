using Unciv.Logic;
using Unciv.Logic.Trade.Routes;
using Unciv.Utils;
using Unciv.View;

namespace Unciv.Queries.Empire;

// CityId, CityName: The target city (the id EstablishTradeRoute takes)
// Civ: Its owner, only when the viewer has met it (else empty)
// Kind: International, or Food / Production for an own city
// PathLength: Steps the trade unit would walk one way
// YourYields, TheirYields: Hundredths in the order Food, Production, Gold, Science, Culture, Faith: what the route gives the viewer and the target's owner
public sealed record TradeRouteOption(string CityId, string CityName, string Civ, TradeRouteKind Kind, int PathLength,
    IReadOnlyList<int> YourYields, IReadOnlyList<int> TheirYields);

// Reason: Why the unit cannot start a route at all ("" when it can); Options is then empty
// Options: Every target EstablishTradeRoute would accept for this unit, sorted by city name, id, then kind
public sealed record TradeRouteOptionsResult(int UnitId, string Reason, IReadOnlyList<TradeRouteOption> Options);

// B020 (bnw-spec 2.4): the destinations of a trade unit with the yields of each side. Only explored cities are offered and the owner
// of a city is named only if the viewer has met it. Exactly the cities EstablishTradeRoute would accept.
[QueryType("empire.TradeRouteOptions")]
public sealed record TradeRouteOptionsQuery(int UnitId) : IQuery<TradeRouteOptionsResult>
{
    public string Type => "empire.TradeRouteOptions";

    public TradeRouteOptionsResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        var game = civ.GameInfo;
        if (!TradeRouteRules.IsEnabled(game) || civ.IsSpectator())
            throw new QueryException(EmpireQueryCodes.NotAvailable, "Trade routes are not available");
        var unit = civ.Units.GetUnitById(UnitId) ?? throw new QueryException(EmpireQueryCodes.BadRequest, $"There is no unit {UnitId} of yours");
        if (TradeRouteRules.CheckUnitReady(unit, out var origin) is { } refusal) return new TradeRouteOptionsResult(UnitId, refusal.Message, []);
        var start = origin.NotNull();

        var options = new List<TradeRouteOption>();
        foreach (var city in game.GetCities().OrderBy(it => it.Name, StringComparer.Ordinal).ThenBy(it => it.Id, StringComparer.Ordinal))
        {
            var kinds = city.Civ == civ ? new[] { TradeRouteKind.Food, TradeRouteKind.Production } : new[] { TradeRouteKind.International };
            foreach (var kind in kinds)
            {
                if (TradeRouteRules.Check(civ, start, city, TradeRouteRules.DomainOf(unit), kind, out var plan) != null) continue;
                var chosen = plan.NotNull();
                var yields = TradeRouteYields.Compute(start, city, chosen.Domain, kind);
                options.Add(new TradeRouteOption(city.Id, city.Name, city.Civ == civ || civ.Knows(city.Civ) ? city.Civ.CivName : "", kind,
                    chosen.Path.Count - 1, yields.Origin, yields.Dest));
            }
        }
        return new TradeRouteOptionsResult(UnitId, "", options);
    }
}
