using System.Text.Json;
using Unciv.Assets;
using Unciv.Commands.Events;
using Unciv.Commands.Turn;
using Unciv.GodotClient.Play;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Net.ClientNet;

// T244: IGameSession over a ClientSession, against a scripted host.
public sealed class NetGameSessionTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private static JsonElement Event(EngineEvent e) => JsonSerializer.SerializeToElement(GameEventRegistry.ToEnvelope(e), Unciv.Json.UncivJson.DefaultOptions);

    private static async Task<(NetGameSession Net, FakeHostConnection Host)> StartAsync(ClientNetRig rig, ClientSnapshot first)
    {
        var host = await rig.StartInGameAsync(first);
        return (new NetGameSession(rig.Session, new Ruleset()), host);
    }

    private static async Task<Command> AnswerAsync(FakeHostConnection host, bool accepted, long? seq, string? code = null)
    {
        var command = await host.ReceiveAsync<Command>();
        await host.SendAsync(new CommandResult { ClientCmdId = command.ClientCmdId, Accepted = accepted, Seq = seq, Code = code });
        return command;
    }

    [Fact]
    public async Task Roster_ListsTheHumanSlotsAndFollowsLobbyUpdates()
    {
        await using var rig = new ClientNetRig();
        var (net, host) = await StartAsync(rig, ClientNetRig.Snap(5, 1));
        await using var _ = net;
        Assert.Empty(net.Roster);
        var changed = new TaskCompletionSource();
        net.RosterChanged += () => changed.TrySetResult();
        await host.SendAsync(new LobbyState
        {
            Settings = JsonDocument.Parse("{}").RootElement.Clone(),
            HostName = "Alice",
            Slots =
            [
                new LobbySlot { Slot = 0, PlayerName = "Alice", Nation = "Rome", Ready = true, Kind = "Human", Connected = true },
                new LobbySlot { Slot = 1, Nation = "Greece", Ready = false, Kind = "Human" },
                new LobbySlot { Slot = 2, Nation = "Egypt", Ready = true, Kind = "AI" },
                new LobbySlot { Slot = 3, Ready = false, Kind = "Open" },
            ]
        });
        await changed.Task.WaitAsync(Limit);
        Assert.Equal([("Rome", "Alice", true), ("Greece", "Greece", false)], net.Roster.Select(it => (it.CivId, it.Name, it.Connected)));
    }

    // C006: the connection state the reconnect overlay follows.
    [Fact]
    public async Task Connection_FollowsDropReconnectAndResync()
    {
        await using var rig = new ClientNetRig();
        var (net, host) = await StartAsync(rig, ClientNetRig.Snap(5, 1));
        await using var _ = net;
        Assert.Equal(Unciv.Client.Net.Session.ClientSessionState.InGame, net.Connection);
        var seen = new List<Unciv.Client.Net.Session.ClientSessionState>();
        var back = new TaskCompletionSource();
        var down = new TaskCompletionSource();
        net.ConnectionChanged += () =>
        {
            seen.Add(net.Connection);
            if (net.Connection == Unciv.Client.Net.Session.ClientSessionState.Reconnecting) down.TrySetResult();
            if (net.Connection == Unciv.Client.Net.Session.ClientSessionState.InGame) back.TrySetResult();
        };
        var next = rig.NewConnection();
        await host.CloseAsync();
        await down.Task.WaitAsync(Limit);
        await rig.HandshakeAsync(next);
        await next.ReceiveAsync<ResyncRequest>();
        await next.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(6, 1)));
        await back.Task.WaitAsync(Limit);
        Assert.Contains(Unciv.Client.Net.Session.ClientSessionState.Reconnecting, seen);
        Assert.Null(net.EndedReason);
        Assert.False(net.HostClosing);
    }

    [Fact]
    public async Task Connection_HostClosingIsFlaggedUntilTheSessionIsBackInGame()
    {
        await using var rig = new ClientNetRig();
        var (net, host) = await StartAsync(rig, ClientNetRig.Snap(5, 1));
        await using var _ = net;
        var flagged = new TaskCompletionSource();
        net.ConnectionChanged += () => { if (net.HostClosing) flagged.TrySetResult(); };
        await host.SendAsync(new HostClosing());
        await flagged.Task.WaitAsync(Limit);
        Assert.True(net.HostClosing);
    }

    [Fact]
    public async Task Connection_EndOfSessionIsReportedWithItsReason()
    {
        await using var rig = new ClientNetRig();
        var (net, _) = await StartAsync(rig, ClientNetRig.Snap(5, 1));
        await using var __ = net;
        var ended = new TaskCompletionSource();
        net.ConnectionChanged += () => { if (net.EndedReason != null) ended.TrySetResult(); };
        await rig.Session.LeaveAsync();
        await ended.Task.WaitAsync(Limit);
        Assert.Equal("left", net.EndedReason);
    }

    [Fact]
    public async Task Constructor_WithoutSnapshot_Throws()
    {
        await using var rig = new ClientNetRig();
        Assert.Throws<InvalidOperationException>(() => new NetGameSession(rig.Session, new Ruleset()));
    }

    // T378: a joined game is keyed by the host's key, so what the viewer saw outlives a reconnect and a restart of the client, and the host sends no more than before.
    [Fact]
    public async Task OwnerMemory_SurvivesReconnectAndRestart_WithoutTheHostSendingFoggedOwners()
    {
        static TileDto Tile(int x, int y, bool visible, string? owner) => new()
        {
            X = x,
            Y = y,
            Visible = visible,
            BaseTerrain = "Grassland",
            Features = [],
            RiverBottom = false,
            RiverBottomLeft = false,
            RiverBottomRight = false,
            Owner = owner,
        };
        static ClientSnapshot With(ClientSnapshot snap, long seq, bool lit) => snap with
        {
            Seq = seq,
            Tiles = [Tile(0, 0, lit, lit ? "Rome" : null), Tile(1, 0, lit, lit ? "Rome" : null), Tile(2, 0, lit, lit ? "Greece" : null), Tile(3, 0, false, null)],
        };
        string folder = Path.Combine(Directory.CreateTempSubdirectory("unciv-owner-memory-").FullName, "store");
        try
        {
            await using var rig = new ClientNetRig();
            var lit = With(ClientNetRig.Snap(5, 1), 5, lit: true);
            var (net, host) = await StartAsync(rig, lit);
            await using var _ = net;
            Assert.StartsWith("host:", net.OwnerMemoryKey, StringComparison.Ordinal);
            var store = new TileOwnerMemoryStore(folder);
            var screenMemory = new TileOwnerMemory();
            Assert.True(screenMemory.Remember(net.Snapshot.Tiles));
            Assert.True(store.Save(net.OwnerMemoryKey!, net.ViewerCivId, net.Snapshot.Turn, net.Snapshot.Tiles, screenMemory));
            // The connection drops; the host's resync answers with everything fogged: the wire carries no owner of a fogged tile
            var back = new TaskCompletionSource();
            net.SnapshotChanged += fresh => { if (fresh.Seq == 6) back.TrySetResult(); };
            var next = rig.NewConnection();
            await host.CloseAsync();
            await rig.HandshakeAsync(next);
            await next.ReceiveAsync<ResyncRequest>();
            await next.SendAsync(ClientNetRig.SnapshotMessage(With(lit, 6, lit: false)));
            await back.Task.WaitAsync(Limit);
            Assert.All(net.Snapshot.Tiles, it => Assert.Null(it.Owner));
            Assert.False(screenMemory.Remember(net.Snapshot.Tiles)); // a fogged snapshot teaches nothing
            // A restarted client (a fresh memory, the same store): the borders of the fogged territory are back
            var restarted = new TileOwnerMemory();
            Assert.True(new TileOwnerMemoryStore(folder).Load(net.OwnerMemoryKey!, net.ViewerCivId, net.Snapshot.Turn, net.Snapshot.Tiles, restarted));
            var edges = MapOverlays.BorderEdges(net.Snapshot.Tiles, restarted);
            Assert.Contains(edges, it => it.Owner == "Rome");
            Assert.Contains(edges, it => it.Owner == "Greece");
            Assert.DoesNotContain(edges, it => it.X == 3); // the tile that was never in sight stays borderless
            Assert.Empty(MapOverlays.BorderEdges(net.Snapshot.Tiles, new TileOwnerMemory()));
            // Another viewer on the same host (a second civ at the same computer) starts empty
            Assert.False(new TileOwnerMemoryStore(folder).Load(net.OwnerMemoryKey!, "Greece", net.Snapshot.Turn, net.Snapshot.Tiles, new TileOwnerMemory()));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task Delta_ReplacesSnapshotAndRaisesEventsInOrder()
    {
        await using var rig = new ClientNetRig();
        var s5 = ClientNetRig.Snap(5, 1);
        var (net, host) = await StartAsync(rig, s5);
        await using var _ = net;
        Assert.Equal("A", net.ViewerCivId);
        var order = new List<string>();
        var batch = new TaskCompletionSource<EventBatch>();
        net.SnapshotChanged += s => order.Add("snap" + s.Seq);
        net.EventsReceived += b => { order.Add("events" + b.Seq); batch.TrySetResult(b); };
        var s6 = ClientNetRig.Snap(6, 1, 2);
        await host.SendAsync(ClientNetRig.Delta(s5, s6, Event(new TurnStarted("A", 1)), JsonDocument.Parse("{\"type\":\"nope\",\"data\":{}}").RootElement.Clone()));
        var got = await batch.Task.WaitAsync(Limit);
        Assert.Equal(["snap6", "events6"], order);
        Assert.Equal(6, net.Snapshot.Seq);
        Assert.Single(got.Events); // the undecodable event is dropped
        Assert.IsType<TurnStarted>(got.Events[0]);
    }

    [Fact]
    public async Task Submit_ReturnsTheEventsOfItsOwnSequence_WhateverTheArrivalOrder()
    {
        await using var rig = new ClientNetRig();
        var s5 = ClientNetRig.Snap(5, 1);
        var (net, host) = await StartAsync(rig, s5);
        await using var _ = net;
        var task = net.SubmitAsync(new EndTurn());
        var s6 = ClientNetRig.Snap(6, 1);
        var command = await host.ReceiveAsync<Command>();
        await host.SendAsync(ClientNetRig.Delta(s5, s6, Event(new PlayerEndedTurn("A", 1)))); // delta before the answer
        await host.SendAsync(new CommandResult { ClientCmdId = command.ClientCmdId, Accepted = true, Seq = 6 });
        var result = await task.WaitAsync(Limit);
        Assert.True(result.Accepted);
        Assert.IsType<PlayerEndedTurn>(Assert.Single(result.Events));

        var task2 = net.SubmitAsync(new EndTurn());
        var command2 = await host.ReceiveAsync<Command>();
        await host.SendAsync(new CommandResult { ClientCmdId = command2.ClientCmdId, Accepted = true, Seq = 7 }); // answer before the delta
        await host.SendAsync(ClientNetRig.Delta(s6, ClientNetRig.Snap(7, 1), Event(new PlayerUndidEndTurn("A", 1))));
        Assert.IsType<PlayerUndidEndTurn>(Assert.Single((await task2.WaitAsync(Limit)).Events));
    }

    [Fact]
    public async Task Submit_RefusedByHost_IsAResultNotAnException()
    {
        await using var rig = new ClientNetRig();
        var (net, host) = await StartAsync(rig, ClientNetRig.Snap(5, 1));
        await using var _ = net;
        var task = net.SubmitAsync(new EndTurn());
        await AnswerAsync(host, false, null, "not-your-turn");
        var result = await task.WaitAsync(Limit);
        Assert.False(result.Accepted);
        Assert.Equal("not-your-turn", result.Message);
    }

    [Fact]
    public async Task Submit_AfterSessionLeft_FaultsAsTransportFailure()
    {
        await using var rig = new ClientNetRig();
        var (net, _) = await StartAsync(rig, ClientNetRig.Snap(5, 1));
        await using var __ = net;
        await rig.Session.LeaveAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => net.SubmitAsync(new EndTurn()));
    }

    [Fact]
    public async Task EndTurn_CompletesWhenTheMirrorShowsALaterTurn()
    {
        await using var rig = new ClientNetRig();
        var s5 = ClientNetRig.Snap(5, 1);
        var (net, host) = await StartAsync(rig, s5);
        await using var _ = net;
        var task = net.EndTurnAsync();
        await AnswerAsync(host, true, 6);
        var s6 = s5 with { Seq = 6, TurnStatus = new TurnStatusDto { Ended = ["A"], Phase = "Playing" } };
        await host.SendAsync(ClientNetRig.Delta(s5, s6, Event(new PlayerEndedTurn("A", 1))));
        await Task.Delay(100);
        Assert.False(task.IsCompleted); // the host has not moved on yet
        var s7 = s6 with { Seq = 7, Turn = 2, TurnStatus = new TurnStatusDto { Ended = [], Phase = "Playing" } };
        await host.SendAsync(ClientNetRig.Delta(s6, s7, Event(new TurnStarted("A", 2))));
        var outcome = await task.WaitAsync(Limit);
        Assert.True(outcome.Accepted);
        Assert.True(outcome.NextTurnBegun);
        Assert.IsType<PlayerEndedTurn>(Assert.Single(outcome.Events));
    }

    [Fact]
    public async Task EndTurn_Cancelled_IsARefusal_AndCallingAgainDoesNotResubmit()
    {
        await using var rig = new ClientNetRig();
        var s5 = ClientNetRig.Snap(5, 1);
        var (net, host) = await StartAsync(rig, s5);
        await using var _ = net;
        using var cts = new CancellationTokenSource();
        var task = net.EndTurnAsync(null, cts.Token);
        await AnswerAsync(host, true, 6);
        var s6 = s5 with { Seq = 6, TurnStatus = new TurnStatusDto { Ended = ["A"], Phase = "Playing" } };
        await host.SendAsync(ClientNetRig.Delta(s5, s6));
        await Task.Delay(100);
        await cts.CancelAsync();
        var cancelled = await task.WaitAsync(Limit);
        Assert.False(cancelled.Accepted);
        Assert.Equal(Unciv.Net.Visibility.ViewerSessionFlow.CancelledMessage, cancelled.Message);

        var again = net.EndTurnAsync(); // waits only: the viewer's end is already recorded in the mirror
        await host.SendAsync(ClientNetRig.Delta(s6, s6 with { Seq = 7, Turn = 2, TurnStatus = new TurnStatusDto { Ended = [], Phase = "Playing" } }));
        Assert.True((await again.WaitAsync(Limit)).Accepted);
    }

    [Fact]
    public async Task EndTurn_WhileOneRuns_IsRefused()
    {
        await using var rig = new ClientNetRig();
        var (net, host) = await StartAsync(rig, ClientNetRig.Snap(5, 1));
        await using var _ = net;
        var first = net.EndTurnAsync();
        await host.ReceiveAsync<Command>();
        var second = await net.AutoPlayTurnAsync();
        Assert.False(second.Accepted);
        Assert.False(first.IsCompleted);
    }

    [Fact]
    public async Task Gap_RaisesDesynchronized_AndResyncSnapshotReplacesTheMirror()
    {
        await using var rig = new ClientNetRig();
        var (net, host) = await StartAsync(rig, ClientNetRig.Snap(5, 1));
        await using var _ = net;
        var reasons = new List<string>();
        var snaps = new List<long>();
        var lost = new TaskCompletionSource();
        var replaced = new TaskCompletionSource();
        net.Desynchronized += r => { reasons.Add(r); lost.TrySetResult(); };
        net.SnapshotChanged += s => { snaps.Add(s.Seq); replaced.TrySetResult(); };
        await host.SendAsync(ClientNetRig.Delta(ClientNetRig.Snap(6, 1), ClientNetRig.Snap(7, 1))); // seq 6 never arrived
        await lost.Task.WaitAsync(Limit);
        await host.ReceiveAsync<ResyncRequest>();
        await host.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(9, 1, 2)));
        await replaced.Task.WaitAsync(Limit);
        Assert.Equal(["sequence-gap"], reasons);
        Assert.Equal([9L], snaps);
        Assert.Equal(9, net.Snapshot.Seq);
    }

    [Fact]
    public async Task ResyncAsync_CompletesWithTheMirrorCurrent()
    {
        await using var rig = new ClientNetRig();
        var (net, host) = await StartAsync(rig, ClientNetRig.Snap(5, 1));
        await using var _ = net;
        var task = net.ResyncAsync();
        await host.ReceiveAsync<ResyncRequest>();
        await host.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(8, 1)));
        await task.WaitAsync(Limit);
        Assert.Equal(8, net.Snapshot.Seq);
    }

    [Fact]
    public async Task ResyncAsync_WithSameSeq_StillWaitsForTheSnapshotToBeApplied()
    {
        await using var rig = new ClientNetRig();
        var (net, host) = await StartAsync(rig, ClientNetRig.Snap(5, 1));
        await using var _ = net;
        var task = net.ResyncAsync();
        await host.ReceiveAsync<ResyncRequest>();
        await host.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(5, 1, 2)));
        await task.WaitAsync(Limit);
        Assert.Equal(2, net.Snapshot.Units.Count);
    }

    [Fact]
    public async Task EndTurn_CancelledWhileTheSubmitWaits_IsACancelledRefusal()
    {
        await using var rig = new ClientNetRig();
        var (net, host) = await StartAsync(rig, ClientNetRig.Snap(5, 1));
        await using var _ = net;
        using var cts = new CancellationTokenSource();
        var task = net.EndTurnAsync(null, cts.Token);
        await host.ReceiveAsync<Command>(); // never answered
        await cts.CancelAsync();
        var outcome = await task.WaitAsync(Limit);
        Assert.False(outcome.Accepted);
        Assert.Equal(Unciv.Net.Visibility.ViewerSessionFlow.CancelledMessage, outcome.Message);
    }

    [Fact]
    public async Task ExportSave_IsNull_AndDisposeStopsThePump()
    {
        await using var rig = new ClientNetRig();
        var (net, host) = await StartAsync(rig, ClientNetRig.Snap(5, 1));
        Assert.Null(await net.ExportSaveAsync());
        await net.DisposeAsync();
        await host.SendAsync(ClientNetRig.Delta(ClientNetRig.Snap(5, 1), ClientNetRig.Snap(6, 1)));
        await Task.Delay(100);
        Assert.Equal(5, net.Snapshot.Seq);
    }
}
