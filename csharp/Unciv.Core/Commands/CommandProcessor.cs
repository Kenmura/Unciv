using Unciv.Utils;

namespace Unciv.Commands;

// The single writer of a game's state. Server role: Submit validates, applies, numbers and logs a command. Follower /
// lockstep role: Replay applies another authority's log in order. Same state + same log = same state.
// lastSeq: the seq of the snapshot the state was loaded from (T194), so the log tail after it can be replayed.
public sealed class CommandProcessor<TState>(TState state, CommandRegistry<TState> registry, long lastSeq = 0)
{
    private readonly List<CommandEnvelope> log = [];

    public TState State { get; } = state;

    public long LastSeq { get; private set; } = lastSeq;

    public IReadOnlyList<CommandEnvelope> Log => log;

    // Set when Apply threw: the state may be half modified, so nothing more is accepted until it is rebuilt.
    public bool IsFaulted { get; private set; }

    public CommandResult Submit(string actorId, ICommand<TState> command) =>
        Submit(registry.ToEnvelope(0, actorId, command));

    public CommandResult Submit(CommandEnvelope envelope)
    {
        ThrowIfFaulted();
        if (!registry.TryFromEnvelope(envelope, out var command, out var failure))
            return Rejected(failure);

        var validation = command.NotNull().Validate(State, envelope.ActorId);
        if (!validation.IsValid)
            return Rejected(validation);

        var events = ApplyAndLog(command.NotNull(), envelope with { Seq = LastSeq + 1 });
        return new CommandResult(true, LastSeq, CommandValidation.Ok, events);
    }

    public IReadOnlyList<GameEvent> Replay(CommandEnvelope envelope)
    {
        ThrowIfFaulted();
        if (envelope.Seq != LastSeq + 1)
            throw new CommandDesyncException($"Log out of order: expected seq {LastSeq + 1}, got {envelope.Seq}");

        var command = registry.FromEnvelope(envelope);
        var validation = command.Validate(State, envelope.ActorId);
        if (!validation.IsValid)
            throw new CommandDesyncException($"Command {envelope.Seq} ({envelope.Type}) no longer validates: {validation.Code} {validation.Message}");

        return ApplyAndLog(command, envelope);
    }

    private IReadOnlyList<GameEvent> ApplyAndLog(ICommand<TState> command, CommandEnvelope envelope)
    {
        IReadOnlyList<GameEvent> events;
        try
        {
            using (TurnProbe.Measure("Command.Apply", envelope.Type)) events = command.Apply(State, envelope.ActorId);
        }
        catch (Exception e)
        {
            IsFaulted = true;
            throw new CommandApplyException($"Command {envelope.Seq} ({envelope.Type}) failed while applying", e);
        }
        log.Add(envelope);
        LastSeq = envelope.Seq;
        return events;
    }

    private void ThrowIfFaulted()
    {
        if (IsFaulted) throw new InvalidOperationException("The command processor is faulted; rebuild the state from a snapshot and the log");
    }

    private static CommandResult Rejected(CommandValidation validation) => new(false, 0, validation, []);
}
