using System.Text.Json;
using Unciv.Commands;
using Unciv.Json;

namespace Unciv.Core.Tests.Commands;

public class CommandTests
{
    // --- toy game: a shared counter that only the player whose turn it is may change ---

    private sealed class CounterState
    {
        public int Value { get; set; }
        public string CurrentPlayer { get; set; } = "p1";
        public List<string> History { get; } = [];
    }

    private sealed record ValueChanged(int NewValue) : GameEvent
    {
        public override string Type => "ValueChanged";
    }

    private sealed record TurnPassed(string NextPlayer) : GameEvent
    {
        public override string Type => "TurnPassed";
    }

    private sealed record AddCommand(int Amount) : ICommand<CounterState>
    {
        public string Type => "Add";

        public CommandValidation Validate(CounterState state, string actorId)
        {
            if (actorId != state.CurrentPlayer) return CommandValidation.Fail("not-your-turn", $"{actorId} is not the current player");
            if (Amount <= 0) return CommandValidation.Fail("bad-amount", "Amount must be positive");
            return CommandValidation.Ok;
        }

        public IReadOnlyList<GameEvent> Apply(CounterState state, string actorId)
        {
            state.Value += Amount;
            state.History.Add($"{actorId}+{Amount}");
            return [new ValueChanged(state.Value)];
        }
    }

    private sealed record EndTurnCommand : ICommand<CounterState>
    {
        public string Type => "EndTurn";

        public CommandValidation Validate(CounterState state, string actorId) =>
            actorId == state.CurrentPlayer ? CommandValidation.Ok : CommandValidation.Fail("not-your-turn", "not your turn");

        public IReadOnlyList<GameEvent> Apply(CounterState state, string actorId)
        {
            state.CurrentPlayer = state.CurrentPlayer == "p1" ? "p2" : "p1";
            state.History.Add($"{actorId} ends turn");
            return [new TurnPassed(state.CurrentPlayer)];
        }
    }

    // Broken on purpose: validates fine, then throws half way through Apply
    private sealed record ExplodingCommand : ICommand<CounterState>
    {
        public string Type => "Explode";
        public CommandValidation Validate(CounterState state, string actorId) => CommandValidation.Ok;

        public IReadOnlyList<GameEvent> Apply(CounterState state, string actorId)
        {
            state.Value = -1;
            throw new InvalidOperationException("boom");
        }
    }

    private static CommandRegistry<CounterState> NewRegistry()
    {
        var registry = new CommandRegistry<CounterState>(UncivJson.DefaultOptions);
        registry.Register<AddCommand>("Add");
        registry.Register<EndTurnCommand>("EndTurn");
        registry.Register<ExplodingCommand>("Explode");
        return registry;
    }

    private static CommandProcessor<CounterState> NewProcessor() => new(new CounterState(), NewRegistry());

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    // --- tests ---

    [Fact]
    public void ValidCommandIsAppliedAssignedASequenceNumberAndLogged()
    {
        var processor = NewProcessor();

        var result = processor.Submit("p1", new AddCommand(5));

        Assert.True(result.Accepted);
        Assert.Equal(1, result.Seq);
        Assert.Equal(5, processor.State.Value);
        Assert.Equal([new ValueChanged(5)], result.Events);
        Assert.Equal(1, processor.LastSeq);
        var logged = Assert.Single(processor.Log);
        Assert.Equal(1, logged.Seq);
        Assert.Equal("Add", logged.Type);
        Assert.Equal("p1", logged.ActorId);
    }

    [Fact]
    public void InvalidCommandIsRejectedWithoutChangingStateLogOrSequence()
    {
        var processor = NewProcessor();

        var wrongPlayer = processor.Submit("p2", new AddCommand(5));
        var badAmount = processor.Submit("p1", new AddCommand(0));

        Assert.False(wrongPlayer.Accepted);
        Assert.Equal("not-your-turn", wrongPlayer.Validation.Code);
        Assert.False(badAmount.Accepted);
        Assert.Equal("bad-amount", badAmount.Validation.Code);
        Assert.Empty(wrongPlayer.Events);
        Assert.Equal(0, processor.State.Value);
        Assert.Empty(processor.State.History);
        Assert.Empty(processor.Log);
        Assert.Equal(0, processor.LastSeq);
    }

    [Fact]
    public void ActorComesFromTheSubmitterNotFromThePayload()
    {
        var processor = NewProcessor();
        // a client trying to smuggle an actor into the payload gets ignored: the command type has no such member
        var envelope = new CommandEnvelope(0, "Add", "p2", Json("""{"amount": 3, "actorId": "p1"}"""));

        var result = processor.Submit(envelope);

        Assert.False(result.Accepted);
        Assert.Equal("not-your-turn", result.Validation.Code);
    }

    [Fact]
    public void UnknownCommandTypeAndMalformedPayloadAreRejectedNotThrown()
    {
        var processor = NewProcessor();

        var unknown = processor.Submit(new CommandEnvelope(0, "Teleport", "p1", Json("{}")));
        var malformed = processor.Submit(new CommandEnvelope(0, "Add", "p1", Json("""{"amount": "lots"}""")));
        var notAnObject = processor.Submit(new CommandEnvelope(0, "Add", "p1", Json("42")));

        Assert.Equal(CommandCodes.UnknownCommand, unknown.Validation.Code);
        Assert.Equal(CommandCodes.MalformedCommand, malformed.Validation.Code);
        Assert.Equal(CommandCodes.MalformedCommand, notAnObject.Validation.Code);
        Assert.Empty(processor.Log);
        Assert.False(processor.IsFaulted);
    }

    [Fact]
    public void EnvelopesRoundTripThroughJson()
    {
        var registry = NewRegistry();
        var envelope = registry.ToEnvelope(7, "p1", new AddCommand(9));

        string json = JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions);
        var back = JsonSerializer.Deserialize<CommandEnvelope>(json, UncivJson.DefaultOptions)!;
        var command = registry.FromEnvelope(back);

        Assert.Equal(7, back.Seq);
        Assert.Equal("Add", back.Type);
        Assert.Equal("p1", back.ActorId);
        Assert.Equal(new AddCommand(9), command);
    }

    [Fact]
    public void RegistryRejectsDuplicateAndMismatchedRegistrations()
    {
        var registry = NewRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.Register<AddCommand>("Add"));
        Assert.Throws<InvalidOperationException>(() => registry.Register<AddCommand>("Other"));
        // an instance whose Type does not match its registration cannot be serialized
        Assert.Throws<InvalidOperationException>(() => registry.ToEnvelope(1, "p1", new MislabelledCommand()));
    }

    private sealed record MislabelledCommand : ICommand<CounterState>
    {
        public string Type => "Add"; // registered for AddCommand
        public CommandValidation Validate(CounterState state, string actorId) => CommandValidation.Ok;
        public IReadOnlyList<GameEvent> Apply(CounterState state, string actorId) => [];
    }

    [Fact]
    public void ReplayingTheLogReproducesTheStateExactly()
    {
        // The lockstep property: same initial state + same ordered log gives the same state, with no other inputs.
        var server = NewProcessor();
        server.Submit("p1", new AddCommand(2));
        server.Submit("p1", new AddCommand(3));
        server.Submit("p1", new EndTurnCommand());
        server.Submit("p2", new AddCommand(10));
        server.Submit("p1", new AddCommand(99)); // rejected: not p1's turn, not logged

        string wire = JsonSerializer.Serialize(server.Log, UncivJson.DefaultOptions);
        var follower = NewProcessor();
        foreach (var envelope in JsonSerializer.Deserialize<List<CommandEnvelope>>(wire, UncivJson.DefaultOptions)!)
            follower.Replay(envelope);

        Assert.Equal(server.LastSeq, follower.LastSeq);
        Assert.Equal(4, follower.LastSeq);
        Assert.Equal(server.State.Value, follower.State.Value);
        Assert.Equal(15, follower.State.Value);
        Assert.Equal(server.State.CurrentPlayer, follower.State.CurrentPlayer);
        Assert.Equal(server.State.History, follower.State.History);
    }

    [Fact]
    public void ReplayDemandsContiguousSequenceNumbers()
    {
        var server = NewProcessor();
        server.Submit("p1", new AddCommand(1));
        server.Submit("p1", new AddCommand(1));
        var follower = NewProcessor();

        var gap = Assert.Throws<CommandDesyncException>(() => follower.Replay(server.Log[1]));
        Assert.Contains("expected seq 1", gap.Message, StringComparison.Ordinal);

        follower.Replay(server.Log[0]);
        Assert.Throws<CommandDesyncException>(() => follower.Replay(server.Log[0])); // duplicate
    }

    [Fact]
    public void ReplayOfACommandThatNoLongerValidatesIsADesync()
    {
        var follower = NewProcessor();
        follower.State.CurrentPlayer = "p2"; // diverged state: p1's logged command is not valid here

        var registry = NewRegistry();
        var envelope = registry.ToEnvelope(1, "p1", new AddCommand(1));

        Assert.Throws<CommandDesyncException>(() => follower.Replay(envelope));
        Assert.Equal(0, follower.State.Value);
    }

    [Fact]
    public void ApplyFailingAfterValidationFaultsTheProcessor()
    {
        var processor = NewProcessor();

        var exception = Assert.Throws<CommandApplyException>(() => processor.Submit("p1", new ExplodingCommand()));

        Assert.Equal("boom", exception.InnerException!.Message);
        Assert.True(processor.IsFaulted);
        Assert.Empty(processor.Log);
        // state may be partially mutated, so the processor refuses to go on: reload from the log/snapshot instead
        Assert.Throws<InvalidOperationException>(() => processor.Submit("p1", new AddCommand(1)));
        Assert.Throws<InvalidOperationException>(() => processor.Replay(NewRegistry().ToEnvelope(1, "p1", new AddCommand(1))));
    }
}
