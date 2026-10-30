using Godot;

namespace Unciv.GodotClient.Settings;

/// <summary>
/// C097: the Godot side of the graphics setting: which renderer is running, which level is actually drawn, and the restart into another renderer.
/// The rules themselves are <see cref="GraphicsModel"/>. Harness scenarios call <see cref="Pin"/> so no scenario draws anything but Low (their goldens were taken on the
/// compatibility renderer and the placeholder palette was tuned there); the settings-graphics scenario overrides the renderer and the restart so it never depends on the machine.
/// </summary>
public static class GraphicsRuntime
{
    /// <summary>A level every <see cref="EffectiveFor"/> answers with, or null for the setting's own.</summary>
    public static GraphicsQuality? Pinned { get; private set; }

    /// <summary>Replaces the real renderer method / adapter name (scenarios and tests); null restores the real ones.</summary>
    public static string? RendererOverride { get; set; }

    public static string? AdapterOverride { get; set; }

    /// <summary>Replaces the integrated-GPU detection (scenarios and tests); null restores the real one.</summary>
    public static bool? IntegratedOverride { get; set; }

    /// <summary>What <see cref="Restart"/> does instead of restarting (scenarios); it receives the rendering method.</summary>
    public static Action<string>? RestartHandler { get; set; }

    public static void Pin(GraphicsQuality? level) => Pinned = level;

    /// <summary>The running rendering method: <c>forward_plus</c>, <c>mobile</c> or <c>gl_compatibility</c>.</summary>
    public static string Renderer => RendererOverride ?? RenderingServer.GetCurrentRenderingMethod();

    public static string Adapter => AdapterOverride ?? RenderingServer.GetVideoAdapterName();

    /// <summary>
    /// Whether the GPU is an integrated one (T288): the device type <c>RenderingServer.GetVideoAdapterType</c> (RenderingDevice.DeviceType) when the renderer reports one (Forward+, Mobile), otherwise the adapter name
    /// (<see cref="GraphicsModel.IsIntegratedAdapterName"/>; the compatibility renderer reports no device type).
    /// </summary>
    public static bool Integrated
    {
        get
        {
            if (IntegratedOverride is { } forced) return forced;
            if (AdapterOverride == null)
            {
                var type = RenderingServer.GetVideoAdapterType();
                if (type == RenderingDevice.DeviceType.IntegratedGpu) return true;
                if (type != RenderingDevice.DeviceType.Other) return false;
            }
            return GraphicsModel.IsIntegratedAdapterName(Adapter);
        }
    }

    /// <summary>The level to draw with for these settings on the running renderer.</summary>
    public static GraphicsQuality EffectiveFor(ClientSettings settings) =>
        Pinned ?? GraphicsModel.Effective(settings.Quality, settings.UltraFailed, Renderer, Adapter);

    /// <summary>The note the settings screen shows under the picker (untranslated); empty when there is nothing to say.</summary>
    public static string NoteFor(ClientSettings settings) =>
        Pinned != null ? "" : GraphicsModel.Note(settings.Quality, settings.UltraFailed, Renderer, Adapter, Integrated);

    public static bool NeedsRestart(ClientSettings settings) =>
        Pinned == null && GraphicsModel.NeedsRestart(settings.Quality, settings.UltraFailed, Renderer, Adapter);

    /// <summary>
    /// Call first thing at start (before building any scene): stores what the start learned (restart worked, or Ultra failed) and, when the renderer is not the one the
    /// setting wants, starts the one restart attempt. True when the client is restarting and the caller must not carry on building the UI.
    /// </summary>
    public static bool StartupCheck(ClientSettingsService settings)
    {
        var current = settings.Current;
        var decision = GraphicsModel.Startup(current.Quality, current.UltraFailed, current.RendererPending, Renderer, Adapter);
        if (DisplayServer.GetName() == "headless" && RendererOverride == null) return false; // no window, no renderer to choose
        bool saved = true;
        if (decision.Pending != current.RendererPending || decision.UltraFailed != current.UltraFailed)
        {
            saved = settings.Update(s => s with { RendererPending = decision.Pending, UltraFailed = decision.UltraFailed });
        }
        if (decision.RestartInto is not { } method) return false;
        if (!saved) return false; // the marker is the loop guard: never restart when it could not be written
        if (Restart(method)) return true;
        settings.Update(s => s with { RendererPending = "" }); // could not restart: no attempt is under way
        return false;
    }

    /// <summary>Quits and starts the client again with <c>--rendering-method</c> <paramref name="method"/> (Godot's restart-on-exit).</summary>
    public static bool Restart(string method)
    {
        if (RestartHandler != null)
        {
            RestartHandler(method);
            return true;
        }
        var args = GraphicsModel.RestartArgs(OS.GetCmdlineArgs(), method);
        if (Engine.GetMainLoop() is not SceneTree tree) return false;
        OS.SetRestartOnExit(true, [.. args]);
        tree.Quit();
        return true;
    }
}
