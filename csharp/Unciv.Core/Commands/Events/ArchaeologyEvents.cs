namespace Unciv.Commands.Events;

// B010 (bnw-spec/SPEC.md section 4.4): the archaeology events. What a site holds is hidden information, so only the digging civ hears of it.

/// <summary><paramref name="CivId"/> resolved a finished dig at (<paramref name="X"/>, <paramref name="Y"/>): the artifact <paramref name="Work"/> was taken, or a Landmark built.</summary>
[EventType("dig-resolved", EventAudience.Participants)]
public sealed record DigResolved(string CivId, int X, int Y, string Work, bool TookArtifact) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];
}
