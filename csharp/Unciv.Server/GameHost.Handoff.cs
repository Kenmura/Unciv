using Unciv.Commands.Log;
using Unciv.Logic;
using Unciv.Net;
using Unciv.Net.Persistence;

namespace Unciv.Server;

// T273: the graceful hand-off (design/HOST-MIGRATION.md section 2, path 1). A host that quits cleanly while its designated successor holds the whole log sends HostHandoff instead of HostClosing;
// nothing is lost, no vote is needed, and authority is the host's own authenticated channel at the current epoch.
public sealed partial class GameHost
{
    /// <summary>Why a hand-off was not made (the host keeps running and the caller closes the plain way).</summary>
    public static class HandoffRefusals
    {
        public const string Closed = "closed";
        public const string NoStore = "no-store";
        public const string NoSuccessor = "no-successor";
        public const string NotFlushed = "not-flushed";
        public const string StoreFailed = "store-failed";
    }

    /// <summary>
    /// Hands the game to the designated successor and closes this host. Waits up to <paramref name="flushTimeout"/> for the successor to hold everything this host has accepted, then, in one actor step (so no command can
    /// slip in between): checks it once more, sends <see cref="HostHandoff"/> (the new epoch, the successor's announced endpoints, the final seq and state hash, and <paramref name="departedKeyId"/>, the key of the
    /// quitting host's own seat) to the successor and to every client that advertised host migration (plain <see cref="HostClosing"/> to the rest), marks this directory superseded and writes the clean marker.
    /// Null on success. Otherwise a <see cref="HandoffRefusals"/> code and nothing was closed: no ready standby, a standby that did not catch up in time, or a host already closed.
    /// </summary>
    public async Task<string?> HandOffAsync(TimeSpan flushTimeout, string? departedKeyId = null)
    {
        using var deadline = new CancellationTokenSource(flushTimeout);
        while (true)
        {
            if (closed) return HandoffRefusals.Closed;
            string? refusal = null;
            var done = false;
            try
            {
                await EnqueueAsync(async () => (refusal, done) = await TryHandOffStepAsync(departedKeyId)).ConfigureAwait(false);
            }
            catch (InvalidOperationException) { return HandoffRefusals.Closed; } // the mailbox closed meanwhile
            if (done)
            {
                await StopActorAsync().ConfigureAwait(false);
                return null;
            }
            if (refusal != null) return refusal;
            if (deadline.IsCancellationRequested) return HandoffRefusals.NotFlushed;
            try { await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return HandoffRefusals.NotFlushed; }
        }
    }

    // One actor step: a hard refusal (a code), "not yet" (no code, not done), or the whole hand-off.
    private async Task<(string? Refusal, bool Done)> TryHandOffStepAsync(string? departedKeyId)
    {
        if (closed) return (HandoffRefusals.Closed, false);
        if (store is not FileGameStore file) return (HandoffRefusals.NoStore, false);
        var stream = standbyStream;
        var epoch = file.ReadMeta().HostEpoch;
        var announced = Standby.Announcement(epoch, []).Successor;
        if (stream == null || announced == null || !Standby.IsAcceptedCandidate(stream.Target.PlayerKeyId) || !stream.Target.IsOpen
            || !sessions.TryGetValue(stream.Target.SessionId, out var live) || !ReferenceEquals(live, stream.Target) || !stream.Target.HostMigration
            || !string.Equals(stream.Target.PlayerKeyId, announced.KeyId, StringComparison.Ordinal))
            return (HandoffRefusals.NoSuccessor, false);

        if (session.Game.TurnState is { Phase: SimultaneousPhase.Resolving }) PumpResolving(); // a half-resolved round finishes here: the final state is a safe point
        var finalSeq = store.LastSeq;
        var flushed = stream is { Phase: StreamPhase.Follow, AwaitingResetAck: false } && stream.SentSeq >= finalSeq && stream.AckedSeq >= finalSeq;
        if (!flushed)
        {
            PostStandbyStep(); // make sure whatever is still unsent goes out; the caller polls again
            return (null, false);
        }

        var handoff = new HostHandoff
        {
            GameId = session.Game.GameId,
            Epoch = epoch + 1,
            Successor = announced,
            FinalSeq = finalSeq,
            FinalHash = ReplayVerifier.FullHash(session.Game).ToString("x16", System.Globalization.CultureInfo.InvariantCulture),
            DepartedKeyId = departedKeyId,
        };
        try
        {
            file.RaiseEpochs(epoch, epoch + 1); // this directory is the old host's now: restarted by mistake it refuses everyone (stale-epoch) instead of forking the game
        }
        catch (Exception e) when (e is IOException or GameStoreException or UnauthorizedAccessException)
        {
            return (HandoffRefusals.StoreFailed, false); // nothing was told, nothing closed: the host keeps running
        }
        closed = true;
        await CloseSessionsAndStoreAsync(handoff).ConfigureAwait(false);
        return (null, true);
    }
}
