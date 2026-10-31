using System.Text.Json.Nodes;
using Unciv.Net.Relay;
using Unciv.Relay;

namespace Unciv.Core.Tests.Relay;

// T367: the accept-reply failure path of RelayServer.Join.cs and the relay-secret file of RelayServer.cs.
public sealed class RelaySecretAndAcceptFaultTests
{
    /// <summary>A stream whose writes fail on demand (the host data channel dying between its accept and the relay's ok).</summary>
    private sealed class FaultyStream(Stream inner) : Stream
    {
        public volatile bool FailWrites;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            FailWrites ? throw new IOException("injected write failure") : inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task AFailedOkReplyToTheHostDataChannelRefusesTheJoinerWithRoomFullInsteadOfHanging()
    {
        await using var rig = await RelayRig.StartAsync();
        var created = new List<FaultyStream>();
        rig.Server.StreamWrapper = s =>
        {
            var wrapped = new FaultyStream(s);
            lock (created) created.Add(wrapped);
            return wrapped;
        };
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        await using var joiner = await rig.ConnectAsync();
        await joiner.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = host.Room });
        var id = (await host.NextEventAsync("incoming"))["id"]!.GetValue<string>();

        await using var data = await rig.ConnectAsync();
        FaultyStream dataSide;
        lock (created) dataSide = created[^1]; // the data channel is the newest connection
        dataSide.FailWrites = true;
        await data.SendAsync(new JsonObject
        {
            ["op"] = "accept",
            ["v"] = 1,
            ["room"] = host.Room,
            ["id"] = id,
            ["sig"] = host.Key.Sign(RelayProtocol.AcceptSignedText(RelayRig.PublicName, data.Challenge, host.Room, id)),
        });

        var reply = await joiner.ReadAsync();
        Assert.Equal("room-full", reply["code"]!.GetValue<string>());
        await RelayHostTests.Until(() => rig.Server.SpliceCount == 0);
    }

    [Fact]
    public async Task ANewSecretIsCreatedWithMode0600AndSurvivesARestart()
    {
        if (OperatingSystem.IsWindows()) return;
        await using var rig = await RelayRig.StartAsync();
        var path = Path.Combine(rig.Dir, "relay-secret");
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Equal(64, File.ReadAllText(path).Trim().Length);
        var before = File.ReadAllText(path);
        await using var again = await RelayRig.StartAsync(prepareDir: d => File.WriteAllText(Path.Combine(d, "relay-secret"), before));
        Assert.Equal(before, File.ReadAllText(Path.Combine(again.Dir, "relay-secret"))); // an existing valid secret is reused, not rewritten
    }

    [Fact]
    public async Task AShortSecretIsReplacedByAFreshOne()
    {
        await using var rig = await RelayRig.StartAsync(prepareDir: d => File.WriteAllText(Path.Combine(d, "relay-secret"), "abcd"));
        Assert.Equal(64, File.ReadAllText(Path.Combine(rig.Dir, "relay-secret")).Trim().Length);
    }

    [Fact]
    public async Task AnInvalidHexSecretIsAUsageErrorNotAFormatException()
    {
        var e = await Assert.ThrowsAsync<RelayUsageException>(async () => await RelayRig.StartAsync(prepareDir: d => File.WriteAllText(Path.Combine(d, "relay-secret"), "not hex at all")));
        Assert.Contains("relay-secret", e.Message);
    }

    [Fact]
    public async Task AnUnexpectedHandlerExceptionIsContainedAndTheRelayKeepsServing()
    {
        await using var rig = await RelayRig.StartAsync();
        rig.Server.StreamWrapper = _ => throw new NotSupportedException("injected bug"); // not one of the expected I/O exceptions: the 'handler-failed' log path
        await using (var broken = await RelayClient.ConnectAsync(rig.Port, rig.UsesTls, readChallenge: false))
            Assert.True(await broken.ClosedAsync());
        rig.Server.StreamWrapper = null;
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        Assert.True(host.Ok);
        Assert.Contains(RelayRig.PublicName, rig.Server.PublicNames);
    }

    [Fact]
    public async Task AClientThatSendsGarbageInsteadOfATlsHelloIsDroppedAndTheRelayKeepsServing()
    {
        await using var rig = await RelayRig.StartAsync(RelayMode.SelfSigned);
        using (var raw = new System.Net.Sockets.TcpClient())
        {
            await raw.ConnectAsync(System.Net.IPAddress.Loopback, rig.Port);
            await raw.GetStream().WriteAsync("this is not a TLS client hello\n\n\n\n"u8.ToArray());
            var buffer = new byte[16];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Assert.Equal(0, await raw.GetStream().ReadAsync(buffer, timeout.Token)); // the relay closes without answering
        }
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        Assert.True(host.Ok);
    }

    [Theory]
    [InlineData("!!! not base64 !!!")]
    [InlineData("AAAA")]
    public async Task AnAcceptWithAMalformedSignatureIsRefusedAsBadSignature(string signature)
    {
        await using var rig = await RelayRig.StartAsync();
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        await using var data = await rig.ConnectAsync();
        var reply = await data.RequestAsync(new JsonObject { ["op"] = "accept", ["v"] = 1, ["room"] = host.Room, ["id"] = "abc", ["sig"] = signature });
        Assert.Equal("bad-signature", reply["code"]!.GetValue<string>());
    }
}
