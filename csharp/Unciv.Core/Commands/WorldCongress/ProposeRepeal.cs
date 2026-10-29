using Unciv.Logic.WorldCongress;

namespace Unciv.Commands.WorldCongress;

/// <summary>Propose repealing a resolution in force (bnw-spec/SPEC.md section 4.7.4 step 1); uses a proposal like <see cref="ProposeResolution"/>.</summary>
[CommandType("propose-repeal")]
public sealed class ProposeRepeal : GameCommand
{
    /// <summary>The id of the active resolution (from <c>LeagueQuery</c>).</summary>
    public int ActiveId { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        return LeagueRules.RepealProblem(context.Game, actor, ActiveId) is { } problem
            ? CommandValidation.Fail(CommandErrors.NotAllowed, problem)
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        LeagueRules.ProposeRepeal(context.Game, context.Actor, ActiveId);
        return [];
    }
}
