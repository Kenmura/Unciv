using System.Globalization;
using Unciv.Net.Relay;

namespace Unciv.Net.Identity;

/// <summary>
/// Parsed invite code of the form <c>unciv://&lt;host&gt;:&lt;port&gt;/&lt;fingerprint&gt;[/&lt;token&gt;]</c> (SERVER.md 6.4).
/// Allows direct join with immediate host certificate verification without a TOFU window.
/// T338: a relay invite <c>unciv://&lt;relayhost&gt;[:&lt;port&gt;]/r/&lt;room&gt;/&lt;fingerprint&gt;[/&lt;token&gt;]</c> (design/RELAY.md section 4) names the relay in
/// <see cref="Host"/>/<see cref="Port"/> (default 47830) and the room in <see cref="Room"/>; it is just as fully authenticated.
/// </summary>
public sealed record InviteCode
{
    public const string Scheme = "unciv://";
    public const int DefaultPort = 47821;

    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Fingerprint { get; init; }
    public string? Token { get; init; }

    /// <summary>T338: the room (display form <c>K7QF-3MXP4A</c>) of a relay invite; null for a direct one. <see cref="Host"/> and <see cref="Port"/> are then the relay's.</summary>
    public string? Room { get; init; }

    public bool IsRelay => Room != null;

    /// <summary>T338: formats a relay invite for a relay address, a room code (any spelling <see cref="RelayProtocol.NormalizeRoom"/> reads) and a host key fingerprint.</summary>
    public static string FormatRelay(string relayHost, int relayPort, string room, string fingerprint, string? token = null)
    {
        var normalizedRoom = RelayProtocol.NormalizeRoom(room) ?? throw new ArgumentException("Invalid room code", nameof(room));
        var direct = Format(relayHost, relayPort, fingerprint, token);
        var slash = direct.IndexOf('/', Scheme.Length);
        return direct[..slash] + "/r/" + RelayProtocol.FormatRoom(normalizedRoom) + direct[slash..];
    }

    /// <summary>Formats an invite code string from host parameters.</summary>
    public static string Format(string host, int port, string fingerprint, string? token = null)
    {
        var normalizedFingerprint = HostKey.Normalize(fingerprint)
            ?? throw new ArgumentException("Invalid host key fingerprint", nameof(fingerprint));

        var formattedHost = host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
        var baseUri = $"{Scheme}{formattedHost}:{port.ToString(CultureInfo.InvariantCulture)}/{normalizedFingerprint}";
        return string.IsNullOrEmpty(token) ? baseUri : $"{baseUri}/{token}";
    }

    /// <summary>Parses an invite code string. Returns true if valid, false if malformed.</summary>
    public static bool TryParse(string? text, out InviteCode? inviteCode)
    {
        inviteCode = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        if (!trimmed.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return false;

        var rest = trimmed[Scheme.Length..];
        var slashIndex = rest.IndexOf('/');
        if (slashIndex <= 0) return false;

        var hostPortPart = rest[..slashIndex];
        var pathPart = rest[(slashIndex + 1)..];

        string host;
        int? port;

        if (hostPortPart.StartsWith('['))
        {
            var closingBracket = hostPortPart.IndexOf(']');
            if (closingBracket < 0) return false;
            host = hostPortPart[1..closingBracket];
            var colonIndex = hostPortPart.IndexOf(':', closingBracket);
            if (colonIndex < 0) port = null;
            else if (!TryPort(hostPortPart[(colonIndex + 1)..], out port)) return false;
        }
        else
        {
            var colonIndex = hostPortPart.LastIndexOf(':');
            if (colonIndex < 0)
            {
                host = hostPortPart;
                port = null;
            }
            else
            {
                host = hostPortPart[..colonIndex];
                if (!TryPort(hostPortPart[(colonIndex + 1)..], out port)) return false;
            }
        }

        if (string.IsNullOrWhiteSpace(host)) return false;

        var segments = pathPart.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? room = null;
        if (segments.Length > 0 && segments[0] == "r")
        {
            if (segments.Length is < 3 or > 4) return false;
            var canonical = RelayProtocol.NormalizeRoom(segments[1]);
            if (canonical == null) return false;
            room = RelayProtocol.FormatRoom(canonical);
            segments = segments[2..];
        }
        if (segments.Length is < 1 or > 2) return false;

        var normalizedFp = HostKey.Normalize(segments[0]);
        if (normalizedFp == null) return false;

        string? token = segments.Length == 2 ? segments[1] : null;

        inviteCode = new InviteCode
        {
            Host = host,
            Port = port ?? (room != null ? RelayEndpoint.DefaultPort : DefaultPort),
            Fingerprint = normalizedFp,
            Token = token,
            Room = room,
        };
        return true;
    }

    private static bool TryPort(string text, out int? port)
    {
        port = null;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 1 or > 65535) return false;
        port = value;
        return true;
    }

    public override string ToString() => Room != null ? FormatRelay(Host, Port, Room, Fingerprint, Token) : Format(Host, Port, Fingerprint, Token);
}
