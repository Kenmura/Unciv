namespace Unciv.GodotClient.Settings;

/// <summary>C097: the graphics quality levels (design/ULTRA-GRAPHICS.md). Stored by name in <see cref="ClientSettings.GraphicsLevel"/>.</summary>
public enum GraphicsQuality
{
    /// <summary>Today's look on the compatibility renderer; the default and what every harness scenario uses.</summary>
    Low,

    /// <summary>Compatibility-safe polish: shadows, tone mapping, glow, MSAA 2x.</summary>
    High,

    /// <summary>Everything in High plus SSAO, MSAA 4x and screen-space reflections; needs the Forward+ renderer.</summary>
    Ultra,
}

/// <summary>What one quality level switches on. A pure table (no Godot types) so the mapping is tested without a renderer.</summary>
/// <param name="Shadows">Directional shadows from the sun.</param>
/// <param name="Tonemap">Filmic tone mapping.</param>
/// <param name="Glow">Mild bloom.</param>
/// <param name="Msaa">Multisample anti-aliasing samples (0, 2 or 4).</param>
/// <param name="Ssao">Screen-space ambient occlusion (Forward+ only).</param>
/// <param name="Ssr">Screen-space reflections (Forward+ only).</param>
/// <param name="ShaderTier">0 = today's materials; 1 = the C098 water and terrain shader (Ultra only; the design lists it under High too, deliberately held back to Ultra so Low and High pictures stay as they are); 2 is reserved.</param>
/// <param name="FarZoom">C085: camera size from which unit formations draw a single figure (see <see cref="Unciv.Assets.UnitLod.FarZoom"/>); looser (further out) at the higher levels.</param>
/// <param name="Exposure">Per-level colour calibration: tone-map exposure that keeps the palette near the Low look.</param>
/// <param name="Saturation">Per-level colour calibration: colour saturation adjustment (1 = none); the Forward+ picture is paler than the compatibility one for the same scene.</param>
/// <param name="AmbientEnergy">Per-level colour calibration: ambient light energy.</param>
public sealed record GraphicsPreset(bool Shadows, bool Tonemap, bool Glow, int Msaa, bool Ssao, bool Ssr, int ShaderTier, float FarZoom, float Exposure, float AmbientEnergy, float Saturation = 1f);

/// <summary>What a start (or a settings change) has to do about the renderer; produced by <see cref="GraphicsModel.Startup"/>.</summary>
/// <param name="Effective">The level to draw with now.</param>
/// <param name="RestartInto">The rendering method to restart into, or null to carry on.</param>
/// <param name="Pending">The value to store as <see cref="ClientSettings.RendererPending"/> (a restart is under way, or empty).</param>
/// <param name="UltraFailed">The value to store as <see cref="ClientSettings.UltraFailed"/>.</param>
public sealed record GraphicsStartup(GraphicsQuality Effective, string? RestartInto, string Pending, bool UltraFailed);

/// <summary>
/// C097: the Godot-free rules of the graphics setting: the preset table, which renderer a level needs, and the restart flow with its loop guard.
/// Deliberate design (design/ULTRA-GRAPHICS.md): Ultra needs Forward+, which Godot fixes at process start, so choosing it restarts the client with
/// <c>--rendering-method forward_plus</c>. Before restarting we persist <see cref="ClientSettings.RendererPending"/>; if the next start is not on the renderer
/// we asked for, the attempt failed: <see cref="ClientSettings.UltraFailed"/> is stored and High is used, so it can never restart-loop.
/// </summary>
public static class GraphicsModel
{
    public const string Compatibility = "gl_compatibility";
    public const string ForwardPlus = "forward_plus";

    /// <summary>The flat ambient light energy of today's look; <c>MapMaterials.Backdrop</c> and the Low preset share it so Low cannot drift from the old picture.</summary>
    public const float LowAmbientEnergy = 0.55f;

    private static readonly string[] SoftwareAdapters = ["llvmpipe", "lavapipe", "swiftshader", "software", "softpipe"];

    /// <summary>The level table. Low is today's look (exposure and ambient exactly as <c>MapMaterials.Backdrop</c> has them) and must stay identity so goldens do not move.
    /// Ultra's exposure, ambient and saturation were calibrated on a real GPU (T271, design/ULTRA-GRAPHICS.md section 7): Forward+ renders the same scene about 40% brighter and paler than the compatibility renderer, so Ultra is pulled down to the brightness of High (within about 6% at the mid and coast views) and its saturation raised. High's own values are the uncalibrated first guess: on the compatibility renderer it is about 45% brighter than Low by design of the film tone map.</summary>
    public static GraphicsPreset Preset(GraphicsQuality level) => level switch
    {
        GraphicsQuality.High => new GraphicsPreset(Shadows: true, Tonemap: true, Glow: true, Msaa: 2, Ssao: false, Ssr: false, ShaderTier: 0, FarZoom: 26f, Exposure: 1f, AmbientEnergy: 0.6f),
        GraphicsQuality.Ultra => new GraphicsPreset(Shadows: true, Tonemap: true, Glow: true, Msaa: 4, Ssao: true, Ssr: true, ShaderTier: 1, FarZoom: 30f, Exposure: 0.6f, AmbientEnergy: 0.3f, Saturation: 1.3f),
        _ => new GraphicsPreset(Shadows: false, Tonemap: false, Glow: false, Msaa: 0, Ssao: false, Ssr: false, ShaderTier: 0, FarZoom: Unciv.Assets.UnitLod.FarZoom, Exposure: 1f, AmbientEnergy: LowAmbientEnergy),
    };

    /// <summary>The stored name as a level; anything unknown (hand-edited file) is Low.</summary>
    public static GraphicsQuality Parse(string? name) => Enum.TryParse<GraphicsQuality>(name, ignoreCase: false, out var level) && Enum.IsDefined(level) ? level : GraphicsQuality.Low;

    /// <summary>The rendering method a level wants.</summary>
    public static string RendererFor(GraphicsQuality level) => level == GraphicsQuality.Ultra ? ForwardPlus : Compatibility;

    /// <summary>Whether the renderer is a software rasterizer (llvmpipe, lavapipe, SwiftShader): Forward+ on one runs, but is not what Ultra is for.</summary>
    public static bool IsSoftwareAdapter(string? adapterName) =>
        !string.IsNullOrEmpty(adapterName) && SoftwareAdapters.Any(it => adapterName.Contains(it, StringComparison.OrdinalIgnoreCase));

    /// <summary>The rendering method to run on for the chosen level: Forward+ for Ultra unless an attempt failed, otherwise the compatibility renderer.</summary>
    public static string WantedRenderer(GraphicsQuality requested, bool ultraFailed) => requested == GraphicsQuality.Ultra && !ultraFailed ? ForwardPlus : Compatibility;

    /// <summary>Whether the running renderer can show Ultra: Forward+ on a real adapter.</summary>
    public static bool CanShowUltra(string renderer, string? adapterName) => renderer == ForwardPlus && !IsSoftwareAdapter(adapterName);

    /// <summary>The level actually drawn: the requested one, except Ultra falls back to High when the renderer cannot show it or an earlier attempt failed.</summary>
    public static GraphicsQuality Effective(GraphicsQuality requested, bool ultraFailed, string renderer, string? adapterName) =>
        requested == GraphicsQuality.Ultra && (ultraFailed || !CanShowUltra(renderer, adapterName)) ? GraphicsQuality.High : requested;

    /// <summary>
    /// What to do at start. <paramref name="pending"/> is the stored restart marker. A marker that matches the running renderer means the restart worked (cleared);
    /// a marker that does not match means the restart failed: for Forward+ that stores the failure flag (Ultra then shows High), and in every case no further
    /// restart is attempted this start. Without a marker, a renderer that differs from what the (effective) level wants starts one attempt and stores the marker.
    /// </summary>
    public static GraphicsStartup Startup(GraphicsQuality requested, bool ultraFailed, string pending, string renderer, string? adapterName)
    {
        pending ??= "";
        if (pending != "")
        {
            bool worked = renderer == pending && (pending != ForwardPlus || !IsSoftwareAdapter(adapterName));
            bool failedUltra = !worked && pending == ForwardPlus;
            bool failed = ultraFailed || failedUltra;
            return new GraphicsStartup(Effective(requested, failed, renderer, adapterName), null, "", failed);
        }
        if (requested == GraphicsQuality.Ultra && !ultraFailed && renderer == ForwardPlus && IsSoftwareAdapter(adapterName)) ultraFailed = true; // Forward+ came up on a software rasterizer: remember, do not retry
        var effective = Effective(requested, ultraFailed, renderer, adapterName);
        string wanted = WantedRenderer(requested, ultraFailed);
        bool onWanted = wanted == ForwardPlus ? CanShowUltra(renderer, adapterName) : renderer != ForwardPlus;
        if (onWanted) return new GraphicsStartup(effective, null, "", ultraFailed);
        // Low and High run on any renderer: coming back from Forward+ is a courtesy, never forced for a mobile or odd renderer
        if (wanted == Compatibility && renderer != ForwardPlus) return new GraphicsStartup(effective, null, "", ultraFailed);
        return new GraphicsStartup(effective, wanted, wanted, ultraFailed);
    }

    /// <summary>Whether choosing <paramref name="requested"/> now needs a restart to take effect (Ultra off Forward+, or Forward+ left for a lower level) and has not been ruled out.</summary>
    public static bool NeedsRestart(GraphicsQuality requested, bool ultraFailed, string renderer, string? adapterName) =>
        Startup(requested, ultraFailed, "", renderer, adapterName).RestartInto != null;

    /// <summary>The settings screen's note under the picker: empty when nothing needs saying.</summary>
    public static string Note(GraphicsQuality requested, bool ultraFailed, string renderer, string? adapterName)
    {
        if (requested != GraphicsQuality.Ultra) return renderer == ForwardPlus ? "Restart to return to the standard renderer" : "";
        if (ultraFailed) return "Ultra needs a Vulkan or Direct3D 12 GPU; showing High";
        if (CanShowUltra(renderer, adapterName)) return "";
        return IsSoftwareAdapter(adapterName) && renderer == ForwardPlus
            ? "Ultra needs a Vulkan or Direct3D 12 GPU; showing High"
            : "Ultra needs a restart to switch the renderer";
    }

    /// <summary>The command line to restart with: the original arguments minus any earlier rendering-method choice, then <c>--rendering-method <paramref name="method"/></c>. Arguments after <c>--</c> are the game's own and are kept as they are.</summary>
    public static IReadOnlyList<string> RestartArgs(IEnumerable<string> original, string method)
    {
        var result = new List<string>();
        bool userArgs = false;
        bool skipNext = false;
        foreach (var arg in original)
        {
            if (skipNext) { skipNext = false; continue; }
            if (userArgs) { result.Add(arg); continue; }
            if (arg == "--")
            {
                userArgs = true;
                result.Add(arg);
            }
            else if (arg is "--rendering-method" or "--rendering-driver") skipNext = true;
            else if (arg.StartsWith("--rendering-method=", StringComparison.Ordinal) || arg.StartsWith("--rendering-driver=", StringComparison.Ordinal)) { }
            else result.Add(arg);
        }
        // the engine's own options must come before the "--" that starts the game's arguments
        int at = result.IndexOf("--");
        string[] added = ["--rendering-method", method];
        if (at < 0) result.AddRange(added);
        else result.InsertRange(at, added);
        return result;
    }
}
