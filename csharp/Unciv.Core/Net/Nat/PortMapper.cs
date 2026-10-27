using System.Net;

namespace Unciv.Net.Nat;

/// <summary>
/// Lifecycle manager for an active NAT port mapping (SERVER.md 6.5).
/// Maps port upon <see cref="StartAsync"/> and automatically cleans up mapping on shutdown (<see cref="DisposeAsync"/>).
/// Failure is non-fatal.
/// </summary>
public sealed class PortMapper : IAsyncDisposable
{
    private readonly INatDevice device;
    private readonly int port;
    private readonly IPAddress localAddress;
    private readonly string protocol;
    private readonly string description;
    private readonly int leaseDurationSeconds;

    private bool isMapped;
    private IPAddress? externalIp;
    private bool disposed;

    public PortMapper(
        INatDevice device,
        int port = 47821,
        IPAddress? localAddress = null,
        string protocol = "TCP",
        string description = "Unciv",
        int leaseDurationSeconds = 7200)
    {
        this.device = device;
        this.port = port;
        this.localAddress = localAddress ?? IPAddress.Loopback;
        this.protocol = protocol;
        this.description = description;
        this.leaseDurationSeconds = leaseDurationSeconds;
    }

    public INatDevice Device => device;

    public int Port => port;

    public bool IsMapped => isMapped;

    public IPAddress? ExternalIp => externalIp;

    /// <summary>
    /// Attempts to add port mapping and query external IP.
    /// Returns true if port mapping was successfully established; false otherwise.
    /// Non-fatal on failure.
    /// </summary>
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (disposed) return false;

        try
        {
            externalIp = await device.GetExternalIpAsync(cancellationToken).ConfigureAwait(false);

            var ok = await device.AddPortMappingAsync(
                internalPort: port,
                externalPort: port,
                localAddress: localAddress,
                protocol: protocol,
                description: description,
                leaseDurationSeconds: leaseDurationSeconds,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            isMapped = ok;
            return ok;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            isMapped = false;
            return false;
        }
    }

    /// <summary>
    /// Removes port mapping on shutdown.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        if (isMapped)
        {
            try
            {
                await device.DeletePortMappingAsync(port, protocol).ConfigureAwait(false);
            }
            catch { }
            isMapped = false;
        }
    }
}
