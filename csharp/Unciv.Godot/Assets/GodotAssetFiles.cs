using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// Reads the client's asset files from the Godot resource filesystem (the only place a <c>res://</c> path is formed).
/// C063: when the running project is the test harness the bundled pack (<c>assets/packs/unciv</c>) lives in the <c>Unciv.Godot</c> project folder instead, so a path
/// that is not under <c>res://</c> is also looked for in that folder; images are read as plain files (no editor import step needed) unless an imported texture exists.
/// </summary>
public sealed class GodotAssetFiles : IAssetFiles
{
    private const string Root = "res://";
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

    /// <summary>Folders that may hold <c>assets/packs</c> besides res://: next to an exported executable (tools/package.sh copies the pack there), or the <c>Unciv.Godot</c> project folder above a test-harness project.</summary>
    private static List<string> ExternalRoots()
    {
        if (externalRoots != null) return externalRoots;
        externalRoots = [];
        var exeDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath());
        if (!string.IsNullOrEmpty(exeDir)) externalRoots.Add(exeDir);
        for (string d = ProjectSettings.GlobalizePath(Root); !string.IsNullOrEmpty(d); d = System.IO.Path.GetDirectoryName(d.TrimEnd('/')) ?? "")
        {
            var candidate = System.IO.Path.Combine(d, "Unciv.Godot");
            if (System.IO.File.Exists(System.IO.Path.Combine(candidate, "project.godot")))
            {
                externalRoots.Add(candidate);
                break;
            }
        }
        return externalRoots;
    }

    private static string? ExternalPath(string relativePath)
    {
        if (relativePath.Contains("..", StringComparison.Ordinal)) return null;
        foreach (var root in ExternalRoots())
        {
            var full = System.IO.Path.Combine(root, relativePath);
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
