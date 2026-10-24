using Unciv.View;

namespace Unciv.Logic.Map.MapUnit.Actions;

// TODO(T156): Seam for UnitAutomation (automatedExplore/automateUnitMoves via GameView)
internal static class UnitAutomation
{
    internal static void AutomatedExplore(MapUnitView unitView) =>
        Unciv.Logic.Automation.Unit.UnitAutomation.AutomatedExplore(unitView);

    internal static void AutomateUnitMoves(MapUnitView unitView) =>
        Unciv.Logic.Automation.Unit.UnitAutomation.AutomateUnitMoves(unitView);
}
