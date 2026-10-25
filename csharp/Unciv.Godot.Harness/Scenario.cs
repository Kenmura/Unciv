using Godot;
using Unciv.Commands;

namespace Unciv.GodotHarness;

/// <summary>
/// A client scenario: builds the UI under test on the runner node, given the seeded session, and returns its script.
/// Every state change must go through <see cref="GameSession.Submit(string, GameCommand)"/>, which is what makes the command log
/// (and so the headless replay) complete.
/// </summary>
public abstract class Scenario
{
    public abstract string Name { get; }

    /// <summary>Questions for the owner on the review sheet; empty means the general checklist.</summary>
    public virtual IReadOnlyList<string> Checklist => [];

    public abstract IReadOnlyList<ScenarioStep> Build(Control host, GameSession session);
}
