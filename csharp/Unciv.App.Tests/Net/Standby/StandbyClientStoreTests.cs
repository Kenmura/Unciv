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

    private ClientNetRig Rig(string standbyRoot, string? joinedGame = null) => new(id => new ClientSessionOptions
    {
        Identity = id,
        DisplayName = "Alice",
        RulesetHash = "hash",
        ExpectedFingerprint = ClientNetRig.Fingerprint,
        ReconnectDelay = (_, _) => Task.CompletedTask,
        StandbyRoot = standbyRoot,
        GameId = joinedGame,
    });


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
