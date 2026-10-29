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

    private static readonly object Gate = new();

    /// <summary>
    /// The content root, resolved once (locked: the temp folder of an extraction is shared). An extraction failure (disk full, unreadable bundle) is logged once and cached as null, so callers fall through to their
    /// own "no content" errors instead of every one of them retrying a 30 MB extraction. The res:// bundle is ignored in the editor, so a staged <c>Content/</c> cannot shadow a checkout's jsons.
    /// </summary>
    public static string? Directory
    {
        get
        {
            lock (Gate)
            {
                if (done) return resolved;
                done = true;
                var exeDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
                var zipOverride = System.Environment.GetEnvironmentVariable("UNCIV_CONTENT_ZIP");
                var zip = string.IsNullOrEmpty(zipOverride) ? BundlePath : zipOverride;
                if (string.IsNullOrEmpty(zipOverride) && OS.HasFeature("editor")) return null;
                try
                {
                    resolved = ContentBundle.Resolve(exeDir, System.IO.Directory.Exists, () => ReadVersion(zip), () => Open(zip), ProjectSettings.GlobalizePath("user://content"));
                }
                catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
                {
                    GD.PushError($"Content bundle {zip} could not be extracted: {e.Message}");
                }
                return resolved;
            }
        }
    }

    // the version sits next to the bundle (Godot paths or plain disk paths): reading 30 MB just to compare versions on every start would defeat the point
    private static string? ReadVersion(string zip)
    {
        var path = System.IO.Path.ChangeExtension(zip, ".version");
        if (Godot.FileAccess.FileExists(path)) return Godot.FileAccess.GetFileAsString(path);
        if (Godot.FileAccess.FileExists(zip)) GD.PushWarning($"Content bundle {zip} has no {path}; it is ignored (the export must include both files)");
        return null;
    }

    private static Stream Open(string zip)
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(zip);
        if (bytes == null || bytes.Length == 0) throw new IOException($"Content bundle {zip} could not be read ({Godot.FileAccess.GetOpenError()})");
        return new MemoryStream(bytes);
    }
}
