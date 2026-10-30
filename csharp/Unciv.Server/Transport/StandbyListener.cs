using System.Security.Cryptography.X509Certificates;
using Unciv.Net;

namespace Unciv.Server.Transport;

/// <summary>
/// T282: the listener a backup host (the successor) keeps open on its announced port while it is only a standby (design/HOST-MIGRATION.md section 3). It terminates TLS with the successor's certificate and
/// answers one thing: <c>Hello</c> gets <c>Welcome{standby: true}</c> (so the host's reachability probe can tell a standby from a closed port or an unrelated TLS service), everything else is refused with
/// <c>not-host-yet</c>. It holds no game state and never touches a store. Each connection lives at most <see cref="ConnectionLifetime"/> and answers at most two frames (a Hello, then a refusal), and at
/// most <see cref="MaxConnections"/> run at once, so it is no resource sink. It must be stopped (<see cref="Stop"/>) before the same port is opened by the promoted host.
/// </summary>
public sealed class StandbyListener : IAsyncDisposable
{
    public const int MaxConnections = 16;
    public const int MaxFrameBytes = 64 * 1024;

    private readonly TcpTlsListener tcp;
    private readonly string fingerprint;
    private readonly CancellationTokenSource stopped = new();
    private int connections;
    private Task? acceptLoop;

    public StandbyListener(X509Certificate2 certificate, int port, TimeSpan? handshakeTimeout = null)
    {
        tcp = new TcpTlsListener(certificate, port, handshakeTimeout ?? TimeSpan.FromSeconds(3));
        fingerprint = HostCertificate.GetFingerprint(certificate);
    }

    /// <summary>How long one connection may stay open (a probe needs milliseconds).</summary>
    public TimeSpan ConnectionLifetime { get; init; } = TimeSpan.FromSeconds(10);

    public int Port => tcp.Port;

    /// <summary>Starts listening (throws a <see cref="System.Net.Sockets.SocketException"/> when the port is taken).</summary>
    public void Start()
    {
        tcp.Start();
        acceptLoop = AcceptLoopAsync(stopped.Token);
    }

    /// <summary>Closes the port at once (the promoted host binds it next).</summary>
    public void Stop()
    {
        try { stopped.Cancel(); } catch (ObjectDisposedException) { }
        tcp.Stop();
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            System.Net.Sockets.TcpClient client;
            try
            {
                client = await tcp.AcceptTcpAsync(token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or System.Net.Sockets.SocketException or InvalidOperationException)
            {
                if (token.IsCancellationRequested || e is ObjectDisposedException) return; await Task.Delay(50, token).ConfigureAwait(false); continue; // a persistent accept error (out of descriptors) must not spin
            }
            if (Interlocked.Increment(ref connections) > MaxConnections)
            {
                Interlocked.Decrement(ref connections);
                client.Close();
                continue;
            }
            _ = ServeAsync(client, token); // per connection: a client that sends nothing must not stall the others
        }
    }

    private async Task ServeAsync(System.Net.Sockets.TcpClient client, CancellationToken token)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(ConnectionLifetime);
            await using var transport = await tcp.HandshakeAsync(client, limit.Token).ConfigureAwait(false);
            for (var frames = 0; ; frames++) // ends at the second frame at the latest: only a first Hello is answered, everything else closes
            {
                var frame = await ((TcpTlsTransport)transport).ReceiveAsync(MaxFrameBytes, limit.Token).ConfigureAwait(false);
                if (frame == null) return;
                if (ProtocolCodec.Decode(frame, MaxFrameBytes) is Hello && frames == 0)
                {
                    await transport.SendAsync(ProtocolCodec.Encode(new Welcome
                    {
                        Protocol = ProtocolVersion.Current,
                        SessionId = "standby",
                        Challenge = "",
                        HostKeyFingerprint = fingerprint,
                        RequiresPassword = false,
                        Standby = true,
                    }), limit.Token).ConfigureAwait(false);
                    continue;
                }
                await transport.SendAsync(ProtocolCodec.Encode(new Error { Code = ErrorCodes.NotHostYet }), limit.Token).ConfigureAwait(false);
                return;
            }
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // a failed handshake, a dropped peer or the lifetime limit: nothing to report
        }
        finally
        {
            Interlocked.Decrement(ref connections);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (acceptLoop != null)
        {
            try { await acceptLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        stopped.Dispose();
    }
}
