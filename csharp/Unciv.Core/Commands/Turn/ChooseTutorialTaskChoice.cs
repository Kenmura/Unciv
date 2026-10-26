using Unciv.Models.Ruleset.Unique;
using Unciv.Queries.Tutorial;
using Unciv.Utils;

namespace Unciv.Commands.Turn;

/// <summary>
/// C054: the player pressed a button of the floating tutorial task card (Kotlin <c>RenderEvent.addChoice</c> for the Floating Event of
/// <c>TutorialTaskTable</c>). Triggers the choice's uniques for the actor. C060: accepted for any Floating Event's offered choice (<c>Choice</c> indexes the Event's own choices), judged without the
/// un-hashed completed-task set, so a host, a log-tail replayer and a loaded save all decide alike; a forged request cannot fire a non-Floating Event.
/// </summary>
[CommandType("choose-tutorial-task-choice")]
public sealed class ChooseTutorialTaskChoice : GameCommand
{
    public string Event { get; init; } = "";
    public int Choice { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequirePlayer().TryGet(out var actor, out var failure)) return failure;
        return TutorialTasks.ChoiceFor(actor, Event, Choice) != null
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "No such tutorial task choice");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        var choice = TutorialTasks.ChoiceFor(actor, Event, Choice).NotNull();
        using var scope = Conditionals.UseSettings(TutorialTasks.NeutralSettings);
        choice.TriggerChoice(actor);
        return [];
    }
}
