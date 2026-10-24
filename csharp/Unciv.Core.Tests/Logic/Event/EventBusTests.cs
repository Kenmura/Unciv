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
}
