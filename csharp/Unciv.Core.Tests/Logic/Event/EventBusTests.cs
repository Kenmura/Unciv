using Unciv.Logic.Event;

namespace Unciv.Core.Tests.Logic.Event;

public class EventBusTests : IDisposable
{
    public EventBusTests()
    {
        EventBus.Reset();
    }

    public void Dispose()
    {
        EventBus.Reset();
        GC.SuppressFinalize(this);
    }

    private sealed record TestBaseEvent(string Name) : IEvent;
    private sealed record TestDerivedEvent(string Name, int Value) : IEvent;

    [Fact]
    public void Send_DispatchesToRegisteredListeners()
    {
        using var receiver = new EventReceiver();
        TestBaseEvent? received = null;

        receiver.Receive<TestBaseEvent>(e => received = e);
        EventBus.Send(new TestBaseEvent("Hello"));

        Assert.NotNull(received);
        Assert.Equal("Hello", received.Name);
    }

    [Fact]
    public void Send_RespectsRegistrationOrder()
    {
        using var receiver = new EventReceiver();
        var callLog = new List<int>();

        receiver.Receive<TestBaseEvent>(_ => callLog.Add(1));
        receiver.Receive<TestBaseEvent>(_ => callLog.Add(2));
        receiver.Receive<TestBaseEvent>(_ => callLog.Add(3));

        EventBus.Send(new TestBaseEvent("OrderTest"));

        Assert.Equal([1, 2, 3], callLog);
    }

    [Fact]
    public void Send_DispatchesToSupertypeListeners()
    {
        using var receiver = new EventReceiver();
        var received = new List<string>();

        receiver.Receive<IEvent>(e => received.Add("IEvent:" + e.GetType().Name));
        receiver.Receive<TestDerivedEvent>(e => received.Add("Derived:" + e.Value));

        EventBus.Send(new TestDerivedEvent("Test", 42));

        Assert.Equal(["IEvent:TestDerivedEvent", "Derived:42"], received);
    }

    [Fact]
    public void Send_AppliesFiltersCorrectly()
    {
        using var receiver = new EventReceiver();
        var receivedValues = new List<int>();

        receiver.Receive<TestDerivedEvent>(
            filter: e => e.Value > 10,
            eventHandler: e => receivedValues.Add(e.Value)
        );

        EventBus.Send(new TestDerivedEvent("Small", 5));
        EventBus.Send(new TestDerivedEvent("Large", 20));
        EventBus.Send(new TestDerivedEvent("Exact", 10));
        EventBus.Send(new TestDerivedEvent("Larger", 30));

        Assert.Equal([20, 30], receivedValues);
    }

    [Fact]
    public void StopReceiving_UnregistersListeners()
    {
        var receiver = new EventReceiver();
        int count = 0;

        receiver.Receive<TestBaseEvent>(_ => count++);
        EventBus.Send(new TestBaseEvent("One"));
        Assert.Equal(1, count);

        receiver.StopReceiving();
        EventBus.Send(new TestBaseEvent("Two"));
        Assert.Equal(1, count);
    }

    [Fact]
    public void Reset_ClearsAllRegistrations()
    {
        var receiver = new EventReceiver();
        int count = 0;

        receiver.Receive<TestBaseEvent>(_ => count++);
        EventBus.Send(new TestBaseEvent("One"));
        Assert.Equal(1, count);

        EventBus.Reset();
        EventBus.Send(new TestBaseEvent("Two"));
        Assert.Equal(1, count);
    }

    [Fact]
    public void EventBusNestedEventReceiverAliasWorks()
    {
        using var receiver = new EventBus.EventReceiver();
        bool received = false;

        receiver.Receive<TestBaseEvent>(_ => received = true);
        EventBus.Send(new TestBaseEvent("AliasTest"));

        Assert.True(received);
    }

    private interface IMarker : IEvent;
    private interface IOther : IEvent;
    private class BaseEvent : IMarker;
    private sealed class DerivedEvent : BaseEvent, IOther;

    [Fact]
    public void Send_RunsListenersInGlobalRegistrationOrderAcrossTypes()
    {
        // Deviation from Kotlin (see EventBus remarks): the subtype listener registered first still runs first
        using var receiver = new EventReceiver();
        var log = new List<string>();

        receiver.Receive<TestDerivedEvent>(_ => log.Add("Derived"));
        receiver.Receive<IEvent>(_ => log.Add("IEvent"));
        receiver.Receive<TestDerivedEvent>(_ => log.Add("Derived2"));

        EventBus.Send(new TestDerivedEvent("x", 1));

        Assert.Equal(["Derived", "IEvent", "Derived2"], log);
    }

    [Fact]
    public void Send_NotifiesEachListenerOnceEvenWhenReachableOnSeveralPaths()
    {
        // Kotlin runs the IEvent listener twice here (once per path through the diamond); we run it once
        using var receiver = new EventReceiver();
        var log = new List<string>();

        receiver.Receive<DerivedEvent>(_ => log.Add("Derived"));
        receiver.Receive<IOther>(_ => log.Add("Other"));
        receiver.Receive<BaseEvent>(_ => log.Add("Base"));
        receiver.Receive<IMarker>(_ => log.Add("Marker"));
        receiver.Receive<IEvent>(_ => log.Add("IEvent"));
        receiver.Receive<DerivedEvent>(_ => log.Add("Derived2"));

        EventBus.Send(new DerivedEvent());

        Assert.Equal(["Derived", "Other", "Base", "Marker", "IEvent", "Derived2"], log);
    }

    [Fact]
    public void Send_ListenersChangedDuringDispatchAreHandledPredictably()
    {
        using var receiver = new EventReceiver();
        using var lateReceiver = new EventReceiver();
        var log = new List<string>();
        var victim = new EventReceiver();

        receiver.Receive<TestBaseEvent>(_ =>
        {
            log.Add("first");
            victim.StopReceiving(); // deactivated before its turn: skipped (Kotlin would still call it once)
            lateReceiver.Receive<TestBaseEvent>(_ => log.Add("late")); // registered during dispatch: next event
        });
        victim.Receive<TestBaseEvent>(_ => log.Add("victim"));

        EventBus.Send(new TestBaseEvent("one"));
        Assert.Equal(["first"], log);

        log.Clear();
        EventBus.Send(new TestBaseEvent("two"));
        Assert.Equal(["first", "late"], log);
    }

    [Fact]
    public void Send_FilterDecidesPerListenerAndRunsBeforeTheHandler()
    {
        using var receiver = new EventReceiver();
        var log = new List<string>();

        receiver.Receive<IEvent>(e => { log.Add("filter-all"); return true; }, _ => log.Add("handler-all"));
        receiver.Receive<TestDerivedEvent>(e => { log.Add("filter-no"); return false; }, _ => log.Add("handler-no"));

        EventBus.Send(new TestDerivedEvent("x", 1));

        Assert.Equal(["filter-all", "handler-all", "filter-no"], log);
    }
}
