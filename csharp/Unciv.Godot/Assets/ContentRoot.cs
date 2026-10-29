using Godot;
using Unciv.Assets.Packaging;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// T276: the folder that plays the part of "the folder beside the executable" for jsons, packs and fonts. On desktop that is the executable folder itself. Without a data folder there (an Android APK) it is the content bundle
/// (<c>res://Content/unciv-content.zip</c>, made by <c>tools/stage-content.py</c>) extracted once to <c>user://content</c> (<see cref="ContentBundle"/>). The environment variable <c>UNCIV_CONTENT_ZIP</c> names a bundle on disk
/// instead (the smoke test for the no-executable-folder case on desktop). Null when neither exists (running from the editor or the harness inside a checkout).
/// </summary>
public static class ContentRoot
{
    public const string BundlePath = "res://Content/unciv-content.zip";
    private static string? resolved;
    private static bool done;

    public static string? Directory
    {
        get
        {
            if (done) return resolved;
            var exeDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
            var zipOverride = System.Environment.GetEnvironmentVariable("UNCIV_CONTENT_ZIP");
            var zip = string.IsNullOrEmpty(zipOverride) ? BundlePath : zipOverride;
            resolved = ContentBundle.Resolve(exeDir, System.IO.Directory.Exists, () => ReadVersion(zip), () => Open(zip), ProjectSettings.GlobalizePath("user://content"));
            done = true;
            return resolved;
        }
    }

    // the version sits next to the bundle (Godot paths or plain disk paths): reading 30 MB just to compare versions on every start would defeat the point
    private static string? ReadVersion(string zip)
    {
        var path = System.IO.Path.ChangeExtension(zip, ".version");
        return Godot.FileAccess.FileExists(path) ? Godot.FileAccess.GetFileAsString(path) : null;
    }

    private static Stream Open(string zip)
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(zip);
        if (bytes == null || bytes.Length == 0) throw new IOException($"Content bundle {zip} could not be read ({Godot.FileAccess.GetOpenError()})");
        return new MemoryStream(bytes);
    }
}
