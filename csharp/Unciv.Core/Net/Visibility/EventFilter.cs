using System.Collections.Frozen;
using Unciv.Commands.Events;
using Unciv.Logic.Map;

namespace Unciv.Net.Visibility;

// T175 (design/SERVER.md sections 2.4, 2.5): per-recipient classifiers for engine events. Default deny: an event reaches a recipient only
// through an explicit rule, and every [EventType] class must have an entry in EventFilter (EventFilterTests enforces it by reflection).
// Outcomes per recipient: the event in full, a redacted replacement (UnitMoved partly in fog becomes UnitAppeared / UnitDisappeared,
// a notification loses the locations the recipient cannot see), or nothing. Visibility is asked through IViewerVisibility, which
// the production implementation (GameViewVisibility) answers from the View classes; the filter never reimplements fog or invisibility.

/// <summary>What one recipient may currently see, evaluated on the state after the command applied.</summary>
public interface IViewerVisibility
{
    /// <summary>Whether the recipient currently sees the tile (explored-but-fogged tiles are not visible).</summary>
    bool CanSeeTile(HexCoord position);

    /// <summary>Whether the recipient sees unit <paramref name="unitId"/> of <paramref name="civId"/> at <paramref name="position"/>; false for a hidden (invisible) unit.</summary>
    bool CanSeeUnit(string civId, int unitId, HexCoord position);

    /// <summary>Whether the recipient has met the civ (the recipient itself counts as known).</summary>
    bool KnowsCiv(string civId);
}

/// <summary>Redacted output of a partly visible <see cref="UnitMoved"/>: a foreign unit is now at this visible position. Server-built only.</summary>
[EventType("unit-appeared", EventAudience.Participants)]
public sealed record UnitAppeared(string CivId, int UnitId, string UnitName, HexCoord Position) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];
}

/// <summary>Redacted output of a partly visible <see cref="UnitMoved"/>: a foreign unit left this visible position for somewhere unseen. Server-built only.</summary>
[EventType("unit-disappeared", EventAudience.Participants)]
public sealed record UnitDisappeared(string CivId, int UnitId, HexCoord Position) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];
}

public static class EventFilter
{
    private delegate IEnumerable<EngineEvent> Classifier(EngineEvent gameEvent, string recipient, IViewerVisibility vis);

    private static readonly FrozenDictionary<Type, Classifier> Classifiers = new Dictionary<Type, Classifier>()
    {
        [typeof(TurnStarted)] = Everyone,
        [typeof(TurnEnded)] = Everyone,
        [typeof(AutoPlayerTurnProcessed)] = Everyone,
        [typeof(PlayerEndedTurn)] = Everyone,
        [typeof(PlayerUndidEndTurn)] = Everyone,
        [typeof(TurnResolving)] = Everyone,
        [typeof(ControllerChanged)] = Everyone,
        [typeof(EventsTruncated)] = Everyone,

        [typeof(UnitMoved)] = ClassifyUnitMoved,
        [typeof(UnitAttacked)] = ClassifyUnitAttacked,
        [typeof(UnitCreated)] = (e, r, v) => LocatedUnit(e, r, v, ((UnitCreated)e).CivId, ((UnitCreated)e).UnitId, ((UnitCreated)e).Position),
        [typeof(UnitCaptured)] = (e, r, v) => LocatedUnit(e, r, v, ((UnitCaptured)e).PreviousOwnerCivId, ((UnitCaptured)e).UnitId, ((UnitCaptured)e).Position),
        [typeof(UnitDestroyed)] = (e, r, v) => LocatedUnit(e, r, v, ((UnitDestroyed)e).CivId, ((UnitDestroyed)e).UnitId, ((UnitDestroyed)e).Position),

        [typeof(CityFounded)] = LocatedAllVisible,
        [typeof(CityCaptured)] = LocatedAllVisible,
        [typeof(CityDestroyed)] = LocatedAllVisible,
        [typeof(CityGrew)] = LocatedAllVisible,
        [typeof(TileOwnerChanged)] = LocatedAllVisible,

        [typeof(NotificationAdded)] = ClassifyNotification,
        [typeof(AlertAdded)] = ParticipantsOnly,
        [typeof(TradeRouteEstablished)] = ParticipantsOnly,
        [typeof(TradeRouteCompleted)] = ParticipantsOnly,
        [typeof(TradeRoutePlundered)] = ParticipantsOnly,
        [typeof(TradeRouteBroken)] = ParticipantsOnly,
        [typeof(SoundRequested)] = ParticipantsOnly,
        [typeof(ResearchCompleted)] = ParticipantsOnly,
        [typeof(PolicyAdopted)] = ParticipantsOnly,
        [typeof(DigResolved)] = ParticipantsOnly,
        [typeof(GreatWorkCreated)] = ParticipantsOnly,
        [typeof(GreatWorkMoved)] = ParticipantsOnly,
        [typeof(GreatWorkSwapped)] = ParticipantsOnly,
        [typeof(DecisionAutoResolved)] = ParticipantsOnly,

        // Wars and peace are public facts for civs that know both parties (design/SERVER.md section 3); trades are private to the two parties.
        [typeof(WarDeclared)] = KnownToBothParties,
        [typeof(PeaceMade)] = KnownToBothParties,
        [typeof(TradeAccepted)] = ParticipantsOnly,
        [typeof(CityTraded)] = ParticipantsOnly,
        [typeof(TradeRequested)] = ParticipantsOnly,
        [typeof(TradeWithdrawn)] = ParticipantsOnly,

        // Built by the server for one recipient; an engine-emitted instance must never be forwarded.
        [typeof(UnitAppeared)] = Nothing,
        [typeof(UnitDisappeared)] = Nothing,
        [typeof(TilesRevealed)] = Nothing,
        [typeof(StatsChanged)] = Nothing,
    }.ToFrozenDictionary();

    /// <summary>Whether <paramref name="eventClass"/> has a classifier (the reflection test requires it for every event type).</summary>
    public static bool HasClassifier(Type eventClass) => Classifiers.ContainsKey(eventClass);

    /// <summary>The events <paramref name="recipient"/> may receive for <paramref name="gameEvent"/>: empty (drop), the event itself, or redacted replacements. An unclassified type is dropped.</summary>
    public static IReadOnlyList<EngineEvent> Classify(EngineEvent gameEvent, string recipient, IViewerVisibility visibility) =>
        Classifiers.TryGetValue(gameEvent.GetType(), out var classifier) ? classifier(gameEvent, recipient, visibility).ToList() : [];

    private static bool IsParticipant(EngineEvent gameEvent, string recipient) => gameEvent.Participants().Contains(recipient, StringComparer.Ordinal);

    private static IEnumerable<EngineEvent> Everyone(EngineEvent gameEvent, string recipient, IViewerVisibility vis) => [gameEvent];

    private static IEnumerable<EngineEvent> Nothing(EngineEvent gameEvent, string recipient, IViewerVisibility vis) => [];

    private static IEnumerable<EngineEvent> ParticipantsOnly(EngineEvent gameEvent, string recipient, IViewerVisibility vis) =>
        IsParticipant(gameEvent, recipient) ? [gameEvent] : [];

    private static IEnumerable<EngineEvent> KnownToBothParties(EngineEvent gameEvent, string recipient, IViewerVisibility vis) =>
        IsParticipant(gameEvent, recipient) || gameEvent.Participants().All(vis.KnowsCiv) ? [gameEvent] : [];

    /// <summary>Participants in full; others only when they see every position of the event (a half-seen event would leak the other coordinate).</summary>
    private static IEnumerable<EngineEvent> LocatedAllVisible(EngineEvent gameEvent, string recipient, IViewerVisibility vis)
    {
        if (IsParticipant(gameEvent, recipient)) return [gameEvent];
        var positions = gameEvent.Positions().ToList();
        return positions.Count > 0 && positions.All(vis.CanSeeTile) ? [gameEvent] : [];
    }

    private static IEnumerable<EngineEvent> LocatedUnit(EngineEvent gameEvent, string recipient, IViewerVisibility vis, string civId, int unitId, HexCoord position) =>
        IsParticipant(gameEvent, recipient) || vis.CanSeeUnit(civId, unitId, position) ? [gameEvent] : [];

    private static IEnumerable<EngineEvent> ClassifyUnitAttacked(EngineEvent gameEvent, string recipient, IViewerVisibility vis)
    {
        var e = (UnitAttacked)gameEvent;
        if (IsParticipant(e, recipient)) return [e];
        // Both sides must be seen, and each attacking or defending unit must not be hidden.
        bool attackerSeen = e.AttackerUnitId is { } a ? vis.CanSeeUnit(e.AttackerCivId, a, e.AttackerPosition) : vis.CanSeeTile(e.AttackerPosition);
        bool defenderSeen = e.DefenderUnitId is { } d ? vis.CanSeeUnit(e.DefenderCivId, d, e.DefenderPosition) : vis.CanSeeTile(e.DefenderPosition);
        return attackerSeen && defenderSeen ? [e] : [];
    }

    private static IEnumerable<EngineEvent> ClassifyUnitMoved(EngineEvent gameEvent, string recipient, IViewerVisibility vis)
    {
        var e = (UnitMoved)gameEvent;
        if (IsParticipant(e, recipient)) return [e];
        if (e.Path.Count == 0) return [];
        var start = e.Path[0];
        var end = e.Path[^1];
        bool startSeen = vis.CanSeeUnit(e.CivId, e.UnitId, start);
        bool endSeen = vis.CanSeeUnit(e.CivId, e.UnitId, end);
        if (startSeen && endSeen && e.Path.All(vis.CanSeeTile)) return [e];
        // A unit that left sight between two deltas is never described by a path: only the visible ends are reported.
        var result = new List<EngineEvent>();
        if (startSeen && !(endSeen && start == end)) result.Add(new UnitDisappeared(e.CivId, e.UnitId, start));
        if (endSeen) result.Add(new UnitAppeared(e.CivId, e.UnitId, e.UnitName, end));
        return result;
    }

    private static IEnumerable<EngineEvent> ClassifyNotification(EngineEvent gameEvent, string recipient, IViewerVisibility vis)
    {
        var e = (NotificationAdded)gameEvent;
        if (!IsParticipant(e, recipient)) return [];
        // Actions pointing at a tile the recipient cannot currently see are stripped (design/SERVER.md section 2.5).
        return [e with { Locations = e.Locations.Where(vis.CanSeeTile).ToList() }];
    }
}
