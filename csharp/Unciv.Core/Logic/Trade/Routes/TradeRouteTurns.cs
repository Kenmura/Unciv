using Unciv.Commands.Events;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Trade.Routes;

/// <summary>
/// The life of a route after it is established (bnw-spec 4.1.6): the walk of its unit at the start of its owner's turn, completion,
/// plunder, breaking, and the memory of ended routes. Every entry point is a no-op for a game that never had a route.
/// </summary>
public static class TradeRouteTurns
{
    /// <summary>[X] (B020, spec 4.1.6 item 4, plunder gold is [?]): what a plunderer takes besides the gold the route has left to earn.</summary>
    public const int BasePlunderGold = 100;

    /// <summary>
    /// Start of <paramref name="civ"/>'s turn, before its stats are updated (spec 2.3 step 1): ends routes that can no longer stand, moves the
    /// civ's route units, completes the ones that ran their circuits and refreshes the yields of every route that touches the civ.
    /// </summary>
    public static void StartTurn(CivilizationObject civ)
    {
        var game = civ.GameInfo;
        if (game.EndedTradeRoutes != null)
        {
            game.EndedTradeRoutes.RemoveAll(it => game.Turns - it.Turn >= TradeRouteRules.RouteMemoryTurns);
            if (game.EndedTradeRoutes.Count == 0) game.EndedTradeRoutes = null;
        }
        if (game.TradeRoutes is not { Count: > 0 }) return;

        BreakInvalid(game);
        foreach (var route in game.TradeRoutes.Where(it => it.OriginOwner == civ.CivId).ToList()) Advance(game, route);
        foreach (var route in game.TradeRoutes.Where(it => it.OriginOwner == civ.CivId || it.DestOwner == civ.CivId))
        {
            var origin = TradeRouteRules.FindCity(game, route.OriginCityId);
            var dest = TradeRouteRules.FindCity(game, route.DestCityId);
            if (origin != null && dest != null) TradeRouteYields.Recompute(route, origin, dest);
        }
    }

    // Spec 4.1.6 step 2 and 3: one step per movement point of the trade unit, back and forth; a circuit is done when it is home again.
    private static void Advance(IGameInfoState game, TradeRoute route)
    {
        int steps = game.Ruleset.Units.TryGetValue(route.UnitName, out var baseUnit) ? TradeRouteRules.StepsPerTurn(baseUnit.Movement) : 1;
        int last = route.Path.Count - 1;
        for (int step = 0; step < steps; step++)
        {
            if (route.MovingForward)
            {
                route.UnitPathIndex = Math.Min(route.UnitPathIndex + 1, last);
                if (route.UnitPathIndex >= last) route.MovingForward = false;
            }
            else
            {
                route.UnitPathIndex = Math.Max(route.UnitPathIndex - 1, 0);
                if (route.UnitPathIndex > 0) continue;
                route.MovingForward = true;
                route.CircuitsCompleted++;
                if (route.CircuitsCompleted < route.CircuitsToComplete) continue;
                Complete(game, route);
                return;
            }
        }
    }

    private static void Complete(IGameInfoState game, TradeRoute route)
    {
        game.TradeRoutes?.Remove(route);
        Remember(game, route, plundered: false);
        var origin = TradeRouteRules.FindCity(game, route.OriginCityId);
        var originCiv = game.GetCivilization(route.OriginOwner);
        if (origin != null) originCiv.Units.AddUnit(route.UnitName, origin);
        var text = $"Your trade route from [{origin?.Name}] to [{TradeRouteRules.FindCity(game, route.DestCityId)?.Name}] is complete";
        if (origin != null) originCiv.AddNotification(text, origin.Location.ToHexCoord(), NotificationCategory.Trade, NotificationIcon.Trade);
        game.EventSink?.Emit(new TradeRouteCompleted(route.Id, route.OriginOwner, route.DestOwner, route.OriginCityId, route.DestCityId));
    }

    internal static void Announce(IGameInfoState game, TradeRoute route, bool established)
    {
        if (!established) return;
        var origin = TradeRouteRules.FindCity(game, route.OriginCityId).NotNull();
        var dest = TradeRouteRules.FindCity(game, route.DestCityId).NotNull();
        if (route.OriginOwner != route.DestOwner)
            dest.Civ.AddNotification($"[{origin.Civ.CivName}] has established a trade route to [{dest.Name}]", dest.Location.ToHexCoord(),
                NotificationCategory.Trade, NotificationIcon.Trade);
        game.EventSink?.Emit(new TradeRouteEstablished(route.Id, route.OriginOwner, route.DestOwner, route.OriginCityId, route.DestCityId, route.Domain, route.Kind));
    }

    // Spec 4.1.6 item 5: ended routes are remembered, plundered ones included
    private static void Remember(IGameInfoState game, TradeRoute route, bool plundered) =>
        (game.EndedTradeRoutes ??= []).Add(new EndedTradeRoute(route.OriginCityId, route.DestCityId, route.OriginOwner, game.Turns, plundered));

    /// <summary>Did a route from <paramref name="originCityId"/> to <paramref name="destCityId"/> end (or get plundered) in the last <see cref="TradeRouteRules.RouteMemoryTurns"/> turns?</summary>
    public static bool WasRecentlyEnded(IGameInfoState game, string originCityId, string destCityId) =>
        game.EndedTradeRoutes?.Any(it => it.OriginCityId == originCityId && it.DestCityId == destCityId && game.Turns - it.Turn < TradeRouteRules.RouteMemoryTurns) ?? false;

    /// <summary>
    /// Ends the routes whose cities are gone or changed owner, whose owner is defeated, or whose two owners are at war (spec 4.1.6 item 4).
    /// [X] (B020): the unit of a broken route is lost; "loss of path" is not checked, the path is fixed when the route starts.
    /// </summary>
    public static void BreakInvalid(IGameInfoState game)
    {
        if (game.TradeRoutes is not { Count: > 0 }) return;
        foreach (var route in game.TradeRoutes.ToList())
        {
            var origin = TradeRouteRules.FindCity(game, route.OriginCityId);
            var dest = TradeRouteRules.FindCity(game, route.DestCityId);
            bool broken = origin == null || dest == null || origin.Civ.CivId != route.OriginOwner || dest.Civ.CivId != route.DestOwner
                || origin.Civ.IsDefeated() || dest.Civ.IsDefeated() || (origin.Civ != dest.Civ && origin.Civ.IsAtWarWith(dest.Civ));
            if (!broken) continue;
            game.TradeRoutes.Remove(route);
            Remember(game, route, plundered: false);
            foreach (var owner in new[] { route.OriginOwner, route.DestOwner }.Distinct())
            {
                var civ = game.GetCivilizationOrNull(owner);
                if (civ == null || civ.IsDefeated()) continue;
                civ.AddNotification($"A trade route between [{origin?.Name ?? "a lost city"}] and [{dest?.Name ?? "a lost city"}] was broken", NotificationCategory.Trade, NotificationIcon.Trade);
                civ.UpdateStatsForNextTurn();
            }
            game.EventSink?.Emit(new TradeRouteBroken(route.Id, route.OriginOwner, route.DestOwner, route.OriginCityId, route.DestCityId));
        }
    }

    /// <summary>After a declaration of war: routes between the two sides end now, not at the next turn start.</summary>
    public static void OnWarDeclared(IGameInfoState game) => BreakInvalid(game);

    /// <summary>
    /// A military unit of an enemy of the route's owner entered <paramref name="tile"/>: every route whose unit is there is destroyed with
    /// its unit and the plunderer takes gold (spec 4.1.6 item 4; city tiles are safe). Barbarians plunder too (they are at war with all).
    /// [X] (B020): the amount is still [?] in the spec: <see cref="BasePlunderGold"/> plus the origin side's gold per turn times the turns left.
    /// </summary>
    public static void TryPlunder(MapUnit plunderer, Tile tile)
    {
        var game = plunderer.Civ.GameInfo;
        if (game.TradeRoutes is not { Count: > 0 } || !plunderer.IsMilitary() || tile.IsCityCenter()) return;
        foreach (var route in game.TradeRoutes.Where(it => it.CurrentPosition == tile.Position).ToList())
        {
            var owner = game.GetCivilization(route.OriginOwner);
            if (owner == plunderer.Civ || !plunderer.Civ.IsAtWarWith(owner)) continue;
            int turnsLeft = Math.Max(0, route.TurnRouteComplete - game.Turns);
            int gold = unchecked(BasePlunderGold + route.OriginYields[TradeYield.Gold] / 100 * turnsLeft);
            game.TradeRoutes.Remove(route);
            Remember(game, route, plundered: true);
            plunderer.Civ.AddGold(gold);
            string where = TradeRouteRules.FindCity(game, route.OriginCityId)?.Name ?? "";
            plunderer.Civ.AddNotification($"We plundered a trade route from [{where}] and took [{gold}] gold", tile.Position, NotificationCategory.Trade, NotificationIcon.Gold);
            owner.AddNotification($"Our trade route from [{where}] was plundered", tile.Position, NotificationCategory.Trade, NotificationIcon.War);
            owner.UpdateStatsForNextTurn();
            var dest = game.GetCivilizationOrNull(route.DestOwner);
            if (dest != null && dest != owner)
            {
                dest.AddNotification($"A trade route to [{TradeRouteRules.FindCity(game, route.DestCityId)?.Name}] was plundered", tile.Position, NotificationCategory.Trade, NotificationIcon.War);
                dest.UpdateStatsForNextTurn();
            }
            game.EventSink?.Emit(new TradeRoutePlundered(route.Id, route.OriginOwner, route.DestOwner, plunderer.Civ.CivId, route.CurrentPosition, gold));
        }
    }
}
