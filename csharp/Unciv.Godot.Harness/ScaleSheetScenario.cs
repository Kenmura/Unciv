using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Ui;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// T311: the UI scale sheet. The real game screen (world HUD, then the in-game menu) rendered into an off-screen viewport of each common window size, so a 4K or an ultrawide window can be looked
/// at on any desktop (a compositor may refuse to make a window larger than the monitor). Each size may carry an OS display scale, <c>WxH@S</c>. The scenario writes <c>scale-WxH[@S]-hud.png</c>,
/// <c>-menu.png</c> and <c>index.md</c> (the auto scale, the logical window and the pixel height of a 32 px control per size) and fails when a size lays out outside its viewport.
/// Not part of CI and never compared to goldens (a real GPU draws it): <c>tools/scale-sheet.sh</c> runs it on the host. Environment: <c>UNCIV_SCALE_OUT</c> (folder, required),
/// <c>UNCIV_SCALE_RES</c> (comma separated sizes, default the six reasonable desktop sizes plus a 4K window at 200%).
/// </summary>
public sealed class ScaleSheetScenario : Scenario
{
    private const string DefaultSizes = "1366x768,1920x1080,1920x1200,2560x1440,3440x1440,3840x2160,3840x2160@2";

    public override string Name => "scale-sheet";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist => ["Open scale-*.png: do buttons, text and panels stay comfortable and inside the window at every size?"];

    /// <summary>The sizes of the sheet from <c>UNCIV_SCALE_RES</c>: width, height and OS display scale of each.</summary>
    internal static IReadOnlyList<(int Width, int Height, float OsScale)> Sizes(string variable = "UNCIV_SCALE_RES", string defaults = DefaultSizes)
    {
        var sizes = new List<(int, int, float)>();
        foreach (string entry in (GpuBenchScenario.Bench(variable) ?? defaults).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var atParts = entry.Split('@');
            var dims = atParts[0].Split('x');
            if (dims.Length != 2 || !int.TryParse(dims[0], out int w) || !int.TryParse(dims[1], out int h) || w <= 0 || h <= 0) throw new InvalidOperationException($"bad size '{entry}' in UNCIV_SCALE_RES (want WxH or WxH@S)");
            float os = atParts.Length > 1 && float.TryParse(atParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? s : 1f;
            sizes.Add((w, h, os));
        }
        return sizes;
    }

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        string outDir = GpuBenchScenario.Bench("UNCIV_SCALE_OUT") ?? throw new InvalidOperationException("UNCIV_SCALE_OUT is not set");
        Directory.CreateDirectory(outDir);
        var index = new List<string> { "| size | OS scale | auto scale | logical window | 32 px control | breakpoint |", "|---|---|---|---|---|---|" };
        var realProfile = DisplayProfile.Current;
        var steps = new List<ScenarioStep>();
        foreach (var (width, height, os) in Sizes())
        {
            string tag = $"{width}x{height}" + (os > 1f ? "@" + os.ToString(CultureInfo.InvariantCulture) : "");
            var holder = new Holder();
            steps.Add(new Act($"build {tag}", () =>
            {
                DisplayProfile.Current = new DisplayProfile(false, 0f, os);
                var sub = holder.Sub = new SubViewport { Size = new Vector2I(width, height), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, TransparentBg = false, World3D = host.GetViewport().FindWorld3D() };
                host.AddChild(sub);
                var localHost = new LocalHost(session, ScenarioGame.HumanA, () => 1000);
                var screen = holder.Screen = new GameScreen();
                sub.AddChild(screen);
                screen.Start(localHost, AssetRegistry.ForRuleset(session.Game.Ruleset));
            }));
            steps.Add(new WaitFrames(6));
            steps.Add(new Act($"capture {tag} hud", () =>
            {
                Save(holder.Sub, Path.Combine(outDir, $"scale-{tag}-hud.png"));
                var theme = holder.Screen.SharedTheme ?? throw new InvalidOperationException("the screen has no theme");
                index.Add($"| {width}x{height} | {os.ToString(CultureInfo.InvariantCulture)} | {theme.Scale.ToString(CultureInfo.InvariantCulture)} | {(int)MathF.Round(width / theme.Scale, MidpointRounding.AwayFromZero)}x{(int)MathF.Round(height / theme.Scale, MidpointRounding.AwayFromZero)} | {theme.Px(UiTokens.ControlMinHeight)} px | {theme.Breakpoint} |");
                holder.Screen.OpenGameMenu();
            }));
            steps.Add(new WaitFrames(6));
            steps.Add(new Act($"capture {tag} menu", () =>
            {
                Save(holder.Sub, Path.Combine(outDir, $"scale-{tag}-menu.png"));
                holder.Sub.QueueFree();
            }));
            steps.Add(new WaitFrames(2));
        }
        steps.Add(new Act("write index", () =>
        {
            DisplayProfile.Current = realProfile;
            File.WriteAllText(Path.Combine(outDir, "index.md"), string.Join("\n", index) + "\n");
            GD.Print($"[scale-sheet] {Sizes().Count} sizes in {outDir}");
        }));
        return steps;
    }

    private sealed class Holder
    {
        private SubViewport? sub;
        private GameScreen? screen;

        public SubViewport Sub
        {
            get => sub ?? throw new InvalidOperationException("not built");
            set => sub = value;
        }

        public GameScreen Screen
        {
            get => screen ?? throw new InvalidOperationException("not built");
            set => screen = value;
        }
    }

    private static void Save(SubViewport viewport, string path)
    {
        using var image = viewport.GetTexture().GetImage() ?? throw new IOException($"no image for {path}");
        var error = image.SavePng(path);
        if (error != Error.Ok) throw new IOException($"Could not write {path}: {error}");
    }
}
