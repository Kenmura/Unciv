using System.Globalization;

namespace Unciv.Net.Identity;

/// <summary>
/// Parsed invite code of the form <c>unciv://&lt;host&gt;:&lt;port&gt;/&lt;fingerprint&gt;[/&lt;token&gt;]</c> (SERVER.md 6.4).
/// Allows direct join with immediate host certificate verification without a TOFU window.
/// </summary>
public sealed record InviteCode
{
    public const string Scheme = "unciv://";
    public const int DefaultPort = 47821;

    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Fingerprint { get; init; }
    public string? Token { get; init; }

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
        int port;

        if (hostPortPart.StartsWith('['))
        {
            var closingBracket = hostPortPart.IndexOf(']');
            if (closingBracket < 0) return false;
            host = hostPortPart[1..closingBracket];
            var colonIndex = hostPortPart.IndexOf(':', closingBracket);
            if (colonIndex < 0) port = DefaultPort;
            else if (!int.TryParse(hostPortPart[(colonIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port)) return false;
        }
        else
        {
            var colonIndex = hostPortPart.LastIndexOf(':');
            if (colonIndex < 0)
            {
                host = hostPortPart;
                port = DefaultPort;
            }
            else
            {
                host = hostPortPart[..colonIndex];
                if (!int.TryParse(hostPortPart[(colonIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port)) return false;
            }
        }

        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) return false;

        var segments = pathPart.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is < 1 or > 2) return false;

        var normalizedFp = HostKey.Normalize(segments[0]);
        if (normalizedFp == null) return false;

        string? token = segments.Length == 2 ? segments[1] : null;

        inviteCode = new InviteCode
        {
            Host = host,
            Port = port,
            Fingerprint = normalizedFp,
            Token = token
        };
        return true;
    }

    public override string ToString() => Format(Host, Port, Fingerprint, Token);
}
