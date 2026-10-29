using Unciv.Logic.WorldCongress;

namespace Unciv.Commands.WorldCongress;

/// <summary>
/// Propose enacting a resolution in the World Congress (bnw-spec/SPEC.md section 4.7.4 step 1). Only the host and the member with the most delegates
/// can propose, once between two sessions, while no session is open. <see cref="Choice"/> is the proposer's decision (another civ, a luxury, a religion
/// or an ideology) for the resolutions that need one.
/// </summary>
[CommandType("propose-resolution")]
public sealed class ProposeResolution : GameCommand
{
    public string Resolution { get; init; } = "";

    public string Choice { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        return LeagueRules.EnactProblem(context.Game, actor, Resolution, Choice) is { } problem
            ? CommandValidation.Fail(CommandErrors.NotAllowed, problem)
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        LeagueRules.Propose(context.Game, context.Actor, Resolution, Choice);
        return [];
    }
}
