namespace Unciv.Commands.Empire;

/// <summary>
/// Pick the free technology. Kotlin: <c>TechPickerScreen.tryExit</c> in free-tech mode (<c>freeTechs != 0</c>): the tech must still be
/// researchable (the "fast-clicking cheat" guard, #4977), then <c>getFreeTechnology</c> and <c>updateResearchProgress</c>.
/// </summary>
[CommandType("pick-free-technology")]
public sealed class PickFreeTechnology : GameCommand
{
    public string Tech { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (actor.Tech.FreeTechs == 0)
            return CommandValidation.Fail(CommandErrors.NotAllowed, "There is no free technology to pick");
        if (!actor.GameInfo.Ruleset.Technologies.ContainsKey(Tech))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no technology \"{Tech}\"");
        return actor.Tech.CanBeResearched(Tech)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"{Tech} cannot be researched now");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var tech = context.Actor.Tech;
        tech.GetFreeTechnology(Tech);
        tech.UpdateResearchProgress();
        return [];
    }
}
