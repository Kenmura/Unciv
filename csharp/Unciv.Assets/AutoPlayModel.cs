using System.Globalization;
using Unciv.Commands.Turn;
using Unciv.GodotClient.Settings;

namespace Unciv.Assets;

public enum AutoPlayKind
{
    /// <summary>The multi-turn AutoPlay with the stored settings (Kotlin "Start AutoPlay").</summary>
    Start,

    /// <summary>AutoPlay exactly this one turn (Kotlin "AutoPlay End Turn").</summary>
    EndTurn,
}

/// <summary><see cref="Id"/> is stable (node names, tests); <see cref="Detail"/> says how long it plays and is shown beside the label.</summary>
public sealed record AutoPlayEntry(string Id, AutoPlayKind Kind, string Label, string Detail, bool Enabled, string? DisabledReason);

/// <summary>
/// C047: what the client knows of a running AutoPlay: how many turns are left (or until the game ends) and how many were played. Immutable; the game screen steps it
/// after every turn the engine accepted. It never decides whether the engine still auto-plays: a refused AutoPlayTurn ends the run.
/// </summary>
public sealed record AutoPlayRun(int TurnsLeft, bool UntilEnd, int TurnsPlayed)
{
    /// <summary>More turns to play.</summary>
    public bool Active => UntilEnd || TurnsLeft > 0;

    public AutoPlayRun AfterTurn() => this with { TurnsLeft = UntilEnd ? TurnsLeft : Math.Max(0, TurnsLeft - 1), TurnsPlayed = TurnsPlayed + 1 };
}

/// <summary>T376: how much of the map's animation a running AutoPlay plays.</summary>
public enum AutoPlayAnimations
{
    /// <summary>None: units jump to their new tiles, no combat effects or sounds. The default.</summary>
    Skip,

    /// <summary>Played several times faster, and whatever is left is dropped when the turn's time is up.</summary>
    Fast,

    /// <summary>At normal speed; the next turn waits for them (up to a limit).</summary>
    Normal,
}

/// <summary>
/// C047: the AutoPlay button and its menu as plain data, Godot-free so it is unit-tested. It builds the engine's <see cref="StartAutoPlay"/> from the stored
/// <see cref="AutoPlaySettings"/> and words the button; the engine owns every rule (who may auto-play, when it stops).
/// Deliberate deviation from Kotlin: the "AutoPlay Military/Civilians/Economy Once" entries are not offered (they call automation pieces no command exposes).
/// </summary>
public static class AutoPlayModel
{
    public const string ButtonLabel = "AutoPlay";
    public const string StartLabel = "Start AutoPlay";
    public const string EndTurnLabel = "AutoPlay End Turn";
    public const string MenuTitle = "AutoPlay";
    public const string NotYourTurn = "You cannot AutoPlay right now";

    /// <summary>T376: playing speed of <see cref="AutoPlayAnimations.Fast"/> (normal speed is 1).</summary>
    public const float FastSpeed = 6f;

    /// <summary>T376: frames (at 60 per second, 0.6 s) a fast run gives one turn's animations before it drops what is left.</summary>
    public const int FastWaitFrames = 36;

    /// <summary>T376: frames (30 s) the longest turn's animations may hold the next turn at normal speed; one turn's script is never that long, this only guards a stuck presenter.</summary>
    public const int NormalWaitFrames = 1800;

    /// <summary>T376: "skip", "fast" or "normal" in any case; anything else is the default, skip.</summary>
    public static AutoPlayAnimations ParseAnimations(string? text) =>
        Enum.TryParse<AutoPlayAnimations>(text, true, out var mode) && Enum.IsDefined(mode) ? mode : AutoPlayAnimations.Skip;

    /// <summary>T376: whether the map animates a run's moves and fights at all.</summary>
    public static bool Animates(AutoPlayAnimations mode) => mode != AutoPlayAnimations.Skip;

    /// <summary>T376: the speed multiplier while a run plays in <paramref name="mode"/>.</summary>
    public static float Speed(AutoPlayAnimations mode) => mode == AutoPlayAnimations.Fast ? FastSpeed : 1f;

    /// <summary>T376: how many frames one turn's animations may take before the next turn starts (and the rest is dropped); 0 when nothing is animated.</summary>
    public static int WaitFrames(AutoPlayAnimations mode) => mode switch
    {
        AutoPlayAnimations.Fast => FastWaitFrames,
        AutoPlayAnimations.Normal => NormalWaitFrames,
        _ => 0,
    };

    public static IReadOnlyList<AutoPlayEntry> Entries(AutoPlaySettings settings, bool canStart)
    {
        string? reason = canStart ? null : NotYourTurn;
        return
        [
            new("AutoPlayStart", AutoPlayKind.Start, StartLabel, settings.UntilEnd ? "until the game ends" : TurnsText(settings.MaxTurns), canStart, reason),
            new("AutoPlayEndTurn", AutoPlayKind.EndTurn, EndTurnLabel, TurnsText(1), canStart, reason),
        ];
    }

    private static string TurnsText(int turns) => turns.ToString(CultureInfo.InvariantCulture) + (turns == 1 ? " turn" : " turns");

    /// <summary>The button's text: "AutoPlay" while idle, "Stop AutoPlay (4 left)" (or without the count when it plays until the end) while running.</summary>
    public static string ButtonText(AutoPlayRun? run) => run switch
    {
        null => ButtonLabel,
        { UntilEnd: true } => "Stop AutoPlay",
        _ => "Stop AutoPlay (" + run.TurnsLeft.ToString(CultureInfo.InvariantCulture) + " left)",
    };

    /// <summary>The engine command that starts the run: the stored number of turns (or until the end), or one turn for "AutoPlay End Turn".</summary>
    public static StartAutoPlay StartCommand(AutoPlaySettings settings, AutoPlayKind kind) => kind == AutoPlayKind.EndTurn
        ? new StartAutoPlay { Turns = 1, UntilEnd = false, FullAutoPlayAI = settings.FullAutoPlayAI }
        : new StartAutoPlay { Turns = settings.MaxTurns, UntilEnd = settings.UntilEnd, FullAutoPlayAI = settings.FullAutoPlayAI };

    /// <summary>The client's run matching <see cref="StartCommand"/>.</summary>
    public static AutoPlayRun StartRun(AutoPlaySettings settings, AutoPlayKind kind) => kind == AutoPlayKind.EndTurn
        ? new AutoPlayRun(1, false, 0)
        : new AutoPlayRun(settings.MaxTurns, settings.UntilEnd, 0);

    /// <summary>Whether the menu may start a run: not while the turn is already ended or being resolved, and not after the game is over for the viewer.</summary>
    public static bool CanStart(HudActionKind action, bool gameOverShown) =>
        !gameOverShown && action is not (HudActionKind.UndoEndTurn or HudActionKind.Resolving);

    /// <summary>The status line when a run ends: <paramref name="refusal"/> is the engine's reason when it refused the next turn, empty when the run simply played out.</summary>
    public static string FinishedText(AutoPlayRun run, bool stoppedByPlayer, string refusal) =>
        (stoppedByPlayer ? "AutoPlay stopped after " : "AutoPlay finished after ") + run.TurnsPlayed.ToString(CultureInfo.InvariantCulture) + (run.TurnsPlayed == 1 ? " turn" : " turns")
        + (refusal.Length > 0 ? ": " + refusal : ".");
}
