using System.Collections.Frozen;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.Utils;
using Unciv.View;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using UnitAutomationClass = Unciv.Logic.Automation.Unit.UnitAutomation;

namespace Unciv.Logic.Map.MapUnit.Actions;

// Ported from core/src/com/unciv/logic/map/mapunit/actions/UnitActions.kt (T080).
// Stripped: the purity annotations. UI concerns (UncivGame.settings, GUI.getWorldScreen) dropped.

/// <summary>
/// Manages creation of <see cref="UnitAction"/> instances.
/// </summary>
public static class UnitActions
{
    /// <summary>
    /// Get an instance of <see cref="UnitAction"/> of the <paramref name="unitActionType"/> type for <paramref name="unit"/>
    /// and execute its action, if enabled.
    /// </summary>
    /// <returns>whether the action was invoked</returns>
    public static bool InvokeUnitAction(MapUnitClass unit, UnitActionType unitActionType)
    {
        var internalAction = GetUnitActions(unit, unitActionType)
            .FirstOrDefault(it => it.Action != null) // If there's more than one, take the first enabled one.
            ?.Action;
        if (internalAction == null) return false;
        internalAction();
        return true;
    }

    /// <summary>
    /// Get all currently possible instances of <see cref="UnitAction"/> for <paramref name="unit"/>.
    /// </summary>
    public static IEnumerable<UnitAction> GetUnitActions(MapUnitClass unit)
    {
        var tile = unit.GetTile();

        // Actions standardized with a directly callable invokeUnitAction
        foreach (var getActionsFunction in ActionTypeToFunctions.Values)
        {
            foreach (var action in getActionsFunction(unit, tile))
                yield return action;
        }

        // Actions not migrated to actionTypeToFunctions
        foreach (var action in AddUnmappedUnitActions(unit))
            yield return action;
    }

    /// <summary>
    /// Get all instances of <see cref="UnitAction"/> of the <paramref name="unitActionType"/> type for <paramref name="unit"/>.
    /// </summary>
    public static IEnumerable<UnitAction> GetUnitActions(MapUnitClass unit, UnitActionType unitActionType)
    {
        if (ActionTypeToFunctions.TryGetValue(unitActionType, out var getActionsFunction))
        {
            return getActionsFunction(unit, unit.GetTile());
        }
        else
        {
            return AddUnmappedUnitActions(unit).Where(it => it.Type == unitActionType);
        }
    }

    private static readonly FrozenDictionary<UnitActionType, Func<MapUnitClass, Tile, IEnumerable<UnitAction>>> ActionTypeToFunctions =
        new Dictionary<UnitActionType, Func<MapUnitClass, Tile, IEnumerable<UnitAction>>>
        {
            // Determined by unit uniques
            { UnitActionType.Transform, UnitActionsFromUniques.GetTransformActions },
            { UnitActionType.Paradrop, UnitActionsFromUniques.GetParadropActions },
            { UnitActionType.AirSweep, UnitActionsFromUniques.GetAirSweepActions },
            { UnitActionType.SetUp, UnitActionsFromUniques.GetSetupActions },
            { UnitActionType.Guard, UnitActionsFromUniques.GetGuardActions },
            { UnitActionType.FoundCity, UnitActionsFromUniques.GetFoundCityActions },
            { UnitActionType.Repair, UnitActionsFromUniques.GetRepairActions },
            { UnitActionType.HurryResearch, UnitActionsGreatPerson.GetHurryResearchActions },
            { UnitActionType.HurryPolicy, UnitActionsGreatWorks.GetHurryPolicyActions },
            { UnitActionType.HurryWonder, UnitActionsGreatPerson.GetHurryWonderActions },
            { UnitActionType.HurryBuilding, UnitActionsGreatPerson.GetHurryBuildingActions },
            { UnitActionType.ConductTradeMission, UnitActionsGreatPerson.GetConductTradeMissionActions },
            { UnitActionType.CreateGreatWork, UnitActionsGreatWorks.GetCreateGreatWorkActions },
            { UnitActionType.ConcertTour, UnitActionsGreatWorks.GetConcertTourActions },
            { UnitActionType.FoundReligion, UnitActionsReligion.GetFoundReligionActions },
            { UnitActionType.EnhanceReligion, UnitActionsReligion.GetEnhanceReligionActions },
            { UnitActionType.CreateImprovement, UnitActionsFromUniques.GetImprovementCreationActions },
            { UnitActionType.SpreadReligion, UnitActionsReligion.GetSpreadReligionActions },
            { UnitActionType.RemoveHeresy, UnitActionsReligion.GetRemoveHeresyActions },
            { UnitActionType.TriggerUnique, UnitActionsFromUniques.GetTriggerUniqueActions },
            { UnitActionType.AddInCapital, UnitActionsFromUniques.GetAddInCapitalActions },
            { UnitActionType.GiftUnit, GetGiftActions },
            { UnitActionType.Pillage, UnitActionsPillage.GetPillageActions }
        }.ToFrozenDictionary();

    private static IEnumerable<UnitAction> AddUnmappedUnitActions(MapUnitClass unit)
    {
        var tile = unit.GetTile();

        // General actions
        foreach (var action in AddAutomateActions(unit))
            yield return action;

        if (unit.IsMoving())
            yield return new UnitAction(UnitActionType.StopMovement, action: () => unit.Action = null);
        if (unit.IsExploring())
            yield return new UnitAction(UnitActionType.StopExploration, action: () => unit.Action = null);
        if (unit.IsAutomated())
            yield return new UnitAction(UnitActionType.StopAutomation, action: () =>
            {
                unit.Action = null;
                unit.Automated = false;
            });

        foreach (var action in UnitActionsUpgrade.GetUpgradeActions(unit))
            yield return action;

        foreach (var action in AddSleepActions(unit, tile))
            yield return action;

        foreach (var action in AddFortifyActions(unit))
            yield return action;

        foreach (var action in AddExplorationActions(unit))
            yield return action;

        foreach (var action in AddSkipAction(unit))
            yield return action;

        foreach (var action in AddEscortAction(unit))
            yield return action;
    }

    private static IEnumerable<UnitAction> AddEscortAction(MapUnitClass unit)
    {
        // Air units cannot escort
        if (unit.BaseUnit.IsAirUnit()) yield break;

        // GUI.getWorldScreen() check dropped: in headless engine, check if there is an escort partner
        if (unit.GetOtherEscortUnit() == null) yield break;

        if (!unit.IsEscorting())
        {
            yield return new UnitAction(
                UnitActionType.EscortFormation,
                action: () => unit.StartEscorting()
            );
        }
        else
        {
            yield return new UnitAction(
                UnitActionType.StopEscortFormation,
                action: () => unit.StopEscorting()
            );
        }
    }

    private static IEnumerable<UnitAction> AddExplorationActions(MapUnitClass unit)
    {
        if (unit.BaseUnit.IsAirUnit()) yield break;
        if (unit.IsExploring()) yield break;
        yield return new UnitAction(
            UnitActionType.Explore,
            action: () =>
            {
                unit.Action = UnitActionType.Explore.Value();
                if (unit.HasMovement())
                    UnitAutomationClass.AutomatedExplore(new GameView((GameInfo)unit.Civ.GameInfo, unit.Civ).GetMapUnitView(unit));
            }
        );
    }

    private static IEnumerable<UnitAction> AddFortifyActions(MapUnitClass unit)
    {
        if (unit.IsFortified())
        {
            yield return new UnitAction(
                unit.IsActionUntilHealed()
                    ? UnitActionType.FortifyUntilHealed
                    : UnitActionType.Fortify,
                isCurrentAction: true,
                title: $"{"Fortification".Tr()} {unit.GetFortificationTurns() * 20}%"
            );
            yield break;
        }

        if (!unit.CanFortify() || !unit.HasMovement()) yield break;

        yield return new UnitAction(
            UnitActionType.Fortify,
            action: !unit.IsFortified() || unit.IsFortifyingUntilHealed()
                ? () => unit.Fortify()
                : null
        );

        if (unit.Health == 100) yield break;
        yield return new UnitAction(
            UnitActionType.FortifyUntilHealed,
            action: !unit.IsFortifyingUntilHealed() && unit.CanHealInCurrentTile()
                ? () => unit.FortifyUntilHealed()
                : null
        );
    }

    private static IEnumerable<UnitAction> AddSleepActions(MapUnitClass unit, Tile tile)
    {
        if (unit.IsFortified() || unit.CanFortify() || unit.IsGuarding() || !unit.HasMovement()) yield break;
        if (tile.HasImprovementInProgress() && unit.CanBuildImprovement(tile.GetTileImprovementInProgress().NotNull())) yield break;

        yield return new UnitAction(
            UnitActionType.Sleep,
            action: !unit.IsSleeping() || unit.IsSleepingUntilHealed()
                ? () => unit.Action = UnitActionType.Sleep.Value()
                : null
        );

        if (unit.Health == 100) yield break;
        yield return new UnitAction(
            UnitActionType.SleepUntilHealed,
            action: !unit.IsSleepingUntilHealed() && unit.CanHealInCurrentTile()
                ? () => unit.Action = UnitActionType.SleepUntilHealed.Value()
                : null
        );
    }

    private static IEnumerable<UnitAction> GetGiftActions(MapUnitClass unit, Tile tile)
    {
        var recipient = (CivilizationObject?)tile.GetOwner();
        // We need to be in another civs territory.
        if (recipient == null || ReferenceEquals(recipient, unit.Civ)) yield break;
        // Sequential keeps the Kotlin rule (no gift in the current player's territory); simultaneous play has no single current player.
        if (!unit.Civ.GameInfo.IsSimultaneousTurns && recipient.IsCurrentPlayer()) yield break;

        if (recipient.IsCityState)
        {
            if (recipient.IsAtWarWith(unit.Civ)) yield break; // No gifts to enemy CS
            // City States only take military units (and units specifically allowed by uniques)
            if (!unit.IsMilitary()
                && !unit.GetMatchingUniques(
                    UniqueType.GainInfluenceWithUnitGiftToCityState,
                    checkCivInfoUniques: true
                ).Any(it => unit.MatchesFilter(it.Params[1])))
                yield break;
        }
        // If gifting to major civ they need to be friendly
        else if (!tile.IsFriendlyTerritory(unit.Civ)) yield break;

        // Transported units can't be gifted
        if (unit.IsTransported) yield break;

        if (!unit.HasMovement())
        {
            yield return new UnitAction(UnitActionType.GiftUnit, action: null);
            yield break;
        }

        Action giftAction = () =>
        {
            if (recipient.IsCityState)
            {
                var influence = 5f;
                foreach (var unique in unit.GetMatchingUniques(
                    UniqueType.GainInfluenceWithUnitGiftToCityState,
                    checkCivInfoUniques: true
                ))
                {
                    if (unit.MatchesFilter(unique.Params[1]))
                    {
                        influence = unique.Params[0].ToFloat();
                        break;
                    }
                }

                // B034: Charitable Missions scales the whole gain
                recipient.GetDiplomacyManager(unit.Civ).NotNull().AddInfluence(CityStateFunctions.ScaleInfluenceGain(unit.Civ, influence));
            }
            else
            {
                recipient.GetDiplomacyManager(unit.Civ).NotNull()
                    .AddModifier(DiplomaticModifiers.GaveUsUnits, 5f);
            }

            if (recipient.IsCityState && unit.IsGreatPerson())
                unit.Destroy(); // City states don't get GPs
            else
                unit.Gift(recipient);
        };

        yield return new UnitAction(UnitActionType.GiftUnit, action: giftAction);
    }

    private static IEnumerable<UnitAction> AddAutomateActions(MapUnitClass unit)
    {
        if (unit.IsAutomated()) yield break;
        yield return new UnitAction(
            UnitActionType.Automate,
            isCurrentAction: unit.IsAutomated(),
            action: unit.HasMovement()
                ? () =>
                {
                    unit.Automated = true;
                    UnitAutomationClass.AutomateUnitMoves(new GameView((GameInfo)unit.Civ.GameInfo, unit.Civ).GetMapUnitView(unit));
                }
        : null
        );
    }

    // Skip one turn: marks a unit as due=false and doesn't cycle back in the queue
    private static IEnumerable<UnitAction> AddSkipAction(MapUnitClass unit)
    {
        yield return new UnitAction(
            type: UnitActionType.Skip,
            action: unit.HasMovement()
                ? (Action)(() =>
                {
                    unit.Due = !unit.Due;
                    // Note: GUI.getWorldScreen() / settings.autoUnitCycle are UI and dropped
                })
                : null,
            isCurrentAction: !unit.Due
        );
    }
}
