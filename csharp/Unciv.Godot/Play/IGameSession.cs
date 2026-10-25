using Unciv.Commands;
using Unciv.Models.Ruleset;
using Unciv.Net.Visibility;
using Unciv.Queries;

namespace Unciv.GodotClient.Play;

/// <summary>What one submitted command did, as the client needs it: whether the host took it and, if not, why, plus the events it produced.</summary>
public sealed record SubmitResult(bool Accepted, string Message, IReadOnlyList<GameEvent> Events);

/// <summary>
/// What one End turn did. <see cref="AiTurnsRun"/> and <see cref="NextTurnBegun"/> are informational: a remote session that cannot tell reports 0 / false.
/// </summary>
public sealed record TurnOutcome(bool Accepted, string Message, int AiTurnsRun, bool NextTurnBegun, IReadOnlyList<GameEvent> Events);

/// <summary>
/// C037: the only door between the client and the game (design/CLIENT-SLICES.md principle 2). Client code reads the world from <see cref="Snapshot"/>
/// (the viewer's own ClientSnapshot, kept current by the implementation: rebuilt locally, or a snapshot plus deltas over the network), asks previews
/// through <see cref="QueryAsync{TResult}"/>, and changes the game only by <see cref="SubmitAsync"/>. It never sees a GameInfo, a clock, a seed or
/// another civ's private state. Everything that can take time is a Task, so the remote implementation (T180 ClientSession) slots in without changing callers;
/// the local one completes synchronously. A source-policy test keeps GameInfo out of the rest of Unciv.Godot.
/// </summary>
public interface IGameSession
{
    /// <summary>The civ this client plays as; every command and query is made as that civ.</summary>
    string ViewerCivId { get; }

    /// <summary>The static ruleset (names, colours, costs by name). Read-only data identical on every peer, so it may be read directly.</summary>
    Ruleset Ruleset { get; }

    /// <summary>The current mirror of the viewer's view of the game; replaced (never mutated) when it changes.</summary>
    ClientSnapshot Snapshot { get; }

    /// <summary>Raised after <see cref="Snapshot"/> was replaced, with the new value.</summary>
    event Action<ClientSnapshot>? SnapshotChanged;

    /// <summary>Raised for every batch of engine events the viewer may see (own commands, other civs' turns, server commands), in order.</summary>
    event Action<IReadOnlyList<GameEvent>>? EventsReceived;

    /// <summary>A preview question as the viewer. A refused query (unknown unit, unexplored target) completes with null, never throws.</summary>
    Task<TResult?> QueryAsync<TResult>(IQuery<TResult> query) where TResult : class;

    /// <summary>Submits one command as the viewer. Rejection is a result, not an exception.</summary>
    Task<SubmitResult> SubmitAsync(GameCommand command);

    /// <summary>Ends the viewer's turn and completes when the host has the viewer's next turn ready (or the end of the game, or a refusal).</summary>
    Task<TurnOutcome> EndTurnAsync();
}
