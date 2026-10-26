using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Utils;

namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Answer a declaration of friendship another civ offered (the human side of <see cref="OfferFriendship"/>). Kotlin:
/// <c>AlertPopup.addDeclarationOfFriendship</c>, which is not shown (the alert is only dropped, as here) when the offerer is defeated or at war with us;
/// accepting runs <c>signDeclarationOfFriendship()</c>, declining sets the offerer's <c>DeclinedDeclarationOfFriendship</c> flag to 20 turns.
/// </summary>
[CommandType("respond-friendship")]
public sealed class RespondToFriendship : GameCommand
{
    /// <summary>The civ that made the offer.</summary>
    public string CivId { get; init; } = "";

    public bool Accept { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive(allowEnded: true).TryGet(out var actor, out var failure)) return failure;
        if (DiplomacyCommandChecks.FindAlert(actor, AlertType.DeclarationOfFriendship, CivId) == null)
            return DiplomacyCommandChecks.NoSuchAlert("declaration of friendship offer from " + CivId);
        return CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        DiplomacyCommandChecks.RemoveAlert(actor, DiplomacyCommandChecks.FindAlert(actor, AlertType.DeclarationOfFriendship, CivId).NotNull());
        var offerer = context.ValidatedOther(CivId);
        var diplomacy = actor.GetDiplomacyManager(offerer).NotNull();
        // A stale offer (offerer defeated or at war with us) is never shown by the Kotlin popup: the alert is only dropped.
        if (offerer.IsDefeated() || diplomacy.DiplomaticStatus == DiplomaticStatus.War) return [];
        if (Accept) diplomacy.SignDeclarationOfFriendship();
        else diplomacy.OtherCivDiplomacy().SetFlag(DiplomacyFlags.DeclinedDeclarationOfFriendship, 20);
        return [];
    }
}
