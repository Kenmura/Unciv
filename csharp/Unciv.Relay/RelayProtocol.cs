using System.Net;
using System.Security.Cryptography;
using System.Text;
using Unciv.Net.Identity;

namespace Unciv.Relay;

/// <summary>The constants, signed strings and codes of the relay tunnel protocol version 1 (design/RELAY.md section 3). Shared by the relay, the test stub and, later, the client (T338).</summary>
public static class RelayProtocol
{
    public const int Version = 1;
    public const string Alpn = "unciv-relay/1";
    public const int PreambleMaxBytes = 2048;
    public const int ControlLineMaxBytes = 4096;
    public const int GetReplyMaxBytes = 512 * 1024;
    public const int ListingBodyMaxBytes = 1024;
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>The string a host signs with its key to open the control channel of a room.</summary>
    public static string HostSignedText(string relayHost, string challenge, string gameTag) => $"unciv-relay-v1|host|{relayHost}|{challenge}|{gameTag}";

    /// <summary>The string a host signs to open the data channel for one announced joiner.</summary>
    public static string AcceptSignedText(string relayHost, string challenge, string room, string id) => $"unciv-relay-v1|accept|{relayHost}|{challenge}|{room}|{id}";

    /// <summary>The bytes a host signs for a listing; <paramref name="body"/> is the JSON string forwarded verbatim.</summary>
    public static string ListingSignedText(string body) => "unciv-listing-v1\n" + body;

    /// <summary>The first 16 bytes (base64) of SHA-256 over the game id, so the id itself never reaches the relay.</summary>
    public static string GameTag(string gameId) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("unciv-relay-game-v1|" + gameId))[..16]);

    /// <summary>The full SHA-256 of a SubjectPublicKeyInfo as 64 lower-case hex characters: the <c>fp</c> of replies and listings.</summary>
    public static string FingerprintHex(ReadOnlySpan<byte> subjectPublicKeyInfo) => Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo));

    /// <summary>50 bits of <paramref name="hash"/> as 10 Crockford base32 symbols.</summary>
    public static string RoomCodeFromHash(ReadOnlySpan<byte> hash)
    {
        ulong bits = 0;
        for (var i = 0; i < 7; i++) bits = (bits << 8) | hash[i];
        bits >>= 6; // 56 -> 50 bits
        var chars = new char[10];
        for (var i = 9; i >= 0; i--)
        {
            chars[i] = CrockfordAlphabet[(int)(bits & 31)];
            bits >>= 5;
        }
        return new string(chars);
    }

    /// <summary>The display form <c>K7QF-3MXP4A</c> of a canonical 10-symbol code.</summary>
    public static string FormatRoom(string canonical) => canonical[..4] + "-" + canonical[4..];

    /// <summary>Canonical (10 upper-case symbols, no dash) for a typed code: case-insensitive, dashes and spaces ignored, O read as 0 and I or L as 1; null when it is not a code.</summary>
    public static string? NormalizeRoom(string? text)
    {
        if (text == null || text.Length > 32) return null;
        var builder = new StringBuilder(10);
        foreach (var raw in text)
        {
            if (raw is '-' or ' ') continue;
            var c = char.ToUpperInvariant(raw);
            c = c switch { 'O' => '0', 'I' or 'L' => '1', _ => c };
            if (CrockfordAlphabet.IndexOf(c, StringComparison.Ordinal) < 0) return null;
            builder.Append(c);
        }
        return builder.Length == 10 ? builder.ToString() : null;
    }

    /// <summary>The source network the limits key on: the IPv4 address, or the /64 prefix of an IPv6 address (an IPv4-mapped one counts as IPv4).</summary>
    public static string NetworkKey(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return address.ToString();
        return Convert.ToHexStringLower(address.GetAddressBytes().AsSpan(0, 8)) + "::/64";
    }

    /// <summary>An opaque 16-hex peer id for a joiner network in one room; stable while the relay secret is.</summary>
    public static string PeerId(byte[] secret, string room, string network)
    {
        var roomKey = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(room));
        return Convert.ToHexStringLower(HMACSHA256.HashData(roomKey, Encoding.UTF8.GetBytes(network))[..8]);
    }

    /// <summary>The fingerprint spelling of an invite (26 base32 characters) for an SPKI, to compare with the optional <c>fp</c> of a join.</summary>
    public static string InviteFingerprint(ReadOnlySpan<byte> subjectPublicKeyInfo) => HostKey.Fingerprint(subjectPublicKeyInfo);
}
