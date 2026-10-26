namespace Unciv.Net.Visibility;

// T174 (design/SERVER.md sections 2.3 and 3): the wire form of one civ's view of the game. These DTOs are a WHITELIST: a field exists
// here only because a View class exposes it as knowable to the recipient. A new engine field can never leak by omission, because
// nothing is serialized that is not declared in this file. VisibilityDtoWhitelistTests pins the exact member list of every type.
// Deliberately absent everywhere: seeds, gameId, RNG state, other civs' gold/science/policies/techs, production queues of foreign
// cities, unexplored tiles, hidden units.

/// <summary>Turn-phase info for simultaneous turns (design/SIMULTANEOUS-TURNS.md section 7), supplied by the host's turn controller.</summary>
public sealed record TurnStatusDto
{
    public required List<string> Ended { get; init; }

    /// <summary>Absolute deadline (Unix ms), or null when no timer runs.</summary>
    public long? Deadline { get; init; }

    public required string Phase { get; init; }
}

/// <summary>Client-specific full state. <see cref="ViewHash"/> is computed over the canonical JSON of this record with <see cref="ViewHash"/> unset.</summary>
public sealed record ClientSnapshot
{
    public required long Seq { get; init; }
    public required int Turn { get; init; }
    public required string Phase { get; init; }
    public required TurnStatusDto TurnStatus { get; init; }
    public required string You { get; init; }
    public required List<CivDto> Civs { get; init; }
    public required List<TileDto> Tiles { get; init; }
    public required List<CityDto> Cities { get; init; }
    public required List<UnitDto> Units { get; init; }
    public required List<DiplomacyDto> Diplomacy { get; init; }
    public required List<PendingTradeDto> PendingTrades { get; init; }
    public required List<NotificationDto> Notifications { get; init; }

    /// <summary>T224: the recipient's own popup alerts, in the engine's order (the index is what <c>ack-alert</c> and friends take). Empty for everyone else's alerts and for spectators.</summary>
    public List<PopupAlertDto> PopupAlerts { get; init; } = [];

    /// <summary>T224: what the recipient still has to decide (the engine's <c>PendingDecisions</c> query), so a reconnecting player sees what blocks End turn.</summary>
    public List<PendingDecisionDto> PendingDecisions { get; init; } = [];

    public string? ViewHash { get; init; }
}

public sealed record CivDto
{
    public required string CivId { get; init; }
    public required string Name { get; init; }
    public required bool IsCityState { get; init; }
    public required bool IsMajor { get; init; }
    public required bool IsBarbarian { get; init; }
    public required bool IsDefeated { get; init; }

    /// <summary>Private detail, present only for the recipient's own civ.</summary>
    public OwnCivDto? Own { get; init; }
}

public sealed record OwnCivDto
{
    public required int Gold { get; init; }
    public required int Happiness { get; init; }
    public string? CurrentTech { get; init; }
    public required List<string> Techs { get; init; }
    public required List<string> Policies { get; init; }

    /// <summary>The recipient's own spies; foreign spies are never described (design/SERVER.md section 3, espionage row).</summary>
    public required List<SpyDto> Spies { get; init; }
}

/// <summary>An explored tile. Fogged tiles carry terrain only; <see cref="Visible"/> tiles add live ownership, road and pillage state.</summary>
public sealed record TileDto
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public required bool Visible { get; init; }
    public required string BaseTerrain { get; init; }
    public required List<string> Features { get; init; }
    public string? NaturalWonder { get; init; }
    public string? Resource { get; init; }
    public string? Improvement { get; init; }
    public required bool RiverBottom { get; init; }
    public required bool RiverBottomLeft { get; init; }
    public required bool RiverBottomRight { get; init; }

    // Visible tiles only
    public string? Owner { get; init; }
    public string? Road { get; init; }
    public bool? RoadPillaged { get; init; }
    public bool? ImprovementPillaged { get; init; }
}

public sealed record CityDto
{
    public required string Name { get; init; }
    public required string Owner { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Population { get; init; }
    public required bool IsCapital { get; init; }
    public required bool IsPuppet { get; init; }
    public required bool IsBeingRazed { get; init; }
    public required int Health { get; init; }
    public required int MaxHealth { get; init; }

    /// <summary>Wonders in the city, knowable by anyone who explored it (T212).</summary>
    public required List<string> VisibleBuildings { get; init; }

    /// <summary>Private detail, present only for the recipient's own cities.</summary>
    public OwnCityDto? Own { get; init; }
}

public sealed record OwnCityDto
{
    /// <summary>The id the city commands and city queries take (C017); private to the owner like the rest of this record.</summary>
    public required string Id { get; init; }

    public required int FoodStored { get; init; }
    public required int CultureStored { get; init; }
    public required List<string> Buildings { get; init; }
    public required List<string> ConstructionQueue { get; init; }
}

public sealed record UnitDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Owner { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }

    /// <summary>Quarters of full health, 1 to 4 (4 = 76-100). Exact health is private.</summary>
    public required int HealthBucket { get; init; }
    public required bool IsEmbarked { get; init; }
    public required bool IsFortified { get; init; }

    /// <summary>Private detail, present only for the recipient's own units.</summary>
    public OwnUnitDto? Own { get; init; }
}

public sealed record OwnUnitDto
{
    public required int Health { get; init; }
    public required float CurrentMovement { get; init; }
    public required int Xp { get; init; }
    public string? Action { get; init; }
    public required List<string> Promotions { get; init; }

    /// <summary>C004: the unit is on automation (auto-work, auto-explore as a flag; Kotlin keeps this outside <see cref="Action"/>). Private to the owner like the rest of this record.</summary>
    public bool Automated { get; init; }

    /// <summary>C004: the unit has the experience to take another promotion (the engine's own check, not recomputed by the client).</summary>
    public bool CanBePromoted { get; init; }
}

/// <summary>Public war/peace fact between two civs the recipient knows (always including the recipient). Detailed status is a gap, see the ticket notes.</summary>
public sealed record DiplomacyDto
{
    public required string A { get; init; }
    public required string B { get; init; }
    public required bool AtWar { get; init; }

    /// <summary>The <c>DiplomaticStatus</c> name (War, Peace, Protector, ...), or null when the pair has no relationship.</summary>
    public string? Status { get; init; }
}

/// <summary>A trade proposal between exactly two civs; only the two parties ever receive it.</summary>
public sealed record PendingTradeDto
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required List<TradeOfferDto> Offered { get; init; }
    public required List<TradeOfferDto> Requested { get; init; }
}

public sealed record TradeOfferDto
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required int Amount { get; init; }
    public required int Duration { get; init; }
}

public sealed record NotificationDto
{
    public required string Category { get; init; }
    public required string Text { get; init; }
    public required List<string> Icons { get; init; }
    public required List<NotificationActionDto> Actions { get; init; }
}

/// <summary>Only whitelisted action kinds survive; location-bearing ones are dropped when the tile is not currently visible to the recipient.</summary>
public sealed record NotificationActionDto
{
    public required string Kind { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public string? Name { get; init; }

    /// <summary>The recipient's own unit a <c>promote</c> action is about.</summary>
    public int? Id { get; init; }
}

/// <summary>T224: one of the recipient's own popup alerts. The value is the alert's subject (a civ, city id, wonder or tech name) exactly as the engine stores it.</summary>
public sealed record PopupAlertDto
{
    public required string Type { get; init; }
    public required string Value { get; init; }
}

/// <summary>T224: one pending decision of the recipient, mirroring the engine's <c>Decision</c>.</summary>
public sealed record PendingDecisionDto
{
    public required string Kind { get; init; }
    public required string Subject { get; init; }

    /// <summary>The command type that resolves it.</summary>
    public required string ResolvedBy { get; init; }

    public required bool Blocking { get; init; }

    /// <summary>The position in the alert or trade request list for those kinds, else -1.</summary>
    public required int Index { get; init; }
}
