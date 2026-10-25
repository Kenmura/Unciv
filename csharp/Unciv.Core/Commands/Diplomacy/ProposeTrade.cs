using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TradeObject = Unciv.Logic.Trade.Trade;

namespace Unciv.Commands.Diplomacy;

/// <summary>One staged offer: the name and type of one of the available offers, and for quantities (gold, resources) how much of it.</summary>
public sealed record TradeOfferSpec(string Name, string Kind, int Amount = 1);

/// <summary>
/// Stage and send a trade to a major civ. Kotlin: <c>TradeTable</c> (offer columns) and <c>TradeView.tryProposeStagedTrade</c>, which only appends a
/// <c>TradeRequest</c> to the other civ's <c>tradeRequests</c>. The verdict is NOT part of this command: an AI answers during its own turn
/// (<c>TradeAutomation.respondToTradeRequests</c>), a human with <see cref="RespondToTradeRequest"/>.
/// <para>
/// Every offer must be one of the available offers of its side (<c>TradeLogic.ourAvailableOffers/theirAvailableOffers</c>) and tradable; only gold,
/// gold per turn and resources take an amount (1 to what is available), every other offer is taken as it is available (so the duration and the
/// research agreement cost are the authority's). A treaty (peace, research agreement, defensive pact) listed on either side is added to both
/// sides (<c>OfferColumnsTable.addOffer</c>). As in <c>TradeTable</c>, a research agreement tops up the gold either side is short of paying
/// its cost. "Negotiate peace" is this command with a peace treaty; it is disabled for 10 turns after the war was declared.
/// </para>
/// </summary>
[CommandType("propose-trade")]
public sealed class ProposeTrade : GameCommand
{
    public string CivId { get; init; } = "";

    /// <summary>What we give.</summary>
    public IReadOnlyList<TradeOfferSpec> OurOffers { get; init; } = [];

    /// <summary>What we ask for.</summary>
    public IReadOnlyList<TradeOfferSpec> TheirOffers { get; init; } = [];

    private static bool IsQuantity(TradeOfferType type) =>
        type is TradeOfferType.Gold or TradeOfferType.Gold_Per_Turn or TradeOfferType.Luxury_Resource
            or TradeOfferType.Strategic_Resource or TradeOfferType.Stockpiled_Resource;

    private static CommandValidation Bad(string message) => CommandValidation.Fail(CommandErrors.InvalidTarget, message);

    private static bool Stage(IReadOnlyList<TradeOfferSpec>? specs, TradeOffersList available, TradeOffersList staged, TradeOffersList counterStaged,
        out CommandValidation failure)
    {
        failure = CommandValidation.Ok;
        var seen = new HashSet<(string, TradeOfferType)>();
        foreach (var spec in specs ?? [])
        {
            if (spec == null || !Enum.TryParse<TradeOfferType>(spec.Kind, out var type) || !Enum.IsDefined(type))
            {
                failure = Bad("Unknown trade offer type");
                return false;
            }
            var match = available.FirstOrDefault(it => it.Name == spec.Name && it.Type == type && it.IsTradable());
            if (match == null)
            {
                failure = Bad($"\"{spec.Name}\" is not available to trade");
                return false;
            }
            if (!seen.Add((spec.Name, type)))
            {
                failure = Bad($"\"{spec.Name}\" is offered twice");
                return false;
            }
            if (IsQuantity(type) && (spec.Amount <= 0 || spec.Amount > match.Amount))
            {
                failure = Bad($"Amount of \"{spec.Name}\" must be 1 to {match.Amount}");
                return false;
            }
            var offer = match.Copy(amount: IsQuantity(type) ? spec.Amount : match.Amount);
            if (staged.Any(it => it.Name == offer.Name && it.Type == offer.Type)) continue; // a treaty mirrored from the other list
            staged.Add(offer);
            if (type == TradeOfferType.Treaty && !counterStaged.Any(it => it.Name == offer.Name && it.Type == type)) counterStaged.Add(offer.Copy());
        }
        return true;
    }

    /// <summary>The staged trade from our point of view (<c>OurOffers</c> = what we give), or the reason it cannot be staged.</summary>
    private bool TryBuild(CommandContext context, CivilizationObject actor, CivilizationObject other, out TradeObject trade, out CommandValidation failure)
    {
        var logic = TradeEvaluationSeam.NewLogic(actor, other);
        trade = logic.CurrentTrade;
        if (!Stage(OurOffers, logic.OurAvailableOffers, trade.OurOffers, trade.TheirOffers, out failure)) return false;
        if (!Stage(TheirOffers, logic.TheirAvailableOffers, trade.TheirOffers, trade.OurOffers, out failure)) return false;
        if (trade.OurOffers.Count == 0 && trade.TheirOffers.Count == 0)
        {
            failure = CommandValidation.Fail(CommandErrors.NotAllowed, "The trade is empty");
            return false;
        }
        if (trade.IsPeaceTreaty())
        {
            if (!context.RelationshipsCanChange())
            {
                failure = DiplomacyCommandChecks.RelationshipsFixed();
                return false;
            }
            if (other.GetDiplomacyManager(actor).NotNull().HasFlag(DiplomacyFlags.DeclaredWar))
            {
                failure = CommandValidation.Fail(CommandErrors.NotAllowed, "Peace cannot be negotiated yet after a declaration of war");
                return false;
            }
        }
        if (trade.OurOffers.Any(it => it.Name == Constants.ResearchAgreement) && !TopUpResearchAgreementGold(actor, other, logic, out failure))
            return false;
        failure = CommandValidation.Ok;
        return true;
    }

    // TradeTable: both civs must be able to pay for the research agreement; gold is added to cover what is missing.
    private static bool TopUpResearchAgreementGold(CivilizationObject actor, CivilizationObject other, TradeLogic logic, out CommandValidation failure)
    {
        failure = CommandValidation.Ok;
        var trade = logic.CurrentTrade;
        int researchCost = actor.DiplomacyFunctions.GetResearchAgreementCost(other);
        int ourGold = trade.OurOffers.FirstOrDefault(it => it.Type == TradeOfferType.Gold)?.Amount ?? 0;
        int theirGold = trade.TheirOffers.FirstOrDefault(it => it.Type == TradeOfferType.Gold)?.Amount ?? 0;
        int newOurGold = unchecked(actor.Gold + theirGold - researchCost);
        int newTheirGold = unchecked(other.Gold + ourGold - researchCost);
        if (newOurGold < 0)
        {
            var goldOffer = logic.TheirAvailableOffers.FirstOrDefault(it => it.Type == TradeOfferType.Gold);
            if (goldOffer == null)
            {
                failure = CommandValidation.Fail(CommandErrors.NotAllowed, "They cannot be asked for the gold the research agreement needs");
                return false;
            }
            trade.TheirOffers.Add(goldOffer.Copy(amount: -newOurGold));
        }
        if (newTheirGold < 0)
        {
            var goldOffer = logic.OurAvailableOffers.FirstOrDefault(it => it.Type == TradeOfferType.Gold);
            if (goldOffer == null)
            {
                failure = CommandValidation.Fail(CommandErrors.NotAllowed, "You cannot offer the gold the research agreement needs");
                return false;
            }
            trade.OurOffers.Add(goldOffer.Copy(amount: -newTheirGold));
        }
        return true;
    }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.Major, out var actor, out var other, out var failure)) return failure;
        if (other.TradeRequests.Any(it => it.RequestingCiv == actor.CivId))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"You already have an offer pending with {CivId}; retract it first");
        return TryBuild(context, actor, other, out _, out failure) ? CommandValidation.Ok : failure;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        var other = context.ValidatedOther(CivId);
        TryBuild(context, actor, other, out var trade, out _);
        other.TradeRequests.Add(new TradeRequest(actor.CivId, trade.Reverse()));
        actor.Cache.UpdateCivResources();
        return [];
    }
}
