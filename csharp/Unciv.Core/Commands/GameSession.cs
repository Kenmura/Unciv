using Unciv.Logic;

namespace Unciv.Commands;

/// <summary>
/// A game plus the single writer that changes it: <see cref="GameInfo"/>, a <see cref="CommandProcessor{TState}"/> and the
/// scanned registry. Commands from any number of clients are applied one at a time in the order they reach
/// <see cref="Submit(CommandEnvelope)"/>; the processor numbers them (<c>Seq</c>) and logs them, which is all simultaneous
/// turns need (first applied wins through ordinary state-dependent validation).
/// </summary>
public sealed class GameSession
{
    private readonly object gate = new();
    private readonly CommandRegistry<GameInfo> registry;
    private readonly CommandProcessor<GameInfo> processor;

    public GameSession(GameInfo game, CommandRegistry<GameInfo>? registry = null)
    {
        this.registry = registry ?? GameCommandRegistry.Create();
        processor = new CommandProcessor<GameInfo>(game, this.registry);
    }

    public GameInfo Game => processor.State;

    public CommandRegistry<GameInfo> Registry => registry;

    public long LastSeq => processor.LastSeq;

    public IReadOnlyList<CommandEnvelope> Log => processor.Log;

    public bool IsFaulted => processor.IsFaulted;

    /// <summary>A command from an authenticated client. The reserved actor <c>server</c> is refused as malformed.</summary>
    public CommandResult Submit(string actorId, GameCommand command) =>
        Submit(registry.ToEnvelope(0, actorId, command));

    /// <summary>An inbound envelope from a client (untrusted). Its Seq is ignored and reassigned.</summary>
    public CommandResult Submit(CommandEnvelope envelope)
    {
        if (envelope.ActorId == CommandContext.ServerActor)
        {
            return new CommandResult(false, 0,
                CommandValidation.Fail(CommandCodes.MalformedCommand, "The actor \"server\" is reserved"), []);
        }
        return SubmitTrusted(envelope with { Seq = 0 });
    }

    /// <summary>A server command (turn flow, timeouts); only the host calls this.</summary>
    public CommandResult SubmitAsServer(GameCommand command) =>
        SubmitTrusted(registry.ToEnvelope(0, CommandContext.ServerActor, command));

    /// <summary>Follower role: apply an already numbered envelope from the host's log.</summary>
    public IReadOnlyList<GameEvent> Replay(CommandEnvelope envelope)
    {
        lock (gate) return processor.Replay(envelope);
    }

    private CommandResult SubmitTrusted(CommandEnvelope envelope)
    {
        lock (gate) return processor.Submit(envelope);
    }
}
