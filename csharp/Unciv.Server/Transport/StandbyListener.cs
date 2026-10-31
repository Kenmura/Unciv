using System.Security.Cryptography.X509Certificates;
using Unciv.Net;

namespace Unciv.Server.Transport;

/// <summary>
/// T282: the listener a backup host (the successor) keeps open on its announced port while it is only a standby (design/HOST-MIGRATION.md section 3). It terminates TLS with the successor's certificate and
/// answers one thing: <c>Hello</c> gets <c>Welcome{standby: true}</c> (so the host's reachability probe can tell a standby from a closed port or an unrelated TLS service), everything else is refused with
/// <c>not-host-yet</c>. It holds no game state and never touches a store. Each connection lives at most <see cref="ConnectionLifetime"/> and answers at most two frames (a Hello, then a refusal), and at
/// most <see cref="MaxConnections"/> run at once, so it is no resource sink. It must be stopped (<see cref="Stop"/>) before the same port is opened by the promoted host.
/// T354: a successor with no reachable address passes a <see cref="RelayListener"/> (an unlisted room on a relay, never <c>SetListing</c>) and answers the same probe on the connections it yields;
/// <see cref="DetachRelay"/> hands that registration to the promoted host, so the room (and the address clients were told) survives the promotion.
/// </summary>
public sealed class StandbyListener : IAsyncDisposable
{
    public const int MaxConnections = 16;
    public const int MaxFrameBytes = 64 * 1024;

    private readonly TcpTlsListener? tcp;
    private readonly string fingerprint;
    private readonly CancellationTokenSource stopped = new();
    private readonly CancellationTokenSource relayStopped;
    private RelayListener? relay;
    private int connections;
    private Task? acceptLoop;
    private Task? relayLoop;

    /// <param name="certificate">The successor's host key.</param>
    /// <param name="port">The TCP port to listen on (0 picks a free one), or null for a relay-only successor.</param>
    /// <param name="handshakeTimeout">How long a direct TLS handshake may take.</param>
    /// <param name="relay">T354: the successor's room on a relay, owned from here on (disposed with the listener unless handed on by <see cref="DetachRelay"/>).</param>
    public StandbyListener(X509Certificate2 certificate, int? port, TimeSpan? handshakeTimeout = null, RelayListener? relay = null)
    {
        if (port == null && relay == null) throw new ArgumentException("A standby listener needs a port, a relay room or both");
        tcp = port == null ? null : new TcpTlsListener(certificate, port.Value, handshakeTimeout ?? TimeSpan.FromSeconds(3));
        this.relay = relay;
        relayStopped = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
        fingerprint = HostCertificate.GetFingerprint(certificate);
    }

    /// <summary>How many connections are served at once (default <see cref="MaxConnections"/>); the rest are closed unanswered.</summary>
    public int ConnectionLimit { get; init; } = MaxConnections;

    /// <summary>How long one connection may stay open (a probe needs milliseconds).</summary>
    public TimeSpan ConnectionLifetime { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The TCP port being listened on, or 0 for a relay-only listener.</summary>
    public int Port => tcp?.Port ?? 0;

    /// <summary>T354: the relay registration, or null (none given, or handed on by <see cref="DetachRelay"/>). Its <see cref="RelayListener.Room"/> is what the successor announces.</summary>
    public RelayListener? Relay => relay;

    /// <summary>Starts listening (throws a <see cref="System.Net.Sockets.SocketException"/> when the port is taken).</summary>
    public void Start()
    {
        tcp?.Start();
        if (tcp != null) acceptLoop = AcceptLoopAsync(tcp, stopped.Token);
        if (relay != null)
        {
            relay.Start();
            relayLoop = RelayLoopAsync(relay, relayStopped.Token);
        }
    }

    /// <summary>Closes the port at once (the promoted host binds it next).</summary>
    public void Stop()
    {
        try { stopped.Cancel(); } catch (ObjectDisposedException) { }
        tcp?.Stop();
    }

    /// <summary>
    /// T354: stops answering probes on the relay room and returns the registration with the room still held, for the promoted host to adopt (<see cref="InProcessHost.Listen"/>). Null when there is none.
    /// Probes in flight are cut. The caller owns the returned listener from here on.
    /// </summary>
    public RelayListener? DetachRelay()
    {
        var detached = Interlocked.Exchange(ref relay, null);
        try { relayStopped.Cancel(); } catch (ObjectDisposedException) { }
        return detached;
    }

    private async Task RelayLoopAsync(RelayListener source, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            ITransport transport;
            try
            {
                transport = await source.AcceptTransportAsync(token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException) { return; }
            if (Interlocked.Increment(ref connections) > ConnectionLimit)
            {
                Interlocked.Decrement(ref connections);
                await transport.CloseAsync().ConfigureAwait(false);
                continue;
            }
            _ = ServeTransportAsync(transport, token);
        }
    }

    private async Task AcceptLoopAsync(TcpTlsListener tcp, CancellationToken token)
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
            if (Interlocked.Increment(ref connections) > ConnectionLimit)
            {
                Interlocked.Decrement(ref connections);
                client.Close();
                continue;
            }
            _ = ServeAsync(tcp, client, token); // per connection: a client that sends nothing must not stall the others
        }
    }

    private async Task ServeAsync(TcpTlsListener tcp, System.Net.Sockets.TcpClient client, CancellationToken token)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(ConnectionLifetime);
            await using var transport = await tcp.HandshakeAsync(client, limit.Token).ConfigureAwait(false);
            await ServeFramesAsync(transport, limit.Token).ConfigureAwait(false);
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

    // A connection the relay yielded: its end-to-end handshake is done already.
    private async Task ServeTransportAsync(ITransport accepted, CancellationToken token)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(ConnectionLifetime);
            await using var transport = accepted;
            await ServeFramesAsync(transport, limit.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // a dropped peer or the lifetime limit: nothing to report
        }
        finally
        {
            Interlocked.Decrement(ref connections);
        }
    }

    private async Task ServeFramesAsync(ITransport transport, CancellationToken token)
    {
        for (var frames = 0; ; frames++) // ends at the second frame at the latest: only a first Hello is answered, everything else closes
        {
            var frame = await ((TcpTlsTransport)transport).ReceiveAsync(MaxFrameBytes, token).ConfigureAwait(false);
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
                }), token).ConfigureAwait(false);
                continue;
            }
            await transport.SendAsync(ProtocolCodec.Encode(new Error { Code = ErrorCodes.NotHostYet }), token).ConfigureAwait(false);
            return;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (acceptLoop != null)
        {
            try { await acceptLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        if (relayLoop != null)
        {
            try { await relayLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        if (Interlocked.Exchange(ref relay, null) is { } owned) await owned.DisposeAsync().ConfigureAwait(false);
        relayStopped.Dispose();
        stopped.Dispose();
    }
}
