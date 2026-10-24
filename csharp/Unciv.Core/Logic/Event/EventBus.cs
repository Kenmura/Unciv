namespace Unciv.Logic.Event;

/// <summary>
/// Deterministic, synchronous event bus after Kotlin's <c>com.unciv.logic.event.EventBus</c>.
/// </summary>
/// <remarks>
/// Deliberate deviations (checked by running the real Kotlin EventBus on <c>Derived : Base, Other</c>, <c>Base : Marker</c>,
/// with every interface extending <c>Event</c>, listeners registered most specific first):
/// <list type="bullet">
/// <item>Kotlin dispatches grouped by registered class, ancestors before the concrete class
/// (<c>[Event, Marker, Base, Event, Other, Derived, Derived2]</c>); registration order only counts within one class.
/// Here listeners run in global registration order across all matching types (<c>[Derived, Other, Base, Marker, Event, Derived2]</c>),
/// because .NET gives no declared supertype order to reproduce.</item>
/// <item>Kotlin notifies a listener once per path through the type hierarchy (the "set" holds fresh wrapper objects), so a diamond
/// runs a listener twice. Here each listener runs at most once per event.</item>
/// <item>Kotlin dispatches over a snapshot, so a listener removed by an earlier handler of the same event still runs once.
/// Here a listener deactivated during dispatch is skipped; a listener registered during dispatch waits for the next event.</item>
/// <item>No weak references: a receiver that is never stopped or disposed keeps its listeners alive.</item>
/// </list>
/// </remarks>
public sealed class EventBusInstance
{
    private long nextSeq;
    private readonly Dictionary<Type, List<EventBus.ListenerRegistration>> listenersByType = [];
    private readonly object syncLock = new();

    public void Send<T>(T @event) where T : IEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        Type eventType = @event.GetType();
        List<EventBus.ListenerRegistration> toInvoke;

        lock (syncLock)
        {
            var matchingListeners = new List<EventBus.ListenerRegistration>();
            var seen = new HashSet<EventBus.ListenerRegistration>();

            foreach (var (targetType, list) in listenersByType)
            {
                if (targetType.IsAssignableFrom(eventType))
                {
                    list.RemoveAll(r => !r.IsActive);
                    foreach (var reg in list)
                    {
                        if (reg.IsActive && seen.Add(reg))
                        {
                            matchingListeners.Add(reg);
                        }
                    }
                }
            }

            // Order deterministically by registration sequence (OrderBy is stable)
            toInvoke = matchingListeners.OrderBy(r => r.Seq).ToList();
        }

        foreach (var reg in toInvoke)
        {
            if (!reg.IsActive) continue;
            if (reg.Filter == null || reg.Filter(@event))
            {
                reg.Handler(@event);
            }
        }
    }

    internal EventBus.ListenerRegistration Register(Type targetType, Func<IEvent, bool>? filter, Action<IEvent> handler)
    {
        lock (syncLock)
        {
            long seq = unchecked(++nextSeq);
            var reg = new EventBus.ListenerRegistration(seq, targetType, filter, handler);
            if (!listenersByType.TryGetValue(targetType, out var list))
            {
                list = [];
                listenersByType[targetType] = list;
            }
            list.Add(reg);
            return reg;
        }
    }

    internal void Unregister(EventBus.ListenerRegistration reg)
    {
        lock (syncLock)
        {
            reg.IsActive = false;
            if (listenersByType.TryGetValue(reg.TargetType, out var list))
            {
                list.Remove(reg);
            }
        }
    }

    public void Reset()
    {
        lock (syncLock)
        {
            listenersByType.Clear();
            nextSeq = 0;
        }
    }
}

public static class EventBus
{
    private static readonly EventBusInstance DefaultBus = new();

    internal sealed class ListenerRegistration(long seq, Type targetType, Func<IEvent, bool>? filter, Action<IEvent> handler)
    {
        public long Seq { get; } = seq;
        public Type TargetType { get; } = targetType;
        public Func<IEvent, bool>? Filter { get; } = filter;
        public Action<IEvent> Handler { get; } = handler;
        public bool IsActive { get; set; } = true;
    }

    public static void Send<T>(T @event) where T : IEvent => DefaultBus.Send(@event);

    internal static ListenerRegistration Register(Type targetType, Func<IEvent, bool>? filter, Action<IEvent> handler) =>
        DefaultBus.Register(targetType, filter, handler);

    internal static void Unregister(ListenerRegistration reg) =>
        DefaultBus.Unregister(reg);

    public static void Reset() => DefaultBus.Reset();

    public class EventReceiver : Unciv.Logic.Event.EventReceiver
    {
    }
}

/// <summary>
/// Manages listener registrations for an object lifecycle, supporting <see cref="StopReceiving"/> or <see cref="IDisposable"/>.
/// </summary>
public class EventReceiver : IDisposable
{
    private readonly List<EventBus.ListenerRegistration> registrations = [];

    public void Receive<T>(Func<T, bool>? filter, Action<T> eventHandler) where T : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(eventHandler);
        Func<IEvent, bool>? wrappedFilter = filter == null
            ? null
            : e => e is T t && filter(t);

        Action<IEvent> wrappedHandler = e =>
        {
            if (e is T t) eventHandler(t);
        };

        var reg = EventBus.Register(typeof(T), wrappedFilter, wrappedHandler);
        registrations.Add(reg);
    }

    public void Receive<T>(Action<T> eventHandler) where T : class, IEvent =>
        Receive(null, eventHandler);

    public void Receive(Type eventType, Action<IEvent> eventHandler, Func<IEvent, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(eventHandler);
        var reg = EventBus.Register(eventType, filter, eventHandler);
        registrations.Add(reg);
    }

    public void StopReceiving()
    {
        foreach (var reg in registrations)
        {
            EventBus.Unregister(reg);
        }
        registrations.Clear();
    }

    public void Dispose()
    {
        StopReceiving();
        GC.SuppressFinalize(this);
    }
}
