using System.Text.Json.Serialization;
using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Trade;
using Unciv.Models.Ruleset.Unique;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NotificationObject = Unciv.Logic.Civilization.Notification;
using TileObject = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic;

// T186: the engine's event sink (design/COMMANDS-IMPLEMENTATION.md section 7). Not part of the Kotlin port.
//
// The engine mutates state directly and has no event emission. GameInfo.EventSink is null by default (tests, AI-only runs,
// simulations, clones): the call sites test it before building an event, so a null sink costs one field read and no allocation.
// A session sets a sink for the duration of one command (GameCommand.Apply does) and takes the events afterwards. Events are
// facts for presentation and sync; they never feed back into the state, and the sink is not serialized, so state hashes are
// identical with and without it.

/// <summary>Collects the events of one command in call order, up to a bound.</summary>
public sealed class GameInfoEventSink(int maxEvents = GameInfoEventSink.DefaultMaxEvents)
{
    /// <summary>A whole AI round stays well under this; beyond it events are counted and dropped.</summary>
    public const int DefaultMaxEvents = 4096;

    private readonly List<EngineEvent> events = [];
    private int dropped;

    public int Count => events.Count;

    public int Dropped => dropped;

    public void Emit(EngineEvent gameEvent)
    {
        if (events.Count >= maxEvents)
        {
            // Full: an essential event takes the place of the newest non-essential one (the rest keep their order); else it is dropped.
            int victim = gameEvent.IsEssential() ? events.FindLastIndex(it => !it.IsEssential()) : -1;
            dropped++;
            if (victim < 0) return;
            events.RemoveAt(victim);
        }
        events.Add(gameEvent);
    }

    /// <summary>The collected events in call order, followed by one <see cref="EventsTruncated"/> marker if any were dropped. Empties the sink.</summary>
    public IReadOnlyList<GameEvent> Drain()
    {
        var result = new List<GameEvent>(events.Count + 1);
        result.AddRange(events);
        if (dropped > 0) result.Add(new EventsTruncated(dropped));
        events.Clear();
        dropped = 0;
        return result;
    }
}

public partial class GameInfo
{
    /// <summary>Where the engine reports events, or <c>null</c> (default, and never copied by <see cref="Clone"/>).</summary>
    [JsonIgnore]
    public GameInfoEventSink? EventSink { get; set; }
}

/// <summary>
/// The engine's call sites for events, one method each, so a choke point in the engine is a single line and its file needs no
/// reference to the event namespace. Every method returns at once when nobody listens (no sink): arguments are references and
/// value types only, nothing is allocated before the sink check. Never mutates game state.
/// </summary>
public static class GameEventHooks
{
    public static void TurnStarted(CivilizationObject civ)
    {
        if (civ.GameInfo.EventSink is { } sink) sink.Emit(new TurnStarted(civ.CivId, civ.GameInfo.Turns));
    }

    public static void TurnEnded(CivilizationObject civ)
    {
        if (civ.GameInfo.EventSink is { } sink) sink.Emit(new TurnEnded(civ.CivId, civ.GameInfo.Turns));
    }

    public static void AutoPlayerTurnProcessed(CivilizationObject civ)
    {
        if (civ.GameInfo.EventSink is { } sink) sink.Emit(new AutoPlayerTurnProcessed(civ.CivId, civ.GameInfo.Turns));
    }

    /// <summary>Reports <paramref name="notification"/> as added to <paramref name="civ"/> and returns it.</summary>
    public static NotificationObject NotificationAdded(CivilizationObject civ, NotificationObject notification)
    {
        if (civ.GameInfo.EventSink is not { } sink) return notification;
        var locations = new List<HexCoord>();
        foreach (var action in notification.Actions)
        {
            switch (action)
            {
                case LocationAction location: locations.Add(location.Location); break;
                case CityAction city: locations.Add(city.City); break;
                case MapUnitAction unit: locations.Add(unit.Location); break;
                case PromoteUnitAction promote: locations.Add(promote.Location); break;
            }
        }
        sink.Emit(new NotificationAdded(civ.CivId, notification.Text, notification.Category, notification.Icons.ToList(), locations));
        return notification;
    }

    public static void AlertAdded(CivilizationObject civ, PopupAlert alert)
    {
        if (civ.GameInfo.EventSink is { } sink) sink.Emit(new AlertAdded(civ.CivId, alert.Type, alert.Value));
    }

    /// <summary>Called by <c>TileMap.PlaceUnitNearTile</c>, which every spawn path (cities, barbarians, uniques) goes through.</summary>
    public static void UnitCreated(TileMap map, IGameContextCivilization civ, IGameContextUnit unit, TileObject tile)
    {
        if (unit is MapUnit mapUnit && map.GameInfo is GameInfo { EventSink: { } sink })
            sink.Emit(new UnitCreated(civ.CivId, mapUnit.Id, mapUnit.Name, tile.Position));
    }

    /// <summary>Call before the unit is removed. Units that were never on a map and repeated destroys are not reported.</summary>
    public static void UnitDestroyed(MapUnit unit)
    {
        if (unit.Civ.GameInfo.EventSink is { } sink && !unit.IsDestroyed && unit.HasTile())
            sink.Emit(new UnitDestroyed(unit.Civ.CivId, unit.Id, unit.Name, unit.CurrentTile.Position));
    }

    /// <summary>Two-step move (air, paradrop); the path is built only when a sink listens.</summary>
    public static void UnitMoved(MapUnit unit, TileObject from, TileObject to)
    {
        if (unit.Civ.GameInfo.EventSink != null) UnitMoved(unit, [from, to]);
    }

    /// <summary><paramref name="path"/> starts at the tile the unit left and ends where it stopped.</summary>
    public static void UnitMoved(MapUnit unit, IReadOnlyList<TileObject> path)
    {
        if (unit.Civ.GameInfo.EventSink is not { } sink || path.Count < 2 || path[0] == path[^1]) return;
        sink.Emit(new UnitMoved(unit.Civ.CivId, unit.Id, unit.Name, path.Select(it => it.Position).ToList()));
    }

    /// <summary>Identity of both sides taken before the blows (a captured civilian changes owner during them).</summary>
    public readonly record struct AttackSnapshot(string AttackerCivId, int? AttackerUnitId, string? AttackerCityId, HexCoord AttackerPosition,
        string DefenderCivId, int? DefenderUnitId, string? DefenderCityId, HexCoord DefenderPosition);

    /// <summary>Call before the blows; <c>null</c> (no allocation) when nobody listens.</summary>
    public static AttackSnapshot? BeginAttack(ICombatant attacker, ICombatant defender) =>
        attacker.GetCivInfo().GameInfo.EventSink == null ? null : new AttackSnapshot(
            attacker.GetCivInfo().CivId, (attacker as MapUnitCombatant)?.Unit.Id, (attacker as CityCombatant)?.City.Id, attacker.GetTile().Position,
            defender.GetCivInfo().CivId, (defender as MapUnitCombatant)?.Unit.Id, (defender as CityCombatant)?.City.Id, defender.GetTile().Position);

    /// <summary>
    /// Emitted after the blows, so effects they caused (a UnitDestroyed of the loser) come before it in the stream: the event is the
    /// summary of the exchange, order within one command is call order.
    /// </summary>
    public static void UnitAttacked(AttackSnapshot? before, ICombatant attacker, ICombatant defender, DamageDealt damage)
    {
        if (before is not { } s || attacker.GetCivInfo().GameInfo.EventSink is not { } sink) return;
        sink.Emit(new UnitAttacked(s.AttackerCivId, s.AttackerUnitId, s.AttackerCityId, s.AttackerPosition,
            s.DefenderCivId, s.DefenderUnitId, s.DefenderCityId, s.DefenderPosition,
            damage.AttackerDealt, damage.DefenderDealt, attacker.IsDefeated(), defender.IsDefeated()));
    }

    public static void CityFounded(CivilizationObject civ, CityObject city)
    {
        if (civ.GameInfo.EventSink is { } sink) sink.Emit(new CityFounded(civ.CivId, city.Id, city.Name, city.Location.ToHexCoord()));
    }

    public static void CityCaptured(CityObject city, CivilizationObject conquering, CivilizationObject previousOwner, CivilizationObject newOwner)
    {
        if (city.Civ.GameInfo.EventSink is { } sink)
            sink.Emit(new CityCaptured(city.Id, city.Name, city.Location.ToHexCoord(), conquering.CivId, previousOwner.CivId, newOwner.CivId));
    }

    /// <summary>A city changed civ: each of its tiles changes owner (sorted by position, the tile set is hash ordered).</summary>
    public static void CityMoved(CityObject city, CivilizationObject oldCiv, CivilizationObject newCiv)
    {
        if (city.Civ.GameInfo.EventSink is not { } sink) return;
        foreach (var position in city.Tiles.OrderBy(it => it.x).ThenBy(it => it.y))
            sink.Emit(new TileOwnerChanged(position, oldCiv.CivId, newCiv.CivId));
    }

    /// <summary>Call before the city is removed from its civ.</summary>
    public static void CityDestroyed(CityObject city)
    {
        if (city.Civ.GameInfo.EventSink is { } sink) sink.Emit(new CityDestroyed(city.Id, city.Name, city.Location.ToHexCoord(), city.Civ.CivId));
    }

    /// <summary>Call before a trade moves <paramref name="city"/> from its civ to <paramref name="to"/>.</summary>
    public static void CityTraded(CityObject city, CivilizationObject to)
    {
        if (city.Civ.GameInfo.EventSink is { } sink)
            sink.Emit(new CityTraded(city.Id, city.Name, city.Location.ToHexCoord(), city.Civ.CivId, to.CivId));
    }

    /// <summary>Call before the unit changes owner.</summary>
    public static void UnitCaptured(MapUnit unit, CivilizationObject captor)
    {
        if (unit.Civ.GameInfo.EventSink is { } sink && unit.HasTile())
            sink.Emit(new UnitCaptured(unit.Id, unit.Name, unit.CurrentTile.Position, captor.CivId, unit.Civ.CivId));
    }

    public static void CityGrew(CityObject city)
    {
        if (city.Civ.GameInfo.EventSink is { } sink)
            sink.Emit(new CityGrew(city.Civ.CivId, city.Id, city.Location.ToHexCoord(), city.Population.Population));
    }

    /// <summary>Reports a change of the civ owning <paramref name="tile"/> (owner of <paramref name="previous"/> to owner of <paramref name="next"/>) and returns <paramref name="next"/>.</summary>
    public static CityObject? TileOwnerChanged(TileObject tile, CityObject? previous, CityObject? next)
    {
        if (tile.TileMap.GameInfo is GameInfo { EventSink: { } sink })
        {
            string before = previous?.Civ.CivId ?? "";
            string after = next?.Civ.CivId ?? "";
            if (before != after) sink.Emit(new TileOwnerChanged(tile.Position, before, after));
        }
        return next;
    }

    /// <summary>A "Play [sound] sound" unique fired for <paramref name="civ"/> (C057).</summary>
    public static void SoundRequested(CivilizationObject civ, string soundName)
    {
        if (civ.GameInfo.EventSink is { } sink) sink.Emit(new SoundRequested(civ.CivId, soundName));
    }

    public static void ResearchCompleted(CivilizationObject civ, string tech)
    {
        if (civ.GameInfo.EventSink is { } sink) sink.Emit(new ResearchCompleted(civ.CivId, tech));
    }

    public static void PolicyAdopted(CivilizationObject civ, string policy)
    {
        if (civ.GameInfo.EventSink is { } sink) sink.Emit(new PolicyAdopted(civ.CivId, policy));
    }

    public static void WarDeclared(CivilizationObject declarer, CivilizationObject target)
    {
        if (declarer.GameInfo.EventSink is { } sink) sink.Emit(new WarDeclared(declarer.CivId, target.CivId));
    }

    public static void PeaceMade(CivilizationObject civ, CivilizationObject other)
    {
        if (civ.GameInfo.EventSink is { } sink) sink.Emit(new PeaceMade(civ.CivId, other.CivId));
    }

    public static void TradeAccepted(CivilizationObject civ, CivilizationObject other, Trade.Trade trade)
    {
        if (civ.GameInfo.EventSink is not { } sink) return;
        static List<TradeOfferFact> Facts(IEnumerable<TradeOffer> offers) =>
            offers.Select(it => new TradeOfferFact(it.Name, it.Type, it.Amount, it.Duration)).ToList();
        sink.Emit(new TradeAccepted(civ.CivId, other.CivId, Facts(trade.OurOffers), Facts(trade.TheirOffers)));
    }
}
