using System.Text.Json;

namespace Unciv.Assets;

/// <summary>Read access to the client's asset files by relative path ("assets/override/terrain/Grassland.png"). Godot supplies a res:// implementation; tests use <see cref="MemoryAssetFiles"/>.</summary>
public interface IAssetFiles
{
    bool Exists(string relativePath);
    string? ReadText(string relativePath);
}

public sealed class MemoryAssetFiles : IAssetFiles
{
    private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);

    public MemoryAssetFiles Add(string relativePath, string content = "")
    {
        files[relativePath] = content;
        return this;
    }

    public bool Exists(string relativePath) => files.ContainsKey(relativePath);

    public string? ReadText(string relativePath) => files.GetValueOrDefault(relativePath);
}

public enum AssetSourceKind
{
    Override,
    Pack,
    Procedural,
}

/// <param name="Requested">The key the caller asked for.</param>
/// <param name="Source">Where the asset comes from.</param>
/// <param name="Matched">The chain entry that supplied the file (equals <paramref name="Requested"/> for procedural).</param>
/// <param name="Path">Relative path of the file for Override/Pack, null for Procedural.</param>
public readonly record struct AssetResolution(AssetKey Requested, AssetKey Matched, AssetSourceKind Source, string? Path);

/// <summary>
/// Engine-independent half of <c>AssetRegistry.Get</c>. For each key of the fallback chain, in order: (1) <c>assets/override/&lt;stem&gt;.{tres,glb,png,ogg}</c>,
/// (2) the selected pack's manifest entry. When no chain entry has a file, the result is Procedural for the requested key. Resolution never throws:
/// unreadable manifests are skipped and listed in <see cref="Diagnostics"/>.
/// </summary>
public sealed class AssetResolver
{
    public const string OverrideDir = "assets/override/";
    public const string PackDir = "assets/packs/";

    /// <summary>C063: the pack of Unciv's icons shipped with the client; every registry consults it after the selected pack.</summary>
    public const string BundledPack = "unciv";
    public static readonly string[] OverrideExtensions = ["tres", "glb", "png", "ogg"];
    private static readonly HashSet<string> ManifestExtensions = new(OverrideExtensions, StringComparer.Ordinal) { "jpg", "webp", "wav", "ttf", "otf" };

    private readonly IAssetFiles files;
    private readonly IAssetFallbacks fallbacks;
    private readonly string? pack;
    private readonly string? basePack;
    private readonly List<string> diagnostics = [];
    private readonly Dictionary<string, Dictionary<string, string>> manifests = new(StringComparer.Ordinal);

    /// <param name="files">Where asset files are read from.</param>
    /// <param name="fallbacks">The fallback chain of every key.</param>
    /// <param name="pack">The selected pack, consulted first.</param>
    /// <param name="basePack">C063: a pack consulted after <paramref name="pack"/> for every key (the bundled default art); null for none. A missing base pack is silent (only a missing selected pack is a diagnostic).</param>
    public AssetResolver(IAssetFiles files, IAssetFallbacks fallbacks, string? pack = null, string? basePack = null)
    {
        this.files = files;
        this.fallbacks = fallbacks;
        this.pack = pack;
        this.basePack = basePack == pack ? null : basePack;
    }

    public IReadOnlyList<string> Diagnostics => diagnostics;

    public AssetResolution Resolve(AssetKey key)
    {
        foreach (var candidate in AssetFallbacks.Chain(key, fallbacks))
        {
            foreach (var ext in OverrideExtensions)
            {
                var path = OverrideDir + candidate.FileStem + "." + ext;
                if (files.Exists(path)) return new AssetResolution(key, candidate, AssetSourceKind.Override, path);
            }
            if ((PackEntry(pack, candidate) ?? PackEntry(basePack, candidate)) is { } packPath) return new AssetResolution(key, candidate, AssetSourceKind.Pack, packPath);
        }
        return new AssetResolution(key, key, AssetSourceKind.Procedural, null);
    }

    private string? PackEntry(string? pack, AssetKey key)
    {
        if (string.IsNullOrEmpty(pack)) return null;
        if (!manifests.TryGetValue(pack, out var manifest)) manifests[pack] = manifest = LoadManifest(pack, quiet: pack == basePack);
        if (!manifest.TryGetValue(key.ToString(), out var relative)) return null;
        var full = PackDir + pack + "/" + relative;
        return files.Exists(full) ? full : null;
    }

    private Dictionary<string, string> LoadManifest(string pack, bool quiet)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!IsSafeSegment(pack))
        {
            diagnostics.Add($"Pack name '{pack}' is not a plain directory name");
            return result;
        }
        var manifestPath = PackDir + pack + "/manifest.json";
        var text = files.ReadText(manifestPath);
        if (text == null)
        {
            if (!quiet) diagnostics.Add($"Pack manifest {manifestPath} not found");
            return result;
        }
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add($"{manifestPath}: expected an object with a \"keys\" object");
                return result;
            }
            foreach (var entry in keys.EnumerateObject())
            {
                var value = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
                if (!AssetKey.TryParse(entry.Name, out _)) diagnostics.Add($"{manifestPath}: '{entry.Name}' is not an asset key");
                else if (value == null || !IsSafeRelativePath(value)) diagnostics.Add($"{manifestPath}: unsafe or non-string path for '{entry.Name}'");
                else result[entry.Name] = value;
            }
        }
        catch (JsonException e)
        {
            diagnostics.Add($"{manifestPath}: {e.Message}");
            result.Clear();
        }
        return result;
    }

    private static bool IsSafeSegment(string name) => name.Length > 0 && name != "." && name != ".." && name.IndexOfAny(['/', '\\', ':']) < 0;

    /// <summary>Relative, forward-slash path without '..', drive/scheme colon or leading slash, with a known file extension.</summary>
    public static bool IsSafeRelativePath(string path)
    {
        if (path.Length == 0 || path[0] == '/' || path.Contains('\\', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal)) return false;
        var segments = path.Split('/');
        if (segments.Any(s => s.Length == 0 || s == "." || s == "..")) return false;
        var dot = path.LastIndexOf('.');
        return dot > 0 && ManifestExtensions.Contains(path[(dot + 1)..].ToLowerInvariant());
    }
}
