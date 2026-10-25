namespace Unciv.Assets.Packaging;

/// <summary>
/// C042: where the client finds the ruleset jsons (and the translations beneath them, <c>jsons/translations</c>). An exported build ships them in a
/// <c>data/jsons</c> folder next to the executable (copied by <c>Unciv.Godot/tools/package.sh</c>); a checkout has them at <c>android/assets/jsons</c>
/// above the Godot project. The bundled copy wins, so a build placed inside a checkout still uses its own data. Pure path logic, no Godot reference.
/// </summary>
public static class ContentLocator
{
    /// <summary>Folder next to the executable that the packaging script fills.</summary>
    public const string BundleFolder = "data";

    public static string BundledJsons(string executableDir) => Path.Combine(executableDir, BundleFolder, "jsons");

    /// <summary>The first existing jsons folder: the bundled one, else <c>android/assets/jsons</c> in the nearest ancestor of <paramref name="projectDir"/>; null when neither exists.</summary>
    public static string? FindJsons(string executableDir, string projectDir, Func<string, bool> directoryExists)
    {
        var bundled = BundledJsons(executableDir);
        if (directoryExists(bundled)) return bundled;
        for (var dir = projectDir.TrimEnd('/', '\\'); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir) ?? "")
        {
            var candidate = Path.Combine(dir, "android", "assets", "jsons");
            if (directoryExists(candidate)) return candidate;
        }
        return null;
    }
}
