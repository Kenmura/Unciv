namespace Unciv.Net;

/// <summary>Wire protocol version (design/SERVER.md 6.1). Peers must match exactly in v1; bump on any breaking schema change.</summary>
public static class ProtocolVersion
{
    /// <summary>T281: 2, because the backup-host messages (<c>StandbyOffer</c> and friends, <c>HostSuccessor</c>) are new types; a version-1 peer is refused as a protocol mismatch.</summary>
    public const int Current = 2;

    /// <summary>Largest single wire frame (one length-prefixed chunk) in bytes. A larger message is split into continuation chunks by <see cref="Transport.LengthPrefixedFraming"/>.</summary>
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    /// <summary>Largest logical message (UTF-8 bytes, after reassembly); larger messages are rejected as an <see cref="ErrorCodes.FrameTooLarge"/> error. Bounds what a peer can make us buffer.</summary>
    public const int MaxMessageBytes = 32 * 1024 * 1024;

    public static bool IsCompatible(int peerVersion) => peerVersion == Current;
}
