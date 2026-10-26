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

    /// <summary>C020: Babylon has a capital and <see cref="Unciv.Harness.ScenarioGame.PolicyCulture"/> stored culture, so policies can be adopted.</summary>
    public virtual bool Policies => false;
    /// <summary>The scenario plays the trade game (<see cref="Unciv.Harness.ScenarioGame.Create"/> with trade): the diplomacy game plus a trade offer and a demand from Rome, trade hints on.</summary>
    public virtual bool Trade => false;

    /// <summary>C013: the diplomacy game plus a founded capital, four notifications and four pending alerts (see <see cref="Unciv.Harness.ScenarioGame.Create"/>).</summary>
    public virtual bool Alerts => false;

    /// <summary>C024: Rome is destroyed and Babylon has a capital, so ending the turn wins a Domination victory (see <see cref="Unciv.Harness.ScenarioGame.Create"/>).</summary>
    public virtual bool Victory => false;

    /// <summary>C024: Babylon has no units and no city while Rome plays on, so the human civ is defeated and the game is not over.</summary>
    public virtual bool Defeat => false;

    /// <summary>C038: the solo game with a capital and every blocking picker pending (free great person, free technology, pantheon) and a warrior ready for a promotion (see <see cref="Unciv.Harness.ScenarioGame.Create"/>).</summary>
    public virtual bool Decisions => false;

    /// <summary>C040: the solo game with <see cref="Unciv.Harness.ScenarioGame.CrowdAiCount"/> AI civs on a larger map, so one End turn runs many automatic civs.</summary>
    public virtual bool Crowd => false;

    /// <summary>C050: the diplomacy game with espionage on, both capitals and the city-state's city explored, an idle spy in the hideout and a set-up spy in the city-state (see <see cref="Unciv.Harness.ScenarioGame.Create"/>).</summary>
    public virtual bool Espionage => false;

    public abstract IReadOnlyList<ScenarioStep> Build(Control host, GameSession session);
}
