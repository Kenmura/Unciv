using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unciv.Relay;

public sealed partial class RelayServer
{
    private const string P256Oid = "1.2.840.10045.3.1.7";

    private async Task HandleHostAsync(RelayConn conn, JsonObject p, string challenge)
    {
        var certText = Text(p, "cert");
        var gameTag = Text(p, "gameTag");
        if (certText == null || gameTag == null || Text(p, "sig") == null || !IsCanonicalTag(gameTag))
        {
            await conn.TrySendAsync(Error("bad-request")).ConfigureAwait(false);
            return;
        }
        byte[] der;
        X509Certificate2 cert;
        try
        {
            der = Convert.FromBase64String(certText);
            cert = X509CertificateLoader.LoadCertificate(der);
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            await conn.TrySendAsync(Error("bad-request")).ConfigureAwait(false);
            return;
        }
        var key = cert.GetECDsaPublicKey();
        var spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
        cert.Dispose();
        if (key == null || key.ExportParameters(false).Curve.Oid.Value != P256Oid)
        {
            key?.Dispose();
            await conn.TrySendAsync(Error("bad-request")).ConfigureAwait(false);
            return;
        }
        Room? room = null;
        try
        {
            if (!VerifyForAnyName(key, Text(p, "sig"), name => RelayProtocol.HostSignedText(name, challenge, gameTag)))
            {
                await conn.TrySendAsync(Error("bad-signature")).ConfigureAwait(false);
                Log.Info("host-refused", reason: "bad-signature", ip: conn.Ip);
                return;
            }
            if (policy.IsKeyBanned(spki))
            {
                await conn.TrySendAsync(Error("banned")).ConfigureAwait(false);
                Log.Info("host-refused", reason: "banned-key", ip: conn.Ip);
                return;
            }
            ControlChannel? control = null;
            ControlChannel? old = null;
            var failure = Register(conn, der, spki, gameTag, key, ref room, ref control, ref old);
            if (failure != null)
            {
                await conn.TrySendAsync(Error(failure)).ConfigureAwait(false);
                Log.Info("host-refused", reason: failure, ip: conn.Ip);
                return;
            }
            if (!ReferenceEquals(room!.Key, key)) key.Dispose(); // an existing room keeps its own (identical) key object
            if (old != null) _ = old.CloseAsync("superseded");
            try
            {
                await conn.SendAsync(
                    new JsonObject
                    {
                        ["ok"] = true,
                        ["room"] = room.Display,
                        ["fp"] = room.FingerprintHex,
                        ["limits"] = new JsonObject { ["idleSeconds"] = limits.IdleSeconds, ["maxJoiners"] = limits.MaxJoinersPerRoom, ["spliceRate"] = limits.SpliceRate, ["maxPending"] = limits.MaxPendingPerRoom },
                        ["ttl"] = limits.ControlSilenceSeconds,
                    }, control!.Cancel.Token).ConfigureAwait(false);
                Log.Info("host", room: room.Display, fp: room.FingerprintHex, ip: conn.Ip, reason: old != null ? "superseded-old" : null);
                await ControlLoopAsync(room, control, conn).ConfigureAwait(false);
            }
            finally
            {
                lock (gate)
                {
                    controls.Remove(control!);
                    if (ReferenceEquals(room.Control, control))
                    {
                        room.Control = null;
                        room.Listing = null;
                        room.LastSeen = clock.NowMillis;
                    }
                }
                Log.Info("host-gone", room: room.Display, fp: room.FingerprintHex);
                await control!.Cancel.CancelAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            if (room == null) key.Dispose();
        }
    }

    private static bool IsCanonicalTag(string tag)
    {
        try { return Convert.FromBase64String(tag).Length == 16 && Convert.ToBase64String(Convert.FromBase64String(tag)) == tag; }
        catch (FormatException) { return false; }
    }

    /// <summary>Finds or creates the room for this key and game and installs the new control channel. Returns the refusal code, or null.</summary>
    private string? Register(RelayConn conn, byte[] der, byte[] spki, string gameTag, ECDsa key, ref Room? room, ref ControlChannel? control, ref ControlChannel? old)
    {
        var network = conn.Network;
        var fpHex = RelayProtocol.FingerprintHex(spki);
        lock (gate)
        {
            if (stopping.IsCancellationRequested) return "relay-full";
            var existing = rooms.Values.FirstOrDefault(r => r.FingerprintHex == fpHex && r.GameTag == gameTag);
            if (controls.Count(c => c.Conn.Network == network) >= limits.MaxControlPerNetwork) return "rate-limited";
            if (rooms.Values.Count(r => r.Live && r != existing && r.HostNetwork == network) >= limits.MaxRoomsPerNetwork) return "rate-limited";
            if (existing is not { Live: true } && rooms.Values.Count(r => r.Live) >= limits.MaxRooms) return "relay-full";
            if (existing == null)
            {
                if (rooms.Count >= 4 * limits.MaxRooms)
                {
                    var oldest = rooms.Values.Where(r => !r.Live).OrderBy(r => r.LastSeen).FirstOrDefault();
                    if (oldest == null) return "relay-full";
                    rooms.Remove(oldest.Code);
                    oldest.Dispose();
                }
                var created = new Room(DeriveCode(SHA256.HashData(spki), gameTag), der, spki, gameTag, key, new ByteBudget(limits.RoomBytesPerDay, 24L * 3600 * 1000, clock), clock.NowMillis);
                rooms[created.Code] = created;
                existing = created;
            }
            room = existing;
            room.HostNetwork = network;
            room.LastSeen = clock.NowMillis;
            old = room.Control;
            room.Listing = null; // a new control channel republishes (its seq restarts)
            control = new ControlChannel(conn, clock.NowMillis);
            room.Control = control;
            controls.Add(control);
            return null;
        }
    }

    /// <summary>The first free code of HMAC(secret, fp || gameTag || counter): the same key and game always get the same one while the secret is stable. Call under the lock.</summary>
    private string DeriveCode(byte[] fp, string gameTag)
    {
        var tag = Convert.FromBase64String(gameTag);
        for (uint counter = 0; ; counter++)
        {
            var input = new byte[fp.Length + tag.Length + 4];
            fp.CopyTo(input, 0);
            tag.CopyTo(input, fp.Length);
            input[^4] = (byte)(counter >> 24);
            input[^3] = (byte)(counter >> 16);
            input[^2] = (byte)(counter >> 8);
            input[^1] = (byte)counter;
            var code = RelayProtocol.RoomCodeFromHash(HMACSHA256.HashData(secret, input));
            if (!rooms.ContainsKey(code)) return code;
        }
    }

    private async Task ControlLoopAsync(Room room, ControlChannel control, RelayConn conn)
    {
        var token = control.Cancel.Token;
        var controlKey = "c" + room.Code + conn.Ip;
        while (!token.IsCancellationRequested)
        {
            string? line;
            try { line = await conn.ReadLineAsync(RelayProtocol.ControlLineMaxBytes, token).ConfigureAwait(false); }
            catch (LineTooLongException)
            {
                await control.CloseAsync("too-large").ConfigureAwait(false);
                return;
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { return; }
            if (line == null) return;
            control.LastSeen = clock.NowMillis;
            if (!controlRate.TryHit(controlKey))
            {
                await control.CloseAsync("rate-limited").ConfigureAwait(false);
                return;
            }
            JsonObject? message;
            try { message = JsonNode.Parse(line) as JsonObject; }
            catch (JsonException) { message = null; }
            if (message == null)
            {
                await conn.TrySendAsync(Error("bad-request")).ConfigureAwait(false);
                continue;
            }
            var op = Text(message, "op");
            switch (op)
            {
                case "ping":
                    await conn.TrySendAsync(new JsonObject { ["ev"] = "pong" }).ConfigureAwait(false);
                    break;
                case "reject":
                    RejectPending(room, Text(message, "id"));
                    break;
                case "list" or "update" or "unlist":
                    await conn.TrySendAsync(HandleListing(room, control, op, message)).ConfigureAwait(false);
                    break;
                default:
                    await conn.TrySendAsync(Error("bad-request")).ConfigureAwait(false);
                    break;
            }
        }
    }

    private void RejectPending(Room room, string? id)
    {
        PendingJoin? pending = null;
        lock (gate)
        {
            if (id != null && room.Pending.Remove(id, out var found)) pending = found;
        }
        if (pending != null && pending.TryClaim()) pending.Accepted.TrySetResult(null);
    }

    private JsonObject HandleListing(Room room, ControlChannel control, string op, JsonObject message)
    {
        JsonObject Rejected(string code, string reason)
        {
            Log.Debug("listing-" + op, room: room.Display, reason: reason);
            return Error(code, ("op", op));
        }

        if (op == "unlist")
        {
            lock (gate) room.Listing = null;
            return new JsonObject { ["ok"] = true, ["op"] = op };
        }
        if (options.Listing == ListingPolicy.Off) return Rejected("listing-disabled", "off");
        if (options.Listing == ListingPolicy.Allowlist && !policy.IsKeyAllowed(room.Spki)) return Rejected("listing-rejected", "not-allowlisted");
        var body = Text(message, "body");
        var signature = Text(message, "sig");
        if (body == null || signature == null) return Rejected("listing-rejected", "fields");
        byte[] rawSignature;
        try { rawSignature = Convert.FromBase64String(signature); }
        catch (FormatException) { return Rejected("listing-rejected", "signature"); }
        bool signed;
        try { signed = rawSignature.Length == 64 && room.Key.VerifyData(Encoding.UTF8.GetBytes(RelayProtocol.ListingSignedText(body)), rawSignature, HashAlgorithmName.SHA256); }
        catch (CryptographicException) { signed = false; }
        if (!signed) return Rejected("listing-rejected", "signature");
        var (fields, seq, error) = ListingValidator.Validate(body);
        if (fields == null) return Rejected("listing-rejected", error ?? "schema");
        if (seq <= control.LastSeq) return Rejected("listing-rejected", "seq");
        if (policy.IsNameDenied(fields.Name) || policy.IsNameDenied(fields.HostName)) return Rejected("listing-rejected", "name");
        if (op == "list" && !listingRate.TryHit(room.HostNetwork)) return Rejected("rate-limited", "rate");
        lock (gate)
        {
            if (op == "update" && room.Listing == null) return Rejected("listing-rejected", "update-before-list");
            if (op == "list" && (rooms.Values.Count(r => r.Listing != null) >= limits.MaxRooms || rooms.Values.Any(r => r != room && r.Listing != null && r.FingerprintHex == room.FingerprintHex)))
                return Rejected("listing-rejected", "limit");
            var now = clock.NowMillis;
            room.Listing = new Listing(body, signature, seq, room.Listing?.FirstSeen ?? now, now, fields);
            control.LastSeq = seq;
        }
        return new JsonObject { ["ok"] = true, ["op"] = op };
    }
}
