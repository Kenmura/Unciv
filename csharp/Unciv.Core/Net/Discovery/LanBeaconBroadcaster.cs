using System.Net;
using System.Net.Sockets;

namespace Unciv.Net.Discovery;

/// <summary>
/// Periodically broadcasts UDP beacons on the local network advertising a hosted lobby (SERVER.md 6.5).
/// Off by default on dedicated servers.
/// </summary>
public sealed class LanBeaconBroadcaster : IAsyncDisposable, IDisposable
{
    private readonly Func<LanBeacon> beaconProvider;
    private readonly int port;
    private readonly IPAddress broadcastAddress;
    private readonly TimeSpan interval;
    private readonly UdpClient udpClient;
    private readonly bool ownsClient;

    private readonly Lock gate = new();
    private CancellationTokenSource? cts;
    private Task? loopTask;
    private bool disposed;

    public LanBeaconBroadcaster(
        Func<LanBeacon> beaconProvider,
        int port = LanBeacon.DefaultPort,
        IPAddress? broadcastAddress = null,
        TimeSpan? interval = null,
        UdpClient? udpClient = null)
    {
        ArgumentNullException.ThrowIfNull(beaconProvider);

        this.beaconProvider = beaconProvider;
        this.port = port;
        this.broadcastAddress = broadcastAddress ?? IPAddress.Broadcast;
        this.interval = interval ?? TimeSpan.FromSeconds(1);

        if (udpClient != null)
        {
            this.udpClient = udpClient;
            ownsClient = false;
        }
        else
        {
            this.udpClient = new UdpClient(AddressFamily.InterNetwork);
            this.udpClient.EnableBroadcast = true;
            ownsClient = true;
        }
    }

    public LanBeaconBroadcaster(
        LanBeacon staticBeacon,
        int port = LanBeacon.DefaultPort,
        IPAddress? broadcastAddress = null,
        TimeSpan? interval = null,
        UdpClient? udpClient = null)
        : this(() => staticBeacon, port, broadcastAddress, interval, udpClient)
    {
    }

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
    /// Starts the background periodic UDP broadcast loop. Idempotent if already running.
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
            loopTask = BroadcastLoopAsync(cts.Token);
        }
    }

    /// <summary>
    /// Broadcasts a single beacon packet asynchronously to the configured broadcast address and port.
    /// </summary>
    public async Task BroadcastOnceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var beacon = beaconProvider();
        var bytes = beacon.ToBytes();
        var endPoint = new IPEndPoint(broadcastAddress, port);
        await udpClient.SendAsync(bytes, endPoint, cancellationToken).ConfigureAwait(false);
    }

    private async Task BroadcastLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastOnceAsync(cancellationToken).ConfigureAwait(false);
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
                // Network down or transient socket error; wait for next cycle
            }

            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Stops the periodic broadcast loop.
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
