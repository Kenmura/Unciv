namespace Unciv.Commands.Empire;

/// <summary>
/// Vote in the World Congress. Kotlin: <c>DiplomaticVotePickerScreen</c> offers <c>getKnownCivsSorted(false)</c> and "Abstain" (null),
/// available while <c>mayVoteForDiplomaticVictory</c>; then <c>diplomaticVoteForCiv</c>.
/// </summary>
[CommandType("vote-diplomatic")]
public sealed class VoteDiplomatic : GameCommand
{
    /// <summary>The civ voted for, or null to abstain.</summary>
    public string? CivId { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!actor.MayVoteForDiplomaticVictory())
            return CommandValidation.Fail(CommandErrors.NotAllowed, "There is no vote to cast");
        if (CivId != null && !actor.DiplomacyFunctions.GetKnownCivsSorted(false).Any(it => it.CivId == CivId))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"{CivId} is not a civilization you can vote for");
        return CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Actor.DiplomaticVoteForCiv(CivId);
        return [];
    }
}
