using Godot;

namespace Unciv.GodotHarness;

/// <summary>One line of a scenario script (CLIENT-SLICES.md, "Verification tooling"). Steps run in order, one input per frame.</summary>
public abstract record ScenarioStep;

/// <summary>Left click at a screen position, resolved when the step runs (so it can depend on what the UI built).</summary>
public sealed record Click(Func<Vector2> At) : ScenarioStep
{
    public Click(Vector2 at) : this(() => at) { }
}

/// <summary>Key press and release through the viewport.</summary>
public sealed record PressKey(Key Code, bool Shift = false, bool Ctrl = false, bool Alt = false) : ScenarioStep;

/// <summary>Click the centre of the enabled, visible button whose text, or <c>ui_label</c> metadata (C061, icon-only buttons), is <paramref name="ButtonText"/> (fails the scenario if there is none).</summary>
public sealed record Press(string ButtonText) : ScenarioStep;

/// <summary>Let <paramref name="Frames"/> frames pass (animations, deferred layout).</summary>
public sealed record WaitFrames(int Frames) : ScenarioStep;

/// <summary>
/// C040: let frames pass until <paramref name="Done"/> is true (fails the scenario after <paramref name="MaxFrames"/>). While it waits the runner measures the
/// wall time between frames: <c>ScenarioRunner.LongestFrameMs</c> / <c>FramesWaited</c> describe the last wait, so a scenario can assert the window stayed responsive.
/// </summary>
public sealed record WaitUntil(string What, Func<bool> Done, int MaxFrames = 3000) : ScenarioStep;

/// <summary>
/// A state assertion from the engine's query API (unit position, turn number, queue contents), not from pixels. Scenarios assert
/// state before they capture, so a wrong state is reported as such rather than as a picture mismatch.
/// </summary>
public sealed record AssertQuery(string What, Func<bool> Check) : ScenarioStep;

/// <summary>Capture the viewport as <c>out/&lt;scenario&gt;/&lt;Step&gt;.png</c>, compare it with <c>golden/&lt;scenario&gt;/&lt;Step&gt;.png</c>; the caption goes on the review sheet.</summary>
public sealed record Capture(string Step, string Caption) : ScenarioStep;

/// <summary>
/// Run view-side code (camera, simulated wheel or drag) that does not touch the game: state changes still go through <c>GameSession.Submit</c>,
/// so the command log stays complete.
/// </summary>
public sealed record Act(string What, Action Run) : ScenarioStep;
