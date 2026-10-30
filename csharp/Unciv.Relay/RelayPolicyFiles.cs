using System.Net;
using System.Text.RegularExpressions;
using Unciv.Net.Identity;

namespace Unciv.Relay;

/// <summary>The operator's files in the data dir (RELAY.md section 6, A8/A10): <c>bans.txt</c>, <c>allow-keys.txt</c>, <c>deny-names.txt</c>. Reloaded on SIGHUP and when a file changes.</summary>
public sealed class RelayPolicyFiles(string dataDir)
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);
    private Snapshot current = Snapshot.Empty;
    private string stamp = "";

    /// <summary>Lines that could not be read at the last reload (shown in the log, never fatal).</summary>
    public IReadOnlyList<string> Warnings => current.Warnings;

    /// <summary>Re-reads the files if any changed (or <paramref name="force"/>); returns whether the content was replaced.</summary>
    public bool Reload(bool force = false)
    {
        var now = StampOfFiles();
        if (!force && now == stamp) return false;
        stamp = now;
        var warnings = new List<string>();
        var nets = new List<IPNetwork>();
        var bannedKeys = new KeySet();
        var bannedNames = new List<Regex>();
        foreach (var line in Lines("bans.txt"))
        {
            if (line.StartsWith("key ", StringComparison.OrdinalIgnoreCase)) { if (!bannedKeys.Add(line[4..].Trim())) warnings.Add($"bans.txt: bad key '{line}'"); }
            else if (line.StartsWith("name ", StringComparison.OrdinalIgnoreCase)) AddRegex(bannedNames, line[5..].Trim(), "bans.txt", warnings);
            else if (TryParseNetwork(line, out var net)) nets.Add(net);
            else warnings.Add($"bans.txt: cannot read '{line}'");
        }
        var allowed = new KeySet();
        foreach (var line in Lines("allow-keys.txt")) if (!allowed.Add(line)) warnings.Add($"allow-keys.txt: bad key '{line}'");
        var denied = new List<Regex>();
        foreach (var line in Lines("deny-names.txt")) AddRegex(denied, line, "deny-names.txt", warnings);
        current = new Snapshot(nets, bannedKeys, bannedNames, allowed, denied, warnings);
        return true;
    }

    public bool IsAddressBanned(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return current.BannedNetworks.Any(n => n.Contains(address));
    }

    public bool IsKeyBanned(ReadOnlySpan<byte> spki) => current.BannedKeys.Contains(spki);

    public bool IsKeyAllowed(ReadOnlySpan<byte> spki) => current.AllowedKeys.Contains(spki);

    /// <summary>True when a ban pattern or a deny-names pattern matches the text.</summary>
    public bool IsNameDenied(string text) => Matches(current.DeniedNames, text) || Matches(current.BannedNames, text);

    private static bool Matches(List<Regex> patterns, string text)
    {
        foreach (var pattern in patterns)
        {
            try { if (pattern.IsMatch(text)) return true; }
            catch (RegexMatchTimeoutException) { return true; } // a pattern that cannot decide in time fails closed
        }
        return false;
    }

    private IEnumerable<string> Lines(string file)
    {
        var path = Path.Combine(dataDir, file);
        if (!File.Exists(path)) yield break;
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (IOException) { yield break; }
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length > 0 && line[0] != '#') yield return line;
        }
    }

    private string StampOfFiles()
    {
        var parts = new List<string>();
        foreach (var file in new[] { "bans.txt", "allow-keys.txt", "deny-names.txt" })
        {
            var info = new FileInfo(Path.Combine(dataDir, file));
            parts.Add(info.Exists ? info.LastWriteTimeUtc.Ticks + ":" + info.Length : "-");
        }
        return string.Join('|', parts);
    }

    private static void AddRegex(List<Regex> target, string pattern, string file, List<string> warnings)
    {
        try { target.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout)); }
        catch (ArgumentException) { warnings.Add($"{file}: bad pattern '{pattern}'"); }
    }

    private static bool TryParseNetwork(string text, out IPNetwork network)
    {
        if (text.Contains('/')) return IPNetwork.TryParse(text, out network);
        if (IPAddress.TryParse(text, out var address))
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            network = new IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
            return true;
        }
        network = default;
        return false;
    }

    /// <summary>Fingerprints as the operator writes them: 64 hex characters (the full SHA-256) or the 26-character invite form.</summary>
    private sealed class KeySet
    {
        private readonly HashSet<string> hex = new(StringComparer.Ordinal);
        private readonly HashSet<string> invite = new(StringComparer.Ordinal);

        public bool Add(string text)
        {
            var lower = text.Trim().ToLowerInvariant();
            if (lower.Length == 64 && lower.All(Uri.IsHexDigit))
            {
                hex.Add(lower);
                return true;
            }
            var normalized = HostKey.Normalize(text);
            if (normalized == null) return false;
            invite.Add(normalized);
            return true;
        }

        public bool Contains(ReadOnlySpan<byte> spki) =>
            (hex.Count > 0 && hex.Contains(RelayProtocol.FingerprintHex(spki))) || (invite.Count > 0 && invite.Contains(RelayProtocol.InviteFingerprint(spki)));
    }

    private sealed record Snapshot(List<IPNetwork> BannedNetworks, KeySet BannedKeys, List<Regex> BannedNames, KeySet AllowedKeys, List<Regex> DeniedNames, List<string> Warnings)
    {
        public static readonly Snapshot Empty = new([], new KeySet(), [], new KeySet(), [], []);
    }
}
