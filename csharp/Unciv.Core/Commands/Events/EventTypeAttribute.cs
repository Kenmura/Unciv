namespace Unciv.Commands.Events;

/// <summary>
/// Who may learn about an event at minimum. The per-recipient visibility classifier (T175) starts from this declaration and may
/// only add or redact detail; it never widens <see cref="Participants"/> events to uninvolved civs without a rule of its own.
/// </summary>
public enum EventAudience
{
    /// <summary>Carries no hidden information (turn flow); every recipient receives it.</summary>
    Everyone,

    /// <summary>Only the civs named by <see cref="EngineEvent.Participants"/>.</summary>
    Participants,

    /// <summary>The participants in full; any other recipient only if it currently sees one of <see cref="EngineEvent.Positions"/>.</summary>
    Located,
}

/// <summary>
/// Marks an <see cref="EngineEvent"/> class and gives it its stable wire name (kebab-case) and its audience class.
/// <see cref="GameEventRegistry"/> finds every marked class by scanning the assembly, so domain tickets add events
/// without editing a shared registry file. The audience is a required argument: no event type exists without one.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EventTypeAttribute(string name, EventAudience audience) : Attribute
{
    public string Name { get; } = name;

    public EventAudience Audience { get; } = audience;

    public static EventTypeAttribute Of(Type eventClass) =>
        (EventTypeAttribute?)GetCustomAttribute(eventClass, typeof(EventTypeAttribute), inherit: false)
        ?? throw new InvalidOperationException($"{eventClass.Name} has no [EventType] attribute");

    public static string NameOf(Type eventClass) => Of(eventClass).Name;
}
