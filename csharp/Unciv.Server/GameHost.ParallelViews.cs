using System.Runtime.ExceptionServices;
using Unciv.Commands.Events;
using Unciv.Logic.Civilization;
using Unciv.Net;
using Unciv.Net.Visibility;

namespace Unciv.Server;

/// <summary>
/// T297 (T263 verdict "go"): optional parallel per-recipient view building. Off unless <see cref="HostLimits.ParallelViewBuilding"/>, and only used when at
/// least two recipients are open. The work runs on the actor between commands, while the engine is idle: <see cref="BuildPerRecipient{T}"/> blocks the actor
/// until every task has finished, so no engine write can overlap a read. The results land in an array indexed by session position and are applied and sent in
/// the existing <c>sessions.Values</c> order, so the frames are byte-identical to the sequential path (each frame depends only on state, recipient and seq).
/// Rule for anything new that view building reaches: a lazy cache must be filled at <c>SetTransients</c> or be safe for concurrent writers (T296 audit,
/// <c>ViewBuildingReadOnlyTests</c>).
/// </summary>
public sealed partial class GameHost
{
    /// <summary>How many command rounds were built with tasks (a test and diagnostics counter; stays 0 while the option is off or below two recipients).</summary>
    public int ParallelViewRounds { get; private set; }

    private bool UseParallelViews(int recipients) => limits.ParallelViewBuilding && limits.MaxViewParallelism >= 2 && recipients >= 2;

    /// <summary>Runs <paramref name="work"/>(0..count-1) on at most <see cref="HostLimits.MaxViewParallelism"/> tasks and returns the results by index. Blocks until all tasks ended.</summary>
    private T[] BuildPerRecipient<T>(int count, Func<int, T> work)
    {
        var results = new T[count];
        var workers = Math.Min(limits.MaxViewParallelism, count);
        var tasks = new Task<int>[workers];
        for (var w = 0; w < workers; w++)
        {
            var first = w;
            // JUSTIFY: T297 host-side view building only (never engine logic, which stays single-threaded): read-only on the game, one recipient per index.
            tasks[w] = Task.Run(() =>
            {
                for (var i = first; i < count; i += workers) results[i] = work(i);
                return 0;
            });
        }

        try
        {
            // The actor waits here on purpose: the engine must stay idle until every reader has finished. WaitAll waits for all tasks even when one faults.
            Task.WaitAll(tasks);
        }
        catch (AggregateException ex)
        {
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }
        ParallelViewRounds++;
        return results;
    }

    private Dictionary<string, ViewDeltaBuilder.ViewMark> CaptureParallel(List<(HostSession Session, Civilization Civ)> targets)
    {
        var game = session.Game;
        var captured = BuildPerRecipient(targets.Count, i => ViewDeltaBuilder.Capture(game, targets[i].Civ));
        var marks = new Dictionary<string, ViewDeltaBuilder.ViewMark>(StringComparer.Ordinal);
        for (var i = 0; i < targets.Count; i++) marks[targets[i].Session.SessionId] = captured[i];
        return marks;
    }

    private void BroadcastParallel(List<(HostSession Session, Civilization Civ, ViewDeltaBuilder.ViewMark Mark)> targets, Unciv.Commands.CommandResult result, TurnStatusDto status)
    {
        var game = session.Game;
        var events = result.Events.OfType<EngineEvent>().ToList(); // enumerated once, then only read by the tasks
        var built = BuildPerRecipient(targets.Count, i =>
        {
            var (s, civ, mark) = targets[i];
            var (delta, after) = ViewDeltaBuilder.BuildWithDiff(game, civ, s.PreviousSnapshot!, mark, events, result.Seq, status);
            return (After: after, Frame: ProtocolCodec.Encode(delta));
        });
        for (var i = 0; i < targets.Count; i++)
        {
            var s = targets[i].Session;
            s.PreviousSnapshot = built[i].After;
            _ = SendFrameAsync(s, built[i].Frame);
        }
    }
}
