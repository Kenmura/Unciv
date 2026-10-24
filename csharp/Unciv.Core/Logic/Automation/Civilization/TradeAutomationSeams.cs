using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Trade;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

// T088: TradeAutomation and DiplomacyAutomation call the members of Kotlin `TradeEvaluation` (T089) below. TradeEvaluation calls back into
// DiplomacyAutomation, so it is ported after them: until then the instance is read from GameClientState.TradeEvaluation (the same hook TurnManager
// uses for isTradeValid) and has to implement this interface. T089 implements it on TradeEvaluation and replaces the lookup by direct calls.

/// <summary>The members of <c>TradeEvaluation</c> that the diplomacy automation calls. TODO(T089)</summary>
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
        civ.GameInfo.ClientState.TradeEvaluation as ITradeAutomationEvaluation
            ?? throw new NotSupportedException("TradeEvaluation is not ported yet (TODO(T089))");

    /// <summary>Kotlin <c>TradeLogic(civInfo, otherCiv)</c>, whose own <c>TradeEvaluation()</c> calls go to the same instance.</summary>
    internal static TradeLogic NewLogic(CivilizationClass civInfo, CivilizationClass otherCiv) => new(civInfo, otherCiv, Of(civInfo));
}
