using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Map.MapUnits;

// Ported from core/src/com/unciv/logic/map/mapunit/UnitUpgradeManager.kt (T066). Stripped: the purity annotations.

/// <summary>How far an upgrade is available.</summary>
public enum UpgradeAvailability
{
    /// <summary>Passes all requirements, including resources</summary>
    Available,
    /// <summary>Passes all requirements (tech, policy, etc) except resources - shown, but can't be performed</summary>
    AvailableExceptResources,
    /// <summary>Passes only when tech/policy/era/wonder requirements and resources are ignored - only free upgrades can be performed</summary>
    AvailableForFree,
    Unavailable
}

/// <summary>A unit this <see cref="MapUnit"/> could upgrade to, as found by <see cref="UnitUpgradeManager.GetUpgrades"/>.</summary>
/// <param name="upgradedUnit">civ-specific equivalent of the target unit</param>
/// <param name="unique">the <see cref="UniqueType.CanUpgrade"/> or <see cref="UniqueType.RuinsUpgrade"/> unique granting this upgrade, <c>null</c> for the base unit's normal <c>upgradesTo</c></param>
/// <param name="isSpecial">from <see cref="UniqueType.RuinsUpgrade"/> (ancient ruins)</param>
/// <param name="availability">how far this upgrade is available</param>
/// <param name="goldCostOfUpgrade">regular gold cost - free upgrades pay 0 instead</param>
/// <param name="newResourceRequirements">additional resources per turn the upgraded unit needs (never negative)</param>
public class PossibleUpgrade(
    BaseUnit upgradedUnit,
    UniqueObject? unique,
    bool isSpecial,
    UpgradeAvailability availability,
    int goldCostOfUpgrade,
    Counter<string> newResourceRequirements)
{
    public BaseUnit UpgradedUnit { get; } = upgradedUnit;
    public UniqueObject? Unique { get; } = unique;
    public bool IsSpecial { get; } = isSpecial;
    public UpgradeAvailability Availability { get; } = availability;
    public int GoldCostOfUpgrade { get; } = goldCostOfUpgrade;
    public Counter<string> NewResourceRequirements { get; } = newResourceRequirements;

    /// <summary>worth listing for the player - disabled if we only lack resources</summary>
    public bool IsShownToPlayer =>
        Availability == UpgradeAvailability.Available || Availability == UpgradeAvailability.AvailableExceptResources;
}

public class UnitUpgradeManager(MapUnit unit)
{
    private Counter<string> GetNewResourceRequirements(BaseUnit upgradedUnit)
    {
        var resourceRequirementsDelta = new Counter<string>();
        foreach (var (resource, amount) in unit.GetResourceRequirementsPerTurn())
            resourceRequirementsDelta.Add(resource, unchecked(-amount));
        foreach (var (resource, amount) in upgradedUnit.GetResourceRequirementsPerTurn(unit.Cache.State))
            resourceRequirementsDelta.Add(resource, amount);
        foreach (var (resource, _) in resourceRequirementsDelta.Where(it => it.Value < 0).ToList()) // filter copies, so no CCM
            resourceRequirementsDelta[resource] = 0;
        return resourceRequirementsDelta;
    }

    /// <summary>Lists every upgrade this unit has, regardless of location, cost or movement. Callers filter on the flags.</summary>
    public List<PossibleUpgrade> GetUpgrades()
    {
        var baseUnitUniques = (IHasUniques)unit.BaseUnit;
        var candidates = new List<(string Name, UniqueObject? Unique, bool IsSpecial)>();
        foreach (var it in baseUnitUniques.GetMatchingUniques(UniqueType.RuinsUpgrade, unit.Cache.State))
            candidates.Add((it.Params[0], it, true));
        foreach (var it in baseUnitUniques.GetMatchingUniques(UniqueType.CanUpgrade, unit.Cache.State))
            candidates.Add((it.Params[0], it, false));
        if (unit.BaseUnit.UpgradesTo is { } upgradesTo)
            candidates.Add((upgradesTo, null, false));

        return candidates
            .Select(it =>
            {
                var upgradedUnit = unit.Civ.GetEquivalentUnit(it.Name);
                return new PossibleUpgrade(
                    upgradedUnit: upgradedUnit,
                    unique: it.Unique,
                    isSpecial: it.IsSpecial,
                    availability: CanUpgrade(upgradedUnit),
                    goldCostOfUpgrade: GetCostOfUpgrade(upgradedUnit),
                    newResourceRequirements: GetNewResourceRequirements(upgradedUnit));
            })
            .ToList();
    }

    /// <summary>
    /// <see cref="GetUpgrades"/> narrowed to the kind requested: if <paramref name="isSpecial"/> and a special (ruins) upgrade exists, only the first of those,
    /// otherwise only the normal ones.
    /// </summary>
    public List<PossibleUpgrade> GetUpgradesOfKind(bool isSpecial)
    {
        var allUpgrades = GetUpgrades();
        var special = allUpgrades.Where(it => it.IsSpecial).Take(1).ToList();
        return isSpecial && special.Count != 0 ? special : allUpgrades.Where(it => !it.IsSpecial).ToList();
    }

    /// <summary>
    /// Check whether this unit can upgrade to <paramref name="unitToUpgradeTo"/>. This does not check or follow the
    /// normal upgrade chain defined by <c>BaseUnit.getUpgradeUnits</c>
    /// </summary>
    /// <returns>
    /// the strictest <see cref="UpgradeAvailability"/> that holds:
    /// <see cref="UpgradeAvailability.AvailableExceptResources"/> is used to display a disabled Upgrade button,
    /// <see cref="UpgradeAvailability.AvailableForFree"/> ignores tech/policy/building requirements (e.g. ancient ruins).
    /// </returns>
    public UpgradeAvailability CanUpgrade(BaseUnit unitToUpgradeTo)
    {
        if (unit.Name == unitToUpgradeTo.Name) return UpgradeAvailability.Unavailable;

        var rejectionReasons = unitToUpgradeTo.GetRejectionReasons(unit.Civ, additionalResources: unit.GetResourceRequirementsPerTurn())
            .Where(it => !(it.IsConstructionRejection() || it.Type == RejectionReasonType.Obsoleted))
            .ToList();

        var withoutResources = rejectionReasons.Where(it => it.Type != RejectionReasonType.ConsumesResources).ToList();
        if (rejectionReasons.Count == 0) return UpgradeAvailability.Available;
        if (withoutResources.Count == 0) return UpgradeAvailability.AvailableExceptResources;
        if (!withoutResources.Any(it => !it.TechPolicyEraWonderRequirements())) return UpgradeAvailability.AvailableForFree;
        return UpgradeAvailability.Unavailable;
    }

    /// <summary>Determine gold cost of a Unit Upgrade, potentially over several steps.</summary>
    /// <param name="unitToUpgradeTo">
    /// the final BaseUnit. Must be reachable via normal upgrades or else
    /// the function will return the cost to upgrade to the last possible and researched normal upgrade.
    /// </param>
    /// <returns>Gold cost in increments of 5, never negative. Will return 0 for invalid inputs (unit can't upgrade or is is already a <paramref name="unitToUpgradeTo"/>)</returns>
    // Only one use from getUpgradeAction at the moment, so AI-specific rules omitted
    //todo Does the AI never buy upgrades???
    public int GetCostOfUpgrade(BaseUnit unitToUpgradeTo)
    {
        // Source rounds to int every step, we don't
        // Upstream open question: from the source, this should apply _Production_ modifiers (Temple of Artemis? GameSpeed! StartEra!), at the moment it doesn't

        var goldCostOfUpgrade = 0;

        var ruleset = unit.Civ.GameInfo.Ruleset;
        var constants = ruleset.ModOptions.Constants.UnitUpgradeCost;
        // apply modifiers: Wonders (Pentagon), Policies (Professional Army). Cached outside loop despite
        // the UniqueType being allowed on a BaseUnit - we don't have a MapUnit in the loop.
        // Actually instantiating every intermediate to support such mods: todo
        var civModifier = 1f;
        var stateForConditionals = unit.Cache.State;
        unit.Civ.ForEachMatchingUnique(UniqueType.UnitUpgradeCost, stateForConditionals, unique =>
        {
            civModifier *= unique.Params[0].ToPercent();
        });

        var cost = constants.Base;
        cost += Math.Max(constants.PerProduction * unchecked(unitToUpgradeTo.Cost - unit.BaseUnit.Cost), 0f);
        var era = ((IHasUniques)unitToUpgradeTo).Era(ruleset);
        if (era != null)
            cost *= 1f + era.EraNumber * constants.EraMultiplier;
        cost = (float)Math.Pow(cost * civModifier, constants.Exponent);
        cost *= unit.Civ.GameInfo.Speed.Modifier;
        goldCostOfUpgrade = unchecked(goldCostOfUpgrade + KotlinMath.FloatToInt(cost / constants.RoundTo) * constants.RoundTo);

        return goldCostOfUpgrade;
    }

    /// <summary>
    /// Whether <paramref name="upgrade"/> can be performed right now: free upgrades only need to be available at all,
    /// paid ones also need gold, movement, own territory, not being embarked and all requirements and resources met.
    /// </summary>
    public bool CanPerformUpgrade(PossibleUpgrade upgrade, bool isFree)
    {
        if (isFree) return upgrade.Availability != UpgradeAvailability.Unavailable;
        return upgrade.Availability == UpgradeAvailability.Available
            && unit.Civ.Gold >= upgrade.GoldCostOfUpgrade
            && unit.HasMovement()
            && ReferenceEquals(unit.CurrentTile.GetOwner(), unit.Civ)
            && !unit.IsEmbarked();
    }

    /// <summary>
    /// _Perform_ an upgrade, assuming validity checks were already passed.
    ///
    /// Continuing to use a reference to this manager or its unit after this call is invalid!
    ///
    /// Please use <c>UnitActionsUpgrade.getUpgradeActions</c> instead if at all possible.
    /// </summary>
    /// <exception cref="InvalidOperationException">if <see cref="CanPerformUpgrade"/> is false (Kotlin IllegalStateException)</exception>
    /// <remarks>
    /// Note - the upgraded unit is a new instance, and it's possible this method will need to place it on a different tile.
    /// It is also possible the placement fails and the original is resurrected - in which case it is a **new instance** as well.
    /// </remarks>
    public void PerformUpgrade(PossibleUpgrade upgrade, bool isFree)
    {
        var upgradedUnit = upgrade.UpgradedUnit;
        // When mashing the upgrade button, you can 'queue' 2 upgrade actions
        //  If both are performed, what you get is the unit is doubled
        //  This prevents this, since we lack another way to do so -_-'
        if (unit.IsDestroyed) return;

        if (!CanPerformUpgrade(upgrade, isFree))
            throw new InvalidOperationException($"Cannot upgrade {unit.Name} to {upgrade.UpgradedUnit.Name}");

        var wasEscorting = unit.IsEscorting();
        unit.Destroy(destroyTransportedUnit: false);
        var civ = unit.Civ;
        var position = unit.CurrentTile.Position;
        var newUnit = civ.Units.PlaceUnitNearTile(position, upgradedUnit, unit.Id, copiedFrom: unit);

        // We were UNABLE to place the new unit, which means that the unit failed to upgrade!
        // The only known cause of this currently is "land units upgrading to water units" which fail to be placed.
        if (newUnit == null)
        {
            _ = civ.Units.PlaceUnitNearTile(position, unit.BaseUnit, copiedFrom: unit).NotNull();
            return;
        }

        // Managed to upgrade
        if (!isFree) civ.AddGold(unchecked(-upgrade.GoldCostOfUpgrade));
        newUnit.CurrentMovement = 0f;
        // wake up if lost ability to fortify
        if (newUnit.IsFortified() && !newUnit.CanFortify(ignoreAlreadyFortified: true))
            newUnit.Action = null;
        // wake up from Guarding if can't Withdraw
        if (newUnit.IsGuarding() && !newUnit.HasUnique(UniqueType.WithdrawsBeforeMeleeCombat))
            newUnit.Action = null;
        // Re-escort if it was escorting
        if (newUnit.CurrentTile.Position == position && wasEscorting)
            newUnit.StartEscorting();
    }
}
