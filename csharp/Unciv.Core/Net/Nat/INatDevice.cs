using System.Net;

namespace Unciv.Net.Nat;

/// <summary>
/// Supported automatic NAT traversal protocols (SERVER.md 6.5).
/// </summary>
public enum NatProtocol
{
    Upnp,
    NatPmp
}

/// <summary>
/// Common interface for automatic port mapping devices (UPnP IGD or NAT-PMP).
/// </summary>
public interface INatDevice
{
    /// <summary>The protocol implemented by this device.</summary>
    NatProtocol Protocol { get; }

    /// <summary>Queries the gateway for the external/public IP address.</summary>
    Task<IPAddress?> GetExternalIpAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or updates an automatic port mapping.</summary>
    Task<bool> AddPortMappingAsync(
        int internalPort,
        int externalPort,
        IPAddress localAddress,
        string protocol = "TCP",
        string description = "Unciv",
        int leaseDurationSeconds = 3600,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes an existing port mapping.</summary>
    Task<bool> DeletePortMappingAsync(
        int externalPort,
        string protocol = "TCP",
        CancellationToken cancellationToken = default);
}
