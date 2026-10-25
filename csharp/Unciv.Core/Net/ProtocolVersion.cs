namespace Unciv.Net;

/// <summary>Wire protocol version (design/SERVER.md 6.1). Peers must match exactly in v1; bump on any breaking schema change.</summary>
public static class ProtocolVersion
{
    public const int Current = 1;

    /// <summary>Largest accepted frame in UTF-8 bytes; larger frames are rejected as an <see cref="ErrorCodes.FrameTooLarge"/> error.</summary>
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    public static bool IsCompatible(int peerVersion) => peerVersion == Current;
}
