using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// Reads the client's asset files from the Godot resource filesystem (the only place a <c>res://</c> path is formed).
/// C063: the bundled pack (<c>assets/packs/unciv</c> as a pack path) is the plain folder <c>Packs/unciv</c> of the <c>Unciv.Godot</c> project, next to the executable in an export, so a pack path that
/// is not under <c>res://</c> is looked for there; its images are read as plain files (no editor import step, one code path in the editor, the harness and an export).
/// </summary>
public sealed class GodotAssetFiles : IAssetFiles
{
    private const string Root = "res://";
    /// <summary>Folder of the bundled packs next to the project or the executable. It sits outside the Godot resource tree (<c>.gdignore</c>, no import step) and its name differs from <c>Assets</c> by more than case.</summary>
    public const string PackFolder = "Packs";
    private static List<string>? externalRoots;

    public bool Exists(string relativePath) => ResourceLoader.Exists(Root + relativePath) || Godot.FileAccess.FileExists(Root + relativePath) || ExternalPath(relativePath) != null;

    public string? ReadText(string relativePath)
    {
        if (Godot.FileAccess.FileExists(Root + relativePath)) return Godot.FileAccess.GetFileAsString(Root + relativePath);
        var external = ExternalPath(relativePath);
        return external == null ? null : ReadAllText(external);
    }

    /// <summary>The names of the sub-directories of a resource directory (empty when it does not exist).</summary>
    public static IReadOnlyList<string> ListDirectories(string relativeDirectory) =>
        [.. (DirAccess.GetDirectoriesAt(Root + relativeDirectory) ?? [])];

    public static Resource? Load(string relativePath)
    {
        if (IsImage(relativePath) && !ResourceLoader.Exists(Root + relativePath)) return LoadImage(relativePath);
        return ResourceLoader.Exists(Root + relativePath) ? ResourceLoader.Load(Root + relativePath) : null;
    }

    private static bool IsImage(string path) =>
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    /// <summary>An un-imported image file as a mipmapped texture (icons are drawn well below their source size).</summary>
    private static ImageTexture? LoadImage(string relativePath)
    {
        var source = Godot.FileAccess.FileExists(Root + relativePath) ? Root + relativePath : ExternalPath(relativePath);
        if (source == null) return null;
        var image = Image.LoadFromFile(source);
        if (image == null || image.IsEmpty()) return null;
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Folders that may hold the <c>Packs</c> folder besides res://: next to an exported executable (tools/package.sh copies it there), the <c>Unciv.Godot</c> project folder itself, or that folder above a test-harness project.</summary>
    private static List<string> ExternalRoots()
    {
        if (externalRoots != null) return externalRoots;
        externalRoots = [];
        var exeDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath());
        if (!string.IsNullOrEmpty(exeDir)) externalRoots.Add(exeDir);
        for (string d = ProjectSettings.GlobalizePath(Root); !string.IsNullOrEmpty(d); d = System.IO.Path.GetDirectoryName(d.TrimEnd('/')) ?? "")
        {
            var found = new[] { d, System.IO.Path.Combine(d, "Unciv.Godot") }.FirstOrDefault(c => System.IO.Directory.Exists(System.IO.Path.Combine(c, PackFolder)) && System.IO.File.Exists(System.IO.Path.Combine(c, "project.godot")));
            if (found != null)
            {
                externalRoots.Add(found);
                break;
            }
        }
        return externalRoots;
    }

    /// <summary>The disk file of a pack path (<c>assets/packs/&lt;pack&gt;/...</c>) in the <c>Packs</c> folder of an external root; null for any other or unsafe path.</summary>
    private static string? ExternalPath(string relativePath)
    {
        if (!relativePath.StartsWith(AssetResolver.PackDir, StringComparison.Ordinal) || relativePath.Contains('\\', StringComparison.Ordinal) || relativePath.Contains(':', StringComparison.Ordinal) || relativePath.Split('/').Any(seg => seg.Length == 0 || seg is "." or "..")) return null;
        var inPacks = System.IO.Path.Combine(PackFolder, relativePath[AssetResolver.PackDir.Length..]);
        foreach (var root in ExternalRoots())
        {
            var full = System.IO.Path.Combine(root, inPacks);
            if (System.IO.File.Exists(full)) return full;
        }
        return null;
    }

    private static string? ReadAllText(string path)
    {
        try { return System.IO.File.ReadAllText(path); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { return null; }
    }
}
