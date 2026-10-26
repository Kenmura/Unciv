using System.Globalization;

namespace Unciv.Net.Visibility;

// T224 (design/SERVER.md section 2.4, amended): the client mirror is moved by the DIFF between a recipient's filtered snapshot before and after a command,
// not by replaying events, because most ClientSnapshot fields (own food stored, unit health, tile improvement, diplomacy status...) have no event.
// The diff is computed from two ClientSnapshot DTOs only, never from GameInfo, so it can carry nothing the recipient's own snapshot would not: the
// whitelist of ClientSnapshot.cs bounds it. Events stay in the ViewDelta for animation and sound.

/// <summary>Changes to one keyed collection of a <see cref="ClientSnapshot"/>.</summary>
public sealed record KeyedDiff<T>
{
    /// <summary>Items whose key was already present and whose content changed (replaced in place).</summary>
    public required List<T> Changed { get; init; }

    /// <summary>New items, in their order in the new snapshot; <see cref="AddedAt"/> holds the matching index each one has in the new list.</summary>
    public required List<T> Added { get; init; }

    public required List<int> AddedAt { get; init; }

    public required List<string> Removed { get; init; }

    /// <summary>The complete key order of the new list; present only when the surviving items changed their relative order (a unit moved to another tile), else null.</summary>
    public List<string>? Order { get; init; }

    public bool IsEmpty => Changed.Count == 0 && Added.Count == 0 && Removed.Count == 0 && Order == null;
}

/// <summary>The difference between two snapshots of one recipient. A null member means "unchanged"; the unkeyed lists are replaced as a whole when they differ.</summary>
public sealed record SnapshotDiff
{
    public required long Seq { get; init; }
    public int? Turn { get; init; }
    public string? Phase { get; init; }
    public TurnStatusDto? TurnStatus { get; init; }
    public KeyedDiff<CivDto>? Civs { get; init; }
    public KeyedDiff<TileDto>? Tiles { get; init; }
    public KeyedDiff<CityDto>? Cities { get; init; }
    public KeyedDiff<UnitDto>? Units { get; init; }
    public KeyedDiff<DiplomacyDto>? Diplomacy { get; init; }
    public List<PendingTradeDto>? PendingTrades { get; init; }
    public List<NotificationDto>? Notifications { get; init; }
    public List<PopupAlertDto>? PopupAlerts { get; init; }
    public List<PendingDecisionDto>? PendingDecisions { get; init; }

    private static string TileKey(TileDto it) => $"{it.X.ToString(CultureInfo.InvariantCulture)},{it.Y.ToString(CultureInfo.InvariantCulture)}";
    private static string CityKey(CityDto it) => $"{it.X.ToString(CultureInfo.InvariantCulture)},{it.Y.ToString(CultureInfo.InvariantCulture)}";
    private static string UnitKey(UnitDto it) => it.Id.ToString(CultureInfo.InvariantCulture);
    private static string CivKey(CivDto it) => it.CivId;
    private static string DiplomacyKey(DiplomacyDto it) => $"{it.A}|{it.B}";

    private static string Json<T>(T value) => SnapshotBuilder.ToJson(value);

    /// <summary>What turns <paramref name="before"/> into <paramref name="after"/>; <see cref="Apply"/> is its inverse.</summary>
    public static SnapshotDiff Compute(ClientSnapshot before, ClientSnapshot after)
    {
        KeyedDiff<T>? Keyed<T>(List<T> old, List<T> now, Func<T, string> key) => Compare(old, now, key) is { IsEmpty: false } d ? d : null;
        List<T>? Whole<T>(List<T> old, List<T> now) => Json(old) == Json(now) ? null : now;

        return new SnapshotDiff
        {
            Seq = after.Seq,
            Turn = before.Turn == after.Turn ? null : after.Turn,
            Phase = before.Phase == after.Phase ? null : after.Phase,
            TurnStatus = Json(before.TurnStatus) == Json(after.TurnStatus) ? null : after.TurnStatus,
            Civs = Keyed(before.Civs, after.Civs, CivKey),
            Tiles = Keyed(before.Tiles, after.Tiles, TileKey),
            Cities = Keyed(before.Cities, after.Cities, CityKey),
            Units = Keyed(before.Units, after.Units, UnitKey),
            Diplomacy = Keyed(before.Diplomacy, after.Diplomacy, DiplomacyKey),
            PendingTrades = Whole(before.PendingTrades, after.PendingTrades),
            Notifications = Whole(before.Notifications, after.Notifications),
            PopupAlerts = Whole(before.PopupAlerts, after.PopupAlerts),
            PendingDecisions = Whole(before.PendingDecisions, after.PendingDecisions)
        };
    }

    /// <summary>The mirror after this diff. <see cref="ClientSnapshot.ViewHash"/> is recomputed from the result, so the caller compares it with the hash the host sent.</summary>
    public ClientSnapshot Apply(ClientSnapshot mirror)
    {
        var applied = mirror with
        {
            Seq = Seq,
            Turn = Turn ?? mirror.Turn,
            Phase = Phase ?? mirror.Phase,
            TurnStatus = TurnStatus ?? mirror.TurnStatus,
            Civs = Merge(mirror.Civs, Civs, CivKey),
            Tiles = Merge(mirror.Tiles, Tiles, TileKey),
            Cities = Merge(mirror.Cities, Cities, CityKey),
            Units = Merge(mirror.Units, Units, UnitKey),
            Diplomacy = Merge(mirror.Diplomacy, Diplomacy, DiplomacyKey),
            PendingTrades = PendingTrades ?? mirror.PendingTrades,
            Notifications = Notifications ?? mirror.Notifications,
            PopupAlerts = PopupAlerts ?? mirror.PopupAlerts,
            PendingDecisions = PendingDecisions ?? mirror.PendingDecisions,
            ViewHash = null
        };
        return applied with { ViewHash = SnapshotBuilder.ComputeViewHash(applied) };
    }

    private static KeyedDiff<T> Compare<T>(List<T> before, List<T> after, Func<T, string> key)
    {
        var oldJson = new Dictionary<string, string>();
        foreach (var item in before) oldJson[key(item)] = Json(item);
        var afterKeys = after.Select(key).ToList();
        var afterSet = afterKeys.ToHashSet();
        var changed = new List<T>();
        var added = new List<T>();
        var addedAt = new List<int>();
        for (var i = 0; i < after.Count; i++)
        {
            if (!oldJson.TryGetValue(afterKeys[i], out var json)) { added.Add(after[i]); addedAt.Add(i); }
            else if (json != Json(after[i])) changed.Add(after[i]);
        }
        var removed = before.Select(key).Where(it => !afterSet.Contains(it)).ToList();
        var survivorsBefore = before.Select(key).Where(afterSet.Contains);
        var survivorsAfter = afterKeys.Where(oldJson.ContainsKey);
        return new KeyedDiff<T>
        {
            Changed = changed,
            Added = added,
            AddedAt = addedAt,
            Removed = removed,
            Order = survivorsBefore.SequenceEqual(survivorsAfter) ? null : afterKeys
        };
    }

    private static List<T> Merge<T>(List<T> current, KeyedDiff<T>? diff, Func<T, string> key)
    {
        if (diff == null) return current;
        var removed = diff.Removed.ToHashSet();
        var replacements = diff.Changed.ToDictionary(key);
        var result = new List<T>(current.Count + diff.Added.Count);
        foreach (var item in current)
        {
            var itemKey = key(item);
            if (!removed.Contains(itemKey)) result.Add(replacements.GetValueOrDefault(itemKey, item));
        }
        if (diff.Order != null)
        {
            result.AddRange(diff.Added);
            var position = new Dictionary<string, int>();
            for (var i = 0; i < diff.Order.Count; i++) position[diff.Order[i]] = i;
            return [.. result.OrderBy(it => position[key(it)])];
        }
        for (var i = 0; i < diff.Added.Count; i++) result.Insert(diff.AddedAt[i], diff.Added[i]);
        return result;
    }
}
