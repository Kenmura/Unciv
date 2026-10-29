using System.Net.Sockets;
using System.Security.Authentication;
using Unciv.Client.Net.Transport;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Server.Transport;

namespace Unciv.Core.Tests.Net.Transport;

public sealed class NetTransportTests
{
    [Fact]
    public void InviteCode_FormatAndParse_RoundTripsCorrectly()
    {
        var host = "192.168.1.100";
        var port = 47821;
        var fingerprint = "ABCDE-FGHIJ-KLMNO-PQRST-UVWXY-Z";
        var token = "token123456";

        var formatted = InviteCode.Format(host, port, fingerprint, token);
        Assert.Equal("unciv://192.168.1.100:47821/ABCDE-FGHIJ-KLMNO-PQRST-UVWXY-Z/token123456", formatted);

        Assert.True(InviteCode.TryParse(formatted, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(host, parsed.Host);
        Assert.Equal(port, parsed.Port);
        Assert.Equal(fingerprint, parsed.Fingerprint);
        Assert.Equal(token, parsed.Token);
    }

    [Fact]
    public void InviteCode_WithoutToken_RoundTripsCorrectly()
    {
        var host = "game.example.com";
        var port = 50000;
        var fingerprint = "ABCDE-FGHIJ-KLMNO-PQRST-UVWXY-Z";

        var formatted = InviteCode.Format(host, port, fingerprint);
        Assert.Equal("unciv://game.example.com:50000/ABCDE-FGHIJ-KLMNO-PQRST-UVWXY-Z", formatted);

        Assert.True(InviteCode.TryParse(formatted, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(host, parsed.Host);
        Assert.Equal(port, parsed.Port);
        Assert.Equal(fingerprint, parsed.Fingerprint);
        Assert.Null(parsed.Token);
    }

    [Fact]
    public void InviteCode_IPv6Address_FormatsWithBracketsAndParses()
    {
        var host = "2001:db8::1";
        var port = 47821;
        var fingerprint = "ABCDE-FGHIJ-KLMNO-PQRST-UVWXY-Z";

        var formatted = InviteCode.Format(host, port, fingerprint);
        Assert.Equal("unciv://[2001:db8::1]:47821/ABCDE-FGHIJ-KLMNO-PQRST-UVWXY-Z", formatted);

        Assert.True(InviteCode.TryParse(formatted, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal("2001:db8::1", parsed.Host);
        Assert.Equal(port, parsed.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://localhost:47821/fp")]
    [InlineData("unciv://")]
    [InlineData("unciv://localhost")]
    [InlineData("unciv://localhost:999999/ABCDE-FGHIJ-KLMNO-PQRST-UVWXY-Z")]
    [InlineData("unciv://localhost:47821/not-a-valid-fingerprint")]
    [InlineData("unciv://localhost:47821/ABCDE-FGHIJ-KLMNO-PQRST-UVWXY-Z/token1/extra")]
    public void InviteCode_InvalidInput_ReturnsFalse(string invalid)
    {
        Assert.False(InviteCode.TryParse(invalid, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void HostCertificate_GenerateAndLoadOrGenerate_PersistsAndMatchesFingerprint()
    {
        var tempPfx = Path.Combine(Path.GetTempPath(), "unciv-test-hostkey.pfx");
        try
        {
            var cert1 = HostCertificate.LoadOrGenerate(tempPfx, "test-password");
            Assert.NotNull(cert1);
            Assert.True(File.Exists(tempPfx));

            var fp1 = HostCertificate.GetFingerprint(cert1);
            Assert.NotNull(fp1);

            // Loading existing certificate yields the same fingerprint
            var cert2 = HostCertificate.LoadOrGenerate(tempPfx, "test-password");
            var fp2 = HostCertificate.GetFingerprint(cert2);
            Assert.Equal(fp1, fp2);
        }
        finally
        {
            if (File.Exists(tempPfx)) File.Delete(tempPfx);
        }
    }

    [Fact]
    public async Task TcpTlsTransport_LoopbackSendAndReceive_ExchangesLengthPrefixedFrames()
    {
        var serverCert = HostCertificate.Generate();
        await using var listener = new TcpTlsListener(serverCert, port: 0);
        listener.Start();

        var serverTask = listener.AcceptTransportAsync();
        var clientTransport = await TcpTlsClientTransport.ConnectAsync(
            "127.0.0.1",
            listener.Port,
            expectedFingerprint: HostCertificate.GetFingerprint(serverCert)
        );
        var serverTransport = await serverTask;

        await using (clientTransport)
        await using (serverTransport)
        {
            Assert.True(clientTransport.IsOpen);
            Assert.True(serverTransport.IsOpen);

            var message1 = """{"type":"Ping"}""";
            await clientTransport.SendAsync(message1);
            var receivedOnServer = await serverTransport.ReceiveAsync();
            Assert.Equal(message1, receivedOnServer);

            var message2 = """{"type":"Pong"}""";
            await serverTransport.SendAsync(message2);
            var receivedOnClient = await clientTransport.ReceiveAsync();
            Assert.Equal(message2, receivedOnClient);
        }
    }

    [Fact]
    public async Task TcpTlsTransport_FingerprintMismatch_HardFailsHandshake()
    {
        var serverCert = HostCertificate.Generate();
        await using var listener = new TcpTlsListener(serverCert, port: 0);
        listener.Start();

        var serverTask = listener.AcceptTransportAsync();

        var wrongFingerprint = "ABCDE-FGHIJ-KLMNO-PQRST-UVWXY-Z";
        await Assert.ThrowsAnyAsync<AuthenticationException>(async () =>
        {
            var client = await TcpTlsClientTransport.ConnectAsync(
                "127.0.0.1",
                listener.Port,
                expectedFingerprint: wrongFingerprint
            );
            await client.CloseAsync();
        });

        try
        {
            var server = await serverTask;
            await server.CloseAsync();
        }
        catch { }
    }

    [Fact]
    public async Task TcpTlsTransport_TofuKnownHosts_PinsOnFirstUseAndRefusesMismatchOnSecond()
    {
        var knownHosts = new KnownHosts();
        var serverCert1 = HostCertificate.Generate();

        // 1. First host runs on port 1
        await using (var listener1 = new TcpTlsListener(serverCert1, port: 0))
        {
            listener1.Start();
            var serverTask = listener1.AcceptTransportAsync();
            var client = await TcpTlsClientTransport.ConnectAsync(
                "127.0.0.1",
                listener1.Port,
                knownHosts: knownHosts
            );
            var server = await serverTask;

            Assert.True(client.IsOpen);
            Assert.Equal(HostTrust.Trusted, knownHosts.Check("127.0.0.1", HostCertificate.GetFingerprint(serverCert1)));

            await client.CloseAsync();
            await server.CloseAsync();
        }

        // 2. Host key changes (spoofed or altered server)
        var serverCert2 = HostCertificate.Generate();
        await using (var listener2 = new TcpTlsListener(serverCert2, port: 0))
        {
            listener2.Start();
            var serverTask = listener2.AcceptTransportAsync();

            // Connecting with existing knownHosts must hard-fail as HostTrust.Mismatch
            await Assert.ThrowsAnyAsync<AuthenticationException>(async () =>
            {
                var client = await TcpTlsClientTransport.ConnectAsync(
                    "127.0.0.1",
                    listener2.Port,
                    knownHosts: knownHosts
                );
                await client.CloseAsync();
            });

            try
            {
                var server = await serverTask;
                var frame = await server.ReceiveAsync();
                Assert.Null(frame);
                await server.CloseAsync();
            }
            catch (Exception)
            {
                // Acceptable if handshake aborted on the server side
            }
        }
    }

    [Fact]
    public async Task TcpTlsTransport_OversizeFrame_ThrowsAndCloses()
    {
        var serverCert = HostCertificate.Generate();
        await using var listener = new TcpTlsListener(serverCert, port: 0);
        listener.Start();

        var serverTask = listener.AcceptTransportAsync();
        var client = await TcpTlsClientTransport.ConnectAsync(
            "127.0.0.1",
            listener.Port,
            expectedFingerprint: HostCertificate.GetFingerprint(serverCert)
        );
        var server = await serverTask;

        await using (client)
        await using (server)
        {
            // Client attempts to send frame exceeding 64KB
            var oversize = new string('a', ProtocolVersion.MaxMessageBytes + 10);
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(oversize).AsTask());
        }
    }

    [Fact]
    public async Task TcpTlsListener_HandshakeTimeout_ClosesUnresponsiveSocket()
    {
        var serverCert = HostCertificate.Generate();
        // Set short 500ms handshake timeout
        await using var listener = new TcpTlsListener(serverCert, port: 0, handshakeTimeout: TimeSpan.FromMilliseconds(500));
        listener.Start();

        var acceptTask = listener.AcceptTransportAsync();

        // Raw TCP client connects but sends no TLS handshake bytes
        using var rawClient = new TcpClient();
        await rawClient.ConnectAsync("127.0.0.1", listener.Port);

        // Handshake should time out on server
        await Assert.ThrowsAnyAsync<Exception>(async () => await acceptTask);
    }

    [Fact]
    public async Task TcpTlsTransport_ClosedBehavior_ThrowsOnSendReturnsNullOnReceiveAndIdempotentClose()
    {
        var serverCert = HostCertificate.Generate();
        await using var listener = new TcpTlsListener(serverCert, port: 0);
        listener.Start();

        var serverTask = listener.AcceptTransportAsync();
        var client = await TcpTlsClientTransport.ConnectAsync(
            "127.0.0.1",
            listener.Port,
            expectedFingerprint: HostCertificate.GetFingerprint(serverCert)
        );
        var server = await serverTask;

        // Verify IsOpen initially
        Assert.True(server.IsOpen);
        Assert.True(client.IsOpen);

        // When client closes, server ReceiveAsync catches EOF/IOException and returns null
        await client.CloseAsync();
        await client.DisposeAsync(); // Idempotent dispose
        Assert.False(client.IsOpen);

        var serverRecvAfterClientClose = await server.ReceiveAsync();
        Assert.Null(serverRecvAfterClientClose);

        // Client Send after close throws
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("test").AsTask());
        // Client Receive after close returns null
        var clientRecv = await client.ReceiveAsync();
        Assert.Null(clientRecv);

        // Close server transport
        await server.CloseAsync();
        await server.DisposeAsync(); // Idempotent dispose
        Assert.False(server.IsOpen);

        // Server Send after close throws
        await Assert.ThrowsAsync<InvalidOperationException>(() => server.SendAsync("test").AsTask());
        // Server Receive after close returns null
        var serverRecv = await server.ReceiveAsync();
        Assert.Null(serverRecv);
    }
}
