using System.Text.Json.Nodes;

namespace Unciv.Relay;

public sealed partial class RelayServer
{
    private async Task HandleJoinAsync(RelayConn conn, JsonObject p)
    {
        var network = conn.Network;
        var code = RelayProtocol.NormalizeRoom(Text(p, "room"));
        Room? room;
        lock (gate) room = code == null ? null : rooms.GetValueOrDefault(code);
        if (room == null)
        {
            if (lookupFailures.Hit(network) >= limits.LookupFailuresPerMinute)
            {
                lock (gate) temporaryBans[network] = clock.NowMillis + limits.BanMinutes * 60_000L;
                Log.Warn("temporary-ban", reason: "room-enumeration", ip: conn.Ip);
            }
            await conn.TrySendAsync(Error("room-unknown")).ConfigureAwait(false);
            return;
        }
        var expected = Text(p, "fp");
        if (expected != null && !string.Equals(Unciv.Net.Identity.HostKey.Normalize(expected), RelayProtocol.InviteFingerprint(room.Spki), StringComparison.Ordinal))
        {
            await conn.TrySendAsync(Error("wrong-host")).ConfigureAwait(false);
            return;
        }

        string? refusal = null;
        PendingJoin? pending = null;
        ControlChannel? control = null;
        var peer = RelayProtocol.PeerId(secret, room.Display, network);
        lock (gate)
        {
            control = room.Control;
            if (control == null) refusal = "host-gone";
            else if (room.Daily.Exceeded || monthly?.Exceeded == true) refusal = "rate-limited";
            else if (splices.Count >= limits.MaxSplices) refusal = "relay-full";
            else if (room.Pending.Count >= limits.MaxPendingPerRoom || room.Pending.Count + room.Splices.Count >= limits.MaxJoinersPerRoom) refusal = "room-full";
            else if (room.Pending.Values.Count(x => x.Network == network) + room.Splices.Count(s => s.JoinerNetwork == network) >= limits.MaxJoinersPerNetwork) refusal = "rate-limited";
            else if (!joinRate.TryHit(room.Code + "|" + network)) refusal = "rate-limited";
            else
            {
                var idBytes = new byte[12];
                random.Fill(idBytes);
                pending = new PendingJoin(Convert.ToHexStringLower(idBytes), network);
                room.Pending[pending.Id] = pending;
            }
        }
        if (pending == null)
        {
            await conn.TrySendAsync(Error(refusal!)).ConfigureAwait(false);
            Log.Info("join-refused", room: room.Display, peer: peer, reason: refusal, ip: conn.Ip);
            return;
        }

        try
        {
            try { await control!.Conn.SendAsync(new JsonObject { ["ev"] = "incoming", ["id"] = pending.Id, ["peer"] = peer }, stopping.Token).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
            {
                await conn.TrySendAsync(Error("host-gone")).ConfigureAwait(false);
                return;
            }
            var winner = await Task.WhenAny(pending.Accepted.Task, Task.Delay(limits.HostTimeoutMillis, stopping.Token)).ConfigureAwait(false);
            if (winner != pending.Accepted.Task && pending.TryClaim())
            {
                await conn.TrySendAsync(Error("host-timeout")).ConfigureAwait(false);
                Log.Info("join-refused", room: room.Display, peer: peer, reason: "host-timeout", ip: conn.Ip);
                return;
            }
            var accepted = await pending.Accepted.Task.ConfigureAwait(false);
            if (accepted == null)
            {
                await conn.TrySendAsync(Error("room-full")).ConfigureAwait(false);
                Log.Info("join-refused", room: room.Display, peer: peer, reason: "host-rejected", ip: conn.Ip);
                return;
            }
            await RunSpliceAsync(conn, room, network, peer, accepted).ConfigureAwait(false);
        }
        finally
        {
            lock (gate) room.Pending.Remove(pending.Id);
        }
    }

    private async Task RunSpliceAsync(RelayConn joiner, Room room, string network, string peer, AcceptResult accepted)
    {
        var splice = new Splice(joiner, accepted.Host, room, network, new TokenBucket(limits.SpliceRate, limits.SpliceBurst, clock), egress, monthly, clock, clock.NowMillis);
        lock (gate)
        {
            splices.Add(splice);
            room.Splices.Add(splice);
        }
        Interlocked.Increment(ref joinsTotal);
        try
        {
            await joiner.SendAsync(new JsonObject { ["ok"] = true }, stopping.Token).ConfigureAwait(false);
            Log.Info("splice", room: room.Display, peer: peer, ip: joiner.Ip);
            await splice.RunAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            splice.Close("error");
        }
        finally
        {
            lock (gate)
            {
                splices.Remove(splice);
                room.Splices.Remove(splice);
            }
            Interlocked.Add(ref bytesTotal, splice.Bytes);
            Log.Info("splice-end", room: room.Display, peer: peer, reason: splice.CloseReason, bytes: splice.Bytes);
            accepted.Done.TrySetResult();
        }
    }

    private async Task HandleAcceptAsync(RelayConn conn, JsonObject p, string challenge)
    {
        var roomText = Text(p, "room");
        var id = Text(p, "id");
        var code = RelayProtocol.NormalizeRoom(roomText);
        Room? room;
        lock (gate) room = code == null ? null : rooms.GetValueOrDefault(code);
        if (room == null || id == null || roomText == null)
        {
            await conn.TrySendAsync(Error(room == null ? "room-unknown" : "bad-request")).ConfigureAwait(false);
            return;
        }
        if (!VerifyForAnyName(room.Key, Text(p, "sig"), name => RelayProtocol.AcceptSignedText(name, challenge, roomText, id)))
        {
            await conn.TrySendAsync(Error("bad-signature")).ConfigureAwait(false);
            Log.Info("accept-refused", room: room.Display, reason: "bad-signature", ip: conn.Ip);
            return;
        }
        PendingJoin? pending;
        lock (gate) room.Pending.Remove(id, out pending);
        if (pending == null || !pending.TryClaim())
        {
            await conn.TrySendAsync(Error("bad-id")).ConfigureAwait(false);
            return;
        }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await conn.SendAsync(new JsonObject { ["ok"] = true }, stopping.Token).ConfigureAwait(false);
        pending.Accepted.TrySetResult(new AcceptResult(conn, done));
        await done.Task.ConfigureAwait(false);
    }
}
