using Unciv.Logic.Automation.Unit;
using Unciv.View;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/BarbarianAutomation.kt (T094).
public class BarbarianAutomation(CivilizationClass civInfo)
{
    public void Automate()
    {
        var units = new GameView((GameInfo)civInfo.GameInfo, civInfo).CivView.GetUnits();
        // ranged go first, after melee and then everyone else
        foreach (var it in units.Where(it => it.GetBaseUnit().IsRanged()).ToList()) AutomateUnit(it);
        foreach (var it in units.Where(it => it.GetBaseUnit().IsMelee()).ToList()) AutomateUnit(it);
        foreach (var it in units.Where(it => !it.GetBaseUnit().IsRanged() && !it.GetBaseUnit().IsMelee()).ToList()) AutomateUnit(it);
        // fix buildup of alerts - to shrink saves and ease debugging
        civInfo.PopupAlerts.Clear();
    }

    private static void AutomateUnit(MapUnitView unit)
    {
        if (unit.IsCivilian()) AutomateCapturedCivilian(unit);
        else if (unit.GetTile().IsBarbarianEncampment()) AutomateUnitOnEncampment(unit);
        else AutomateCombatUnit(unit);
    }

    private static void AutomateCapturedCivilian(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        // 1 - Stay on current encampment
        if (unit.CurrentTile.IsBarbarianEncampment()) return;

        var campTiles = unit.Civ.GameInfo.Barbarians.GetEncampmentTiles();
        campTiles = campTiles.OrderBy(it => unit.CurrentTile.AerialDistanceTo(it)).ToList(); // sortBy is stable
        var bestCamp = campTiles.FirstOrDefault(it => it.CivilianUnit == null && unit.Movement.CanReach(it));
        if (bestCamp != null)
            unit.Movement.HeadTowards(bestCamp); // 2 - Head towards an encampment
        else
            UnitAutomation.Wander(unitView); // 3 - Can't find a reachable encampment, wander aimlessly
    }

    private static void AutomateUnitOnEncampment(MapUnitView unitView)
    {
        // 1 - trying to upgrade
        if (UnitAutomation.TryUpgradeUnit(unitView)) return;

        // 2 - trying to attack somebody - but don't leave the encampment
        if (BattleHelper.TryAttackNearbyEnemy(unitView, stayOnTile: true)) return;

        // 3 - at least fortifying
        unitView.Unwrap().FortifyIfCan();
    }

    private static void AutomateCombatUnit(MapUnitView unit)
    {
        // 1 - Try pillaging to restore health (barbs don't auto-heal)
        if (unit.UnitHealth < 50 && UnitAutomation.TryPillageImprovement(unit, true) && !unit.HasMovement()) return;

        // 2 - trying to upgrade
        if (UnitAutomation.TryUpgradeUnit(unit)) return;

        // 3 - trying to attack enemy
        // if a embarked melee unit can land and attack next turn, do not attack from water.
        if (BattleHelper.TryDisembarkUnitToAttackPosition(unit.Unwrap())) return;
        if (!unit.IsCivilian() && BattleHelper.TryAttackNearbyEnemy(unit)) return;

        // 4 - trying to pillage tile or route
        while (UnitAutomation.TryPillageImprovement(unit))
        {
            if (!unit.HasMovement()) return;
        }

        // 6 - wander
        UnitAutomation.Wander(unit);
    }
}
