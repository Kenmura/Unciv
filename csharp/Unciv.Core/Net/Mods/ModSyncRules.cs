using System.Text;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;

namespace Unciv.Net.Mods;

/// <summary>
/// T247: what may travel in a mod sync, and the bounds that keep it harmless. Transferred mod content is untrusted data from another player: only the ruleset's own json data files
/// (the fixed names of <see cref="RulesetFile.Values"/>, directly inside the mod's <c>jsons</c> folder) are ever listed, requested, written or loaded. Images, sounds, music, atlases and
/// anything else a mod folder may hold are never transferred, and nothing is executed: a ruleset is read as data by the json loader.
/// </summary>
public static class ModSyncRules
{
    /// <summary>Largest single file, bytes (its base64 form must fit in one wire frame).</summary>
    public const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>Largest total of one mod, bytes.</summary>
    public const int MaxModBytes = 16 * 1024 * 1024;

    /// <summary>Largest total of one sync (every mod together), bytes.</summary>
    public const int MaxTotalBytes = 32 * 1024 * 1024;

    public const int MaxMods = 8;
    public const int MaxNameLength = 64;

    /// <summary>The only file names that are transferred: the ruleset json files, by exact name.</summary>
    public static IReadOnlyList<string> AllowedFiles { get; } = RulesetFile.Values.Select(it => it.Filename).Order(StringComparer.Ordinal).ToList();

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>True for a file name that may be transferred: exactly one of <see cref="AllowedFiles"/> (so no separator, no <c>..</c>, no drive, no stream, no hidden file).</summary>
    public static bool IsAllowedFile(string? path) => path != null && AllowedFiles.Contains(path, StringComparer.Ordinal);

    /// <summary>
    /// True for a mod name that is safe as a folder name on every platform and cannot be mistaken for a built-in ruleset: 1 to <see cref="MaxNameLength"/> characters of ASCII letters, digits,
    /// space, <c>_ - . '</c>, starting with a letter or digit, not ending in a space or a dot, and not a Windows device name.
    /// </summary>
    public static bool IsValidModName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength) return false;
        if (!char.IsAsciiLetterOrDigit(name[0])) return false;
        if (name[^1] is ' ' or '.') return false;
        foreach (var c in name)
            if (!char.IsAsciiLetterOrDigit(c) && c is not (' ' or '_' or '-' or '.' or '\'')) return false;
        var stem = name.Split('.')[0].TrimEnd();
        if (ReservedNames.Contains(stem)) return false;
        return !IsBuiltinRuleset(name);
    }

    /// <summary>True when <paramref name="name"/> is one of the rulesets the game ships (compared ignoring case: such a folder name could shadow it on a case-insensitive disk).</summary>
    public static bool IsBuiltinRuleset(string name) => Enum.GetValues<BaseRuleset>().Any(it => string.Equals(it.FullName(), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for 64 lower-case hex characters.</summary>
    public static bool IsSha256Hex(string? text) => text != null && text.Length == 64 && text.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    /// <summary>The UTF-8 bytes a json file counts as: the loader reads files as text, so the text is what is compared and written (a byte order mark is not part of it).</summary>
    public static byte[] BytesOf(string text) => new UTF8Encoding(false).GetBytes(text);

    /// <summary>Decodes received bytes as strict UTF-8; null when they are not valid text (a ruleset file never is binary).</summary>
    public static string? TryDecode(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return null; }
    }
}

/// <summary>Stable codes of a failed or finished mod sync, shown to the player as fixed sentences (never text from the host).</summary>
public static class ModSyncCodes
{
    /// <summary>The session ended because mods were installed: the caller hot-loads them (<c>ModSyncService.HotLoad</c>) and joins again.</summary>
    public const string Synced = "mods-synced";

    public const string Declined = "mod-sync-declined";
    public const string BadManifest = "mod-sync-bad-manifest";
    public const string BadFile = "mod-sync-bad-file";
    public const string Failed = "mod-sync-failed";
}
