using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Net.Relay;
using Unciv.Server.Transport;

namespace Unciv.Client.Net.Hosting;

/// <summary>What <see cref="RelaySettings.Choose"/> decided for a relay named by an invite.</summary>
/// <param name="Endpoint">The relay to dial (the configured one, with its certificate pin, when the invite names it).</param>
/// <param name="Dial">How to dial it.</param>
/// <param name="NeedsPrompt">The invite names a relay the player never agreed to: ask "Connect through relay X?" once, then <see cref="RelaySettings.Remember"/> the answer.</param>
public sealed record RelayChoice(RelayEndpoint Endpoint, RelayDialOptions Dial, bool NeedsPrompt);

/// <summary>
/// T338: the player's relay settings, <c>relay.json</c> in the user folder (design/RELAY.md sections 4, 5 and 8). There is NO default relay in the source (R13): a fresh install has none, and the
/// Host screen offers "Reach me through a relay" only once <see cref="Url"/> is set. An owner's own build may ship an uncommitted <c>relay-default.json</c> next to the client; it is read only when the
/// player has no <c>relay.json</c> yet, and the value it fills in is visible and editable in the settings like any other.
/// </summary>
public sealed class RelaySettings
{
    /// <summary><c>unciv-relay://host[:port][?fp=...]</c>, or null/empty for no relay.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Host through the relay as well as on the local port. Only meaningful once a relay is set (default on).</summary>
    [JsonPropertyName("reachMeThroughRelay")]
    public bool ReachMeThroughRelay { get; set; } = true;

    /// <summary>Relays (<c>host:port</c>) the player agreed to connect through when an invite named them. Never added to <see cref="Url"/> by itself.</summary>
    [JsonPropertyName("acceptedRelays")]
    public List<string> AcceptedRelays { get; set; } = [];

    /// <summary>The configured relay; null when none is set or the text is not a relay URL.</summary>
    [JsonIgnore]
    public RelayEndpoint? Endpoint => RelayEndpoint.TryParse(Url, out var endpoint) ? endpoint : null;

    /// <summary>True when hosting should register on the relay: one is set and the player did not turn it off.</summary>
    [JsonIgnore]
    public bool HostThroughRelay => ReachMeThroughRelay && Endpoint != null;

    /// <summary>Why <see cref="Url"/> was rejected, or null when it is empty or fine. For the settings field.</summary>
    public static string? Problem(string? url) =>
        string.IsNullOrWhiteSpace(url) || RelayEndpoint.TryParse(url, out _) ? null : "A relay address looks like unciv-relay://relay.example.org or unciv-relay://203.0.113.10:47830?fp=FINGERPRINT.";

    /// <summary>Reads <paramref name="path"/>; a missing or damaged file is "no relay" (a damaged one is never trusted into a default), then <paramref name="defaultPath"/> for an owner's own build.</summary>
    public static RelaySettings Load(string path, string? defaultPath = null)
    {
        foreach (var candidate in new[] { path, defaultPath })
        {
            if (candidate == null) continue;
            try
            {
                if (!File.Exists(candidate)) continue;
                return JsonSerializer.Deserialize<RelaySettings>(File.ReadAllText(candidate)) ?? new RelaySettings();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                if (candidate == path) return new RelaySettings();
            }
        }
        return new RelaySettings();
    }

    public void Save(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the setting stays for this run; the next start asks again
        }
    }

    /// <summary>Decides how to dial a relay an invite names: the configured one as configured; one the player agreed to before, with its certificate accepted (the inner TLS authenticates the host); else a prompt is due.</summary>
    public RelayChoice Choose(string relayHost, int relayPort)
    {
        var named = new RelayEndpoint(relayHost, relayPort);
        if (Endpoint is { } configured && configured.Key == named.Key) return new RelayChoice(configured, new RelayDialOptions(), false);
        var remembered = AcceptedRelays.Contains(named.Key, StringComparer.OrdinalIgnoreCase);
        return new RelayChoice(named, new RelayDialOptions { TrustAnyCertificate = true }, !remembered);
    }

    /// <summary>The player answered yes to "Connect through relay X?": not asked again for that relay host and port.</summary>
    public void Remember(RelayEndpoint relay)
    {
        if (!AcceptedRelays.Contains(relay.Key, StringComparer.OrdinalIgnoreCase)) AcceptedRelays.Add(relay.Key);
    }
}
