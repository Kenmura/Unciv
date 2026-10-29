using System.Text.Json;
using Unciv.Client.Net.Session;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Net.ClientNet;

/// <summary>The scripted host side of one connection.</summary>
internal sealed class FakeHostConnection(InMemoryTransport transport)
{
    public InMemoryTransport Transport { get; } = transport;

    public async Task<T> ReceiveAsync<T>() where T : Message
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var frame = await Transport.ReceiveAsync(cts.Token);
        Assert.NotNull(frame);
        return Assert.IsType<T>(ProtocolCodec.Decode(frame));
    }

    public async Task SendAsync(Message message) => await Transport.SendAsync(ProtocolCodec.Encode(message));

    public ValueTask CloseAsync() => Transport.CloseAsync();
}

/// <summary>A client session wired to scripted hosts: every connect attempt takes the next prepared in-memory pair.</summary>
internal sealed class ClientNetRig : IAsyncDisposable
{
    public static readonly string Fingerprint = HostKey.Fingerprint(new byte[] { 1, 2, 3, 4 });

    private readonly Queue<InMemoryTransport> clientEnds = new();
    private readonly ClientIdentity identity = ClientIdentity.Generate();
    public ClientSession Session { get; }
    public int ConnectAttempts { get; private set; }

    public ClientNetRig(Func<ClientIdentity, ClientSessionOptions>? configure = null)
    {
        var options = configure?.Invoke(identity) ?? Default(identity);
        Session = new ClientSession(_ =>
        {
            ConnectAttempts++;
            if (clientEnds.Count == 0) throw new IOException("no host");
            return Task.FromResult<ITransport>(clientEnds.Dequeue());
        }, options);
    }

    public static ClientSessionOptions Default(ClientIdentity identity) => new()
    {
        Identity = identity,
        DisplayName = "Alice",
        RulesetHash = "hash",
        ExpectedFingerprint = Fingerprint,
        ReconnectDelay = (_, _) => Task.CompletedTask
    };

    public string PlayerKey => identity.PlayerKey;

    public FakeHostConnection NewConnection()
    {
        var (client, host) = InMemoryTransport.CreatePair();
        clientEnds.Enqueue(client);
        return new FakeHostConnection(host);
    }

    /// <summary>Plays the host's half of Hello, Welcome, Join, Auth, JoinOk (verifying the key proof).</summary>
    public async Task HandshakeAsync(FakeHostConnection host, string? fingerprint = null)
    {
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s1", Challenge = "c1", HostKeyFingerprint = fingerprint ?? Fingerprint, RequiresPassword = false });
        var join = await host.ReceiveAsync<Join>();
        Assert.Equal(PlayerKey, join.PlayerKey);
        var auth = await host.ReceiveAsync<Auth>();
        Assert.True(PlayerKeys.VerifyProof(join.PlayerKey, fingerprint ?? Fingerprint, "s1", "c1", auth.Proof));
        await host.SendAsync(new JoinOk());
    }

    public async Task<ClientEvent> NextAsync(Func<ClientEvent, bool> match)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await Session.Events.WaitToReadAsync(cts.Token))
        {
            while (Session.Events.TryRead(out var next))
                if (match(next)) return next;
        }
        throw new InvalidOperationException("event stream ended");
    }

    /// <summary>Everything up to and including the next <typeparamref name="T"/>.</summary>
    public async Task<List<ClientEvent>> CollectUntilAsync<T>() where T : ClientEvent
    {
        List<ClientEvent> seen = [];
        await NextAsync(it => { seen.Add(it); return it is T; });
        return seen;
    }

    public async Task<T> NextAsync<T>() where T : ClientEvent => (T)await NextAsync(it => it is T);

    public async ValueTask DisposeAsync()
    {
        await Session.DisposeAsync();
        identity.Dispose();
    }

    // ---- snapshots ----

    public static ClientSnapshot Snap(long seq, params int[] unitIds) => new()
    {
        Seq = seq,
        Turn = 1,
        Phase = "Playing",
        TurnStatus = new TurnStatusDto { Ended = [], Phase = "Playing" },
        You = "A",
        Civs = [],
        Tiles = [],
        Cities = [],
        Units = [.. unitIds.Select(id => new UnitDto { Id = id, Name = "Warrior", Owner = "A", X = 0, Y = 0, HealthBucket = 4, IsEmbarked = false, IsFortified = false })],
        Diplomacy = [],
        PendingTrades = [],
        Notifications = []
    };

    public static Snapshot SnapshotMessage(ClientSnapshot snapshot) => SnapshotBuilder.ToMessage(snapshot);

    public static ViewDelta Delta(ClientSnapshot before, ClientSnapshot after, params JsonElement[] events) => new()
    {
        Seq = after.Seq,
        Events = [.. events],
        Diff = SnapshotBuilder.ToElement(SnapshotDiff.Compute(before, after)),
        ViewHash = SnapshotBuilder.ComputeViewHash(after)
    };

    /// <summary>Joins and receives the first snapshot; returns the host end of the connection.</summary>
    public async Task<FakeHostConnection> StartInGameAsync(ClientSnapshot first)
    {
        var host = NewConnection();
        var join = Session.JoinAsync();
        await HandshakeAsync(host);
        Assert.True((await join).Joined);
        await host.SendAsync(new GameStarting());
        await host.SendAsync(SnapshotMessage(first));
        await NextAsync<SnapshotReplaced>();
        return host;
    }
}
