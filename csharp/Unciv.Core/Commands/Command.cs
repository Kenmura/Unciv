using System.Text.Json;

namespace Unciv.Commands;

// A player intent against game state TState (T012, see migration/COMMANDS.md).
//
// Contract: Validate is pure (no mutation, no clock, no unseeded randomness). Apply is deterministic given the state and
// must not fail once Validate passed; any randomness comes from the RNG owned by TState. The acting player is passed in by
// the caller (the authenticated session), never read from the command's own payload.
public interface ICommand<in TState>
{
    string Type { get; }

    CommandValidation Validate(TState state, string actorId);

    IReadOnlyList<GameEvent> Apply(TState state, string actorId);
}

public readonly record struct CommandValidation(bool IsValid, string Code, string Message)
{
    public static CommandValidation Ok { get; } = new(true, "", "");

    public static CommandValidation Fail(string code, string message) => new(false, code, message);
}

// Machine-readable codes for failures the processor itself produces; commands add their own.
public static class CommandCodes
{
    public const string UnknownCommand = "unknown-command";
    public const string MalformedCommand = "malformed-command";
}

// A fact that happened, in the past tense, for clients to present (animation, notifications) or to apply as a diff.
public abstract record GameEvent
{
    public abstract string Type { get; }
}

// The wire form of a command. Seq is assigned by the authority (0 on the way in), ActorId by the authenticated session.
public sealed record CommandEnvelope(long Seq, string Type, string ActorId, JsonElement Payload);

public sealed record CommandResult(bool Accepted, long Seq, CommandValidation Validation, IReadOnlyList<GameEvent> Events);

// Replay found the log and the state disagree: the follower has diverged and must resync from a snapshot.
public sealed class CommandDesyncException(string message) : Exception(message);

// Apply threw after Validate had passed; the state may be half modified.
public sealed class CommandApplyException(string message, Exception inner) : Exception(message, inner);
