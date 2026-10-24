using Unciv.Logic.Civilization.Managers;
using Unciv.View;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

/// <summary>The default <see cref="INextTurnAutomation"/> of a game: forwards to <see cref="NextTurnAutomation"/>, wrapping the civ as Kotlin's <c>GameView(gameInfo, civ).civView</c>.</summary>
internal sealed class NextTurnAutomationHook : INextTurnAutomation
{
    public static readonly NextTurnAutomationHook Instance = new();

    private static CivView ViewOf(CivilizationClass civ) => new GameView((GameInfo)civ.GameInfo, civ).CivView;

    public void AutomateGoldToSciencePercentage(CivilizationClass civInfo) => NextTurnAutomation.AutomateGoldToSciencePercentage(ViewOf(civInfo));

    public void AutomateCityBombardment(CivilizationClass civInfo) => NextTurnAutomation.AutomateCityBombardment(ViewOf(civInfo));

    public void ChooseGreatPerson(CivilizationClass civInfo) => NextTurnAutomation.ChooseGreatPerson(ViewOf(civInfo));

    public void AutomateCivMoves(CivilizationClass civInfo) => NextTurnAutomation.AutomateCivMoves(civInfo);

    public int ValueCityStateAlliance(CivilizationClass civInfo, CivilizationClass cityState, bool includeQuests = false) =>
        NextTurnAutomation.ValueCityStateAlliance(ViewOf(civInfo), cityState, includeQuests);
}
