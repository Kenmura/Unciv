using Unciv.Queries.Tutorial;
using Unciv.Utils;

namespace Unciv.Commands.Turn;

/// <summary>
/// C054: the player pressed a button of the floating tutorial task card (Kotlin <c>RenderEvent.addChoice</c> for the Floating Event of
/// <c>TutorialTaskTable</c>). Triggers the choice's uniques for the actor. Accepted only for the Event that is the actor's current task, so a stale or forged
/// request cannot fire an arbitrary Event.
/// </summary>
[CommandType("choose-tutorial-task-choice")]
public sealed class ChooseTutorialTaskChoice : GameCommand
{
    public string Event { get; init; } = "";
    public int Choice { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequirePlayer().TryGet(out var actor, out var failure)) return failure;
        var current = TutorialTasks.Current(actor, showTutorials: true);
        if (current == null || current.Event.Name != Event) return CommandValidation.Fail(CommandErrors.StaleReference, "That is not the current tutorial task");
        return Choice >= 0 && Choice < current.Choices.Count
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "No such choice");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        TutorialTasks.Current(actor, showTutorials: true).NotNull().Choices[Choice].TriggerChoice(actor);
        return [];
    }
}
