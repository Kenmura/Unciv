using Unciv.Commands;
using Unciv.Models.Ruleset;
using Unciv.Net.Visibility;
using Unciv.Queries;

namespace Unciv.GodotClient.Play;

/// <summary>
/// One batch of events the viewer may see (already filtered for the viewer). <see cref="Seq"/> is the sequence number of the command that produced them;
/// a consumer that sees a gap in the sequence it has been given has missed events and must call <see cref="IGameSession.ResyncAsync"/>.
/// </summary>
public sealed record EventBatch(long Seq, IReadOnlyList<GameEvent> Events);

/// <summary>
/// C037: the only door between the client and the game (design/CLIENT-SLICES.md principle 2). Client code reads the world from <see cref="Snapshot"/>
/// (the viewer's own ClientSnapshot, kept current by the implementation: rebuilt locally, or a snapshot plus deltas over the network), asks previews
/// through <see cref="QueryAsync{TResult}"/>, and changes the game only by <see cref="SubmitAsync"/>. It never sees a GameInfo, a clock, a seed or
/// another civ's private state. Everything that can take time is a Task, so the remote implementation (T180 ClientSession) slots in without changing callers;
/// the local one completes synchronously. A source-policy test keeps GameInfo out of the rest of Unciv.Godot.
/// <para>Async error contract: a command or query the host refuses is a <b>result</b> (<see cref="SubmitResult.Accepted"/> false, <see cref="TurnOutcome.Accepted"/>
/// false, a null query result), never an exception. A <b>transport failure</b> (connection lost, timeout, protocol violation) faults the returned Task with an
/// exception, which callers must observe and show; they must not drop the Task. Events are filtered to what the viewer may see before they reach
/// <see cref="EventsReceived"/>, <see cref="SubmitResult.Events"/> and <see cref="TurnOutcome.Events"/>.</para>
/// </summary>
public interface IGameSession
{
    /// <summary>The civ this client plays as; every command and query is made as that civ.</summary>
    string ViewerCivId { get; }

    /// <summary>C089: the id of the game, which keys the player's private client-side data (map markers); null when the session does not know it (a joined network game), then such data is not persisted.</summary>
    string? GameId => null;

    /// <summary>The static ruleset (names, colours, costs by name). Read-only data identical on every peer, so it may be read directly.</summary>
    Ruleset Ruleset { get; }

    /// <summary>The current mirror of the viewer's view of the game; replaced (never mutated) when it changes.</summary>
    ClientSnapshot Snapshot { get; }

    /// <summary>Raised after <see cref="Snapshot"/> was replaced, with the new value.</summary>
    event Action<ClientSnapshot>? SnapshotChanged;

    /// <summary>Raised for every batch of engine events the viewer may see (own commands, other civs' turns, server commands), in order, numbered by command sequence.</summary>
    event Action<EventBatch>? EventsReceived;

    /// <summary>Raised when the session knows its mirror may be out of date (event sequence gap, reconnect) with a reason; the session then resyncs by itself or the client calls <see cref="ResyncAsync"/>.</summary>
    event Action<string>? Desynchronized;

    /// <summary>Replaces <see cref="Snapshot"/> with a fresh full snapshot of the viewer's view (raising <see cref="SnapshotChanged"/>). Faults on transport failure.</summary>
    Task ResyncAsync();

    /// <summary>A preview question as the viewer. A refused query (unknown unit, unexplored target) completes with null, never throws.</summary>
    Task<TResult?> QueryAsync<TResult>(IQuery<TResult> query) where TResult : class;

    /// <summary>Submits one command as the viewer. Rejection is a result, not an exception.</summary>
    Task<SubmitResult> SubmitAsync(GameCommand command);

    /// <summary>
    /// Ends the viewer's turn and completes when the host has the viewer's next turn ready (or the end of the game, or a refusal).
    /// C040: the host may work off the calling thread, so the UI stays responsive; <paramref name="progress"/> is told before each automatic civ plays (it is called on the
    /// caller's synchronization context), and <paramref name="cancellation"/> stops between civs (a refusal "cancelled"; calling again continues). Faults on a real failure.
    /// </summary>
    Task<TurnOutcome> EndTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default);

    /// <summary>C047: plays the viewer's turn with the AI (the engine's AutoPlayTurn, after a StartAutoPlay) and completes like <see cref="EndTurnAsync"/> (same progress and cancellation); a refusal (no auto-play running, or it ended) is a result.</summary>
    Task<TurnOutcome> AutoPlayTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default);

    /// <summary>
    /// C016: the whole game as save-file text (what <c>LocalHost.FromSave</c> reads back), or null when this session does not hold the game (a network client: the host saves).
    /// Faults only on a real failure to serialize.
    /// </summary>
    Task<string?> ExportSaveAsync();
}
