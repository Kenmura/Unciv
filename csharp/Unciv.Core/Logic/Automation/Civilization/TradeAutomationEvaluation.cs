using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Trade;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

// T088/T089: TradeAutomation and DiplomacyAutomation call the members of Kotlin `TradeEvaluation` through this interface. The real TradeEvaluation
// (T089) implements it and is the default; the instance installed on GameClientState.TradeEvaluation (the hook TurnManager uses for isTradeValid)
// replaces it when it implements this interface too, which is how the tests stand in for the valuation.

/// <summary>The members of <c>TradeEvaluation</c> that the diplomacy automation calls.</summary>
internal interface ITradeAutomationEvaluation : ITradeEvaluation, ITradeValidity
{
    /// <summary><c>TradeEvaluation().isTradeAcceptable(trade, evaluator, tradePartner)</c></summary>
    bool IsTradeAcceptable(Trade.Trade trade, CivilizationClass evaluator, CivilizationClass tradePartner);

    /// <summary><c>TradeEvaluation().evaluateBuyCostWithInflation(offer, civInfo, tradePartner, trade)</c></summary>
    int EvaluateBuyCostWithInflation(TradeOffer offer, CivilizationClass civInfo, CivilizationClass tradePartner, Trade.Trade trade);

    /// <summary><c>TradeEvaluation().evaluateSellCostWithInflation(offer, civInfo, tradePartner, trade)</c></summary>
    int EvaluateSellCostWithInflation(TradeOffer offer, CivilizationClass civInfo, CivilizationClass tradePartner, Trade.Trade trade);

    /// <summary><c>TradeEvaluation().evaluatePeaceCostForThem(ourCiv, otherCiv)</c></summary>
    int EvaluatePeaceCostForThem(CivilizationClass ourCiv, CivilizationClass otherCiv);
}

internal static class TradeEvaluationSeam
{
    /// <summary>Kotlin <c>TradeEvaluation()</c>: the evaluation installed on the game's client state.</summary>
    internal static ITradeAutomationEvaluation Of(CivilizationClass civ) =>
        civ.GameInfo.ClientState.TradeEvaluation as ITradeAutomationEvaluation ?? TradeEvaluation.Default;

    /// <summary>Kotlin <c>TradeLogic(civInfo, otherCiv)</c>, whose own <c>TradeEvaluation()</c> calls go to the same instance.</summary>
    internal static TradeLogic NewLogic(CivilizationClass civInfo, CivilizationClass otherCiv) => new(civInfo, otherCiv, Of(civInfo));
}
