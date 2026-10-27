using System.Net;
using System.Net.Sockets;
using Unciv.Net.Turns;

namespace Unciv.Net.Discovery;

/// <summary>
/// Listens for LAN discovery beacons broadcast on UDP port 47822 (SERVER.md 6.5).
/// Maintains a list of active lobbies discovered on the local network, pruning expired lobbies.
/// </summary>
public sealed class LanBeaconListener : IAsyncDisposable, IDisposable
{
    private readonly int port;
    private readonly IClock clock;
    private readonly TimeSpan expiryTimeout;
    private readonly UdpClient udpClient;
    private readonly bool ownsClient;

    private readonly Dictionary<string, DiscoveredLobby> lobbies = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private CancellationTokenSource? cts;
    private Task? loopTask;
    private bool disposed;

    /// <summary>
    /// Event raised when a new lobby is discovered or an existing lobby is updated.
    /// </summary>
    public event Action<DiscoveredLobby>? LobbyDiscovered;

    /// <summary>
    /// Event raised when a discovered lobby expires due to no beacons received within the expiry timeout.
    /// </summary>
    public event Action<DiscoveredLobby>? LobbyExpired;

    public LanBeaconListener(
        int port = LanBeacon.DefaultPort,
        IClock? clock = null,
        TimeSpan? expiryTimeout = null,
        UdpClient? udpClient = null)
    {
        this.port = port;
        this.clock = clock ?? SystemClock.Instance;
        this.expiryTimeout = expiryTimeout ?? TimeSpan.FromSeconds(5);

        if (udpClient != null)
        {
            this.udpClient = udpClient;
            ownsClient = false;
        }
        else
        {
            this.udpClient = new UdpClient();
            this.udpClient.ExclusiveAddressUse = false;
            this.udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            this.udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            ownsClient = true;
        }
    }

    public int Port => ((IPEndPoint?)udpClient.Client.LocalEndPoint)?.Port ?? port;

    public bool IsRunning
    {
        get
        {
            lock (gate)
            {
                return loopTask != null && !loopTask.IsCompleted;
            }
        }
    }

    /// <summary>
    /// Starts listening for UDP beacon packets. Idempotent if already running.
    /// </summary>
    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (loopTask != null && !loopTask.IsCompleted)
            {
                return;
            }

            cts = new CancellationTokenSource();
            loopTask = ReceiveLoopAsync(cts.Token);
        }
    }

    /// <summary>
    /// Processes a received datagram directly. Can be called directly in tests without sockets.
    /// </summary>
    public void ProcessPacket(ReadOnlySpan<byte> data, IPEndPoint remoteEndPoint)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);

        var beacon = LanBeacon.TryParse(data);
        if (beacon == null)
        {
            return;
        }

        var lobby = new DiscoveredLobby(beacon, remoteEndPoint.Address, clock.NowMillis);
        Action<DiscoveredLobby>? callback = null;

        lock (gate)
        {
            PruneExpiredInternal();
            lobbies[lobby.Key] = lobby;
            callback = LobbyDiscovered;
        }

        callback?.Invoke(lobby);
    }

    /// <summary>
    /// Returns the currently active (non-expired) discovered lobbies.
    /// Automatically prunes any lobbies whose last seen timestamp exceeds the expiry timeout.
    /// </summary>
    public IReadOnlyList<DiscoveredLobby> GetActiveLobbies()
    {
        lock (gate)
        {
            PruneExpiredInternal();
            return [.. lobbies.Values];
        }
    }

    private void PruneExpiredInternal()
    {
        var now = clock.NowMillis;
        var timeoutMillis = (long)expiryTimeout.TotalMilliseconds;
        List<DiscoveredLobby>? expired = null;

        foreach (var (_, lobby) in lobbies)
        {
            if (lobby.IsExpired(now, timeoutMillis))
            {
                expired ??= [];
                expired.Add(lobby);
            }
        }

        if (expired != null)
        {
            foreach (var lobby in expired)
            {
                lobbies.Remove(lobby.Key);
            }

            if (LobbyExpired != null)
            {
                foreach (var lobby in expired)
                {
                    LobbyExpired.Invoke(lobby);
                }
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                ProcessPacket(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                // Port closed or socket error; exit loop if cancellation requested
            }
        }
    }

    /// <summary>
    /// Stops the receive loop.
    /// </summary>
    public async Task StopAsync()
    {
        CancellationTokenSource? tokenSource;
        Task? task;

        lock (gate)
        {
            tokenSource = cts;
            task = loopTask;
            cts = null;
            loopTask = null;
        }

        if (tokenSource != null)
        {
            tokenSource.Cancel();
            tokenSource.Dispose();
        }

        if (task != null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public void Stop() => StopAsync().GetAwaiter().GetResult();

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }

        await StopAsync().ConfigureAwait(false);

        if (ownsClient)
        {
            udpClient.Dispose();
        }
    }
}
