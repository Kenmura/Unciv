using System.Text.Json;
using Unciv.Net.Lobby;

namespace Unciv.Net.Discovery;

/// <summary>
/// Payload broadcast over UDP on the local network to advertise joinable lobbies (SERVER.md 6.5).
/// Contains no secrets, passwords, or authentication keys.
/// </summary>
public sealed record LanBeacon
{
    public const int DefaultPort = 47822;
    public const int DefaultTcpPort = 47821;
    public const int MaxPacketBytes = 4096;

    /// <summary>Magic prefix ('UNCL') indicating an Unciv LAN discovery beacon packet.</summary>
    public static ReadOnlySpan<byte> MagicHeader => "UNCL"u8;

    public string GameName { get; init; } = "Multiplayer Game";
    public int ProtocolVersion { get; init; } = Unciv.Net.ProtocolVersion.Current;
    public int FreeSlots { get; init; }
    public int MaxSlots { get; init; }
    public int TcpPort { get; init; } = DefaultTcpPort;
    public string? Ruleset { get; init; }
    public string? RulesetHash { get; init; }
    public bool RequiresPassword { get; init; }
    public string? HostKeyFingerprint { get; init; }
    public string? HostName { get; init; }

    /// <summary>
    /// Serializes this beacon to a UDP datagram payload prefixed with the 4-byte magic header <see cref="MagicHeader"/>.
    /// </summary>
    public byte[] ToBytes()
    {
        var magic = MagicHeader;
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(this);
        var result = new byte[magic.Length + jsonBytes.Length];
        magic.CopyTo(result);
        jsonBytes.CopyTo(result, magic.Length);
        return result;
    }

    /// <summary>
    /// Attempts to parse a received UDP datagram into a <see cref="LanBeacon"/>.
    /// Accepts payloads prefixed with <see cref="MagicHeader"/> or raw JSON objects.
    /// Returns null if the payload is invalid, malformed, or exceeds <see cref="MaxPacketBytes"/>.
    /// </summary>
    public static LanBeacon? TryParse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxPacketBytes)
        {
            return null;
        }

        ReadOnlySpan<byte> payload;
        var magic = MagicHeader;
        if (bytes.Length >= magic.Length && bytes[..magic.Length].SequenceEqual(magic))
        {
            payload = bytes[magic.Length..];
        }
        else if (bytes[0] == (byte)'{')
        {
            payload = bytes;
        }
        else
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LanBeacon>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Constructs a <see cref="LanBeacon"/> from an active <see cref="LobbyHost"/>.
    /// </summary>
    public static LanBeacon FromLobbyHost(LobbyHost host, int tcpPort = DefaultTcpPort)
    {
        ArgumentNullException.ThrowIfNull(host);
        var openSlots = host.Slots.Count(s => s.Kind == SlotKind.Open);
        return new LanBeacon
        {
            GameName = host.Settings.GameName,
            ProtocolVersion = Unciv.Net.ProtocolVersion.Current,
            FreeSlots = openSlots,
            MaxSlots = host.Slots.Count,
            TcpPort = tcpPort,
            Ruleset = host.Settings.Ruleset,
            RequiresPassword = !string.IsNullOrEmpty(host.Settings.Password),
            HostKeyFingerprint = host.HostFingerprint,
            HostName = host.HostName,
        };
    }
}
