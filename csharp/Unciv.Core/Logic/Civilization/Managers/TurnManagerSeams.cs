
using TradeObject = Unciv.Logic.Trade.Trade;

namespace Unciv.Logic.Civilization.Managers;

// T070 (TurnManager): the parts of Automation (T094, ported: the default hook forwards to NextTurnAutomation) and TradeEvaluation (T089, ported: the hook is an optional override) that TurnManager calls. They hang off
// GameClientState (the client/AI layer the game owns); tests replace them with stand-ins. Remove in the T104 sweep (T104 is the open owner).

/// <summary>What <c>TurnManager</c> and <c>MayaCalendar</c> call on <c>NextTurnAutomation</c> (T094). The Kotlin parameter is <c>GameView(gameInfo, civ).civView</c>; the civ is passed instead.</summary>
public interface INextTurnAutomation
{
    /// <summary><c>NextTurnAutomation.automateGoldToSciencePercentage(civView)</c></summary>
    void AutomateGoldToSciencePercentage(Civilization civInfo);

    /// <summary><c>NextTurnAutomation.automateCityBombardment(civView)</c></summary>
    void AutomateCityBombardment(Civilization civInfo);

    /// <summary><c>NextTurnAutomation.chooseGreatPerson(civView)</c></summary>
    void ChooseGreatPerson(Civilization civInfo);

    /// <summary><c>NextTurnAutomation.automateCivMoves(civInfo)</c></summary>
    void AutomateCivMoves(Civilization civInfo);

    /// <summary><c>NextTurnAutomation.valueCityStateAlliance(civView, cityState, includeQuests)</c> (Battle.automateCityConquer, UseGoldAutomation). With no hook installed the alliance is valued 0.</summary>
    int ValueCityStateAlliance(Civilization civInfo, Civilization cityState, bool includeQuests = false);
}

/// <summary>What <c>TurnManager</c> calls on <c>TradeEvaluation</c> (T089).</summary>
public interface ITradeValidity
{
    /// <summary><c>TradeEvaluation().isTradeValid(trade, offerer, tradePartner)</c></summary>
    bool IsTradeValid(TradeObject trade, Civilization offerer, Civilization tradePartner);
}
