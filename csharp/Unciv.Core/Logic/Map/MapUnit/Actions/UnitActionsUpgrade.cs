using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Translations;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Logic.Map.MapUnit.Actions;

// Ported from core/src/com/unciv/logic/map/mapunit/actions/UnitActionsUpgrade.kt (T079).
public static class UnitActionsUpgrade
{
    public static IEnumerable<UnitAction> GetUpgradeActions(MapUnitClass unit)
    {
        if (!ReferenceEquals(unit.GetTile().GetOwner(), unit.Civ)) yield break;

        foreach (var upgrade in unit.Upgrade.GetUpgradesOfKind(isSpecial: false))
        {
            // Upgrades we lack only the resources for are shown, but disabled
            if (!upgrade.IsShownToPlayer) continue;

            yield return new UpgradeUnitAction(
                title: GetUpgradeTitle(upgrade),
                upgrade: upgrade,
                action: unit.Upgrade.CanPerformUpgrade(upgrade, isFree: false)
                    ? () => unit.Upgrade.PerformUpgrade(upgrade, isFree: false)
                    : null);
        }
    }

    public static string GetUpgradeTitle(PossibleUpgrade upgrade)
    {
        string newResourceRequirementsString = string.Join(", ",
            upgrade.NewResourceRequirements.Select(it => $"{it.Value} {{{it.Key}}}".Tr()));
        string name = upgrade.UpgradedUnit.Name;
        int gold = upgrade.GoldCostOfUpgrade;
        return newResourceRequirementsString.Length == 0 ? $"Upgrade to [{name}] ([{gold}] gold)"
            : $"Upgrade to [{name}]\n([{gold}] gold, [{newResourceRequirementsString}])";
    }
}
