using Unciv.Queries.Tutorial;

namespace Unciv.Commands.Turn;

/// <summary>
/// C054: the client reports that the player did what a tutorial task asked (Kotlin <c>settings.addCompletedTutorialTask</c> from the UI). Only changes
/// <c>GameClientState.TutorialTasksCompleted</c>, which is not part of the saved game or the state hash; the command exists so every peer holding the game
/// records it. Only task names some ruleset Event refers to are accepted.
/// </summary>
[CommandType("complete-tutorial-task")]
public sealed class CompleteTutorialTask : GameCommand
{
    public string Task { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequirePlayer().TryGet(out _, out var failure)) return failure;
        return TutorialTasks.IsKnownTask(context.Game.Ruleset, Task)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "Unknown tutorial task");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Game.ClientState.AddCompletedTutorialTask(Task);
        return [];
    }
}
