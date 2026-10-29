using Unciv.Json;
using Unciv.Net;
using Unciv.Net.Persistence;
using Unciv.Net.Standby;
using Unciv.Server.Transport;

namespace Unciv.Server;

// T272: standby replication to the designated backup host (design/HOST-MIGRATION.md section 1). Asynchronous: the host never waits for the successor. The stream reads the command log
// back from the store (the verbatim lines, so the successor's file is the host's file) whenever the store reports a write, and paces itself on the session's outbound queue, so a slow or
// dead successor can neither stall the game nor get its connection (and so its seat) dropped as a slow reader. Everything here runs on the actor except the store's change callback, which only signals.
public sealed partial class GameHost
{
    private enum StreamPhase
    {
        /// <summary>Sending a snapshot file in chunks.</summary>
        Snapshot,

        /// <summary>Sending log lines as they appear (nothing to send means the successor is caught up).</summary>
        Follow,

        /// <summary>Too far behind: nothing is sent until everything already sent is acked, then a snapshot resync follows.</summary>
        Lagging,
    }

    private sealed class StandbyStream(HostSession target, int epoch)
    {
        public HostSession Target { get; } = target;
        public int Epoch { get; } = epoch;
        public StreamPhase Phase { get; set; } = StreamPhase.Follow;

        /// <summary>Highest log seq (commands) sent to the successor.</summary>
        public long SentSeq { get; set; }

        /// <summary>Highest seq the successor acked (-1: none yet).</summary>
        public long AckedSeq { get; set; } = -1;

        public long Offset { get; set; }
        public long Generation { get; set; } = -1;
        public long LastProgressMillis { get; set; }
        public long LastSnapshotSent { get; set; } = -1;
        public bool CaughtUp { get; set; }
        public bool Synced { get; set; }
        public long SyncTarget { get; set; } = -1;
        public string? LastMeta { get; set; }
        public volatile bool SnapshotDirty;
        public volatile bool MetaDirty;

        // A restart from a snapshot: acks sent before the successor saw the snapshot are in flight, so none counts until the one for the snapshot's own seq.
        public bool AwaitingResetAck { get; set; }
        public long ResetSeq { get; set; } = -1;

        public byte[]? SnapBytes { get; set; }
        public long SnapSeq { get; set; }
        public int SnapNext { get; set; }
        public int SnapParts { get; set; }
        public string SnapSha { get; set; } = "";
        public bool SnapReset { get; set; }
    }

    private StandbyStream? standbyStream;
    private int standbyStepPosted;
    private long lastStandbySubscribeMillis = long.MinValue;

    private void InitReplication()
    {
        if (store is IReplicationSource source) source.Changed += OnStoreChanged;
    }

    // Called inside the store's lock, possibly off the actor: it may only signal.
    private void OnStoreChanged(ReplicationChange change)
    {
        var current = Volatile.Read(ref standbyStream);
        if (current == null) return;
        if (change == ReplicationChange.Snapshot) current.SnapshotDirty = true;
        else if (change == ReplicationChange.Meta) current.MetaDirty = true;
        PostStandbyStep();
    }

    private void PostStandbyStep()
    {
        if (Interlocked.Exchange(ref standbyStepPosted, 1) != 0) return;
        var written = mailbox.Writer.TryWrite(async () =>
        {
            Volatile.Write(ref standbyStepPosted, 0);
            try
            {
                await StandbyStepAsync();
            }
            catch (Exception e) when (e is GameStoreException or IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
            {
                StopStandbyStream(); // a store that cannot be read ends the stream; the successor resubscribes after its next reconnect
            }
        });
        if (!written) Volatile.Write(ref standbyStepPosted, 0);
    }

    private void PostStandbyStepLater(TimeSpan delay)
    {
        try
        {
            _ = Task.Delay(delay, cts.Token).ContinueWith(_ => PostStandbyStep(), TaskScheduler.Default);
        }
        catch (ObjectDisposedException) { /* the host closed */ }
    }

    /// <summary>Ends the stream (revoke, decline, the successor's session ended). Nothing Standby-related is sent afterwards. True when a stream existed.</summary>
    private bool StopStandbyStream()
    {
        var current = Interlocked.Exchange(ref standbyStream, null);
        if (current == null) return false;
        Standby.SetReplication(null, 0);
        return true;
    }

    private int PacingCap() => Math.Max(2, Math.Min(Standby.Limits.MaxQueuedFrames, limits.MaxQueuedFrames / 2));

    private static int QueuedFrames(HostSession target) => (target.Transport as BoundedSendTransport)?.QueuedFrames ?? 0;

    // ------------------------------------------------------------ subscribe and ack (candidate to host)

    private async Task ProcessStandbySubscribeAsync(HostSession from, StandbySubscribe subscribe)
    {
        // Judged by the key the host authenticated for this session, never by a field of the frame; one generic code for everyone else.
        if (!Standby.IsAcceptedCandidate(from.PlayerKeyId))
        {
            await SafeSendAsync(from, new Error { Code = ErrorCodes.NotDesignated });
            return;
        }
        if (store is not IReplicationSource source || !string.Equals(subscribe.GameId, session.Game.GameId, StringComparison.Ordinal))
        {
            await SafeSendAsync(from, new Error { Code = ErrorCodes.InvalidMessage });
            return;
        }
        var now = clock.NowMillis;
        if (lastStandbySubscribeMillis != long.MinValue && now - lastStandbySubscribeMillis < Standby.Limits.MinSubscribeMillis)
        {
            await SafeSendAsync(from, new Error { Code = ErrorCodes.StandbyViolation, Detail = "subscribe-rate" }); // a snapshot send is the costly part: the designated key may not trigger it in a loop
            return;
        }
        lastStandbySubscribeMillis = now;
        StopStandbyStream();
        var stream = new StandbyStream(from, store.ReadMeta().HostEpoch) { LastProgressMillis = clock.NowMillis };
        Volatile.Write(ref standbyStream, stream);
        if (CanContinue(source, subscribe))
        {
            stream.SentSeq = subscribe.HaveSeq;
            stream.AckedSeq = subscribe.HaveSeq;
            await SendStandbyMetaAsync(stream, withHeader: false);
        }
        else
        {
            await BeginResetAsync(stream);
        }
        PostStandbyStep();
    }

    // The successor's store continues the host's log when it holds the host's own line at HaveSeq (same game history) and the gap is not larger than the lag limit.
    private bool CanContinue(IReplicationSource source, StandbySubscribe subscribe)
    {
        var have = subscribe.HaveSeq;
        var last = store!.LastSeq;
        if (have < 0 || have > last || last - have > Standby.Limits.MaxLagEntries) return false;
        if (have == 0) return subscribe.HaveLineSha256 == null;
        if (subscribe.HaveLineSha256 is not { Length: 64 } claimed) return false;
        // The command at HaveSeq or, for a store that holds only a reset's checkpoint, the checkpoint line at HaveSeq.
        return source.ReadReplicationBatch(have - 1, 0, 0, 8).Lines.Any(it => it.Seq == have && string.Equals(StandbyStore.LineHash(it.Text), claimed, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ProcessStandbyAckAsync(HostSession from, StandbyAck ack)
    {
        var stream = standbyStream;
        if (stream == null || !ReferenceEquals(stream.Target, from) || !Standby.IsAcceptedCandidate(from.PlayerKeyId))
        {
            await SafeSendAsync(from, new Error { Code = ErrorCodes.NotDesignated });
            return;
        }
        if (stream.AwaitingResetAck)
        {
            if (stream.ResetSeq < 0 || ack.Seq != stream.ResetSeq) return; // acks sent before the successor saw the snapshot: not counted, not a violation
            stream.AwaitingResetAck = false;
            stream.AckedSeq = ack.Seq;
            stream.LastProgressMillis = clock.NowMillis;
        }
        else
        {
            if (ack.Seq < stream.AckedSeq || ack.Seq > Math.Min(stream.SentSeq, store!.LastSeq))
            {
                StopStandbyStream();
                await SafeSendAsync(from, new Error { Code = ErrorCodes.StandbyViolation });
                await AnnounceSuccessorAsync();
                return;
            }
            if (ack.Seq > stream.AckedSeq) stream.LastProgressMillis = clock.NowMillis;
            stream.AckedSeq = ack.Seq;
        }
        PostStandbyStep();
    }

    // ------------------------------------------------------------ the stream

    private async Task SendStandbyMetaAsync(StandbyStream stream, bool withHeader)
    {
        var source = (IReplicationSource)store!;
        // game.json as the successor keeps it: not this host's own bookkeeping (what it has seen from peers, who it designated).
        var text = UncivJson.Serialize(store!.ReadMeta() with { SeenEpoch = 0, Standby = null });
        if (!withHeader && string.Equals(text, stream.LastMeta, StringComparison.Ordinal)) return;
        stream.LastMeta = text;
        await SafeSendAsync(stream.Target, new StandbyMeta { Epoch = stream.Epoch, Header = withHeader ? source.ReadHeaderLine() : null, Meta = text });
    }

    private async Task BeginResetAsync(StandbyStream stream)
    {
        stream.Phase = StreamPhase.Snapshot;
        stream.SnapReset = true;
        stream.SnapBytes = null;
        stream.AwaitingResetAck = true;
        stream.ResetSeq = -1;
        stream.AckedSeq = -1;
        stream.Synced = false;
        stream.SyncTarget = -1;
        stream.CaughtUp = false;
        stream.LastProgressMillis = clock.NowMillis;
        await SendStandbyMetaAsync(stream, withHeader: true);
    }

    private async Task StandbyStepAsync()
    {
        var stream = standbyStream;
        if (stream == null || store is not IReplicationSource source) return;
        var target = stream.Target;
        // Defence in depth: whatever path changed the designation, no frame goes to a session that is not the consenting designated key.
        if (!Standby.IsAcceptedCandidate(target.PlayerKeyId) || !target.IsOpen || !sessions.TryGetValue(target.SessionId, out var live) || !ReferenceEquals(live, target))
        {
            StopStandbyStream();
            return;
        }

        var now = clock.NowMillis;
        if (stream.Phase == StreamPhase.Follow && IsLagging(stream, now)) stream.Phase = StreamPhase.Lagging;
        if (stream.Phase == StreamPhase.Lagging && !stream.AwaitingResetAck && stream.AckedSeq >= stream.SentSeq) await BeginResetAsync(stream);

        var cap = PacingCap();
        var batch = Math.Max(1, Math.Min(Standby.Limits.BatchLines, cap / 2));
        for (var guard = 0; guard < 10_000; guard++)
        {
            if (stream.Phase == StreamPhase.Lagging) break;
            if (QueuedFrames(target) >= cap)
            {
                PostStandbyStepLater(TimeSpan.FromMilliseconds(10));
                break;
            }
            var more = stream.Phase == StreamPhase.Snapshot ? await SendSnapshotChunkAsync(stream, source) : await SendFollowBatchAsync(stream, source, batch);
            if (!more) break;
        }
        if (!ReferenceEquals(standbyStream, stream)) return;
        await ReportReplicationAsync(stream);
    }

    private bool IsLagging(StandbyStream stream, long now)
    {
        var acked = stream.AwaitingResetAck ? (stream.ResetSeq >= 0 ? stream.ResetSeq : store!.LastSeq) : stream.AckedSeq;
        if (store!.LastSeq - Math.Max(acked, 0) > Standby.Limits.MaxLagEntries) return true;
        var outstanding = stream.AwaitingResetAck ? stream.Phase != StreamPhase.Snapshot : stream.SentSeq > stream.AckedSeq;
        return outstanding && now - stream.LastProgressMillis > Standby.Limits.MaxLagMillis;
    }

    private async Task<bool> SendSnapshotChunkAsync(StandbyStream stream, IReplicationSource source)
    {
        if (stream.SnapBytes == null)
        {
            var newest = source.ReadNewestSnapshot();
            if (newest == null && store!.TrySnapshot(session.Game)) newest = source.ReadNewestSnapshot();
            if (newest == null)
            {
                PostStandbyStepLater(TimeSpan.FromMilliseconds(500)); // not a safe moment to snapshot (a round is resolving): try again
                return false;
            }
            if (newest.Value.Bytes.Length > Standby.Limits.MaxSnapshotBytes)
            {
                StopStandbyStream();
                await SafeSendAsync(stream.Target, new Error { Code = ErrorCodes.StandbyViolation, Detail = "snapshot-too-large" });
                return false;
            }
            LoadSnapshot(stream, newest.Value.Seq, newest.Value.Bytes);
            if (stream.SnapReset) stream.ResetSeq = newest.Value.Seq;
        }

        var bytes = stream.SnapBytes!;
        var chunk = Standby.Limits.SnapshotChunkBytes;
        var offset = stream.SnapNext * chunk;
        var length = Math.Min(chunk, bytes.Length - offset);
        await SafeSendAsync(stream.Target, new StandbySnapshot
        {
            Epoch = stream.Epoch,
            Seq = stream.SnapSeq,
            Part = stream.SnapNext,
            Parts = stream.SnapParts,
            Sha256 = stream.SnapSha,
            Data = Convert.ToBase64String(bytes, offset, length),
            Reset = stream.SnapReset,
        });
        stream.SnapNext++;
        stream.LastProgressMillis = clock.NowMillis;
        if (stream.SnapNext < stream.SnapParts) return true;

        stream.LastSnapshotSent = stream.SnapSeq;
        stream.SnapBytes = null;
        if (stream.SnapReset)
        {
            // The successor restarted its store at this snapshot: the log lines from its checkpoint on follow.
            stream.SentSeq = stream.SnapSeq;
            stream.Offset = 0;
            stream.Generation = -1;
        }
        stream.Phase = StreamPhase.Follow;
        return true;
    }

    private void LoadSnapshot(StandbyStream stream, long seq, byte[] bytes)
    {
        var chunk = Standby.Limits.SnapshotChunkBytes;
        stream.SnapBytes = bytes;
        stream.SnapSeq = seq;
        stream.SnapNext = 0;
        stream.SnapSha = StandbyStore.HashOf(bytes);
        stream.SnapParts = Math.Max(1, (bytes.Length + chunk - 1) / chunk);
    }

    private async Task<bool> SendFollowBatchAsync(StandbyStream stream, IReplicationSource source, int batchLines)
    {
        if (stream.MetaDirty)
        {
            stream.MetaDirty = false;
            await SendStandbyMetaAsync(stream, withHeader: false);
        }
        var result = source.ReadReplicationBatch(stream.SentSeq, stream.Offset, stream.Generation, batchLines);
        foreach (var line in result.Lines)
        {
            // A hole in the log (a rotation that dropped what the successor still needed, a store that was rewritten): restart from a snapshot rather than send a stream with a gap.
            if (line.IsCheckpoint ? line.Seq != stream.SentSeq : line.Seq != stream.SentSeq + 1)
            {
                await BeginResetAsync(stream);
                return true;
            }
            var wasIdle = stream.AckedSeq >= stream.SentSeq;
            await SafeSendAsync(stream.Target, new StandbyEntry { Epoch = stream.Epoch, Seq = line.Seq, Line = line.Text });
            if (wasIdle) stream.LastProgressMillis = clock.NowMillis;
            if (!line.IsCheckpoint) stream.SentSeq = line.Seq;
        }
        stream.Offset = result.NextOffset;
        stream.Generation = result.Generation;
        if (result.Lines.Count > 0)
        {
            stream.CaughtUp = false;
            return true;
        }

        stream.CaughtUp = true;
        if (stream.SyncTarget < 0) stream.SyncTarget = stream.SentSeq;
        if (!stream.SnapshotDirty) return false;
        stream.SnapshotDirty = false;
        // The host took a snapshot: the successor gets it too (it only adds to the store). It is sent once the stream has reached its seq.
        var newest = source.ReadNewestSnapshot();
        if (newest == null || newest.Value.Seq <= stream.LastSnapshotSent) return false;
        if (newest.Value.Seq > stream.SentSeq) { stream.SnapshotDirty = true; return false; }
        if (newest.Value.Bytes.Length > Standby.Limits.MaxSnapshotBytes) return false;
        LoadSnapshot(stream, newest.Value.Seq, newest.Value.Bytes);
        stream.SnapReset = false;
        stream.Phase = StreamPhase.Snapshot;
        return true;
    }

    private async Task ReportReplicationAsync(StandbyStream stream)
    {
        if (stream.CaughtUp && !stream.Synced && stream.Phase == StreamPhase.Follow && !stream.AwaitingResetAck && stream.AckedSeq >= stream.SyncTarget) stream.Synced = true;
        var state = stream.Phase switch
        {
            StreamPhase.Lagging => StandbyStates.Lagging,
            StreamPhase.Snapshot => StandbyStates.Syncing,
            _ => stream.Synced ? StandbyStates.Ready : StandbyStates.Syncing,
        };
        var acked = stream.AwaitingResetAck ? (stream.ResetSeq >= 0 ? stream.ResetSeq : store!.LastSeq) : Math.Max(stream.AckedSeq, 0);
        if (!Standby.SetReplication(state, (int)Math.Min(int.MaxValue, Math.Max(0, store!.LastSeq - acked)))) return;
        await SafeSendAsync(stream.Target, StatusMessage());
        await AnnounceSuccessorAsync();
    }
}
