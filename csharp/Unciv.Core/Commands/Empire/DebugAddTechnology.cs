namespace Unciv.Commands.Empire;

/// <summary>
/// GodMode debug command. Kotlin: <c>TechPickerScreen.selectTechnology</c> calls <c>tech.addTechnology</c> when
/// <c>gameParameters.godMode</c> and the tech is not researched yet. This is the only godMode UI path that mutates state besides the
/// "God mode" checkbox of the debug options (the switch itself, a game setup option, is not a command).
/// </summary>
[CommandType("debug-add-technology")]
public sealed class DebugAddTechnology : GameCommand
{
    public string Tech { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!actor.GameInfo.GameParameters.GodMode)
            return CommandValidation.Fail(CommandErrors.NotAllowed, "God mode is off");
        if (actor.Tech.FreeTechs != 0)
            return CommandValidation.Fail(CommandErrors.NotAllowed, "A free technology must be picked first"); // the picker is in free-tech mode
        if (!actor.GameInfo.Ruleset.Technologies.ContainsKey(Tech))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no technology \"{Tech}\"");
        return actor.Tech.IsResearched(Tech)
            ? CommandValidation.Fail(CommandErrors.NotAllowed, $"{Tech} is already researched")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Actor.Tech.AddTechnology(Tech);
        return [];
    }
}
