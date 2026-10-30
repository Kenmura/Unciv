using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;

namespace Unciv.Relay;

public sealed partial class RelayServer
{
    private const int HttpHeadMaxBytes = 8192;

    /// <summary>The optional read-only HTTP listener: GET only, no request bodies, a rate limit per client network (RELAY.md section 7, A13).</summary>
    private async Task HandleHttpAsync(TcpClient tcp)
    {
        var remote = (IPEndPoint)tcp.Client.RemoteEndPoint!;
        using var conn = new RelayConn(tcp, remote.Address);
        lock (gate) liveConnections.Add(conn);
        string? counted = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            timeout.CancelAfter(limits.PreambleTimeoutMillis);
            if (options.HttpTls && certificate != null)
            {
                var ssl = new SslStream(conn.Stream, leaveInnerStreamOpen: false);
                conn.Stream = ssl;
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12 }, timeout.Token).ConfigureAwait(false);
            }
            var head = await ReadHeadAsync(conn.Stream, timeout.Token).ConfigureAwait(false);
            if (head == null)
            {
                await WriteHttpAsync(conn, 431, "text/plain", "request head too large\n", null).ConfigureAwait(false);
                return;
            }
            var lines = head.Split("\r\n");
            var request = lines[0].Split(' ');
            var headers = lines.Skip(1).Select(l => l.Split(':', 2)).Where(a => a.Length == 2).GroupBy(a => a[0].Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Last()[1].Trim());
            if (headers.TryGetValue("x-forwarded-for", out var forwarded) && IsTrustedProxy(remote.Address) && IPAddress.TryParse(forwarded.Split(',')[0].Trim(), out var client)) conn.Address = client;
            var network = conn.Network;
            if (!EnterConnection(network)) { await WriteHttpAsync(conn, 429, "text/plain", "too many connections\n", null).ConfigureAwait(false); return; }
            counted = network;
            if (!httpRate.TryHit(network)) { await WriteHttpAsync(conn, 429, "text/plain", "rate limited\n", null).ConfigureAwait(false); return; }
            if (IsBanned(conn)) { await WriteHttpAsync(conn, 403, "text/plain", "banned\n", null).ConfigureAwait(false); return; }
            if (request.Length != 3 || !request[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) { await WriteHttpAsync(conn, 400, "text/plain", "bad request\n", null).ConfigureAwait(false); return; }
            if (request[0] != "GET") { await WriteHttpAsync(conn, 405, "text/plain", "GET only\n", null).ConfigureAwait(false); return; }
            if ((headers.TryGetValue("content-length", out var length) && length != "0") || headers.ContainsKey("transfer-encoding"))
            {
                await WriteHttpAsync(conn, 400, "text/plain", "the API takes no request bodies\n", null).ConfigureAwait(false);
                return;
            }
            var target = request[1];
            if (target == "/healthz") { await WriteHttpAsync(conn, 200, "text/plain", $"ok rooms={RoomCount} splices={SpliceCount}\n", null).ConfigureAwait(false); return; }
            if (target == "/metrics" && options.Metrics) { await WriteHttpAsync(conn, 200, "text/plain; version=0.0.4", Metrics(), null).ConfigureAwait(false); return; }
            var reply = target.StartsWith("/v1/", StringComparison.Ordinal) ? ServeDirectory(target) : new DirectoryReply(404, null);
            if (reply.Body == null) { await WriteHttpAsync(conn, reply.Status, "text/plain", "not found\n", null).ConfigureAwait(false); return; }
            var json = reply.Body.ToJsonString();
            var etag = "\"" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))[..8]) + "\"";
            if (headers.TryGetValue("if-none-match", out var match) && match == etag) await WriteHttpAsync(conn, 304, "application/json", "", etag).ConfigureAwait(false);
            else await WriteHttpAsync(conn, 200, "application/json", json, etag).ConfigureAwait(false);
        }
        finally
        {
            if (counted != null) LeaveConnection(counted);
            lock (gate) liveConnections.Remove(conn);
        }
    }

    private static async Task<string?> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(512);
        var one = new byte[1];
        while (bytes.Count < HttpHeadMaxBytes)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0) throw new IOException("closed inside the request head");
            bytes.Add(one[0]);
            var n = bytes.Count;
            if (n >= 4 && bytes[n - 4] == '\r' && bytes[n - 3] == '\n' && bytes[n - 2] == '\r' && bytes[n - 1] == '\n') return Encoding.Latin1.GetString([.. bytes], 0, n - 4);
        }
        return null;
    }

    private static async Task WriteHttpAsync(RelayConn conn, int status, string contentType, string body, string? etag)
    {
        var reason = status switch { 200 => "OK", 304 => "Not Modified", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", 429 => "Too Many Requests", 431 => "Request Header Fields Too Large", _ => "Error" };
        var payload = Encoding.UTF8.GetBytes(body);
        var head = new StringBuilder().Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        head.Append("Content-Type: ").Append(contentType).Append("\r\nContent-Length: ").Append(payload.Length).Append("\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n");
        if (etag != null) head.Append("ETag: ").Append(etag).Append("\r\nCache-Control: max-age=5\r\n");
        if (status == 405) head.Append("Allow: GET\r\n");
        head.Append("\r\n");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await conn.Stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), timeout.Token).ConfigureAwait(false);
            await conn.Stream.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
            await conn.Stream.FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
}
