using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Utils;

namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Denounce a major civilization. Kotlin: <c>MajorCivDiplomacyTable.getDenounceButton</c>, shown while not at war and when neither a
/// denunciation nor a declaration of friendship is in force, then <c>diplomacyManager.denounce()</c>.
/// </summary>
[CommandType("denounce")]
public sealed class Denounce : GameCommand
{
    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.Major, out var actor, out var other, out var failure)) return failure;
        if (actor.IsAtWarWith(other))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"You are at war with {CivId}");
        var diplomacy = actor.GetDiplomacyManager(other).NotNull();
        return diplomacy.HasFlag(DiplomacyFlags.Denunciation) || diplomacy.HasFlag(DiplomacyFlags.DeclarationOfFriendship)
            ? CommandValidation.Fail(CommandErrors.NotAllowed, $"You cannot denounce {CivId} now")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Actor.GetDiplomacyManager(context.ValidatedOther(CivId)).NotNull().Denounce();
        return [];
    }
}
