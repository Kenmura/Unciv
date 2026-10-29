using System.Security.Cryptography;
using System.Text;
using Unciv.Models.Ruleset;

namespace Unciv.Net.Mods;

/// <summary>Where the host (or a client comparing its own copy) finds the folder of a mod by name; null when there is none (a built-in ruleset, an unknown name).</summary>
public interface IModFolders
{
    IRulesetFolder? FolderOf(string modName);
}

/// <summary>The folders of the mods <see cref="RulesetCache"/> loaded from disk (<see cref="Ruleset.FolderLocation"/>); built-in rulesets have none.</summary>
public sealed class RulesetCacheModFolders : IModFolders
{
    public static RulesetCacheModFolders Instance { get; } = new();

    public IRulesetFolder? FolderOf(string modName) => RulesetCache.Get(modName)?.FolderLocation is { IsDirectory: true } folder ? folder : null;
}

/// <summary>Builds and checks the per-mod checksum manifest (T247). Reads only the allowed ruleset files (<see cref="ModSyncRules.AllowedFiles"/>) and enforces the size caps.</summary>
public static class ModManifestBuilder
{
    /// <summary>The entry of a mod folder, or null when the mod has no ruleset files, a file or the whole mod is over its cap, or the name is not a safe mod name.</summary>
    public static ModEntry? Build(string modName, IRulesetFolder modFolder)
    {
        if (!ModSyncRules.IsValidModName(modName)) return null;
        var jsons = modFolder.Child("jsons");
        var files = new List<ModFileEntry>();
        long total = 0;
        foreach (var name in ModSyncRules.AllowedFiles)
        {
            var file = jsons.Child(name);
            if (!file.Exists || file.IsDirectory) continue;
            var bytes = ReadBytes(file);
            if (bytes == null || bytes.Length > ModSyncRules.MaxFileBytes) return null;
            total += bytes.Length;
            if (total > ModSyncRules.MaxModBytes) return null;
            files.Add(new ModFileEntry { Path = name, Size = bytes.Length, Sha256 = HashOf(bytes) });
        }
        if (files.Count == 0) return null;
        return new ModEntry { Name = modName, Files = files, Checksum = ChecksumOf(files) };
    }

    /// <summary>The bytes of one allowed file of a mod, as the manifest counts them; null when it is absent, not allowed or unreadable.</summary>
    public static byte[]? ReadFile(IRulesetFolder modFolder, string path)
    {
        if (!ModSyncRules.IsAllowedFile(path)) return null;
        var file = modFolder.Child("jsons").Child(path);
        return file.Exists && !file.IsDirectory ? ReadBytes(file) : null;
    }

    public static string HashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>SHA-256 over the file list: for each file in ordinal path order <c>path NUL size NUL sha256 LF</c>. It binds names, sizes and contents, so equal checksums mean equal mods.</summary>
    public static string ChecksumOf(IEnumerable<ModFileEntry> files)
    {
        var text = new StringBuilder();
        foreach (var file in files.OrderBy(it => it.Path, StringComparer.Ordinal))
            text.Append(file.Path).Append('\0').Append(file.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\0').Append(file.Sha256).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static byte[]? ReadBytes(IRulesetFolder file)
    {
        try { return ModSyncRules.BytesOf(file.ReadString()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Checks a manifest received from a host before anything is requested: bounded counts and sizes, safe names, allowed files only, no duplicates, well-formed hashes, and every
    /// <see cref="ModEntry.Checksum"/> equal to the one recomputed from its file list. Returns null when it is acceptable, else a short reason.
    /// </summary>
    public static string? Validate(ModManifest manifest)
    {
        if (manifest.Entries.Count > ModSyncRules.MaxMods || manifest.Mods.Count > 4 * ModSyncRules.MaxMods) return "too many mods";
        if (manifest.RulesetHash.Length is 0 or > 64 || !manifest.RulesetHash.All(char.IsAsciiHexDigit)) return "bad ruleset hash";
        if (manifest.BaseRuleset.Length > ModSyncRules.MaxNameLength || manifest.Mods.Any(it => it.Length is 0 || it.Length > ModSyncRules.MaxNameLength)) return "bad name";
        long total = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Entries)
        {
            if (!ModSyncRules.IsValidModName(entry.Name)) return "bad mod name";
            if (!names.Add(entry.Name)) return "duplicate mod";
            if (entry.Files.Count is 0 || entry.Files.Count > ModSyncRules.AllowedFiles.Count) return "bad file count";
            var paths = new HashSet<string>(StringComparer.Ordinal);
            long modTotal = 0;
            foreach (var file in entry.Files)
            {
                if (!ModSyncRules.IsAllowedFile(file.Path)) return "file not allowed";
                if (!paths.Add(file.Path)) return "duplicate file";
                if (file.Size < 0 || file.Size > ModSyncRules.MaxFileBytes) return "file too large";
                if (!ModSyncRules.IsSha256Hex(file.Sha256)) return "bad file hash";
                modTotal += file.Size;
            }
            if (modTotal > ModSyncRules.MaxModBytes) return "mod too large";
            total += modTotal;
            if (total > ModSyncRules.MaxTotalBytes) return "download too large";
            if (!string.Equals(entry.Checksum, ChecksumOf(entry.Files), StringComparison.Ordinal)) return "checksum mismatch";
        }
        return null;
    }
}
