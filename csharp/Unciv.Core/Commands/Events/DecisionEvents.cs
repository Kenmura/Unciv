namespace Unciv.Commands.Events;

/// <summary>
/// T226: a forced end of turn (<c>force-end-turn</c>, the host's timer) settled a decision the civ left open, the way the AI would have. Only the civ itself
/// is told which choice was made. <paramref name="Kind"/> is the <see cref="Unciv.Logic.DecisionKind"/>, <paramref name="Subject"/> what it was about
/// (a city, an alert type, or ""), <paramref name="Resolution"/> the choice in words.
/// </summary>
[EventType("decision-auto-resolved", EventAudience.Participants)]
public sealed record DecisionAutoResolved(string CivId, string Kind, string Subject, string Resolution) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];
}
