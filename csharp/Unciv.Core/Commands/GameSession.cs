using System.Text.Json;
using System.Text.Json.Nodes;
using Unciv.Commands.Turn;
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

    /// <summary>
    /// The authority's clock (epoch milliseconds). When set, every <see cref="ITimedCommand"/> is stamped with it before it is validated and logged and the
    /// value a client sent is discarded; the log then carries the time, so replay never reads a clock. Null: the command's own value is kept (tests, replay hosts).
    /// </summary>
    public Func<long>? Clock { get; init; }

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
        lock (gate) return processor.Submit(Stamp(envelope));
    }

    private CommandEnvelope Stamp(CommandEnvelope envelope)
    {
        if (Clock == null || registry.TypeOf(envelope.Type) is not { } type || !typeof(ITimedCommand).IsAssignableFrom(type)) return envelope;
        if (envelope.Payload.ValueKind != JsonValueKind.Object) return envelope; // the processor rejects it as malformed
        // Rebuilt from the element so duplicate or case-variant "now" keys cannot throw or survive: every one is dropped, one authoritative value is added.
        var payload = new JsonObject();
        foreach (var property in envelope.Payload.EnumerateObject())
        {
            if (string.Equals(property.Name, nameof(ITimedCommand.Now), StringComparison.OrdinalIgnoreCase)) continue;
            payload[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }
        payload["now"] = Clock();
        return envelope with { Payload = JsonSerializer.SerializeToElement(payload) };
    }
}
