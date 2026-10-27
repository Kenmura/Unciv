using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Unciv.Net;
using Unciv.Net.Discovery;
using Unciv.Net.Lobby;
using Unciv.Net.Turns;

namespace Unciv.Core.Tests.Net.Discovery;

public class NetDiscoveryTests
{
    [Fact]
    public void LanBeacon_Serialization_RoundTripsWithMagicHeader()
    {
        var beacon = new LanBeacon
        {
            GameName = "Civ Masters",
            ProtocolVersion = ProtocolVersion.Current,
            FreeSlots = 3,
            MaxSlots = 6,
            TcpPort = 47821,
            Ruleset = "Civ V - Gods & Kings",
            RulesetHash = "abc123hash",
            RequiresPassword = true,
            HostKeyFingerprint = "FINGERPRINT-TEST-123",
            HostName = "Alice"
        };

        var bytes = beacon.ToBytes();
        Assert.True(bytes.Length > LanBeacon.MagicHeader.Length);
        Assert.Equal((byte)'U', bytes[0]);
        Assert.Equal((byte)'N', bytes[1]);
        Assert.Equal((byte)'C', bytes[2]);
        Assert.Equal((byte)'L', bytes[3]);

        var parsed = LanBeacon.TryParse(bytes);
        Assert.NotNull(parsed);
        Assert.Equal(beacon.GameName, parsed.GameName);
        Assert.Equal(beacon.ProtocolVersion, parsed.ProtocolVersion);
        Assert.Equal(beacon.FreeSlots, parsed.FreeSlots);
        Assert.Equal(beacon.MaxSlots, parsed.MaxSlots);
        Assert.Equal(beacon.TcpPort, parsed.TcpPort);
        Assert.Equal(beacon.Ruleset, parsed.Ruleset);
        Assert.Equal(beacon.RulesetHash, parsed.RulesetHash);
        Assert.Equal(beacon.RequiresPassword, parsed.RequiresPassword);
        Assert.Equal(beacon.HostKeyFingerprint, parsed.HostKeyFingerprint);
        Assert.Equal(beacon.HostName, parsed.HostName);
    }

    [Fact]
    public void LanBeacon_TryParse_AcceptsRawJsonWithoutMagicHeader()
    {
        var beacon = new LanBeacon
        {
            GameName = "Raw JSON Lobby",
            FreeSlots = 2,
            MaxSlots = 4
        };

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(beacon);
        var parsed = LanBeacon.TryParse(jsonBytes);

        Assert.NotNull(parsed);
        Assert.Equal("Raw JSON Lobby", parsed.GameName);
        Assert.Equal(2, parsed.FreeSlots);
    }

    [Fact]
    public void LanBeacon_TryParse_RejectsMalformedOrOversizedData()
    {
        Assert.Null(LanBeacon.TryParse([]));

        var oversized = new byte[LanBeacon.MaxPacketBytes + 1];
        Assert.Null(LanBeacon.TryParse(oversized));

        var invalidMagic = Encoding.UTF8.GetBytes("XXXX{\"GameName\":\"Fail\"}");
        Assert.Null(LanBeacon.TryParse(invalidMagic));

        var brokenJson = Encoding.UTF8.GetBytes("UNCL{\"GameName\":");
        Assert.Null(LanBeacon.TryParse(brokenJson));

        var randomGarbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        Assert.Null(LanBeacon.TryParse(randomGarbage));
    }

    [Fact]
    public async Task LanBeacon_FromLobbyHost_ExtractsCorrectInformation()
    {
        var settings = new LobbySettings
        {
            GameName = "Host's Game",
            MaxMajorCivs = 4,
            Ruleset = "Civ V - Gods & Kings",
            Password = "secret"
        };

        await using var host = new LobbyHost(settings, hostFingerprint: "FP-XYZ", hostName: "Bob");

        var beacon = LanBeacon.FromLobbyHost(host, tcpPort: 50000);

        Assert.Equal("Host's Game", beacon.GameName);
        Assert.Equal(ProtocolVersion.Current, beacon.ProtocolVersion);
        Assert.Equal(4, beacon.FreeSlots);
        Assert.Equal(4, beacon.MaxSlots);
        Assert.Equal(50000, beacon.TcpPort);
        Assert.Equal("Civ V - Gods & Kings", beacon.Ruleset);
        Assert.True(beacon.RequiresPassword);
        Assert.Equal("FP-XYZ", beacon.HostKeyFingerprint);
        Assert.Equal("Bob", beacon.HostName);
    }

    [Fact]
    public void DiscoveredLobby_Properties_AndExpiry()
    {
        var beacon = new LanBeacon
        {
            GameName = "Test Game",
            TcpPort = 47821,
            HostKeyFingerprint = "FP-1"
        };
        var address = IPAddress.Parse("192.168.1.50");
        var lobby = new DiscoveredLobby(beacon, address, 1000);

        Assert.Equal(new IPEndPoint(address, 47821), lobby.EndPoint);
        Assert.Equal("FP-1", lobby.Key);
        Assert.False(lobby.IsExpired(nowMillis: 5000, timeoutMillis: 5000));
        Assert.True(lobby.IsExpired(nowMillis: 6000, timeoutMillis: 5000));

        // When HostKeyFingerprint is null, fallback to Address:TcpPort
        var noFingerprintBeacon = new LanBeacon { TcpPort = 47821 };
        var fallbackLobby = new DiscoveredLobby(noFingerprintBeacon, address, 1000);
        Assert.Equal("192.168.1.50:47821", fallbackLobby.Key);
    }

    [Fact]
    public void LanBeaconListener_ProcessPacket_RegistersAndDeduplicatesLobbies()
    {
        var clock = new ManualClock(1000);
        using var dummyUdp = new UdpClient(0);
        using var listener = new LanBeaconListener(port: 0, clock: clock, expiryTimeout: TimeSpan.FromSeconds(5), udpClient: dummyUdp);

        DiscoveredLobby? discoveredEvent = null;
        listener.LobbyDiscovered += lobby => discoveredEvent = lobby;

        var beacon1 = new LanBeacon { GameName = "Lobby 1", HostKeyFingerprint = "FP-1", FreeSlots = 4 };
        var ep1 = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 47822);

        listener.ProcessPacket(beacon1.ToBytes(), ep1);

        Assert.NotNull(discoveredEvent);
        Assert.Equal("Lobby 1", discoveredEvent.Beacon.GameName);
        var lobbies = listener.GetActiveLobbies();
        Assert.Single(lobbies);
        Assert.Equal(4, lobbies[0].Beacon.FreeSlots);

        // Update from same host with 3 free slots at a later time
        clock.AdvanceSeconds(2);
        var beacon1Updated = new LanBeacon { GameName = "Lobby 1", HostKeyFingerprint = "FP-1", FreeSlots = 3 };
        listener.ProcessPacket(beacon1Updated.ToBytes(), ep1);

        lobbies = listener.GetActiveLobbies();
        Assert.Single(lobbies); // Still single lobby
        Assert.Equal(3, lobbies[0].Beacon.FreeSlots);
        Assert.Equal(3000, lobbies[0].LastSeenMillis);

        // Another host arrives
        var beacon2 = new LanBeacon { GameName = "Lobby 2", HostKeyFingerprint = "FP-2", FreeSlots = 2 };
        var ep2 = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 47822);
        listener.ProcessPacket(beacon2.ToBytes(), ep2);

        lobbies = listener.GetActiveLobbies();
        Assert.Equal(2, lobbies.Count);
    }

    [Fact]
    public void LanBeaconListener_ExpiryPruning_RemovesOldLobbies()
    {
        var clock = new ManualClock(1000);
        using var dummyUdp = new UdpClient(0);
        using var listener = new LanBeaconListener(port: 0, clock: clock, expiryTimeout: TimeSpan.FromSeconds(5), udpClient: dummyUdp);

        DiscoveredLobby? expiredEvent = null;
        listener.LobbyExpired += lobby => expiredEvent = lobby;

        var beacon = new LanBeacon { GameName = "Transient Lobby", HostKeyFingerprint = "FP-T" };
        var ep = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 47822);
        listener.ProcessPacket(beacon.ToBytes(), ep);

        Assert.Single(listener.GetActiveLobbies());

        // Advance 4 seconds (within 5s timeout)
        clock.AdvanceSeconds(4);
        Assert.Single(listener.GetActiveLobbies());
        Assert.Null(expiredEvent);

        // Advance 2 more seconds (6s total, past timeout)
        clock.AdvanceSeconds(2);
        var active = listener.GetActiveLobbies();
        Assert.Empty(active);
        Assert.NotNull(expiredEvent);
        Assert.Equal("Transient Lobby", expiredEvent.Beacon.GameName);
    }

    [Fact]
    public async Task LanBeacon_LoopbackUdp_BroadcasterAndListener()
    {
        // Bind listener to loopback on ephemeral port
        using var listenerUdp = new UdpClient();
        listenerUdp.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = Assert.IsType<IPEndPoint>(listenerUdp.Client.LocalEndPoint);
        var listenerPort = listenerEndPoint.Port;

        using var listener = new LanBeaconListener(port: listenerPort, udpClient: listenerUdp);
        listener.Start();

        var tcs = new TaskCompletionSource<DiscoveredLobby>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.LobbyDiscovered += lobby => tcs.TrySetResult(lobby);

        var beacon = new LanBeacon
        {
            GameName = "Loopback Test Game",
            TcpPort = 47821,
            FreeSlots = 5,
            MaxSlots = 8,
            HostKeyFingerprint = "FP-LOOPBACK",
            HostName = "TestHost"
        };

        // Broadcast to loopback targeting the listener's ephemeral port
        using var broadcasterUdp = new UdpClient();
        using var broadcaster = new LanBeaconBroadcaster(
            beacon,
            port: listenerPort,
            broadcastAddress: IPAddress.Loopback,
            udpClient: broadcasterUdp);

        await broadcaster.BroadcastOnceAsync();

        var discovered = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(discovered);
        Assert.Equal("Loopback Test Game", discovered.Beacon.GameName);
        Assert.Equal(5, discovered.Beacon.FreeSlots);
        Assert.Equal(IPAddress.Loopback, discovered.Address);

        var activeLobbies = listener.GetActiveLobbies();
        Assert.Single(activeLobbies);
        Assert.Equal("Loopback Test Game", activeLobbies[0].Beacon.GameName);

        await broadcaster.StopAsync();
        await listener.StopAsync();
    }

    [Fact]
    public async Task LanBeacon_Broadcaster_PeriodicLoop_SendsMultipleBeacons()
    {
        using var listenerUdp = new UdpClient();
        listenerUdp.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = Assert.IsType<IPEndPoint>(listenerUdp.Client.LocalEndPoint);
        var listenerPort = listenerEndPoint.Port;

        using var listener = new LanBeaconListener(port: listenerPort, udpClient: listenerUdp);
        listener.Start();

        var beaconCount = 0;
        listener.LobbyDiscovered += _ => Interlocked.Increment(ref beaconCount);

        var beacon = new LanBeacon
        {
            GameName = "Periodic Lobby",
            HostKeyFingerprint = "FP-PERIODIC"
        };

        using var broadcasterUdp = new UdpClient();
        using var broadcaster = new LanBeaconBroadcaster(
            beacon,
            port: listenerPort,
            broadcastAddress: IPAddress.Loopback,
            interval: TimeSpan.FromMilliseconds(40),
            udpClient: broadcasterUdp);

        Assert.False(broadcaster.IsRunning);
        broadcaster.Start();
        Assert.True(broadcaster.IsRunning);

        // Wait for multiple broadcasts to arrive
        await Task.Delay(160);

        await broadcaster.StopAsync();
        Assert.False(broadcaster.IsRunning);

        Assert.True(beaconCount >= 2, $"Expected at least 2 beacons, received {beaconCount}");

        await listener.StopAsync();
    }

    [Fact]
    public void LanBeacon_DisposalAndLifecycle_IsIdempotent()
    {
        using var dummyUdp = new UdpClient(0);
        var broadcaster = new LanBeaconBroadcaster(new LanBeacon(), port: 0, udpClient: dummyUdp);

        // Double start is idempotent
        broadcaster.Start();
        broadcaster.Start();
        Assert.True(broadcaster.IsRunning);

        broadcaster.Stop();
        broadcaster.Stop();
        Assert.False(broadcaster.IsRunning);

        broadcaster.Dispose();
        broadcaster.Dispose();

        var listener = new LanBeaconListener(port: 0, udpClient: dummyUdp);
        listener.Start();
        listener.Start();
        Assert.True(listener.IsRunning);

        listener.Stop();
        listener.Stop();
        Assert.False(listener.IsRunning);

        listener.Dispose();
        listener.Dispose();
    }
}
