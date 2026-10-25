namespace Unciv.Commands.Empire;

/// <summary>
/// Replace the research queue. Kotlin: <c>TechPickerScreen</c> edits a temporary copy of <c>techsToResearch</c> (each queued tech
/// comes with its missing prerequisites, in prerequisite order) and commits it on exit, then calls <c>updateResearchProgress</c>.
/// The screen is in free-tech mode instead while <c>freeTechs != 0</c>; that mode never edits the queue.
/// </summary>
[CommandType("set-research-queue")]
public sealed class SetResearchQueue : GameCommand
{
    public List<string> Techs { get; init; } = [];

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        var tech = actor.Tech;
        if (tech.FreeTechs != 0)
            return CommandValidation.Fail(CommandErrors.NotAllowed, "A free technology must be picked first");
        if (Techs.Count == 0)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, "The research queue cannot be empty");
        var ruleset = actor.GameInfo.Ruleset;
        var queued = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in Techs)
        {
            if (!ruleset.Technologies.TryGetValue(name, out var technology))
                return CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no technology \"{name}\"");
            if (!queued.Add(name))
                return CommandValidation.Fail(CommandErrors.InvalidTarget, $"{name} is queued twice");
            if (tech.IsUnresearchable(technology))
                return CommandValidation.Fail(CommandErrors.NotAllowed, $"{name} cannot be researched");
            if (tech.IsResearched(name) && !technology.IsContinuallyResearchable())
                return CommandValidation.Fail(CommandErrors.NotAllowed, $"{name} is already researched");
            // Prerequisites must be researched or queued before this tech (the screen queues the path in that order).
            foreach (var prerequisite in technology.Prerequisites)
                if (!tech.IsResearched(prerequisite) && !queued.Contains(prerequisite))
                    return CommandValidation.Fail(CommandErrors.NotAllowed, $"{name} needs {prerequisite} first");
        }
        return CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var tech = context.Actor.Tech;
        tech.TechsToResearch = [.. Techs];
        tech.UpdateResearchProgress();
        return [];
    }
}
