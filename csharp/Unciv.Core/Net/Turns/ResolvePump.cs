using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Logic;

namespace Unciv.Net.Turns;

/// <summary>
/// Loops <see cref="GameInfo.NextResolveStep"/> and submits matching server commands to resolve the simultaneous round (SERVER.md 4).
/// </summary>
public sealed class ResolvePump
{
    private readonly GameSession session;

    public ResolvePump(GameSession session)
    {
        this.session = session;
    }

    /// <summary>Maps a <see cref="ResolveStep"/> to its matching server <see cref="GameCommand"/>.</summary>
    public static GameCommand StepCommand(ResolveStep step) => step switch
    {
        ResolveStep.ProcessEndTurn s => new ProcessEndTurn { CivId = s.CivId },
        ResolveStep.RunAiTurn s => new RunAiTurn { CivId = s.CivId },
        ResolveStep.StartNextTurn => new StartNextTurn(),
        ResolveStep.BeginTurn s => new BeginTurn { CivId = s.CivId },
        _ => throw new InvalidOperationException($"Unknown resolve step: {step}"),
    };

    /// <summary>
    /// Executes the next step of resolution if one is available. Returns the command result, or null if no step was ready.
    /// </summary>
    public Unciv.Commands.CommandResult? Step() => Step(session);

    /// <summary>
    /// Loops through all steps of <see cref="GameInfo.NextResolveStep"/> until the round finishes resolving.
    /// </summary>
    public IReadOnlyList<Unciv.Commands.CommandResult> Pump() => Pump(session);

    /// <summary>
    /// Executes the next step of resolution on <paramref name="session"/> if one is available.
    /// </summary>
    public static Unciv.Commands.CommandResult? Step(GameSession session)
    {
        var step = session.Game.NextResolveStep();
        if (step == null) return null;
        return session.SubmitAsServer(StepCommand(step));
    }

    /// <summary>
    /// Loops through all steps of <see cref="GameInfo.NextResolveStep"/> on <paramref name="session"/> until the round finishes resolving.
    /// </summary>
    public static IReadOnlyList<Unciv.Commands.CommandResult> Pump(GameSession session)
    {
        var results = new List<Unciv.Commands.CommandResult>();
        while (session.Game.NextResolveStep() is { } step)
        {
            var result = session.SubmitAsServer(StepCommand(step));
            results.Add(result);
            if (!result.Accepted)
            {
                throw new InvalidOperationException($"Resolve step failed ({result.Validation.Code}): {result.Validation.Message}");
            }
        }
        return results;
    }
}
