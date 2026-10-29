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
    public static KnownHosts LoadKnownHosts(string path)
    {
        var known = new KnownHosts();
        try
        {
            if (!File.Exists(path)) return known;
            var pins = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (pins != null)
                foreach (var (address, fingerprint) in pins.OrderBy(it => it.Key, StringComparer.Ordinal))
                    known.Pin(address, fingerprint);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new KnownHosts();
        }
        return known;
    }

    public static void SaveKnownHosts(string path, KnownHosts known)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var sorted = new SortedDictionary<string, string>(known.Pins.ToDictionary(it => it.Key, it => it.Value), StringComparer.Ordinal);
        File.WriteAllText(path, JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true }));
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
