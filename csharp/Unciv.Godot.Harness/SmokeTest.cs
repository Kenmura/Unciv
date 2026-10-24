using Godot;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// C000 gate scene. Renders one hex prism under a perspective camera, captures the viewport to PNG, checks the pixels,
/// checks synthetic mouse input picks the expected ground point, optionally compares to / updates a golden image, then exits 0 or 1.
/// User arguments (after <c>--</c>): <c>--out=DIR</c> (default <c>user://</c>), <c>--golden=FILE</c>, <c>--update-golden</c>.
/// </summary>
public partial class SmokeTest : Node3D
{
    private const int WarmupFrames = 3;
    private static readonly Vector3 PrismCenter = new(0, 0.25f, 0);

    private Camera3D camera = null!;
    private int frame;
    private int clicksReceived;
    private Vector3? lastPick;
    private string outDir = "user://";
    private string? goldenPath;
    private bool updateGolden;
    private readonly List<string> failures = [];

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--out=", StringComparison.Ordinal)) outDir = arg["--out=".Length..];
            else if (arg.StartsWith("--golden=", StringComparison.Ordinal)) goldenPath = arg["--golden=".Length..];
            else if (arg == "--update-golden") updateGolden = true;
        }

        GetViewport().TransparentBg = false;
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.08f, 0.12f, 0.24f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.4f,
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, 30, 0), LightEnergy = 1.2f });
        camera = new Camera3D { Position = new Vector3(0, 4, 4), Current = true };
        AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up);

        // A flat-topped hex prism: a 6-sided cylinder, radius 1, height 0.5, sitting on y = 0
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 0.5f, RadialSegments = 6, Rings = 1 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.65f, 0.2f), Roughness = 0.9f },
            Position = PrismCenter,
        });
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            clicksReceived++;
            lastPick = HexPicking.PickGroundPoint(camera, click.Position);
        }
    }

    public override void _Process(double delta)
    {
        frame++;
        if (frame == WarmupFrames) RunChecks();
    }

    private void RunChecks()
    {
        string pngPath = outDir.TrimEnd('/') + "/smoke.png";
        var image = ViewportCapture.CaptureViewportPng(GetViewport(), pngPath);
        GD.Print($"[harness] captured {image.Width}x{image.Height} -> {ProjectSettings.GlobalizePath(pngPath)}");

        var stats = ImageStats.Analyze(image);
        GD.Print($"[harness] non-background {stats.NonBackgroundFraction:P1}, {stats.DistinctColors} colours");
        if (stats.IsBlank) failures.Add("frame is blank");
        else if (stats.NonBackgroundFraction is < 0.01 or > 0.6) failures.Add($"unexpected prism coverage {stats.NonBackgroundFraction:P1}");

        // A render of green geometry must contain clearly green pixels (guards against a black/white fallback frame)
        if (!ContainsGreenDominantPixels(image)) failures.Add("no green prism pixels in frame");

        CheckPicking();
        CheckGolden(image);

        foreach (var failure in failures) GD.PrintErr("[harness] FAIL: " + failure);
        GD.Print(failures.Count == 0 ? "[harness] PASS" : "[harness] FAILED");
        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }

    private static bool ContainsGreenDominantPixels(RgbaImage image)
    {
        long green = 0;
        for (int i = 0; i < image.Pixels.Length; i += 4)
            if (image.Pixels[i + 1] > image.Pixels[i] + 30 && image.Pixels[i + 1] > image.Pixels[i + 2] + 30) green++;
        return green > image.Width * image.Height / 100;
    }

    private void CheckPicking()
    {
        // Click exactly where known ground points project; each pick must land on its point.
        Vector3[] targets = [new(0.4f, 0, -0.3f), new(-0.7f, 0, 0.5f)];
        foreach (var target in targets)
        {
            lastPick = null;
            int before = clicksReceived;
            InputSimulator.SimulateMouseClick(GetViewport(), camera.UnprojectPosition(target));
            if (clicksReceived != before + 1) failures.Add($"click at {target} was not delivered");
            else if (lastPick is not { } picked || picked.DistanceTo(target) > 0.01f) failures.Add($"pick {lastPick} != {target}");
        }

        // A ray parallel to the ground never hits it
        var level = new Camera3D { Position = new Vector3(0, 2, 4) };
        AddChild(level);
        level.LookAt(new Vector3(0, 2, 0), Vector3.Up);
        if (HexPicking.PickGroundPoint(level, new Vector2(320, 240)) is not null) failures.Add("horizontal ray unexpectedly hit the ground");
        level.QueueFree();
    }

    private void CheckGolden(RgbaImage image)
    {
        if (goldenPath == null) return;
        if (updateGolden)
        {
            var error = GetViewport().GetTexture().GetImage().SavePng(goldenPath);
            if (error != Error.Ok) failures.Add($"could not write golden {goldenPath}: {error}");
            else GD.Print("[harness] golden updated: " + goldenPath);
            return;
        }
        if (!Godot.FileAccess.FileExists(goldenPath)) { failures.Add($"golden {goldenPath} does not exist (run with --update-golden)"); return; }
        var result = GoldenComparer.Compare(image, ViewportCapture.LoadPng(goldenPath));
        GD.Print($"[harness] golden: {result.Message} (hash {result.HashDistance}, mean {result.MeanAbsoluteDifference:F4}, px {result.DifferingPixelFraction:P2})");
        if (!result.Passed) failures.Add("golden mismatch: " + result.Message);
    }
}
