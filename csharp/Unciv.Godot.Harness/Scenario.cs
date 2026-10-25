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

    /// <summary>A scenario that draws a 3D world: the runner then omits its opaque 2D backdrop, which would cover the 3D layer.</summary>
    public virtual bool Uses3D => false;

    /// <summary>The scenario plays the single-human game (<see cref="Unciv.Harness.ScenarioGame.Create"/> with solo) instead of the two-human one.</summary>
    public virtual bool Solo => false;

    /// <summary>The game is created with God mode on (debug commands allowed); recorded in the result so the headless replay creates it the same way.</summary>
    public virtual bool GodMode => false;
    /// <summary>The scenario plays the skirmish game (<see cref="Unciv.Harness.ScenarioGame.Create"/> with skirmish): the solo game plus an adjacent enemy warrior at war and a worker.</summary>
    public virtual bool Skirmish => false;


    /// <summary>The scenario plays the diplomacy game (<see cref="Unciv.Harness.ScenarioGame.Create"/> with diplomacy): the solo game plus a city-state and an unmet second AI civ.</summary>
    public virtual bool Diplomacy => false;

    /// <summary>Every human civ starts with <see cref="Unciv.Harness.ScenarioGame.RichGold"/> gold (C018: real purchases without God mode).</summary>
    public virtual bool Rich => false;

    public abstract IReadOnlyList<ScenarioStep> Build(Control host, GameSession session);
}
