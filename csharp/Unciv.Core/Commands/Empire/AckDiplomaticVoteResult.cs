using Unciv.Logic.Civilization;

namespace Unciv.Commands.Empire;

/// <summary>
/// The player has seen the World Congress vote result. Kotlin: <c>DiplomaticVoteResultScreen</c>'s Continue button runs
/// <c>viewingCiv.addFlag(CivFlags.ShowDiplomaticVotingResults.name, -1)</c>, so <c>shouldShowDiplomaticVotingResults</c> is false from then on
/// (the turn manager removes the flag when the votes are reset). Part of the command log, so every replay and every peer agrees.
/// </summary>
[CommandType("ack-diplomatic-vote-result")]
public sealed class AckDiplomaticVoteResult : GameCommand
{
    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        return actor.ShouldShowDiplomaticVotingResults()
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.StaleReference, "There is no vote result to acknowledge");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Actor.AddFlag(nameof(CivFlags.ShowDiplomaticVotingResults), -1);
        return [];
    }
}
