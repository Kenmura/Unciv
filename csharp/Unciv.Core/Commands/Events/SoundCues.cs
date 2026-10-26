namespace Unciv.Commands.Events;

/// <summary>
/// C057: the sounds a client should play for a batch of events it received. A pure function of the events and the viewer, so the same stream always gives the same
/// cues (no clock, no UI state). The viewer filter already delivers a <see cref="SoundRequested"/> to its civ only; the civ check here is a second guard for a stream that was not filtered.
/// </summary>
public static class SoundCues
{
    /// <summary>The sound names for <paramref name="viewerCivId"/>, in event order, one per request.</summary>
    public static IReadOnlyList<string> For(IEnumerable<GameEvent> events, string viewerCivId) =>
        events.OfType<SoundRequested>()
            .Where(it => string.Equals(it.CivId, viewerCivId, StringComparison.Ordinal) && it.SoundName.Length > 0)
            .Select(it => it.SoundName)
            .ToList();
}
