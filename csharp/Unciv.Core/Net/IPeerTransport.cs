namespace Unciv.Net;

/// <summary>
/// T338: a transport that knows who the other end is without an address. Through a relay every connection comes from the relay, so the relay hands the host an opaque, stable
/// per-joiner-network id (design/RELAY.md section 3, R9); the host keys its failed-password back-off on it instead of on a key the joiner can rotate.
/// </summary>
public interface IPeerTransport
{
    /// <summary>The relay's opaque id of the joiner's network, or null for a direct connection.</summary>
    string? PeerId { get; }
}
