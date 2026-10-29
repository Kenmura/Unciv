using System.Text.Json;
using Unciv.Net.Identity;

namespace Unciv.Client.Net.Hosting;

/// <summary>
/// C006: the join side's files under the user folder. <c>known-hosts.json</c> holds the pinned fingerprints (<see cref="KnownHosts"/>), <c>last-join.txt</c> the address typed last,
/// so a restart offers it again. The identity key is <see cref="HostFiles.LoadIdentity"/> (the same key serves as host and as friend). A missing or damaged file is "nothing yet", never a crash:
/// a damaged known-hosts file becomes an empty one, so every host is asked about again rather than trusted blindly.
/// </summary>
public static class JoinFiles
{
    private const string VouchesKey = "$vouches";

    public static KnownHosts LoadKnownHosts(string path)
    {
        var known = new KnownHosts();
        try
        {
            if (!File.Exists(path)) return known;
            var entries = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
            if (entries != null)
            {
                foreach (var (address, value) in entries.OrderBy(it => it.Key, StringComparer.Ordinal))
                    if (value.ValueKind == JsonValueKind.String && value.GetString() is { } fingerprint) known.Pin(address, fingerprint);
                // T281: who vouched for a pin (a backup host announced by the pinned host), kept beside the pins under a key no address can have.
                if (entries.TryGetValue(VouchesKey, out var vouches) && vouches.ValueKind == JsonValueKind.Object)
                    foreach (var vouch in vouches.EnumerateObject())
                        if (vouch.Value.Deserialize<Vouch>() is { } restored) known.RestoreVouch(vouch.Name, restored);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new KnownHosts();
        }
        return known;
    }

    public static void SaveKnownHosts(string path, KnownHosts known)
    {
        var sorted = new SortedDictionary<string, object>(known.Pins.ToDictionary(it => it.Key, it => (object)it.Value), StringComparer.Ordinal);
        if (known.Vouches.Count > 0) sorted[VouchesKey] = new SortedDictionary<string, Vouch>(known.Vouches.ToDictionary(it => it.Key, it => it.Value), StringComparer.Ordinal);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the pin stays in memory for this run; the next trust prompt asks again after a restart
        }
    }

    public static string LoadLastAddress(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>Remembers what the player typed. An invite is not kept (it may carry a one-time token); only its address is.</summary>
    public static void SaveLastAddress(string path, string address)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, address);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // remembering the address is a convenience only
        }
    }
}
