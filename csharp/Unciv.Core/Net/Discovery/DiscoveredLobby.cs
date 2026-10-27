using System.Net;

namespace Unciv.Net.Discovery;

/// <summary>
/// A game lobby discovered on the local area network via UDP beacon (SERVER.md 6.5).
/// </summary>
public sealed record DiscoveredLobby(
    LanBeacon Beacon,
    IPAddress Address,
    long LastSeenMillis
)
{
    /// <summary>TCP endpoint to connect to for joining the lobby.</summary>
    public IPEndPoint EndPoint => new(Address, Beacon.TcpPort);

    /// <summary>Unique identity key for deduplication (by host fingerprint, or IP + port if fingerprint is absent).</summary>
    public string Key => Beacon.HostKeyFingerprint ?? $"{Address}:{Beacon.TcpPort}";

    /// <summary>Returns true if the beacon has not been seen within <paramref name="timeoutMillis"/>.</summary>
    public bool IsExpired(long nowMillis, long timeoutMillis = 5000) =>
        nowMillis - LastSeenMillis >= timeoutMillis;
}
