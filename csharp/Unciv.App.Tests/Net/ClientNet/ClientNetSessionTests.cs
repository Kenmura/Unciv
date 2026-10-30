using System.Text.Json;
using Unciv.Client.Net.Session;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Remote;
using Unciv.Net.Visibility;
using Unciv.Queries;

namespace Unciv.Core.Tests.Net.ClientNet;

public sealed class ClientNetSessionTests
{
    [QueryType("t180.Echo")]
    public sealed record EchoQuery(int Value = 0) : IQuery<EchoResult>
    {
        public string Type => "t180.Echo";

        public EchoResult Run(Unciv.View.GameView view) => new(Value);
    }

    public sealed record EchoResult(int Value);

    private static QueryRegistry EchoRegistry()
    {
        var registry = new QueryRegistry(Unciv.Json.UncivJson.DefaultOptions);
        registry.Register<EchoQuery>("t180.Echo");
        return registry;
    }

    private static ClientNetRig QueryRig() => new(id => new ClientSessionOptions
    {
        Identity = id,
        DisplayName = "A",
        RulesetHash = "h",
        ExpectedFingerprint = ClientNetRig.Fingerprint,
        Queries = EchoRegistry()
    });

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    // ---------------------------------------------------------------- join and host key

    [Fact]
    public async Task Join_WithInviteFingerprint_RunsHandshakeAndEntersLobby()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        Assert.True((await join).Joined);
        await host.SendAsync(new LobbyState { Settings = EmptyObject, Slots = [], HostName = "Bob" });
        var lobby = await rig.NextAsync<LobbyUpdated>();
        Assert.Equal("Bob", lobby.Lobby.HostName);
        Assert.Equal(ClientSessionState.Lobby, rig.Session.State);
    }

    [Fact]
    public async Task Join_FingerprintDifferentFromInvite_IsRefusedBeforeAnythingIsSent()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = HostKey.Fingerprint(new byte[] { 9 }), RequiresPassword = false });
        var result = await join;
        Assert.False(result.Joined);
        Assert.Equal(ClientCodes.HostKeyMismatch, result.Code);
        Assert.Null(await host.Transport.ReceiveAsync()); // no Join, no password: the client just hung up
        Assert.Equal(ClientSessionState.Disconnected, rig.Session.State);
    }

    [Fact]
    public async Task Join_HelloAdvertisesHostMigrationAndKnownEpoch_OnlyWithAGame()
    {
        await using var fresh = new ClientNetRig();
        var first = fresh.NewConnection();
        var joinFresh = fresh.Session.JoinAsync();
        var hello = await first.ReceiveAsync<Hello>();
        Assert.True(hello.HostMigration);
        Assert.Null(hello.KnownEpoch);
        Assert.Null(hello.GameId);
        await first.Transport.CloseAsync();
        await joinFresh;

        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            GameId = "g1",
            KnownEpoch = 2
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        var known = await host.ReceiveAsync<Hello>();
        Assert.Equal("g1", known.GameId);
        Assert.Equal(2, known.KnownEpoch);
        await host.Transport.CloseAsync();
        await join;
    }

    [Fact]
    public async Task Join_HostBelowTheStoredEpoch_IsAHardStop()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            GameId = "g1",
            KnownEpoch = 2
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false, Epoch = 1 });
        var result = await join;
        Assert.False(result.Joined);
        Assert.Equal(ClientCodes.StaleEpoch, result.Code);
        Assert.Null(await host.Transport.ReceiveAsync()); // no Join was sent
    }

    [Fact]
    public async Task Join_AStandbyListenerIsNotAHost()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "standby", Challenge = "", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false, Standby = true });
        var result = await join;
        Assert.False(result.Joined);
        Assert.Equal(ClientCodes.NotHostYet, result.Code);
        Assert.Null(await host.Transport.ReceiveAsync()); // no Join was sent
    }

    [Fact]
    public async Task Join_HostWithoutAnEpochField_IsEpochZero()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            GameId = "g1",
            KnownEpoch = 1
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false });
        Assert.Equal(ClientCodes.StaleEpoch, (await join).Code);
    }

    [Fact]
    public async Task Join_RefusedAfterAHigherEpochWelcome_PersistsNothing()
    {
        var persisted = new List<int>();
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            GameId = "g1",
            KnownEpoch = 1,
            PersistKnownEpoch = persisted.Add
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false, Epoch = int.MaxValue });
        await host.ReceiveAsync<Join>();
        await host.ReceiveAsync<Auth>();
        await host.SendAsync(new JoinRefused { Code = JoinRefusedCodes.BadCredentials });
        Assert.False((await join).Joined);
        Assert.Empty(persisted);
    }

    [Fact]
    public async Task Join_EpochStoreThatCannotWrite_DoesNotAbortTheJoin()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            GameId = "g1",
            KnownEpoch = 1,
            PersistKnownEpoch = _ => throw new IOException("disk full")
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false, Epoch = 2 });
        await host.ReceiveAsync<Join>();
        await host.ReceiveAsync<Auth>();
        await host.SendAsync(new JoinOk());
        Assert.True((await join).Joined);
    }

    [Fact]
    public async Task Join_HostAboveTheStoredEpoch_IsRememberedAndPersisted()
    {
        var persisted = new List<int>();
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            GameId = "g1",
            KnownEpoch = 1,
            PersistKnownEpoch = persisted.Add
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false, Epoch = 3 });
        await host.ReceiveAsync<Join>();
        await host.ReceiveAsync<Auth>();
        await host.SendAsync(new JoinOk());
        Assert.True((await join).Joined);
        Assert.Equal([3], persisted);
    }

    [Fact]
    public async Task Join_UnknownHost_TofuAccepted_PinsAndPersists()
    {
        var known = new KnownHosts();
        KnownHosts? persisted = null;
        string? prompted = null;
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "Alice",
            RulesetHash = "h",
            HostAddress = "1.2.3.4:47821",
            KnownHosts = known,
            PersistKnownHosts = it => persisted = it,
            TrustPrompt = (_, fp) => { prompted = fp; return Task.FromResult(true); }
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        Assert.True((await join).Joined);
        Assert.Equal(ClientNetRig.Fingerprint, prompted);
        Assert.Same(known, persisted);
        Assert.Equal(HostTrust.Trusted, known.Check("1.2.3.4:47821", ClientNetRig.Fingerprint));
    }

    [Fact]
    public async Task Join_UnknownHost_TofuDeclinedOrMissing_IsRefused()
    {
        foreach (Func<string, string, Task<bool>>? prompt in new Func<string, string, Task<bool>>?[] { (_, _) => Task.FromResult(false), null })
        {
            var known = new KnownHosts();
            await using var rig = new ClientNetRig(id => new ClientSessionOptions
            {
                Identity = id,
                DisplayName = "A",
                RulesetHash = "h",
                HostAddress = "h:1",
                KnownHosts = known,
                TrustPrompt = prompt
            });
            var host = rig.NewConnection();
            var join = rig.Session.JoinAsync();
            await host.ReceiveAsync<Hello>();
            await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false });
            Assert.Equal(ClientCodes.HostUntrusted, (await join).Code);
            Assert.Empty(known.Pins);
        }
    }

    [Fact]
    public async Task Join_PinnedHostWithChangedKey_IsHardStopWithoutPrompt()
    {
        var known = new KnownHosts();
        known.Pin("h:1", HostKey.Fingerprint(new byte[] { 7 }));
        var prompted = false;
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            HostAddress = "h:1",
            KnownHosts = known,
            TrustPrompt = (_, _) => { prompted = true; return Task.FromResult(true); }
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false });
        Assert.Equal(ClientCodes.HostKeyChanged, (await join).Code);
        Assert.False(prompted);
    }

    [Fact]
    public async Task Join_UnpinnedHostWithoutOptIn_IsRefused()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions { Identity = id, DisplayName = "A", RulesetHash = "h" });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false });
        Assert.Equal(ClientCodes.HostUntrusted, (await join).Code);
    }

    [Fact]
    public async Task Join_WelcomeWithOtherProtocol_IsRefused()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = 99, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false });
        Assert.Equal(ClientCodes.ProtocolMismatch, (await join).Code);
    }

    [Fact]
    public async Task Join_SendsPasswordAndInviteToken()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "Alice",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            Password = "pw",
            InviteToken = "tok"
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = true });
        var message = await host.ReceiveAsync<Join>();
        Assert.Equal("pw", message.Password);
        Assert.Equal("tok", message.InviteToken);
        Assert.Equal("Alice", message.DisplayName);
        await host.ReceiveAsync<Auth>();
        await host.SendAsync(new JoinOk());
        Assert.True((await join).Joined);
    }

    [Fact]
    public async Task Join_Refused_EndsSessionWithHostCode()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new JoinRefused { Code = JoinRefusedCodes.BadCredentials });
        var result = await join;
        Assert.False(result.Joined);
        Assert.Equal(JoinRefusedCodes.BadCredentials, result.Code);
        Assert.Equal(JoinRefusedCodes.BadCredentials, (await rig.NextAsync<JoinRefusedEvent>()).Code);
        Assert.Equal(1, rig.ConnectAttempts); // a refusal is not retried
    }

    [Fact]
    public async Task Join_ConnectFails_ReportsConnectFailed()
    {
        await using var rig = new ClientNetRig();
        var result = await rig.Session.JoinAsync();
        Assert.Equal(new JoinResult(false, ClientCodes.ConnectFailed), result);
    }

    // ---------------------------------------------------------------- mirror: snapshot and deltas

    [Fact]
    public async Task GameStart_SnapshotFillsMirrorAndEntersGame()
    {
        await using var rig = new ClientNetRig();
        var first = ClientNetRig.Snap(5, 1, 2);
        await rig.StartInGameAsync(first);
        Assert.Equal(ClientSessionState.InGame, rig.Session.State);
        Assert.Equal(5, rig.Session.Game.Seq);
        Assert.Equal([1, 2], rig.Session.Game.Snapshot!.Units.Select(it => it.Id));
    }

    [Fact]
    public async Task Delta_InOrder_UpdatesMirrorAndRaisesEventsIncludingOthersCommands()
    {
        await using var rig = new ClientNetRig();
        var s5 = ClientNetRig.Snap(5, 1);
        var s6 = ClientNetRig.Snap(6, 1, 2);
        var s7 = ClientNetRig.Snap(7, 2);
        var host = await rig.StartInGameAsync(s5);
        await host.SendAsync(ClientNetRig.Delta(s5, s6, JsonDocument.Parse("{\"e\":1}").RootElement.Clone())); // nobody here asked for it: another human's command
        await host.SendAsync(ClientNetRig.Delta(s6, s7));
        var one = await rig.NextAsync<DeltaApplied>();
        var two = await rig.NextAsync<DeltaApplied>();
        Assert.Equal(6, one.Seq);
        Assert.Single(one.Events);
        Assert.Equal(7, two.Seq);
        Assert.Equal(SnapshotBuilder.ComputeViewHash(s7), rig.Session.Game.ViewHash);
        Assert.Equal([2], rig.Session.Game.Snapshot!.Units.Select(it => it.Id));
    }

    [Fact]
    public async Task Delta_AlreadySeen_IsIgnoredWithoutResync()
    {
        await using var rig = new ClientNetRig();
        var s5 = ClientNetRig.Snap(5, 1);
        var s6 = ClientNetRig.Snap(6, 1, 2);
        var host = await rig.StartInGameAsync(s5);
        var delta = ClientNetRig.Delta(s5, s6);
        await host.SendAsync(delta);
        await host.SendAsync(delta);
        await host.SendAsync(new Ping());
        await host.SendAsync(new Pong());
        await rig.NextAsync<DeltaApplied>();
        await host.SendAsync(new TurnStatus { Turn = 1, Ended = [], Phase = "Playing" });
        await rig.NextAsync<TurnStatusChanged>();
        Assert.Equal(6, rig.Session.Game.Seq);
        Assert.DoesNotContain(Drain(rig), it => it is Desynchronized or DeltaApplied);
    }

    [Fact]
    public async Task Delta_WithGap_KeepsMirrorRaisesDesyncAndResyncsFromSnapshot()
    {
        await using var rig = new ClientNetRig();
        var s5 = ClientNetRig.Snap(5, 1);
        var s7 = ClientNetRig.Snap(7, 1, 2);
        var s8 = ClientNetRig.Snap(8, 1, 2, 3);
        var host = await rig.StartInGameAsync(s5);
        await host.SendAsync(ClientNetRig.Delta(ClientNetRig.Snap(6, 1), s7)); // seq 6 never arrived
        Assert.Equal("sequence-gap", (await rig.NextAsync<Desynchronized>()).Reason);
        await host.ReceiveAsync<ResyncRequest>();
        Assert.Equal(5, rig.Session.Game.Seq);

        await host.SendAsync(ClientNetRig.Delta(s7, s8)); // arrives while the snapshot is in flight: dropped
        await host.SendAsync(ClientNetRig.SnapshotMessage(s8));
        var replaced = await rig.NextAsync<SnapshotReplaced>();
        Assert.Equal(8, replaced.Snapshot.Seq);
        Assert.Equal(8, rig.Session.Game.Seq);
        Assert.DoesNotContain(Drain(rig), it => it is DeltaApplied);
    }

    [Fact]
    public async Task Delta_ViewHashMismatch_DiscardsDeltaAndResyncs()
    {
        await using var rig = new ClientNetRig();
        var s5 = ClientNetRig.Snap(5, 1);
        var s6 = ClientNetRig.Snap(6, 1, 2);
        var host = await rig.StartInGameAsync(s5);
        await host.SendAsync(ClientNetRig.Delta(s5, s6) with { ViewHash = "0000000000000000" });
        Assert.Equal("view-hash-mismatch", (await rig.NextAsync<Desynchronized>()).Reason);
        await host.ReceiveAsync<ResyncRequest>();
        Assert.Equal(5, rig.Session.Game.Seq);
        Assert.Single(rig.Session.Game.Snapshot!.Units);
    }

    [Fact]
    public async Task Delta_MalformedJsonDiff_DiscardsDeltaAndResyncs()
    {
        await using var rig = new ClientNetRig();
        var s5 = ClientNetRig.Snap(5, 1);
        var host = await rig.StartInGameAsync(s5);
        var bad = JsonDocument.Parse("{\"seq\":\"not a number\"}").RootElement.Clone();
        await host.SendAsync(new ViewDelta { Seq = 6, Events = [], Diff = bad, ViewHash = "x" });
        Assert.Equal("malformed-diff", (await rig.NextAsync<Desynchronized>()).Reason);
        await host.ReceiveAsync<ResyncRequest>();
        Assert.Equal(5, rig.Session.Game.Seq);
    }

    [Fact]
    public async Task Delta_HostileDiffThatApplyRejects_DiscardsDeltaAndResyncs()
    {
        await using var rig = new ClientNetRig();
        var s5 = ClientNetRig.Snap(5, 1);
        var host = await rig.StartInGameAsync(s5);
        // Added.Count != AddedAt.Count: SnapshotDiff.Apply throws InvalidDataException.
        var diff = new SnapshotDiff
        {
            Seq = 6,
            Units = new KeyedDiff<UnitDto>
            {
                Changed = [],
                Added = [s5.Units[0] with { Id = 9 }],
                AddedAt = [],
                Removed = []
            }
        };
        await host.SendAsync(new ViewDelta { Seq = 6, Events = [], Diff = SnapshotBuilder.ToElement(diff), ViewHash = "x" });
        Assert.Equal("malformed-diff", (await rig.NextAsync<Desynchronized>()).Reason);
        await host.ReceiveAsync<ResyncRequest>();
        Assert.Equal([1], rig.Session.Game.Snapshot!.Units.Select(it => it.Id));
    }

    [Fact]
    public async Task Delta_DiffSeqDifferentFromDeltaSeq_IsMalformed()
    {
        await using var rig = new ClientNetRig();
        var s5 = ClientNetRig.Snap(5, 1);
        var host = await rig.StartInGameAsync(s5);
        var delta = ClientNetRig.Delta(s5, ClientNetRig.Snap(6, 1, 2)) with { Seq = 6 };
        var tampered = SnapshotBuilder.FromElement<SnapshotDiff>(delta.Diff!.Value) with { Seq = 99 };
        await host.SendAsync(delta with { Diff = SnapshotBuilder.ToElement(tampered) });
        Assert.Equal("malformed-diff", (await rig.NextAsync<Desynchronized>()).Reason);
    }

    [Fact]
    public async Task Delta_WithoutDiff_Resyncs()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        await host.SendAsync(new ViewDelta { Seq = 6, Events = [] });
        Assert.Equal("delta-without-diff", (await rig.NextAsync<Desynchronized>()).Reason);
        await host.ReceiveAsync<ResyncRequest>();
    }

    [Fact]
    public async Task Delta_BeforeAnySnapshot_Resyncs()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        await join;
        await host.SendAsync(new GameStarting());
        await rig.NextAsync<GameStarted>();
        // GameStarting makes the session wait for the snapshot, so an early delta is simply dropped.
        await host.SendAsync(new ViewDelta { Seq = 6, Events = [] });
        await host.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(6, 1)));
        await rig.NextAsync<SnapshotReplaced>();
        Assert.DoesNotContain(Drain(rig), it => it is Desynchronized);
    }

    [Fact]
    public async Task Snapshot_WithWrongViewHash_IsRejected()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        await join;
        await host.SendAsync(new GameStarting());
        await host.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(5, 1)) with { ViewHash = "bad" });
        Assert.Equal("snapshot-hash-mismatch", (await rig.NextAsync<Desynchronized>()).Reason);
        Assert.Null(rig.Session.Game.Snapshot);
    }

    [Fact]
    public async Task Snapshot_WithMalformedDto_IsRejected()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        await join;
        var message = ClientNetRig.SnapshotMessage(ClientNetRig.Snap(5, 1)) with { Units = [JsonDocument.Parse("[1]").RootElement.Clone()] };
        await host.SendAsync(message);
        Assert.Equal("malformed-snapshot", (await rig.NextAsync<Desynchronized>()).Reason);
    }

    [Fact]
    public async Task ResyncAsync_CompletesWhenSnapshotReplacesMirror()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var resync = rig.Session.ResyncAsync();
        await host.ReceiveAsync<ResyncRequest>();
        Assert.False(resync.IsCompleted);
        await host.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(9, 4)));
        await resync;
        Assert.Equal(9, rig.Session.Game.Seq);
    }

    // ---------------------------------------------------------------- T180 review fixes: no hangs, no faulted loop

    private static ClientSessionOptions Plain(ClientIdentity id) => new()
    {
        Identity = id,
        DisplayName = "A",
        RulesetHash = "h",
        ExpectedFingerprint = ClientNetRig.Fingerprint,
        ReconnectDelay = (_, _) => Task.CompletedTask
    };

    [Fact]
    public async Task Join_ConnectFactoryThrowsUnexpectedException_CompletesJoinAndDisposesCleanly()
    {
        using var identity = ClientIdentity.Generate();
        var session = new ClientSession(_ => throw new ArgumentException("factory bug"), Plain(identity));
        Assert.Equal(new JoinResult(false, ClientCodes.ConnectFailed), await session.JoinAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        await session.DisposeAsync(); // must not rethrow
    }

    [Fact]
    public async Task Join_TrustPromptThrows_IsARefusalNotACrash()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            HostAddress = "h:1",
            KnownHosts = new KnownHosts(),
            TrustPrompt = (_, _) => throw new NotSupportedException("ui gone")
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false });
        Assert.Equal(ClientCodes.HostUntrusted, (await join.WaitAsync(TimeSpan.FromSeconds(10))).Code);
    }

    [Fact]
    public async Task Join_UndecodableFrameFromHost_IsReportedAndTheHandshakeStillCompletes()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.Transport.SendAsync("this is not json");
        Assert.Equal(ErrorCodes.Malformed, (await rig.NextAsync<ProtocolError>()).Code);
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s1", Challenge = "c1", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false });
        await host.ReceiveAsync<Join>();
        await host.ReceiveAsync<Auth>();
        await host.SendAsync(new JoinOk());
        Assert.True((await join.WaitAsync(TimeSpan.FromSeconds(10))).Joined);
    }

    [Fact]
    public async Task Join_HostNeverAnswers_TimesOutWithHandshakeTimeout()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            HandshakeTimeout = TimeSpan.FromMilliseconds(200)
        });
        _ = rig.NewConnection(); // connects, then stays silent
        var result = await rig.Session.JoinAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new JoinResult(false, ClientCodes.HandshakeTimeout), result);
    }

    [Fact]
    public async Task Join_SlowTrustPrompt_DoesNotCountAgainstTheHandshakeTimeout()
    {
        var known = new KnownHosts();
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            HostAddress = "h:1",
            KnownHosts = known,
            HandshakeTimeout = TimeSpan.FromMilliseconds(150),
            TrustPrompt = async (_, _) =>
            {
                await Task.Delay(500);
                return true;
            }
        });
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        Assert.True((await join.WaitAsync(TimeSpan.FromSeconds(10))).Joined);
    }

    [Fact]
    public async Task Snapshot_RejectedOnFirstSync_EndsSessionInsteadOfHanging()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        await join;
        await host.SendAsync(new GameStarting());
        await host.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(5, 1)) with { ViewHash = "bad" });
        Assert.Equal(ClientCodes.SnapshotRejected, (await rig.NextAsync<SessionEnded>()).Reason);
        Assert.Equal(ClientSessionState.Disconnected, rig.Session.State);
    }

    [Fact]
    public async Task ResyncAsync_AnswerIsARejectedSnapshot_FaultsKeepsOldMirrorAndStaysInGame()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var resync = rig.Session.ResyncAsync();
        await host.ReceiveAsync<ResyncRequest>();
        await host.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(9, 4)) with { ViewHash = "bad" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => resync.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(5, rig.Session.Game.Seq);
        Assert.Equal(ClientSessionState.InGame, rig.Session.State);

        var again = rig.Session.ResyncAsync(); // the caller may retry
        await host.ReceiveAsync<ResyncRequest>();
        await host.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(9, 4)));
        await again.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(9, rig.Session.Game.Seq);
    }

    [Fact]
    public async Task ResyncAsync_AfterSessionEnded_FaultsInsteadOfHanging()
    {
        await using var rig = new ClientNetRig();
        _ = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        await rig.Session.LeaveAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Session.ResyncAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(ClientCodes.SessionClosed, ex.Message);
    }

    [Fact]
    public async Task Submit_InLobby_IsRefusedInsteadOfWaitingForever()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        await join;
        Assert.Equal(ClientSessionState.Lobby, rig.Session.State);
        var outcome = await rig.Session.SubmitAsync("a", EmptyObject).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ClientCodes.NotInGame, outcome.Code);
        Assert.Equal(0, rig.Session.PendingCommandCount);
    }

    [Fact]
    public async Task Reconnect_HostThatAcceptsThenDropsImmediately_DoesNotResetTheAttemptCounter()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            MaxReconnectAttempts = 2,
            ReconnectDelay = (_, _) => Task.CompletedTask
        });
        var first = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var flaky = new[] { rig.NewConnection(), rig.NewConnection(), rig.NewConnection(), rig.NewConnection() };
        await first.CloseAsync();
        foreach (var connection in flaky)
        {
            if (rig.Session.State == ClientSessionState.Disconnected) break;
            try
            {
                await rig.HandshakeAsync(connection);
                await connection.CloseAsync();
            }
            catch (Exception) { break; } // the session gave up and closed this connection's client end
        }
        Assert.Equal("reconnect-failed", (await rig.NextAsync<SessionEnded>()).Reason);
        Assert.True(rig.ConnectAttempts <= 4, $"attempts {rig.ConnectAttempts}"); // first + 2 retries + at most the one that exceeded the limit
    }

    [Fact]
    public async Task Join_ConnectFactoryHangs_TimesOutWithHandshakeTimeout()
    {
        using var identity = ClientIdentity.Generate();
        var options = new ClientSessionOptions
        {
            Identity = identity,
            DisplayName = "A",
            RulesetHash = "h",
            AllowUnpinnedHost = true,
            HandshakeTimeout = TimeSpan.FromMilliseconds(150)
        };
        var session = new ClientSession(async token => { await Task.Delay(Timeout.Infinite, token); return null!; }, options);
        Assert.Equal(new JoinResult(false, ClientCodes.HandshakeTimeout), await session.JoinAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Reconnect_DelayFunctionThrows_EndsWithInternalErrorInsteadOfFaultingTheLoop()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            ReconnectDelay = (_, _) => throw new InvalidOperationException("delay bug")
        });
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        await host.CloseAsync();
        Assert.Equal(ClientCodes.InternalError, (await rig.NextAsync<SessionEnded>()).Reason);
        Assert.Equal(ClientSessionState.Disconnected, rig.Session.State);
    }

    [Fact]
    public async Task Leave_WhileReconnecting_EndsTheSessionAndCancelsTheBackoff()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            ReconnectDelay = (_, token) => Task.Delay(Timeout.Infinite, token)
        });
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        await host.CloseAsync();
        await rig.NextAsync(it => it is StateChanged { State: ClientSessionState.Reconnecting });
        await rig.Session.LeaveAsync().WaitAsync(TimeSpan.FromSeconds(10)); // the Leave message cannot be sent: not an error
        Assert.Equal("left", (await rig.NextAsync<SessionEnded>()).Reason);
    }

    [Fact]
    public async Task Reconnect_ResumedByDeltasMoreOftenThanTheAttemptLimit_StaysInGameAndAnnouncesEachOutage()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            GameId = "game-1",
            MaxReconnectAttempts = 2,
            ReconnectDelay = (_, _) => Task.CompletedTask
        });
        var current = ClientNetRig.Snap(5, 1);
        var host = await rig.StartInGameAsync(current);
        for (var cycle = 0; cycle < 4; cycle++)
        {
            var next = rig.NewConnection();
            await host.CloseAsync();
            Assert.Equal("reconnect", (await rig.NextAsync<Desynchronized>()).Reason);
            await rig.HandshakeAsync(next);
            await next.ReceiveAsync<Resume>();
            var after = ClientNetRig.Snap(current.Seq + 1, 1, cycle + 2);
            await next.SendAsync(ClientNetRig.Delta(current, after));
            await rig.NextAsync<DeltaApplied>();
            current = after;
            host = next;
            Assert.Equal(ClientSessionState.InGame, rig.Session.State);
        }
    }

    [Fact]
    public async Task Submit_AlreadyCancelledToken_DoesNotLeakAPendingCommand()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Session.SubmitAsync("a", EmptyObject, cts.Token));
        var next = rig.NewConnection();
        await host.CloseAsync();
        await rig.HandshakeAsync(next);
        await next.ReceiveAsync<ResyncRequest>();
        await next.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(5, 1)));
        await rig.NextAsync<SnapshotReplaced>();
        _ = rig.Session.SubmitAsync("b", EmptyObject);
        Assert.Equal("b", (await next.ReceiveAsync<Command>()).CommandType); // the cancelled "a" was not resent first
    }

    [Fact]
    public async Task Events_AreBoundedWhenNobodyReads_AndTheEndIsKept()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        await join;
        for (var i = 0; i < 5000; i++) await host.SendAsync(new LobbyChat { Sender = "x", Text = "m" + i });
        await rig.Session.LeaveAsync();
        var seen = 0;
        ClientEvent? last = null;
        while (rig.Session.Events.TryRead(out var next))
        {
            seen++;
            last = next;
        }
        Assert.True(seen <= 4096, $"read {seen}");
        Assert.IsType<SessionEnded>(last);
    }

    // T180 review finding: the resend order is submission order; ClientCmdId text order puts "10" before "2".
    [Fact]
    public async Task Submit_ResentCommandsKeepSubmissionOrderBeyondNine()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var next = rig.NewConnection();
        await host.CloseAsync();
        await rig.NextAsync<StateChanged>();
        var tasks = Enumerable.Range(0, 12).Select(i => rig.Session.SubmitAsync("t" + i, EmptyObject)).ToList();
        await rig.HandshakeAsync(next);
        await next.ReceiveAsync<ResyncRequest>();
        var sent = new List<string>();
        for (var i = 0; i < 12; i++) sent.Add((await next.ReceiveAsync<Command>()).CommandType);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => "t" + i), sent);
        Assert.All(tasks, it => Assert.False(it.IsCompleted));
    }

    // T180 review finding: a TCP broken pipe on the command send must not fault the caller while the command is pending and will be resent.
    [Fact]
    public async Task Submit_SendFailureWhileConnected_WaitsForTheAnswerInsteadOfFaulting()
    {
        using var identity = ClientIdentity.Generate();
        var (clientEnd, hostEnd) = InMemoryTransport.CreatePair();
        await using var session = new ClientSession(_ => Task.FromResult<ITransport>(new CommandSendFailingTransport(clientEnd)), Plain(identity));
        var host = new FakeHostConnection(hostEnd);
        var join = session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s1", Challenge = "c1", HostKeyFingerprint = ClientNetRig.Fingerprint, RequiresPassword = false });
        await host.ReceiveAsync<Join>();
        await host.ReceiveAsync<Auth>();
        await host.SendAsync(new JoinOk());
        await host.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(5, 1)));
        Assert.True((await join).Joined);
        while (session.Game.Snapshot == null) await Task.Delay(10);

        var task = session.SubmitAsync("a", EmptyObject);
        await Task.Delay(100); // the send has thrown IOException inside the session by now
        Assert.False(task.IsCompleted);
        Assert.Equal(1, session.PendingCommandCount);
        await session.DisposeAsync();
        Assert.Equal(ClientCodes.SessionClosed, (await task).Code);
    }

    // T180 note: Desynchronized "on a sequence gap / reconnect"; IGameSession.Desynchronized documents the same for T244.
    [Fact]
    public async Task Reconnect_RaisesDesynchronizedOncePerOutageBeforeResuming()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        await host.CloseAsync();
        var seen = await rig.CollectUntilAsync<StateChanged>();
        Assert.Single(seen, it => it is Desynchronized { Reason: "reconnect" });
        Assert.Equal(ClientSessionState.Reconnecting, ((StateChanged)seen[^1]).State);
    }

    // ---------------------------------------------------------------- commands

    [Fact]
    public async Task Submit_CompletesWithHostAnswer_AndIdsAreUnique()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var first = rig.Session.SubmitAsync("a", EmptyObject);
        var second = rig.Session.SubmitAsync("b", EmptyObject);
        var c1 = await host.ReceiveAsync<Command>();
        var c2 = await host.ReceiveAsync<Command>();
        Assert.NotEqual(c1.ClientCmdId, c2.ClientCmdId);
        Assert.Equal(2, rig.Session.PendingCommandCount);
        await host.SendAsync(new CommandResult { ClientCmdId = c2.ClientCmdId, Accepted = false, Code = "nope" });
        await host.SendAsync(new CommandResult { ClientCmdId = c1.ClientCmdId, Accepted = true, Seq = 6 });
        Assert.Equal(new CommandOutcome(c1.ClientCmdId, true, null, 6), await first);
        Assert.Equal(new CommandOutcome(c2.ClientCmdId, false, "nope", null), await second);
        Assert.Equal(0, rig.Session.PendingCommandCount);
    }

    [Fact]
    public async Task Submit_DuplicateResultForSameId_IsCompletedOnce()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var task = rig.Session.SubmitAsync("a", EmptyObject);
        var command = await host.ReceiveAsync<Command>();
        var result = new CommandResult { ClientCmdId = command.ClientCmdId, Accepted = true, Seq = 6 };
        await host.SendAsync(result);
        await host.SendAsync(result with { Accepted = false, Code = "later-contradiction" });
        await host.SendAsync(new TurnStatus { Turn = 1, Ended = [], Phase = "Playing" });
        var seen = await rig.CollectUntilAsync<TurnStatusChanged>();
        Assert.Single(seen.OfType<CommandCompleted>());
        Assert.True((await task).Accepted);
    }

    [Fact]
    public async Task Submit_UnknownResultId_IsIgnored()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        await host.SendAsync(new CommandResult { ClientCmdId = "ghost", Accepted = true, Seq = 6 });
        await host.SendAsync(new TurnStatus { Turn = 1, Ended = [], Phase = "Playing" });
        await rig.NextAsync<TurnStatusChanged>();
        Assert.Equal(0, rig.Session.PendingCommandCount);
    }

    [Fact]
    public async Task Submit_PendingCommandIsResentWithSameIdAfterReconnect()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var task = rig.Session.SubmitAsync("a", EmptyObject);
        var original = await host.ReceiveAsync<Command>();

        var next = rig.NewConnection();
        await host.CloseAsync(); // the answer is lost with the connection
        await rig.NextAsync<StateChanged>(); // Reconnecting
        await rig.HandshakeAsync(next);
        await next.ReceiveAsync<ResyncRequest>(); // no game id kept: a full snapshot, then the resend
        var resent = await next.ReceiveAsync<Command>();
        Assert.Equal(original.ClientCmdId, resent.ClientCmdId);
        await next.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(6, 1)));
        await next.SendAsync(new CommandResult { ClientCmdId = resent.ClientCmdId, Accepted = true, Seq = 6 });
        Assert.True((await task).Accepted);
    }

    [Fact]
    public async Task Submit_WhileDisconnected_IsSentAfterResume()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var next = rig.NewConnection(); // queued before the drop: the session retries at once
        await host.CloseAsync();
        await rig.NextAsync<StateChanged>();
        var task = rig.Session.SubmitAsync("late", EmptyObject);
        await rig.HandshakeAsync(next);
        await next.ReceiveAsync<ResyncRequest>();
        var command = await next.ReceiveAsync<Command>();
        Assert.Equal("late", command.CommandType);
        await next.SendAsync(new CommandResult { ClientCmdId = command.ClientCmdId, Accepted = true, Seq = 6 });
        Assert.True((await task).Accepted);
    }

    [Fact]
    public async Task Submit_TooManyPending_IsRefusedLocally()
    {
        await using var rig = new ClientNetRig();
        await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var tasks = Enumerable.Range(0, 64).Select(_ => rig.Session.SubmitAsync("a", EmptyObject)).ToList();
        var overflow = await rig.Session.SubmitAsync("a", EmptyObject);
        Assert.Equal(ClientCodes.TooManyPending, overflow.Code);
        Assert.All(tasks, it => Assert.False(it.IsCompleted));
    }

    [Fact]
    public async Task Submit_CancelledCommand_IsForgotten()
    {
        await using var rig = new ClientNetRig();
        await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        using var cts = new CancellationTokenSource();
        var task = rig.Session.SubmitAsync("a", EmptyObject, cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, rig.Session.PendingCommandCount);
    }

    [Fact]
    public async Task Submit_EngineCommand_UsesRegisteredType()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var task = rig.Session.SubmitAsync(new Unciv.Commands.Turn.EndTurn { Now = 5 });
        var command = await host.ReceiveAsync<Command>();
        Assert.Equal(new Unciv.Commands.Turn.EndTurn().Type, command.CommandType);
        await host.SendAsync(new CommandResult { ClientCmdId = command.ClientCmdId, Accepted = true, Seq = 6 });
        Assert.True((await task).Accepted);
    }

    // ---------------------------------------------------------------- reconnect, resume, host closing

    [Fact]
    public async Task Reconnect_WithGameId_SendsResumeWithLastSeqAndHash()
    {
        await using var withGame = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            GameId = "game-1",
            ReconnectDelay = (_, _) => Task.CompletedTask
        });
        var s5 = ClientNetRig.Snap(5, 1);
        var host = await withGame.StartInGameAsync(s5);
        await host.SendAsync(new HostClosing());
        await withGame.NextAsync<HostClosed>();
        var next = withGame.NewConnection();
        await host.CloseAsync();
        Assert.Equal(ClientSessionState.Reconnecting, (await withGame.NextAsync<StateChanged>()).State);

        await withGame.HandshakeAsync(next);
        var resume = await next.ReceiveAsync<Resume>();
        Assert.Equal(new Resume { GameId = "game-1", LastSeq = 5, LastViewHash = SnapshotBuilder.ComputeViewHash(s5) }, resume);

        // The host answers with the missed deltas instead of a snapshot; they apply in order.
        var s6 = ClientNetRig.Snap(6, 1, 2);
        await next.SendAsync(ClientNetRig.Delta(s5, s6));
        Assert.Equal(6, (await withGame.NextAsync<DeltaApplied>()).Seq);
    }

    [Fact]
    public async Task Reconnect_AfterResume_SnapshotReplacesMirror()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var next = rig.NewConnection();
        await host.CloseAsync();
        await rig.NextAsync<StateChanged>();
        await rig.HandshakeAsync(next);
        await next.ReceiveAsync<ResyncRequest>();
        await next.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(12, 7)));
        await rig.NextAsync<SnapshotReplaced>();
        Assert.Equal(ClientSessionState.InGame, rig.Session.State);
        Assert.Equal([7], rig.Session.Game.Snapshot!.Units.Select(it => it.Id));
    }

    [Fact]
    public async Task Reconnect_Refused_EndsSessionWithoutFurtherAttempts()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var next = rig.NewConnection();
        await host.CloseAsync();
        await next.ReceiveAsync<Hello>();
        await next.SendAsync(new JoinRefused { Code = JoinRefusedCodes.AuthFailed });
        Assert.Equal(JoinRefusedCodes.AuthFailed, (await rig.NextAsync<JoinRefusedEvent>()).Code);
        Assert.Equal(2, rig.ConnectAttempts);
    }

    [Fact]
    public async Task Reconnect_GivesUpAfterMaxAttempts_AndFailsPendingCommands()
    {
        await using var rig = new ClientNetRig(id => new ClientSessionOptions
        {
            Identity = id,
            DisplayName = "A",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            MaxReconnectAttempts = 2,
            ReconnectDelay = (_, _) => Task.CompletedTask
        });
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var task = rig.Session.SubmitAsync("a", EmptyObject);
        await host.ReceiveAsync<Command>();
        await host.CloseAsync();
        var ended = await rig.NextAsync<SessionEnded>();
        Assert.Equal("reconnect-failed", ended.Reason);
        Assert.Equal(ClientCodes.SessionClosed, (await task).Code);
        Assert.Equal(3, rig.ConnectAttempts); // the first connection plus two failed retries
        Assert.Equal(ClientSessionState.Disconnected, rig.Session.State);
    }

    [Fact]
    public async Task Leave_SendsLeaveAndEndsSession()
    {
        await using var rig = new ClientNetRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        await rig.Session.LeaveAsync();
        await host.ReceiveAsync<Leave>();
        Assert.Equal(ClientSessionState.Disconnected, rig.Session.State);
        Assert.Equal("left", (await rig.NextAsync<SessionEnded>()).Reason);
        Assert.Equal(ClientCodes.SessionClosed, (await rig.Session.SubmitAsync("a", EmptyObject)).Code);
    }

    // ---------------------------------------------------------------- lobby

    [Fact]
    public async Task Lobby_ChatSlotAndTurnStatusFlow()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        await join;
        await rig.Session.SetLobbySlotAsync(2, "Rome", "red", true);
        var set = await host.ReceiveAsync<LobbySet>();
        Assert.Equal((2, "Rome", true), (set.Slot, set.Nation, set.Ready));
        await rig.Session.SendChatAsync("hi");
        Assert.Equal("hi", (await host.ReceiveAsync<LobbyChat>()).Text);
        await rig.Session.PingAsync();
        await host.ReceiveAsync<Ping>();
        await host.SendAsync(new LobbyChat { Sender = "Bob", Text = "yo" });
        Assert.Equal("Bob", (await rig.NextAsync<LobbyChatReceived>()).Sender);
        await host.SendAsync(new Error { Code = "bad" });
        Assert.Equal("bad", (await rig.NextAsync<ProtocolError>()).Code);
        await host.SendAsync(new Join { PlayerKey = "x", DisplayName = "y" }); // a client-to-host message from the host is a protocol error
        Assert.Equal(ErrorCodes.InvalidMessage, (await rig.NextAsync<ProtocolError>()).Code);
        await host.SendAsync(new TurnStatus { Turn = 3, Ended = ["A"], Phase = "Playing", Deadline = 99 });
        Assert.Equal(99, (await rig.NextAsync<TurnStatusChanged>()).Status.Deadline);
        Assert.Equal(3, rig.Session.TurnStatus!.Turn);
    }

    [Fact]
    public async Task MapPing_IsSentWithTheTileAndKind_AndARelayedNoticeBecomesAnEvent()
    {
        await using var rig = new ClientNetRig();
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        await join;

        await rig.Session.SendMapPingAsync(4, 7, MapPingKinds.Help);
        var sent = await host.ReceiveAsync<MapPing>();
        Assert.Equal((4, 7, MapPingKinds.Help), (sent.X, sent.Y, sent.Kind));

        await host.SendAsync(new MapPingNotice { X = 4, Y = 7, Kind = MapPingKinds.Help, Sender = "Bob" });
        var got = (await rig.NextAsync<MapPingReceived>()).Ping;
        Assert.Equal(("Bob", 4, 7), (got.Sender, got.X, got.Y));
    }

    // ---------------------------------------------------------------- queries

    [Fact]
    public async Task Query_TimesOutWithCode()
    {
        await using var rig = QueryRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var result = await rig.Session.QueryAsync(new EchoQuery(1), TimeSpan.FromMilliseconds(50));
        await host.ReceiveAsync<Query>();
        Assert.False(result.Ok);
        Assert.Equal(ClientCodes.QueryTimeout, result.Code);
    }

    [Fact]
    public async Task Query_AnswerArrivingAfterResync_IsFailedNotApplied()
    {
        await using var rig = QueryRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var pending = rig.Session.QueryAsync(new EchoQuery(1));
        var query = await host.ReceiveAsync<Query>();
        await host.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(6, 1))); // replaces the mirror the query was asked against
        var result = await pending;
        Assert.Equal(QueryWireCodes.SessionClosed, result.Code);
        await host.SendAsync(new QueryResult { QueryId = query.QueryId, Ok = true, Payload = EmptyObject }); // late answer: nobody is waiting
    }

    [Fact]
    public async Task Query_FailsWhenConnectionDrops()
    {
        await using var rig = QueryRig();
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(5, 1));
        var pending = rig.Session.QueryAsync(new EchoQuery(1));
        await host.ReceiveAsync<Query>();
        await host.CloseAsync();
        Assert.Equal(QueryWireCodes.SessionClosed, (await pending).Code);
    }

    private static List<ClientEvent> Drain(ClientNetRig rig)
    {
        List<ClientEvent> all = [];
        while (rig.Session.Events.TryRead(out var next)) all.Add(next);
        return all;
    }
}
