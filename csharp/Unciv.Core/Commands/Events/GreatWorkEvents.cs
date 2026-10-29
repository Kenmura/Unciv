namespace Unciv.Commands.Events;

// B007 (bnw-spec/SPEC.md section 2.4): the Great Work events. A Great Work is named data (no hidden information), but who holds which work
// is the owner's business until the classifier (T175) decides otherwise, so the audience is Participants: the civs named below.

/// <summary><paramref name="CivId"/> created <paramref name="Work"/> (a work of <paramref name="GreatWorkClass"/>) in a slot of one of its cities.</summary>
[EventType("great-work-created", EventAudience.Participants)]
public sealed record GreatWorkCreated(string CivId, string Work, string GreatWorkClass, string CityId, string Building, int Slot) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];
}

/// <summary><paramref name="CivId"/> moved <paramref name="Work"/> between two of its slots; a work that was in the target slot took the source slot.</summary>
[EventType("great-work-moved", EventAudience.Participants)]
public sealed record GreatWorkMoved(string CivId, string Work, string FromCityId, string FromBuilding, int FromSlot, string ToCityId, string ToBuilding, int ToSlot) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];
}

/// <summary><paramref name="CivId"/> took the offered <paramref name="Taken"/> of <paramref name="WithCivId"/> and gave <paramref name="Given"/>.</summary>
[EventType("great-work-swapped", EventAudience.Participants)]
public sealed record GreatWorkSwapped(string CivId, string WithCivId, string Given, string Taken) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId, WithCivId];
}
