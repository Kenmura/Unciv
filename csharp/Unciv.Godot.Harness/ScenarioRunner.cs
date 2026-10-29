using Godot;
using Unciv.GodotClient.Play;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// Runs one scenario (C008): seeded session, the scenario's UI, its script. State assertions run before captures; each capture is
/// compared to its golden; at the end the session's state hash must equal the hash of a headless replay of the command log
/// (<see cref="ScenarioReplay"/>, no Godot involved). Writes <c>result.json</c> and the PNGs to <c>--out/&lt;scenario&gt;</c>.
/// User arguments (after <c>--</c>): <c>--scenario=NAME</c>, <c>--out=DIR</c>, <c>--golden-dir=DIR</c>, <c>--update-golden</c>, <c>--golden-tolerance=strict|renderer</c>.
/// The process exits 0 only when nothing failed.
/// </summary>
public partial class ScenarioRunner : Control
{
    private readonly List<string> failures = [];
    private readonly List<ScenarioCapture> captures = [];
    private string scenarioName = "";
    private string outDir = "out";
    private string goldenDir = "golden";
    private bool updateGolden;
    private GoldenTolerance tolerance = new();

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--scenario=", StringComparison.Ordinal)) scenarioName = arg["--scenario=".Length..];
            else if (arg.StartsWith("--out=", StringComparison.Ordinal)) outDir = arg["--out=".Length..];
            else if (arg.StartsWith("--golden-dir=", StringComparison.Ordinal)) goldenDir = arg["--golden-dir=".Length..];
            else if (arg == "--update-golden") updateGolden = true;
            else if (arg.StartsWith("--golden-tolerance=", StringComparison.Ordinal))
            {
                string profile = arg["--golden-tolerance=".Length..];
                var chosen = GoldenTolerance.FromName(profile);
                if (chosen == null) failures.Add($"unknown golden tolerance profile '{profile}' (strict, renderer)");
                else tolerance = chosen;
            }
        }
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        GetTree().NodeAdded += FreezeRealTime;
        _ = RunAsync();
    }

    /// <summary>
    /// C058: a scenario's result must not depend on how fast the machine renders frames. Every node whose behaviour is driven by the real frame
    /// time is switched to explicit stepping as soon as it enters the tree (so no frame ever runs with the real-time default): the hover delay
    /// of <see cref="Unciv.GodotClient.Ui.TooltipHost"/> becomes unreachable (scenarios advance it by hand), the map camera stops panning from
    /// held keys / the window edge. (The engine's own native tooltip delay is set in project.godot; <c>HarnessDeterminismTests</c> guards both.)
    /// </summary>
    public const double UnreachableDelay = 1e9;

    private static void FreezeRealTime(Node node)
    {
        switch (node)
        {
            case Unciv.GodotClient.Ui.TooltipHost host: host.Delay = UnreachableDelay; break;
            case Unciv.GodotClient.Map.MapView map: map.RealTimePanning = false; break;
        }
    }

    private async Task RunAsync()
    {
        int exitCode = 1;
        try
        {
            exitCode = await RunScenario();
        }
        catch (Exception e)
        {
            GD.PrintErr("[scenario] crashed: " + e);
        }
        GetTree().Quit(exitCode);
    }

    private async Task<int> RunScenario()
    {
        var scenario = ScenarioCatalog.Find(scenarioName);
        if (scenario == null)
        {
            GD.PrintErr($"[scenario] unknown scenario '{scenarioName}'; known: {string.Join(", ", ScenarioCatalog.All.Select(it => it.Name))}");
            return 1;
        }

        string scenarioOut = Path.Combine(outDir, scenario.Name);
        Directory.CreateDirectory(scenarioOut);
        foreach (string stale in Directory.GetFiles(scenarioOut)) File.Delete(stale);

        if (scenario.Viewport != DisplayServer.WindowGetSize())
        {
            GetWindow().Size = scenario.Viewport;
            await NextFrame();
            await NextFrame();
            if ((Vector2I)GetViewport().GetVisibleRect().Size != scenario.Viewport)
            {
                failures.Add($"the window could not be resized to {scenario.Viewport.X}x{scenario.Viewport.Y} (is {GetViewport().GetVisibleRect().Size})");
                return 1;
            }
        }
        GetViewport().TransparentBg = false;
        MouseFilter = MouseFilterEnum.Ignore; // keep mouse events flowing to scenarios that handle them in the 3D world
        if (!scenario.Uses3D) AddChild(new ColorRect { Color = new Color(0.12f, 0.14f, 0.2f), AnchorRight = 1, AnchorBottom = 1 });
        var session = ScenarioGame.Create(solo: scenario.Solo, godMode: scenario.GodMode, skirmish: scenario.Skirmish, diplomacy: scenario.Diplomacy, rich: scenario.Rich, policies: scenario.Policies, alerts: scenario.Alerts, victory: scenario.Victory, defeat: scenario.Defeat, trade: scenario.Trade, decisions: scenario.Decisions, crowd: scenario.Crowd, espionage: scenario.Espionage, proposals: scenario.Proposals, religion: scenario.Religion, voteResult: scenario.VoteResult, tutorialTasks: scenario.TutorialTasks);
        var script = scenario.Build(this, session);

        foreach (var step in script) await Execute(step, scenario.Name, scenarioOut);

        string stateHash = ScenarioGame.StateHash(session);
        var result = new ScenarioResult(scenario.Name, ScenarioGame.GameSeed, ScenarioGame.MapSeed, stateHash, session.Log, captures, scenario.Checklist, failures, scenario.Solo, scenario.GodMode, scenario.Skirmish, scenario.Diplomacy, scenario.Rich, scenario.Policies, scenario.Alerts, scenario.Victory, scenario.Defeat, scenario.Trade, scenario.Decisions, scenario.Crowd, scenario.Espionage, scenario.Proposals, scenario.Religion, scenario.VoteResult, scenario.TutorialTasks);
        string? replayFailure = ScenarioReplay.Verify(result);
        if (replayFailure != null) failures.Add(replayFailure);
        result = result with { Failures = failures.ToList() };
        File.WriteAllText(Path.Combine(scenarioOut, "result.json"), result.ToJson());

        GD.Print($"[scenario] {scenario.Name}: {session.Log.Count} command(s), state hash {stateHash}, headless replay {(replayFailure == null ? "equal" : "DIFFERS")}");
        foreach (string failure in failures) GD.PrintErr("[scenario] FAIL: " + failure);
        GD.Print(failures.Count == 0 ? "[scenario] PASS" : "[scenario] FAILED");
        return failures.Count == 0 ? 0 : 1;
    }

    private async Task Execute(ScenarioStep step, string scenario, string scenarioOut)
    {
        switch (step)
        {
            case Click click:
                InputSimulator.SimulateMouseClick(GetViewport(), click.At());
                await NextFrame();
                await SettleTurnProcessing();
                break;
            case PressKey key:
                InputSimulator.SimulateKey(GetViewport(), key.Code, key.Shift, key.Ctrl, key.Alt);
                await NextFrame();
                await SettleTurnProcessing();
                break;
            case Press press:
                var button = FindButton(this, press.ButtonText);
                if (button == null) failures.Add($"Press: no enabled visible button \"{press.ButtonText}\" (by text or ui_label)");
                else InputSimulator.SimulateMouseClick(GetViewport(), button.GetGlobalRect().GetCenter());
                await NextFrame();
                await SettleTurnProcessing();
                break;
            case WaitFrames wait:
                for (int i = 0; i < wait.Frames; i++) await NextFrame();
                break;
            case WaitUntil wait:
                await WaitUntilDone(wait);
                break;
            case Act act:
                act.Run();
                await NextFrame();
                break;
            case AssertQuery assert:
                if (!assert.Check()) failures.Add("AssertQuery: " + assert.What);
                break;
            case Capture capture:
                await CaptureStep(capture, scenario, scenarioOut);
                break;
            default:
                failures.Add("unknown step " + step);
                break;
        }
    }

    private async Task CaptureStep(Capture capture, string scenario, string scenarioOut)
    {
        await NextFrame();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string png = Path.Combine(scenarioOut, capture.Step + ".png");
        var image = ViewportCapture.CaptureViewportPng(GetViewport(), png);
        captures.Add(new ScenarioCapture(capture.Step, capture.Caption));
        string golden = Path.Combine(goldenDir, scenario, capture.Step + ".png");
        if (updateGolden)
        {
            Directory.CreateDirectory(Path.Combine(goldenDir, scenario));
            File.Copy(png, golden, overwrite: true);
            GD.Print("[scenario] golden updated: " + golden);
            return;
        }
        if (!File.Exists(golden))
        {
            failures.Add($"golden {golden} does not exist (run with --update-golden)");
            return;
        }
        var compared = GoldenComparer.Compare(image, ViewportCapture.LoadPng(golden), tolerance);
        GD.Print($"[scenario] {capture.Step}: {compared.Message} (hash {compared.HashDistance}, mean {compared.MeanAbsoluteDifference:F4}, px {compared.DifferingPixelFraction:P2})");
        if (!compared.Passed) failures.Add($"golden mismatch at {capture.Step}: {compared.Message}");
    }

    /// <summary>C040: the longest wall time between two consecutive frames during the last <see cref="WaitUntil"/> (milliseconds).</summary>
    public static double LongestFrameMs { get; private set; }

    /// <summary>C040: how many frames the last <see cref="WaitUntil"/> took.</summary>
    public static int FramesWaited { get; private set; }

    private async Task WaitUntilDone(WaitUntil wait)
    {
        LongestFrameMs = 0;
        FramesWaited = 0;
        var last = Time.GetTicksUsec();
        while (!wait.Done())
        {
            if (FramesWaited >= wait.MaxFrames)
            {
                failures.Add($"WaitUntil: never became true within {wait.MaxFrames} frames: {wait.What}");
                return;
            }
            await NextFrame();
            var now = Time.GetTicksUsec();
            LongestFrameMs = Math.Max(LongestFrameMs, (now - last) / 1000.0);
            last = now;
            FramesWaited++;
        }
        GD.Print($"[scenario] waited {FramesWaited} frame(s), longest frame {LongestFrameMs:F0} ms: {wait.What}");
    }

    /// <summary>
    /// C040: End turn is processed on a worker, so after an input the script waits until no game screen is still processing a turn (scripts written before it assume the turn is over when
    /// the input step returns). A scenario that tests the processing itself presses through an <see cref="Act"/>, which does not settle.
    /// </summary>
    private async Task SettleTurnProcessing()
    {
        for (int frames = 0; AnyEndingTurn(this); frames++)
        {
            if (frames >= 3000)
            {
                failures.Add("a turn was still being processed after 3000 frames");
                return;
            }
            await NextFrame();
        }
    }

    private static bool AnyEndingTurn(Node node)
    {
        if (node is GameScreen { IsEndingTurn: true }) return true;
        foreach (var child in node.GetChildren())
            if (AnyEndingTurn(child)) return true;
        return false;
    }

    private async Task NextFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    /// <summary>The metadata key (C061) a button carries to be pressable by label although it shows no text (an icon-only button).</summary>
    public const string LabelMeta = "ui_label";

    private static Button? FindButton(Node node, string text)
    {
        if (node is Button { Disabled: false } button && button.IsVisibleInTree()
            && (button.Text == text || (button.HasMeta(LabelMeta) && button.GetMeta(LabelMeta).AsString() == text))) return button;
        foreach (var child in node.GetChildren())
            if (FindButton(child, text) is { } found) return found;
        return null;
    }
}
