using System.Globalization;
using System.Net;
using Unciv.Net.Identity;

namespace Unciv.Net.Relay;

/// <summary>
/// T338: the relay a player or a dedicated server points at: <c>unciv-relay://host[:port][?fp=&lt;relay fingerprint&gt;]</c> (design/RELAY.md section 8). The software ships with no default one (R13).
/// <see cref="Fingerprint"/> pins the relay's own TLS certificate (a self-signed standalone relay); without it the certificate must be valid for <see cref="Host"/>, or the player must have
/// accepted the relay in a prompt (the inner TLS, not the relay certificate, authenticates the host).
/// </summary>
public sealed record RelayEndpoint(string Host, int Port, string? Fingerprint = null)
{
    public const string Scheme = "unciv-relay://";
    public const int DefaultPort = 47830;

    /// <summary>The name a host signs into its login: lower-cased, an IP literal in canonical form, no port, no brackets.</summary>
    public string SignedHost => Canonical(Host);

    /// <summary>The key a remembered answer ("connect through this relay") is stored under: <c>host:port</c>.</summary>
    public string Key => (SignedHost.Contains(':', StringComparison.Ordinal) ? $"[{SignedHost}]" : SignedHost) + ":" + Port.ToString(CultureInfo.InvariantCulture);

    public static string Canonical(string host) => IPAddress.TryParse(host, out var ip) ? ip.ToString() : host.ToLowerInvariant();

    public override string ToString()
    {
        var host = Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]" : Host;
        return Scheme + host + (Port == DefaultPort ? "" : ":" + Port.ToString(CultureInfo.InvariantCulture)) + (Fingerprint == null ? "" : "?fp=" + Fingerprint);
    }

    /// <summary>Parses the setting text; false for anything that is not a relay URL (no scheme, empty host, bad port, a fingerprint that is not one). A bare <c>host[:port]</c> is not accepted: the scheme keeps a relay from being mistaken for a game host.</summary>
    public static bool TryParse(string? text, out RelayEndpoint? endpoint)
    {
        endpoint = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !trimmed.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = trimmed[Scheme.Length..];
        string? fingerprint = null;
        var query = rest.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            var parameters = rest[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries);
            rest = rest[..query];
            foreach (var parameter in parameters)
            {
                if (!parameter.StartsWith("fp=", StringComparison.Ordinal)) return false;
                fingerprint = HostKey.Normalize(parameter[3..]);
                if (fingerprint == null) return false;
            }
        }
        rest = rest.TrimEnd('/');
        if (rest.Length == 0 || rest.Contains('/', StringComparison.Ordinal) || rest.Any(char.IsWhiteSpace)) return false;

        string host;
        int port = DefaultPort;
        string? portText = null;
        if (rest.StartsWith('['))
        {
            var close = rest.IndexOf(']', StringComparison.Ordinal);
            if (close < 0) return false;
            host = rest[1..close];
            var tail = rest[(close + 1)..];
            if (tail.Length > 0)
            {
                if (!tail.StartsWith(':')) return false;
                portText = tail[1..];
            }
        }
        else
        {
            var colon = rest.LastIndexOf(':');
            if (colon >= 0)
            {
                host = rest[..colon];
                portText = rest[(colon + 1)..];
            }
            else
            {
                host = rest;
            }
        }
        if (host.Length == 0) return false;
        if (portText != null && (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)) return false;
        endpoint = new RelayEndpoint(host, port, fingerprint);
        return true;
    }
}
