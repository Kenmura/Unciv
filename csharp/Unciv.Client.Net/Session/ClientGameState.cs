using System.Text.Json;
using Unciv.Net;
using Unciv.Net.Visibility;

namespace Unciv.Client.Net.Session;

public enum DeltaOutcome
{
    Applied,
    /// <summary>Already seen (seq at or below the mirror's): ignored.</summary>
    Duplicate,
    Gap,
    NoSnapshot,
    NoDiff,
    BadDiff,
    HashMismatch
}

public enum SnapshotOutcome { Accepted, Malformed, HashMismatch }

/// <summary>
/// The client mirror of the viewer's filtered state (SERVER.md 2.3, T224): replaced by a <see cref="Snapshot"/> message, moved by <see cref="ViewDelta"/>s in sequence order.
/// A delta that cannot be applied (gap, hash mismatch, malformed or hostile diff) never touches the mirror: the caller resyncs from a full snapshot.
/// Reads are safe from any thread (the mirror is an immutable record swapped atomically); writes come from the session's receive loop only.
/// </summary>
public sealed class ClientGameState
{
    private volatile ClientSnapshot? snapshot;

    public ClientSnapshot? Snapshot => snapshot;

    /// <summary>Sequence number of the last applied command, or -1 before the first snapshot.</summary>
    public long Seq => snapshot?.Seq ?? -1;

    public string? ViewHash => snapshot?.ViewHash;

    public SnapshotOutcome Replace(Snapshot message)
    {
        ClientSnapshot built;
        try
        {
            built = new ClientSnapshot
            {
                Seq = message.Seq,
                Turn = message.Turn,
                Phase = message.Phase,
                TurnStatus = new TurnStatusDto { Ended = message.TurnStatus.Ended, Deadline = message.TurnStatus.Deadline, Phase = message.TurnStatus.Phase },
                You = message.You,
                Civs = Read<CivDto>(message.Civs),
                Tiles = Read<TileDto>(message.Tiles),
                Cities = Read<CityDto>(message.Cities),
                Units = Read<UnitDto>(message.Units),
                Diplomacy = Read<DiplomacyDto>(message.Diplomacy),
                PendingTrades = Read<PendingTradeDto>(message.PendingTrades),
                Notifications = Read<NotificationDto>(message.Notifications),
                PopupAlerts = Read<PopupAlertDto>(message.PopupAlerts),
                PendingDecisions = Read<PendingDecisionDto>(message.PendingDecisions),
                ViewHash = message.ViewHash
            };
        }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        {
            return SnapshotOutcome.Malformed;
        }
        if (SnapshotBuilder.ComputeViewHash(built) != message.ViewHash) return SnapshotOutcome.HashMismatch;
        snapshot = built;
        return SnapshotOutcome.Accepted;
    }

    /// <summary>Applies one delta if it is the next in sequence and reproduces the host's <see cref="ViewDelta.ViewHash"/>; otherwise says why not and leaves the mirror as it was.</summary>
    public DeltaOutcome ApplyDelta(ViewDelta delta)
    {
        var current = snapshot;
        if (current == null) return DeltaOutcome.NoSnapshot;
        if (delta.Seq <= current.Seq) return DeltaOutcome.Duplicate;
        if (delta.Seq != current.Seq + 1) return DeltaOutcome.Gap;
        if (delta.Diff is not { } diffElement || delta.ViewHash == null) return DeltaOutcome.NoDiff;

        ClientSnapshot next;
        try
        {
            var diff = SnapshotBuilder.FromElement<SnapshotDiff>(diffElement);
            if (diff.Seq != delta.Seq) return DeltaOutcome.BadDiff;
            next = diff.Apply(current);
        }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        {
            return DeltaOutcome.BadDiff;
        }
        if (next.ViewHash != delta.ViewHash) return DeltaOutcome.HashMismatch;
        snapshot = next;
        return DeltaOutcome.Applied;
    }

    private static List<T> Read<T>(List<JsonElement> elements)
    {
        List<T> result = new(elements.Count);
        foreach (var element in elements) result.Add(SnapshotBuilder.FromElement<T>(element));
        return result;
    }
}
