using Unciv.Logic;
using Unciv.Logic.Map;

namespace Unciv.Commands.Events;

// B020 (bnw-spec 2.4): the trade route events. Routes are visible to the two owners only (the unit is not on the map, so there is
// nothing to see); a plunder is also told to the plunderer.

/// <summary>A trade unit of <paramref name="CivId"/> started route <paramref name="RouteId"/> from <paramref name="OriginCityId"/> to <paramref name="DestCityId"/>.</summary>
[EventType("trade-route-established", EventAudience.Participants)]
public sealed record TradeRouteEstablished(int RouteId, string CivId, string DestCivId, string OriginCityId, string DestCityId, TradeDomain Domain, TradeRouteKind Kind) : EngineEvent
{
    public override IEnumerable<string> Participants() => CivId == DestCivId ? [CivId] : [CivId, DestCivId];
}

/// <summary>The route ran its circuits; its unit is back in the origin city.</summary>
[EventType("trade-route-completed", EventAudience.Participants)]
public sealed record TradeRouteCompleted(int RouteId, string CivId, string DestCivId, string OriginCityId, string DestCityId) : EngineEvent
{
    public override IEnumerable<string> Participants() => CivId == DestCivId ? [CivId] : [CivId, DestCivId];
}

/// <summary>An enemy unit of <paramref name="PlundererCivId"/> destroyed the route (and its unit) at <paramref name="Position"/> and took <paramref name="Gold"/> gold.</summary>
[EventType("trade-route-plundered", EventAudience.Participants)]
public sealed record TradeRoutePlundered(int RouteId, string CivId, string DestCivId, string PlundererCivId, HexCoord Position, int Gold) : EngineEvent
{
    public override IEnumerable<string> Participants() => new[] { CivId, DestCivId, PlundererCivId }.Distinct().ToList();

    public override IEnumerable<HexCoord> Positions() => [Position];
}

/// <summary>War, a captured city or a lost owner ended the route; its unit is lost.</summary>
[EventType("trade-route-broken", EventAudience.Participants)]
public sealed record TradeRouteBroken(int RouteId, string CivId, string DestCivId, string OriginCityId, string DestCityId) : EngineEvent
{
    public override IEnumerable<string> Participants() => CivId == DestCivId ? [CivId] : [CivId, DestCivId];
}
