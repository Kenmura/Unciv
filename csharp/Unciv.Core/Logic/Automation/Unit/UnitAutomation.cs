using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;
using Unciv.View;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/UnitAutomation.kt. T090 ports only tryUpgradeUnit (UseGoldAutomation calls it);
// the rest of the file is TODO(T156), which continues this partial class.
// - Kotlin `civ.isHuman() && !UncivGame.Current.settings.automatedUnitsCanUpgrade && worldScreen?.autoPlay...`: the setting and the world screen are
//   UI (dropped). The setting defaults to true, so the guard never trips in the headless engine.
public static partial class UnitAutomation
{
    internal static bool TryUpgradeUnit(MapUnitView unitView)
    {
        var civ = unitView.Civ();

        var upgrade = unitView.GetPossibleUpgrades()
            .Where(it => it.Availability == UpgradeAvailability.Available // for resource reasons, usually
                && !(civ.IsBarbarian() && ((IHasUniques)it.UpgradedUnit).HasUnique(UniqueType.CannotBeBarbarian)))
            .MinBy(it => it.UpgradedUnit.Cost);
        if (upgrade == null) return false;

        // The upgrade requires new resource types, so check if we are willing to invest them
        if (upgrade.NewResourceRequirements.Any(it => it.Value > 0)
            && !Automation.AllowSpendingResource(civ, upgrade.UpgradedUnit)) return false;

        return unitView.TryUpgradeTo(upgrade.UpgradedUnit);
    }
}
