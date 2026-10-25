using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Utils;

namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Offer a declaration of friendship. Kotlin: <c>MajorCivDiplomacyTable.getDeclareFriendshipButton</c>, shown while not at war and without a
/// declaration in force, disabled while the same offer is still pending: it only adds a <c>DeclarationOfFriendship</c> popup alert to the other
/// civ. An AI answers in its own turn (<c>NextTurnAutomation.respondToPopupAlerts</c>), a human with <see cref="RespondToFriendship"/>.
/// </summary>
[CommandType("offer-friendship")]
public sealed class OfferFriendship : GameCommand
{
    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.Major, out var actor, out var other, out var failure)) return failure;
        if (actor.IsAtWarWith(other))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"You are at war with {CivId}");
        if (actor.GetDiplomacyManager(other).NotNull().HasFlag(DiplomacyFlags.DeclarationOfFriendship))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"You already have a declaration of friendship with {CivId}");
        return DiplomacyCommandChecks.FindAlert(other, AlertType.DeclarationOfFriendship, actor.CivId) != null
            ? CommandValidation.Fail(CommandErrors.NotAllowed, $"{CivId} has not answered your offer yet")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.ValidatedOther(CivId).AddPopupAlert(new PopupAlert(AlertType.DeclarationOfFriendship, context.ActorId));
        return [];
    }
}
