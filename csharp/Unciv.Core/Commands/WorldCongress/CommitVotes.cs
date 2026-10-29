using Unciv.Logic.WorldCongress;
using Unciv.Utils;

namespace Unciv.Commands.WorldCongress;

/// <summary>
/// Put delegates on a choice of a proposal of the open World Congress session (bnw-spec/SPEC.md section 4.7.4 step 3). Repeatable until the
/// delegates run out; <see cref="Choice"/> is "yes" or "no", or the id of a member civ for a vote on a civ. Delegates left unspent are abstentions.
/// </summary>
[CommandType("commit-votes")]
public sealed class CommitVotes : GameCommand
{
    public int ProposalId { get; init; }

    public string Choice { get; init; } = "";

    public int Count { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        return LeagueRules.VoteProblem(context.Game, actor, ProposalId, Choice, Count) is { } problem
            ? CommandValidation.Fail(CommandErrors.NotAllowed, problem)
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        LeagueRules.CommitVotes(context.Game.League.NotNull(), context.Actor.CivId, ProposalId, Choice, Count);
        return [];
    }
}
