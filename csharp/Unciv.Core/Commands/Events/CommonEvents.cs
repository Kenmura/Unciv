using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Commands.Events;

// Initial event set (T186, design/COMMANDS-IMPLEMENTATION.md section 7). Each domain ticket adds its own events in its own file
// with [EventType]; nothing here is edited for that. Events name civs by CivId, never "the current player": with simultaneous turns
// several civs act in one command log. Fog reveals and stat changes are not events (server-computed diffs, T175).
//
// Audience notes for the classifier (T175): Participants events go at least to the listed civs; the classifier may widen them with
// a rule of its own (e.g. a war declaration to civs that know both parties), never silently.

#region Turn flow

[EventType("turn-started", EventAudience.Everyone)]
public sealed record TurnStarted(string CivId, int Turn) : EngineEvent
{
    public override bool IsEssential() => true;
}

[EventType("turn-ended", EventAudience.Everyone)]
public sealed record TurnEnded(string CivId, int Turn) : EngineEvent
{
    public override bool IsEssential() => true;
}

[EventType("auto-player-turn-processed", EventAudience.Everyone)]
public sealed record AutoPlayerTurnProcessed(string CivId, int Turn) : EngineEvent;

/// <summary>Simultaneous turns: a human marked their turn as ended (emitted by the turn commands, T202).</summary>
[EventType("player-ended-turn", EventAudience.Everyone)]
public sealed record PlayerEndedTurn(string CivId, int Turn) : EngineEvent
{
    public override bool IsEssential() => true;
}

/// <summary>Simultaneous turns: a human took the end-of-turn mark back (T202).</summary>
[EventType("player-undid-end-turn", EventAudience.Everyone)]
public sealed record PlayerUndidEndTurn(string CivId, int Turn) : EngineEvent
{
    public override bool IsEssential() => true;
}

/// <summary>Simultaneous turns: every active human has ended, the host starts resolving the turn (T200/T202).</summary>
[EventType("turn-resolving", EventAudience.Everyone)]
public sealed record TurnResolving(int Turn) : EngineEvent
{
    public override bool IsEssential() => true;
}

/// <summary>The sink hit its bound and dropped <paramref name="Dropped"/> events; clients should resync from a snapshot.</summary>
[EventType("events-truncated", EventAudience.Everyone)]
public sealed record EventsTruncated(int Dropped) : EngineEvent;

#endregion
#region Units

/// <summary><paramref name="Path"/> starts at the tile the unit left and ends where it stopped.</summary>
[EventType("unit-moved", EventAudience.Located)]
public sealed record UnitMoved(string CivId, int UnitId, string UnitName, IReadOnlyList<HexCoord> Path) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];

    public override IEnumerable<HexCoord> Positions() => Path;
}

/// <summary>
/// Combat. Exactly one of <paramref name="AttackerUnitId"/> / <paramref name="AttackerCityId"/> is set, likewise for the defender.
/// <paramref name="DamageToDefender"/> is health lost by the defender, <paramref name="DamageToAttacker"/> by the attacker.
/// </summary>
[EventType("unit-attacked", EventAudience.Located)]
public sealed record UnitAttacked(
    string AttackerCivId, int? AttackerUnitId, string? AttackerCityId, HexCoord AttackerPosition,
    string DefenderCivId, int? DefenderUnitId, string? DefenderCityId, HexCoord DefenderPosition,
    int DamageToDefender, int DamageToAttacker, bool AttackerKilled, bool DefenderKilled) : EngineEvent
{
    public override IEnumerable<string> Participants() => [AttackerCivId, DefenderCivId];

    public override IEnumerable<HexCoord> Positions() => [AttackerPosition, DefenderPosition];
}

[EventType("unit-created", EventAudience.Located)]
public sealed record UnitCreated(string CivId, int UnitId, string UnitName, HexCoord Position) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];

    public override IEnumerable<HexCoord> Positions() => [Position];
}

[EventType("unit-destroyed", EventAudience.Located)]
public sealed record UnitDestroyed(string CivId, int UnitId, string UnitName, HexCoord Position) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];

    public override IEnumerable<HexCoord> Positions() => [Position];
}

/// <summary>A unit changed owner by capture (a civilian taken in battle, an escorted or recaptured unit); it stays on the map.</summary>
[EventType("unit-captured", EventAudience.Located)]
public sealed record UnitCaptured(int UnitId, string UnitName, HexCoord Position, string CapturingCivId, string PreviousOwnerCivId) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CapturingCivId, PreviousOwnerCivId];

    public override IEnumerable<HexCoord> Positions() => [Position];
}

#endregion
#region Cities and tiles

[EventType("city-founded", EventAudience.Located)]
public sealed record CityFounded(string CivId, string CityId, string CityName, HexCoord Position) : EngineEvent
{
    public override bool IsEssential() => true;

    public override IEnumerable<string> Participants() => [CivId];

    public override IEnumerable<HexCoord> Positions() => [Position];
}

/// <summary>A city changed hands through conquest, liberation, puppeting or annexing (not through a trade).</summary>
[EventType("city-captured", EventAudience.Located)]
public sealed record CityCaptured(string CityId, string CityName, HexCoord Position, string ConqueringCivId, string PreviousOwnerCivId, string NewOwnerCivId) : EngineEvent
{
    public override bool IsEssential() => true;

    public override IEnumerable<string> Participants() => [ConqueringCivId, PreviousOwnerCivId, NewOwnerCivId];

    public override IEnumerable<HexCoord> Positions() => [Position];
}

/// <summary>A city was razed or destroyed (its tiles report their own <see cref="TileOwnerChanged"/>).</summary>
[EventType("city-destroyed", EventAudience.Located)]
public sealed record CityDestroyed(string CityId, string CityName, HexCoord Position, string CivId) : EngineEvent
{
    public override bool IsEssential() => true;

    public override IEnumerable<string> Participants() => [CivId];

    public override IEnumerable<HexCoord> Positions() => [Position];
}

/// <summary>A city changed hands through a trade offer (conquest is <see cref="CityCaptured"/>).</summary>
[EventType("city-traded", EventAudience.Located)]
public sealed record CityTraded(string CityId, string CityName, HexCoord Position, string FromCivId, string ToCivId) : EngineEvent
{
    public override bool IsEssential() => true;

    public override IEnumerable<string> Participants() => [FromCivId, ToCivId];

    public override IEnumerable<HexCoord> Positions() => [Position];
}

[EventType("city-grew", EventAudience.Located)]
public sealed record CityGrew(string CivId, string CityId, HexCoord Position, int Population) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];

    public override IEnumerable<HexCoord> Positions() => [Position];
}

/// <summary>A tile's owning civ changed. An empty owner id means unowned.</summary>
[EventType("tile-owner-changed", EventAudience.Located)]
public sealed record TileOwnerChanged(HexCoord Position, string OldOwnerCivId, string NewOwnerCivId) : EngineEvent
{
    public override IEnumerable<string> Participants() => [OldOwnerCivId, NewOwnerCivId];

    public override IEnumerable<HexCoord> Positions() => [Position];
}

#endregion
#region Civilization

/// <summary>
/// A notification for one civ. <paramref name="Locations"/> are the map positions of its actions; the classifier strips actions
/// whose tile the recipient cannot see (design/SERVER.md 2.5). Texts stay untranslated keys.
/// </summary>
[EventType("notification-added", EventAudience.Participants)]
public sealed record NotificationAdded(string CivId, string Text, NotificationCategory Category, IReadOnlyList<string> Icons, IReadOnlyList<HexCoord> Locations) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];

    public override IEnumerable<HexCoord> Positions() => Locations;
}

[EventType("alert-added", EventAudience.Participants)]
public sealed record AlertAdded(string CivId, AlertType AlertType, string Value) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];
}

/// <summary>
/// A "Play [sound] sound" unique fired for one human civ (C057). Derived from game state only, so replay produces the same cues. Participant-only: another civ never learns that
/// the cue was requested. <paramref name="SoundName"/> is a sound file name (a generated placeholder in the client).
/// </summary>
[EventType("sound-requested", EventAudience.Participants)]
public sealed record SoundRequested(string CivId, string SoundName) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];
}

[EventType("research-completed", EventAudience.Participants)]
public sealed record ResearchCompleted(string CivId, string Tech) : EngineEvent
{
    public override bool IsEssential() => true;

    public override IEnumerable<string> Participants() => [CivId];
}

[EventType("policy-adopted", EventAudience.Participants)]
public sealed record PolicyAdopted(string CivId, string Policy) : EngineEvent
{
    public override bool IsEssential() => true;

    public override IEnumerable<string> Participants() => [CivId];
}

#endregion
#region Diplomacy and trade

[EventType("war-declared", EventAudience.Participants)]
public sealed record WarDeclared(string DeclarerCivId, string TargetCivId) : EngineEvent
{
    public override bool IsEssential() => true;

    public override IEnumerable<string> Participants() => [DeclarerCivId, TargetCivId];
}

[EventType("peace-made", EventAudience.Participants)]
public sealed record PeaceMade(string CivId, string OtherCivId) : EngineEvent
{
    public override bool IsEssential() => true;

    public override IEnumerable<string> Participants() => [CivId, OtherCivId];
}

/// <summary>One side of a trade, as plain data.</summary>
public sealed record TradeOfferFact(string Name, TradeOfferType Type, int Amount, int Duration);

/// <summary><paramref name="OffersFromFirst"/> are what <paramref name="CivId"/> gives, <paramref name="OffersFromSecond"/> what <paramref name="OtherCivId"/> gives.</summary>
[EventType("trade-accepted", EventAudience.Participants)]
public sealed record TradeAccepted(string CivId, string OtherCivId, IReadOnlyList<TradeOfferFact> OffersFromFirst, IReadOnlyList<TradeOfferFact> OffersFromSecond) : EngineEvent
{
    public override bool IsEssential() => true;

    public override IEnumerable<string> Participants() => [CivId, OtherCivId];
}

#endregion
