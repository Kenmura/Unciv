using System.Text.RegularExpressions;

namespace Unciv.Tests.Harness;

// C058: a scenario's outcome must not depend on wall-clock time (a loaded machine renders frames slowly: hover delays elapse, animations
// advance, frame budgets blow). Source guard over the harness and the client: every real-time source is either frozen by the runner or listed here.
public sealed partial class HarnessDeterminismTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Unciv.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Unciv.sln not found");
    }

    private static IEnumerable<(string Name, string Text)> Sources(string project) =>
        Directory.GetFiles(Path.Combine(Root, project), "*.cs", SearchOption.AllDirectories)
            .Where(it => !it.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !it.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(it => (Path.GetFileName(it), File.ReadAllText(it)));

    // A scenario file may read the clock only where the ticket that owns the measurement says so (frame-time reporting, never a golden or an unbudgeted assertion).
    private static readonly Dictionary<string, string> AllowedClockUsers = new(StringComparer.Ordinal)
    {
        ["ScenarioRunner.cs"] = "reports the wall time between frames of a WaitUntil (C040); asserted only by next-turn with a generous budget",
        ["TerrainBlendScenario.cs"] = "frame-time budget of the Standard-size map (C040 follow-up), best of three batches",
    };

    // The client reads the wall clock exactly twice, to seed a brand-new game at launch (a one-off, not frame-driven; scenarios build their own seeded sessions).
    private static readonly HashSet<string> AllowedClientClock = new(StringComparer.Ordinal) { "App.cs", "QuickStart.cs", "GameScreen.cs" }; // GameScreen: the turn bar countdown reads GameScreen.NowMs (C036), which every multiplayer scenario replaces with a fixed time

    // C080: these files only find a model's AnimationPlayer and read it while it is held paused in its idle pose (nothing plays until the presenter drives clip time, UNIT-MODELS.md section 2);
    // the word AnimationPlayer is ignored there, every other clock/timer API still fails the test.
    private static readonly HashSet<string> PausedAnimationUsers = new(StringComparer.Ordinal) { "UnitModels.cs", "UnitNode.cs", "UnitVisualsScenario.cs" };

    private static bool UsesClock(string name, string text) => ClockApi().IsMatch(PausedAnimationUsers.Contains(name) ? text.Replace("AnimationPlayer", "", StringComparison.Ordinal) : text);

    [GeneratedRegex(@"GetTicks(Msec|Usec)|OS\.GetTicks|GetUnixTime|GetDatetime|OS\.DelayM?Usec|OS\.DelayMsec|DateTime(Offset)?\.(Utc)?(Now|Today)|TimeProvider|Environment\.TickCount|Stopwatch|CreateTween|CreateTimer|new\s+([A-Za-z]+\.)*(Periodic)?Timer\b|Task\.Delay|Thread\.Sleep|AnimationPlayer|AnimationTree|\bTween\b|Engine\.TimeScale", RegexOptions.None)]
    private static partial Regex ClockApi();

    // Any per-frame callback (renamed parameter or physics tick included) is a real-time consumer that must be accounted for below.
    [GeneratedRegex(@"override\s+void\s+_(Physics)?Process\s*\(", RegexOptions.None)]
    private static partial Regex ProcessOverride();

    [Fact]
    public void ScenariosDoNotReadTheWallClockOrStartTimers()
    {
        var offenders = Sources("Unciv.Godot.Harness")
            .Where(it => UsesClock(it.Name, it.Text) && !AllowedClockUsers.ContainsKey(it.Name))
            .Select(it => it.Name)
            .ToList();
        Assert.True(offenders.Count == 0, "wall-clock/timer APIs in the harness (use WaitFrames/explicit stepping, or add the file to AllowedClockUsers with a reason): " + string.Join(", ", offenders));
    }

    [Fact]
    public void ClientRealTimeNodesAreOnlyTheOnesTheRunnerFreezes()
    {
        // The client's own use of real time: frame deltas in _Process. Each must have a switch the runner turns (or be frame-counted/idle in scenarios).
        var clientClock = Sources("Unciv.Godot")
            .Where(it => UsesClock(it.Name, it.Text) && !AllowedClientClock.Contains(it.Name))
            .Select(it => it.Name)
            .OrderBy(it => it, StringComparer.Ordinal)
            .ToList();
        Assert.True(clientClock.Count == 0, "new wall-clock/timer use in the client; freeze it in ScenarioRunner.FreezeRealTime and extend this test: " + string.Join(", ", clientClock));

        var processUsers = Sources("Unciv.Godot")
            .Where(it => ProcessOverride().IsMatch(it.Text))
            .Select(it => Path.GetFileNameWithoutExtension(it.Name))
            .OrderBy(it => it, StringComparer.Ordinal)
            .ToList();
        // TurnBarView counts toast frames (no time); GameScreen redraws the turn bar when the countdown text changes (reads NowMs, fixed in scenarios); LobbyScreen drains the session events and rebuilds on change (no time); HostScreen refreshes the lobby slot table when it changed (no time); HudView/MinimapView count frames or redraw on change (no time); TooltipHost, MapView, CombatPresenter use the delta and are frozen/off in the harness
        Assert.Equal(["CombatPresenter", "GameScreen", "HostScreen", "HudView", "LobbyScreen", "MapView", "MinimapView", "TooltipHost", "TurnBarView"], processUsers);
    }

    [Fact]
    public void RunnerFreezesTooltipDelayAndPanning()
    {
        string runner = File.ReadAllText(Path.Combine(Root, "Unciv.Godot.Harness", "ScenarioRunner.cs"));
        Assert.Contains("NodeAdded += FreezeRealTime", runner, StringComparison.Ordinal);
        Assert.Contains("host.Delay = UnreachableDelay", runner, StringComparison.Ordinal);
        Assert.Contains("map.RealTimePanning = false", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeControlTooltipsNeverAppearInTheHarnessProject()
    {
        string project = File.ReadAllText(Path.Combine(Root, "Unciv.Godot.Harness", "project.godot"));
        var match = Regex.Match(project, @"^\[gui\]\s*$(?:(?!^\[).)*?^timers/tooltip_delay_sec=([0-9.]+)\s*$", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(match.Success, "project.godot must set timers/tooltip_delay_sec in its [gui] section");
        Assert.True(double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) >= 3600, "native tooltip delay must be unreachable");
    }

    [Fact]
    public void ScenariosThatPlayCombatAnimationStepItByHand()
    {
        foreach (var (name, text) in Sources("Unciv.Godot.Harness"))
            if (text.Contains("Presenter.Enabled = true", StringComparison.Ordinal))
                Assert.True(text.Contains("ManualStepping = true", StringComparison.Ordinal), $"{name} enables the combat presenter but does not step it by hand (real frame deltas)");
    }
}
