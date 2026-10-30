using Unciv.Client.Net.Session;
using Unciv.Core.Tests.Net.ClientNet;
using Unciv.Json;
using Unciv.Net;
using Unciv.Net.Persistence;
using Unciv.Net.Standby;

namespace Unciv.Core.Tests.Net.Standby;

/// <summary>T272: the successor's client against a scripted (and misbehaving) host. Replicated state is only stored for a player who accepted, and a bad stream restarts a bounded number of times.</summary>
public sealed class StandbyClientStoreTests : IDisposable
{
    private const string GameId = "game-1";
    private static readonly string MetaJson = UncivJson.Serialize(new GameMeta { GameId = GameId });
    private readonly string root = Directory.CreateTempSubdirectory("unciv-standby-client-").FullName;


    public void Dispose()
    {
        if (!OperatingSystem.IsWindows()) try { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best effort */ }
        try { Directory.Delete(root, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best effort */ }
    }

    private readonly List<int> resubscribeDelays = [];
    private int maxResubscribes = 5;

    private ClientNetRig Rig(string standbyRoot, string? joinedGame = null) => new(id => new ClientSessionOptions
    {
        Identity = id,
        DisplayName = "Alice",
        RulesetHash = "hash",
        ExpectedFingerprint = ClientNetRig.Fingerprint,
        ReconnectDelay = (_, _) => Task.CompletedTask,
        StandbyRoot = standbyRoot,
        GameId = joinedGame,
        StandbyResubscribeDelay = (attempt, _) =>
        {
            lock (resubscribeDelays) resubscribeDelays.Add(attempt);
            return Task.CompletedTask;
        },
        MaxStandbyResubscribes = maxResubscribes,
    });

    // The client acks while the test scripts the host, so the next frame of a type is waited for rather than the next frame.
    private static async Task<T> NextFromClientAsync<T>(FakeHostConnection host) where T : Message
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var frame = await host.Transport.ReceiveAsync(cts.Token) ?? throw new InvalidOperationException("closed");
            if (ProtocolCodec.Decode(frame) is T hit) return hit;
        }
    }


    private async Task<(ClientNetRig Rig, FakeHostConnection Host)> AcceptedAsync(string standbyRoot, bool accept = true, string? joinedGame = null)
    {
        var rig = Rig(standbyRoot, joinedGame);
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        Assert.True((await join).Joined);
        await host.SendAsync(new StandbyOffer { KeyId = rig.PlayerKey });
        await rig.NextAsync<StandbyOffered>();
        if (accept)
        {
            await rig.Session.AcceptStandbyAsync(["friend.example"], 47821, "fp");
            await host.ReceiveAsync<StandbyAccept>();
        }
        return (rig, host);
    }

    [Fact]
    public async Task WithoutAccepting_AHostStatusCreatesNothing_AndFramesAreIgnored()
    {
        var (rig, host) = await AcceptedAsync(root, accept: false);
        await using var _ = rig;
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
        await host.SendAsync(new StandbyEntry { Epoch = 0, Seq = 1, Line = "{}" });
        await rig.NextAsync<StandbyStatusChanged>();
        await Task.Delay(100);
        Assert.False(Directory.Exists(StandbyStore.DirectoryFor(root, GameId)!));
        Assert.Equal(-1, rig.Session.StandbyHaveSeq);
    }

    [Fact]
    public async Task AnUnsafeGameId_IsNeverUsedAsADirectory()
    {
        var (rig, host) = await AcceptedAsync(root);
        await using var _ = rig;
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = "../escape" });
        await rig.NextAsync<StandbyStatusChanged>();
        await Task.Delay(100);
        Assert.Empty(Directory.GetDirectories(root));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(root)!, "escape")));
    }

    [Fact]
    public async Task AStoreThatCannotBeOpened_IsReportedAndNotSubscribed()
    {
        var blocker = Path.Combine(root, "file");
        File.WriteAllText(blocker, "x");
        var (rig, host) = await AcceptedAsync(Path.Combine(blocker, "below"));
        await using var _ = rig;
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
        Assert.Equal("store-unavailable", (await rig.NextAsync<StandbyRejected>()).Reason);
    }

    [Fact]
    public async Task AStreamThatKeepsBeingRejected_RestartsThreeTimes_ThenStopsStoring()
    {
        var (rig, host) = await AcceptedAsync(root);
        await using var _ = rig;
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
        var subscribe = await host.ReceiveAsync<StandbySubscribe>();
        Assert.Equal(-1, subscribe.HaveSeq);

        // A frame of another epoch is not this stream's.
        await host.SendAsync(new StandbyEntry { Epoch = 99, Seq = 1, Line = "x" });
        await host.SendAsync(new StandbyEntry { Epoch = 0, Seq = 1, Line = "not json" });
        Assert.Equal("no-store", (await rig.NextAsync<StandbyRejected>()).Reason);
        Assert.Equal(-1, (await host.ReceiveAsync<StandbySubscribe>()).HaveSeq); // restart request
        await host.SendAsync(new StandbyEntry { Epoch = 0, Seq = 1, Line = "not json" }); // still the old stream's frame: skipped, no new rejection

        for (var round = 0; round < 3; round++)
        {
            await host.SendAsync(new StandbyMeta { Epoch = 0, Header = "{\"formatVersion\":1}\n{", Meta = MetaJson }); // the new stream's first frame, bad again
            Assert.Equal("header-invalid", (await rig.NextAsync<StandbyRejected>()).Reason);
            if (round < 2) Assert.Equal(-1, (await host.ReceiveAsync<StandbySubscribe>()).HaveSeq);
        }
        await host.SendAsync(new StandbyEntry { Epoch = 0, Seq = 1, Line = "not json" });
        await Task.Delay(100);
        Assert.Equal(-1, rig.Session.StandbyHaveSeq);
    }

    [Fact]
    public async Task ADiskThatRefusesTheCopy_StopsAckingAndReportsIt()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return; // permissions do not bind root
        var (rig, host) = await AcceptedAsync(root);
        await using var _ = rig;
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
        await host.ReceiveAsync<StandbySubscribe>();
        File.SetUnixFileMode(StandbyStore.DirectoryFor(root, GameId)!, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        await host.SendAsync(new StandbyMeta { Epoch = 0, Header = "{\"formatVersion\":1}", Meta = MetaJson });
        Assert.Equal("store-io", (await rig.NextAsync<StandbyRejected>()).Reason);
    }

    [Fact]
    public async Task RevokeAndNoneEndTheReplication()
    {
        var (rig, host) = await AcceptedAsync(root);
        await using var _ = rig;
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
        await host.ReceiveAsync<StandbySubscribe>();
        await host.SendAsync(new StandbyRevoke());
        await rig.NextAsync<StandbyRevoked>();
        await host.SendAsync(new StandbyEntry { Epoch = 0, Seq = 1, Line = "{}" });
        await Task.Delay(100);
        Assert.Equal(-1, rig.Session.StandbyHaveSeq);
    }

    [Fact]
    public async Task DecliningRemovesTheCopy()
    {
        var (rig, host) = await AcceptedAsync(root);
        await using var _ = rig;
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
        await host.ReceiveAsync<StandbySubscribe>();
        await host.SendAsync(new StandbyMeta { Epoch = 0, Header = "{\"formatVersion\":1}", Meta = MetaJson });
        var dir = StandbyStore.DirectoryFor(root, GameId)!;
        for (var attempt = 0; attempt < 250 && !File.Exists(Path.Combine(dir, "game.json")); attempt++) await Task.Delay(20);
        Assert.True(File.Exists(Path.Combine(dir, "game.json")));

        await rig.Session.DeclineStandbyAsync();
        await host.ReceiveAsync<StandbyDecline>();
        Assert.False(Directory.Exists(dir));
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
        await rig.NextAsync<StandbyStatusChanged>();
        await Task.Delay(100);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task StandbyProtocolError_ResubscribesWithBackoff_ButOnlyAFewTimes()
    {
        maxResubscribes = 2;
        var (rig, host) = await AcceptedAsync(root);
        await using var _ = rig;
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
        await NextFromClientAsync<StandbySubscribe>(host);

        for (var round = 0; round < 2; round++)
        {
            await host.SendAsync(new Error { Code = ErrorCodes.StandbyViolation, Detail = "store-io" });
            await NextFromClientAsync<Ping>(host); // the delay ended: the ping wakes the receive loop through its pong
            await host.SendAsync(new Pong());
            var again = await NextFromClientAsync<StandbySubscribe>(host);
            Assert.Equal(GameId, again.GameId);
            Assert.Equal(-1, again.HaveSeq);
        }
        await host.SendAsync(new Error { Code = ErrorCodes.StandbyViolation, Detail = "store-io" });
        await Task.Delay(200);
        lock (resubscribeDelays) Assert.Equal([1, 2], resubscribeDelays); // growing attempt numbers; the third error is not retried
    }

    [Fact]
    public async Task ADisposedSession_EndsAPendingResubscribeDelay()
    {
        var gate = new TaskCompletionSource();
        var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "Alice",
            RulesetHash = "hash",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            StandbyRoot = root,
            StandbyResubscribeDelay = async (_, token) => await gate.Task.WaitAsync(token),
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        Assert.True((await join).Joined);
        await host.SendAsync(new StandbyOffer { KeyId = rig.PlayerKey });
        await rig.NextAsync<StandbyOffered>();
        await rig.Session.AcceptStandbyAsync(["friend.example"], 47821, "fp");
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
        await NextFromClientAsync<StandbySubscribe>(host);
        await host.SendAsync(new Error { Code = ErrorCodes.StandbyViolation, Detail = "ack" });
        await rig.NextAsync<ProtocolError>();
        await rig.DisposeAsync(); // the delay is still waiting: it ends with the connection instead of resubscribing into a dead session
    }

    [Fact]
    public async Task ARestartRequestRefusedByTheThrottle_IsRepeatedAfterTheDelay()
    {
        var (rig, host) = await AcceptedAsync(root);
        await using var _ = rig;
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
        await NextFromClientAsync<StandbySubscribe>(host);
        await host.SendAsync(new StandbyEntry { Epoch = 0, Seq = 1, Line = "not json" });
        await rig.NextAsync<StandbyRejected>();
        await NextFromClientAsync<StandbySubscribe>(host); // the restart request

        await host.SendAsync(new Error { Code = ErrorCodes.StandbyViolation, Detail = "subscribe-rate" }); // the host refused it
        await NextFromClientAsync<Ping>(host);
        await host.SendAsync(new Pong());
        Assert.Equal(-1, (await NextFromClientAsync<StandbySubscribe>(host)).HaveSeq);
    }

    private static string CommandLine(long seq) => "{\"seq\":" + seq + ",\"type\":\"Noop\",\"actor\":\"Babylon\",\"payload\":{}}";

    [Fact]
    public async Task TheRestartCounter_DecaysOverAHealthyStream()
    {
        var (rig, host) = await AcceptedAsync(root);
        await using var _ = rig;
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
        await NextFromClientAsync<StandbySubscribe>(host);
        var bytes = System.Text.Encoding.UTF8.GetBytes("snapshot");
        await host.SendAsync(new StandbyMeta { Epoch = 0, Header = "{\"formatVersion\":1}", Meta = MetaJson });
        await host.SendAsync(new StandbySnapshot { Epoch = 0, Seq = 0, Part = 0, Parts = 1, Sha256 = StandbyStore.HashOf(bytes), Data = Convert.ToBase64String(bytes), Reset = true });
        await NextFromClientAsync<StandbyAck>(host);

        for (var round = 0; round < 3; round++)
        {
            await host.SendAsync(new StandbyEntry { Epoch = 0, Seq = 1, Line = "not json" });
            await rig.NextAsync<StandbyRejected>();
            await NextFromClientAsync<StandbySubscribe>(host);
            await host.SendAsync(new StandbyMeta { Epoch = 0, Header = "{\"formatVersion\":1}", Meta = MetaJson }); // the new stream's first frame
        }
        for (var i = 0; i < 100; i++) await host.SendAsync(new StandbyEntry { Epoch = 0, Seq = 0, Line = CommandLine(0) }); // already stored: accepted, a healthy stream
        await host.SendAsync(new StandbyEntry { Epoch = 0, Seq = 1, Line = "not json" });
        Assert.Equal(-1, (await NextFromClientAsync<StandbySubscribe>(host)).HaveSeq); // without the decay the fourth rejection would have stopped storing
    }

    [Fact]
    public async Task Consent_IsRecordedPerGame_AndSurvivesARestart_ButNotARevoke()
    {
        var (rig, host) = await AcceptedAsync(root, joinedGame: GameId);
        await using (rig)
        {
            Assert.True(StandbyStore.HasConsent(root, GameId));
            Assert.False(StandbyStore.HasConsent(root, "other-game"));
            await host.SendAsync(new StandbyRevoke());
            await rig.NextAsync<StandbyRevoked>();
            Assert.False(StandbyStore.HasConsent(root, GameId));
        }

        // A directory that merely exists is no consent (the old rule); a record is.
        Directory.CreateDirectory(StandbyStore.DirectoryFor(root, GameId)!);
        var (second, secondHost) = await AcceptedAsync(root, accept: false, joinedGame: GameId);
        await using (second)
        {
            await secondHost.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
            await second.NextAsync<StandbyStatusChanged>();
            await Task.Delay(100);
            Assert.False(File.Exists(Path.Combine(StandbyStore.DirectoryFor(root, GameId)!, "game.json")));
        }
        Assert.True(StandbyStore.RecordConsent(root, GameId));
        var (third, thirdHost) = await AcceptedAsync(root, accept: false, joinedGame: GameId);
        await using (third)
        {
            await thirdHost.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
            Assert.Equal(GameId, (await NextFromClientAsync<StandbySubscribe>(thirdHost)).GameId);
        }
    }

    [Fact]
    public async Task AStatusForAnotherGame_IsIgnored()
    {
        var (rig, host) = await AcceptedAsync(root, joinedGame: GameId);
        await using var _ = rig;
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = "other-game" });
        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0, GameId = GameId });
        Assert.Equal(GameId, (await host.ReceiveAsync<StandbySubscribe>()).GameId); // the first frame the client sent is for the joined game
        Assert.False(Directory.Exists(StandbyStore.DirectoryFor(root, "other-game")!));
    }
}
