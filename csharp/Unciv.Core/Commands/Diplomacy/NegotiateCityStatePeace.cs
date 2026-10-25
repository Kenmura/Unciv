using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using Unciv.Utils;

namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Make peace with a city-state, which needs no answer. Kotlin: <c>CityStateDiplomacyTable.getNegotiatePeaceCityStateButton</c>, shown at war
/// when the mod lets relationships change, disabled while the city-state's ally is a known civ at war with us and for 10 turns after the war was
/// declared (the city-state's <c>DeclaredWar</c> flag), then a peace treaty on both sides is accepted at once through <c>TradeLogic.acceptTrade()</c>.
/// </summary>
[CommandType("negotiate-city-state-peace")]
public sealed class NegotiateCityStatePeace : GameCommand
{
    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.CityState, out var actor, out var other, out var failure)) return failure;
        if (!context.RelationshipsCanChange()) return DiplomacyCommandChecks.RelationshipsFixed();
        if (!actor.IsAtWarWith(other))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"You are not at war with {CivId}");
        if (actor.GetKnownCivs().Any(it => it == other.AllyCiv && it.IsAtWarWith(actor)))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"You are at war with the ally of {CivId}");
        return other.GetDiplomacyManager(actor).NotNull().HasFlag(DiplomacyFlags.DeclaredWar)
            ? CommandValidation.Fail(CommandErrors.NotAllowed, "Peace cannot be negotiated yet after a declaration of war")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        var tradeLogic = TradeEvaluationSeam.NewLogic(actor, context.ValidatedOther(CivId));
        tradeLogic.CurrentTrade.OurOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, context.Game.Speed));
        tradeLogic.CurrentTrade.TheirOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, context.Game.Speed));
        tradeLogic.AcceptTrade();
        return [];
    }
}
