using System.IO.Compression;

namespace Unciv.Assets.Packaging;

/// <summary>
/// T276 (Android gap A-1): content delivery when no data folder sits beside the executable (an APK has none). The packaging step zips the same tree an exported desktop build keeps next to
/// its executable (<c>data/jsons</c>, <c>Packs</c>, <c>Assets/fonts</c>) into one file that rides in the pck; on start it is extracted once into a folder under <c>user://</c>, stamped with
/// the bundle's version so an update re-extracts. The extracted folder then plays the part of the executable folder for <see cref="ContentLocator"/>, the pack reader and the font loader.
/// Pure file logic, no Godot reference.
/// </summary>
public static class ContentBundle
{
    /// <summary>File in the extracted folder holding the version of the bundle it came from; written last, so a half-finished extraction is never trusted.</summary>
    public const string StampFile = ".content-version";

    /// <summary>
    /// The folder to use as the content root: <paramref name="executableDir"/> when it already holds the bundled jsons (desktop, unchanged); else the extracted bundle in
    /// <paramref name="stagingDir"/> (extracted now when missing or of another version); null when the build carries no bundle (a checkout run from the editor).
    /// </summary>
    /// <param name="executableDir">The folder of the running executable.</param>
    /// <param name="directoryExists">File-system probe (injected for tests).</param>
    /// <param name="stagingDir">Where the bundle is extracted (a folder under <c>user://</c>).</param>
    /// <param name="readVersion">The bundle's version string, or null when the build carries no bundle. Cheap: called on every start.</param>
    /// <param name="openZip">Opens the bundle; only called when an extraction is needed.</param>
    public static string? Resolve(string executableDir, Func<string, bool> directoryExists, Func<string?> readVersion, Func<Stream> openZip, string stagingDir)
    {
        if (directoryExists(ContentLocator.BundledJsons(executableDir))) return executableDir;
        var version = readVersion()?.Trim();
        if (string.IsNullOrEmpty(version)) return null;
        EnsureExtracted(version, openZip, stagingDir);
        return stagingDir;
    }

    /// <summary>Extracts the bundle into <paramref name="stagingDir"/> unless that folder already carries <paramref name="version"/>. Returns whether it extracted.</summary>
    public static bool EnsureExtracted(string version, Func<Stream> openZip, string stagingDir)
    {
        if (IsCurrent(version, stagingDir)) return false;
        var work = stagingDir.TrimEnd('/', '\\') + ".tmp";
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        try
        {
            using var stream = openZip();
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var root = Path.GetFullPath(work) + Path.DirectorySeparatorChar;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                var target = Path.GetFullPath(Path.Combine(work, entry.FullName));
                if (!target.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException($"Content bundle entry escapes the target folder: {entry.FullName}");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
            File.WriteAllText(Path.Combine(work, StampFile), version);
        }
        catch
        {
            Directory.Delete(work, true);
            throw;
        }
        if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
        Directory.Move(work, stagingDir);
        return true;
    }

    private static bool IsCurrent(string version, string stagingDir)
    {
        var stamp = Path.Combine(stagingDir, StampFile);
        return File.Exists(stamp) && File.ReadAllText(stamp).Trim() == version && Directory.Exists(ContentLocator.BundledJsons(stagingDir));
    }
}
