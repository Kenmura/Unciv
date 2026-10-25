using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.UI.Components;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using PlayerType = Unciv.Logic.Civilization.PlayerType;

namespace Unciv.Logic.Map.MapUnit.Actions;

// Ported from core/src/com/unciv/logic/map/mapunit/actions/UnitActionsFromUniques.kt (T080).
// Stripped: the purity annotations. UncivGame.settings / GUI are UI concerns and dropped.
// Tile.GetCity() is typed by its seam; the owning city is always a City, hence the casts.
public static class UnitActionsFromUniques
{
    internal static IEnumerable<UnitAction> GetFoundCityActions(MapUnitClass unit, Tile tile)
    {
        var action = GetFoundCityAction(unit, tile);
        if (action != null) yield return action;
    }

    /// <summary>
    /// Produce a <see cref="UnitAction"/> for founding a city.
    /// </summary>
    /// <param name="unit">The unit to do the founding.</param>
    /// <param name="tile">The tile to found a city on.</param>
    /// <returns>
    /// <c>null</c> if impossible (the unit lacks the ability to found),
    /// or else a <see cref="UnitAction"/> 'defining' the founding.
    /// The <see cref="UnitAction.Action"/> field will be <c>null</c> if the action cannot be done here and now
    /// (no movement left, too close to another city).
    /// </returns>
    internal static UnitAction? GetFoundCityAction(MapUnitClass unit, Tile tile)
    {
        // FoundPuppetCity is to found a puppet city for modding.
        var unique = UnitActionModifiers.GetUsableUnitActionUniques(unit, UniqueType.FoundCity).FirstOrDefault()
            ?? UnitActionModifiers.GetUsableUnitActionUniques(unit, UniqueType.FoundPuppetCity).FirstOrDefault();
        if (unique == null) return null;

        if (tile.IsWater || tile.IsImpassible()) return null;
        // Spain should still be able to build Conquistadors in a one city challenge - but can't settle them
        if (unit.Civ.IsOneCityChallenger() && unit.Civ.HasEverOwnedOriginalCapital) return null;

        if (!unit.HasMovement() || !tile.CanBeSettled(unit.Civ))
            return new UnitAction(UnitActionType.FoundCity, associatedUnique: unique, action: null);

        bool hasActionModifiers = unique.Modifiers.Any(it => it.Type?.TargetTypes().Contains(UniqueTarget.UnitActionModifier) == true);
        Action foundAction = () =>
        {
            // Note: UI tutorial task UncivGame.Current.settings.addCompletedTutorialTask("Found city") is dropped
            // Get the city to be able to change it into puppet, for modding.
            var city = unit.Civ.AddCity(tile.Position, unit);

            if (hasActionModifiers) UnitActionModifiers.ActivateSideEffects(unit, unique);
            else unit.Destroy();

            // If unit has FoundPuppetCity make it into a puppet city.
            if (unique.Type == UniqueType.FoundPuppetCity)
            {
                city.IsPuppet = true;
            }
        };

        if (unit.Civ.PlayerType == PlayerType.AI)
            return new UnitAction(UnitActionType.FoundCity, associatedUnique: unique, action: foundAction);

        string title = hasActionModifiers
            ? UnitActionModifiers.ActionTextWithSideEffects(UnitActionType.FoundCity.Value(), unique, unit)
            : UnitActionType.FoundCity.Value();

        // check if we would be breaking a promise
        string? leadersPromisedNotToSettleNear = GetLeadersWePromisedNotToSettleNear(unit.Civ, tile);
        var confirmation = leadersPromisedNotToSettleNear == null
            ? null
            : new UnitActionConfirmation(
                $"Do you want to break your promise to [{leadersPromisedNotToSettleNear}]?",
                "Break promise");

        return new UnitAction(
            type: UnitActionType.FoundCity,
            title: title,
            associatedUnique: unique,
            action: UnitActionModifiers.CanActivateSideEffects(unit, unique) ? foundAction : null,
            confirmation: confirmation
        );
    }

    /// <summary>
    /// Checks whether a civ founding a city on a certain tile would break a promise.
    /// </summary>
    /// <param name="civInfo">The civilization trying to found a city</param>
    /// <param name="tile">The tile where the new city would go</param>
    /// <returns><c>null</c> if no promises broken, else a String listing the leader(s) we would p* off.</returns>
    private static string? GetLeadersWePromisedNotToSettleNear(CivilizationObject civInfo, Tile tile)
    {
        var leadersWePromisedNotToSettleNear = new HashSet<string>();
        foreach (var otherCiv in civInfo.GetKnownCivs().Where(it => it.IsMajorCiv() && !civInfo.IsAtWarWith(it)))
        {
            var diplomacyManager = otherCiv.GetDiplomacyManager(civInfo).NotNull();
            if (diplomacyManager.HasFlag(DiplomacyFlags.AgreedToNotSettleNearUs))
            {
                bool hasNearbyCity = otherCiv.Cities
                    .Where(it => it.GetCenterTile().AerialDistanceTo(tile) <= 6)
                    .Any(it => otherCiv.HasExplored(it.GetCenterTile()));
                if (hasNearbyCity)
                    leadersWePromisedNotToSettleNear.Add(otherCiv.GetLeaderDisplayName());
            }
        }
        return leadersWePromisedNotToSettleNear.Count == 0 ? null : string.Join(", ", leadersWePromisedNotToSettleNear);
    }

    internal static IEnumerable<UnitAction> GetSetupActions(MapUnitClass unit, Tile tile)
    {
        _ = tile;
        if (!unit.HasUnique(UniqueType.MustSetUp) || unit.IsEmbarked()) yield break;
        bool isSetUp = unit.IsSetUpForSiege();
        yield return new UnitAction(
            UnitActionType.SetUp,
            isCurrentAction: isSetUp,
            action: unit.HasMovement() && !isSetUp
                ? () =>
                {
                    unit.Action = UnitActionType.SetUp.Value();
                    unit.UseMovementPoints(1f);
                }
        : null
        );
    }

    internal static IEnumerable<UnitAction> GetParadropActions(MapUnitClass unit, Tile tile)
    {
        _ = tile;
        unit.Cache.ParadropDestinationTileFilters.Clear();

        // Retrieve all paradrop uniques, considering the state of the unit
        var paradropUniques = unit.GetMatchingUniques(UniqueType.MayParadrop, unit.Cache.State);
        Unique? frequencyUnique = null;

        // Construct the list of possible destination tile filters, keeping the largest distance
        foreach (var unique in paradropUniques)
        {
            string tileFilter = unique.Params[0];
            int distance = unique.Params[1].ToInt();
            if (!unit.Cache.ParadropDestinationTileFilters.TryGetValue(tileFilter, out int existingDistance) || distance > existingDistance)
            {
                unit.Cache.ParadropDestinationTileFilters[tileFilter] = distance;
                frequencyUnique = unique;
            }
        }
        if (unit.Cache.ParadropDestinationTileFilters.Count == 0) yield break;

        yield return new UnitAction(
            UnitActionType.Paradrop,
            isCurrentAction: unit.IsPreparingParadrop(),
            associatedUnique: frequencyUnique,
            action: !unit.HasUnitMovedThisTurn()
                ? () =>
                {
                    if (unit.IsPreparingParadrop()) unit.Action = null;
                    else unit.Action = UnitActionType.Paradrop.Value();
                }
        : null
        );
    }

    internal static IEnumerable<UnitAction> GetAirSweepActions(MapUnitClass unit, Tile tile)
    {
        _ = tile;
        var airsweepUnique = unit.GetMatchingUniques(UniqueType.CanAirsweep).FirstOrDefault();
        if (airsweepUnique == null) yield break;

        yield return new UnitAction(
            UnitActionType.AirSweep,
            isCurrentAction: unit.IsPreparingAirSweep(),
            associatedUnique: airsweepUnique,
            action: unit.CanAttack()
                ? () =>
                {
                    if (unit.IsPreparingAirSweep()) unit.Action = null;
                    else unit.Action = UnitActionType.AirSweep.Value();
                }
        : null
        );
    }

    // Instead of Withdrawing, stand your ground!
    // Different than Fortify
    internal static IEnumerable<UnitAction> GetGuardActions(MapUnitClass unit, Tile tile)
    {
        _ = tile;
        var unique = unit.GetMatchingUniques(UniqueType.WithdrawsBeforeMeleeCombat).FirstOrDefault();
        if (unique == null) yield break;

        if (unit.IsGuarding())
        {
            string title = unit.CanFortify() ? $"{"Guarding".Tr()} {unit.GetFortificationTurns() * 20}%" : "Guarding".Tr();
            yield return new UnitAction(
                UnitActionType.Guard,
                isCurrentAction: true,
                title: title,
                associatedUnique: unique
            );
            yield break;
        }

        if (!unit.HasMovement()) yield break;

        yield return new UnitAction(
            UnitActionType.Guard,
            associatedUnique: unique,
            action: !unit.IsGuarding()
                ? () => unit.Action = UnitActionType.Guard.Value()
                : null
        );
    }

    internal static IEnumerable<UnitAction> GetTriggerUniqueActions(MapUnitClass unit, Tile tile)
    {
        _ = tile;
        foreach (var unique in unit.GetUniques())
        {
            // not a unit action
            if (!unique.Modifiers.Any(it => it.Type?.TargetTypes().Contains(UniqueTarget.UnitActionModifier) == true)) continue;
            // has a dedicated action handler (e.g. ConstructImprovementInstantly → CreateImprovement)
            if (unique.Type?.TargetTypes().Contains(UniqueTarget.UnitAction) == true) continue;
            // extends an existing unit action
            if (unique.HasModifier(UniqueType.UnitActionExtraLimitedTimes)) continue;
            if (!unique.IsTriggerable) continue;
            if (!unique.ConditionalsApply(unit.Cache.State)) continue;
            if (!UnitActionModifiers.CanUse(unit, unique)) continue;

            string baseTitle;
            switch (unique.Type)
            {
                case UniqueType.OneTimeEnterGoldenAgeTurns:
                    baseTitle = unique.PlaceholderText.FillPlaceholders(
                        unit.Civ.GoldenAges.CalculateGoldenAgeLength(
                            unique.Params[0].ToInt()).ToString().Tr());
                    break;
                case UniqueType.OneTimeGainStat:
                    if (unique.HasModifier(UniqueType.ModifiedByGameSpeed))
                    {
                        string statName = unique.Params[1];
                        var stat = StatExtensions.SafeValueOf(statName);
                        float modifier = stat != null && unit.Civ.GameInfo.Speed.StatCostModifiers.TryGetValue(stat.Value, out float mod)
                            ? mod
                            : unit.Civ.GameInfo.Speed.Modifier;
                        int amount = (int)(unique.Params[0].ToInt() * modifier);
                        baseTitle = UniqueType.OneTimeGainStat.PlaceholderText().FillPlaceholders(
                            amount.ToString().Tr(), statName
                        );
                    }
                    else
                    {
                        baseTitle = unique.Text.RemoveConditionals();
                    }
                    break;
                case UniqueType.OneTimeGainStatRange:
                    {
                        string statName = unique.Params[2];
                        var stat = StatExtensions.SafeValueOf(statName);
                        float modifier = stat != null && unit.Civ.GameInfo.Speed.StatCostModifiers.TryGetValue(stat.Value, out float mod)
                            ? mod
                            : unit.Civ.GameInfo.Speed.Modifier;
                        int amount1 = (int)(unique.Params[0].ToInt() * modifier);
                        int amount2 = (int)(unique.Params[1].ToInt() * modifier);
                        baseTitle = unique.PlaceholderText.FillPlaceholders(
                            amount1.ToString().Tr(),
                            amount2.ToString().Tr(),
                            statName
                        );
                    }
                    break;
                case UniqueType.TriggerEvent:
                    baseTitle = unique.Params[0];
                    break;
                default:
                    baseTitle = unique.Text.RemoveConditionals();
                    break;
            }
            string title = UnitActionModifiers.ActionTextWithSideEffects(baseTitle, unique, unit);

            Action? unitAction = null;
            if (unit.HasMovement())
            {
                var triggerFunction = UniqueTriggerActivation.GetTriggerFunction(unique, unit.Civ, unit: unit, tile: unit.CurrentTile);
                if (triggerFunction != null)
                {
                    unitAction = () =>
                    {
                        int multiplier = unique.GetUniqueMultiplier(unit.Cache.State);
                        for (int i = 0; i < multiplier; i++)
                        {
                            triggerFunction();
                        }
                        UnitActionModifiers.ActivateSideEffects(unit, unique);
                    };
                }
            }

            yield return new UnitAction(
                UnitActionType.TriggerUnique,
                title,
                associatedUnique: unique,
                action: unitAction != null && UnitActionModifiers.CanActivateSideEffects(unit, unique) ? unitAction : null
            );
        }
    }

    internal static IEnumerable<UnitAction> GetAddInCapitalActions(MapUnitClass unit, Tile tile)
    {
        var unique = unit.GetMatchingUniques(UniqueType.AddInCapital).FirstOrDefault();
        if (unique == null) yield break;

        yield return new UnitAction(
            UnitActionType.AddInCapital,
            title: $"Add to [{unique.Params[0]}]",
            associatedUnique: unique,
            action: tile.IsCityCenter()
                && ((CityObject)tile.GetCity().NotNull()).IsCapital()
                && ReferenceEquals(((CityObject)tile.GetCity().NotNull()).Civ, unit.Civ)
                ? () =>
                {
                    unit.Civ.VictoryManager.CurrentsSpaceshipParts.Add(unit.Name, 1);
                    unit.Destroy();
                }
        : null
        );
    }

    internal static IEnumerable<UnitAction> GetImprovementCreationActions(MapUnitClass unit, Tile tile)
    {
        var waterImprovementAction = GetWaterImprovementAction(unit, tile);
        if (waterImprovementAction != null) yield return waterImprovementAction;
        foreach (var action in GetImprovementConstructionActionsFromGeneralUnique(unit, tile))
            yield return action;
    }

    private static UnitAction? GetWaterImprovementAction(MapUnitClass unit, Tile tile)
    {
        var unique = unit.GetMatchingUniques(UniqueType.CreateWaterImprovements).FirstOrDefault();
        if (!tile.IsWater || unique == null) return null;

        var improvement = tile.TileResource?.GetImprovingImprovement(tile, unit.Cache.State);
        if (improvement == null) return null;
        if (!tile.ImprovementFunctions.CanBuildImprovement(improvement, unit.Cache.State)) return null;

        return new UnitAction(
            UnitActionType.CreateImprovement,
            $"Create [{improvement.Name}]",
            associatedUnique: unique,
            key: improvement.Name,
            action: unit.HasMovement()
                ? () =>
                {
                    tile.SetImprovement(improvement, unit.Civ, unit);
                    unit.Destroy(); // Modders may wish for a nondestructive way, but that should be another Unique
                }
        : null
        );
    }

    // Not internal: Used in SpecificUnitAutomation
    public static IEnumerable<UnitAction> GetImprovementConstructionActionsFromGeneralUnique(MapUnitClass unit, Tile tile)
    {
        var uniquesToCheck = UnitActionModifiers.GetUsableUnitActionUniques(unit, UniqueType.ConstructImprovementInstantly);

        var civResources = unit.Civ.GetCivResourcesByName();
        var gameContext = new GameContext(civInfo: unit.Civ, unit: unit, tile: tile);

        foreach (var unique in uniquesToCheck)
        {
            string improvementFilter = unique.Params[0];
            var improvements = tile.Ruleset.TileImprovements.Values.Where(it => it.MatchesFilter(improvementFilter, gameContext));

            foreach (var improvement in improvements)
            {
                // Try to skip Improvements we can never build
                // (getImprovementBuildingProblems catches those so the button is always disabled, but it nevertheless looks nicer)
                if (tile.ImprovementFunctions.GetImprovementBuildingProblems(improvement, gameContext).Any(it => it.IsPermanent()))
                    continue;

                bool resourcesAvailable = ((IHasUniques)improvement).GetMatchingUniques(UniqueType.ConsumesResources).All(improvementUnique =>
                    (civResources.TryGetValue(improvementUnique.Params[1], out int count) ? count : 0) >= improvementUnique.Params[0].ToInt());

                yield return new UnitAction(
                    UnitActionType.CreateImprovement,
                    title: UnitActionModifiers.ActionTextWithSideEffects(
                        $"Create [{improvement.Name}]",
                        unique,
                        unit
                    ),
                    associatedUnique: unique,
                    key: improvement.Name,
                    action: resourcesAvailable
                        && unit.HasMovement()
                        && tile.ImprovementFunctions.CanBuildImprovement(improvement, unit.Cache.State)
                        // Next test is to prevent interfering with UniqueType.CreatesOneImprovement -
                        // not pretty, but users *can* remove the building from the city queue an thus clear this:
                        && !tile.IsMarkedForCreatesOneImprovement()
                        && UnitActionModifiers.CanActivateSideEffects(unit, unique)
                        ? () =>
                        {
                            var unitTile = unit.GetTile();
                            unitTile.SetImprovement(improvement, unit.Civ, unit);

                            unit.Civ.Cache.UpdateViewableTiles(); // to update 'last seen improvement'

                            UnitActionModifiers.ActivateSideEffects(unit, unique);
                        }
                : null
                );
            }
        }
    }

    internal static IEnumerable<UnitAction> GetTransformActions(MapUnitClass unit, Tile tile)
    {
        _ = tile;
        var unitTile = unit.GetTile();
        var civInfo = unit.Civ;
        var stateForConditionals = unit.Cache.State;

        foreach (var unique in unit.GetMatchingUniques(UniqueType.CanTransform, stateForConditionals))
        {
            var unitToTransformTo = civInfo.GetEquivalentUnit(unique.Params[0]);

            // Respect OnlyAvailable criteria
            if (((IHasUniques)unitToTransformTo).GetMatchingUniques(
                    UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState
                ).Any(it => !it.ConditionalsApply(stateForConditionals)))
                continue;

            // Respect Unavailable criteria
            if (((IHasUniques)unitToTransformTo).GetMatchingUniques(UniqueType.Unavailable, stateForConditionals).Any())
                continue;

            // Check _new_ resource requirements
            // Using Counter to aggregate is a bit exaggerated, but - respect the mad modder.
            var resourceRequirementsDelta = new Counter<string>();
            foreach (var (resource, amount) in unit.GetResourceRequirementsPerTurn())
                resourceRequirementsDelta.Add(resource, -amount);
            foreach (var (resource, amount) in unitToTransformTo.GetResourceRequirementsPerTurn(unit.Cache.State))
                resourceRequirementsDelta.Add(resource, amount);
            string newResourceRequirementsString = string.Join(", ", resourceRequirementsDelta
                .Where(it => it.Value > 0)
                .Select(it => $"{it.Value} {{{it.Key}}}".Tr()));

            string title = $"Transform to [{unitToTransformTo.Name}] ";
            title += UnitActionModifiers.GetSideEffectString(unit, unique, true);
            if (newResourceRequirementsString.Length > 0)
                title += $"\n([{newResourceRequirementsString}])";

            yield return new UnitAction(
                UnitActionType.Transform,
                title: title,
                associatedUnique: unique,
                action: !unit.IsEmbarked() && UnitActionModifiers.CanActivateSideEffects(unit, unique)
                    ? () =>
                    {
                        float oldMovement = unit.CurrentMovement;
                        unit.Destroy(destroyTransportedUnit: false);
                        var newUnit = civInfo.Units.PlaceUnitNearTile(unitTile.Position, unitToTransformTo, unit.Id, copiedFrom: unit);

                        /* We were UNABLE to place the new unit, which means that the unit failed to upgrade!
                         * The only known cause of this currently is "land units upgrading to water units" which fail to be placed.
                         */
                        if (newUnit == null)
                        {
                            var resurrectedUnit = civInfo.Units.PlaceUnitNearTile(unitTile.Position, unit.BaseUnit, unit.Id, copiedFrom: unit).NotNull();
                            _ = resurrectedUnit;
                        }
                        else // Managed to upgrade
                        {
                            unit.Movement.TeleportTransportedUnitsTo(unitTile, newUnit.CurrentTile);
                            // have to handle movement manually because we killed the old unit
                            // a .destroy() unit has 0 movement
                            // and a new one may have less Max Movement
                            newUnit.CurrentMovement = oldMovement;
                            // adjust if newUnit has lower Max Movement
                            if ((int)newUnit.CurrentMovement > newUnit.GetMaxMovement())
                                newUnit.CurrentMovement = (float)newUnit.GetMaxMovement();
                            // execute any side effects, Stat and Movement adjustments
                            UnitActionModifiers.ActivateSideEffects(newUnit, unique, true);
                        }
                    }
            : null
            );
        }
    }

    internal static int GetRepairTurns(MapUnitClass unit)
    {
        var tile = unit.CurrentTile;
        if (!tile.IsPillaged()) return 0;
        if (tile.ImprovementInProgress == Constants.Repair) return tile.TurnsToImprovement;
        int repairTurns = tile.Ruleset.TileImprovements[Constants.Repair].GetTurnsToBuild(unit.Civ, unit);

        var pillagedImprovement = tile.GetImprovementToRepair().NotNull();
        int turnsToBuild = pillagedImprovement.GetTurnsToBuild(unit.Civ, unit);
        // cap repair to number of turns to build original improvement
        return Math.Min(repairTurns, turnsToBuild);
    }

    internal static IEnumerable<UnitAction> GetRepairActions(MapUnitClass unit, Tile tile)
    {
        _ = tile;
        var action = GetRepairAction(unit);
        if (action != null) yield return action;
    }

    // Public - used in WorkerAutomation
    public static UnitAction? GetRepairAction(MapUnitClass unit)
    {
        if (!unit.CurrentTile.Ruleset.TileImprovements.ContainsKey(Constants.Repair)) return null;
        if (!unit.Cache.HasUniqueToBuildImprovements) return null;
        if (unit.IsEmbarked()) return null;
        var tile = unit.GetTile();
        if (tile.IsCityCenter()) return null;
        if (!tile.IsPillaged()) return null;
        var uniques = unit.GetMatchingUniques(UniqueType.BuildImprovements).ToList();
        var improvement = tile.GetImprovementToRepair().NotNull();
        var civ = unit.Civ;
        if (!uniques.Any(unique =>
            // Engage the MultiFilter on the entire filter, prior to checking the individual filters
            MultiFilter.Filter(unique.Params[0], it =>
                improvement.MatchesFilter(it, tile.StateThisTile) || tile.MatchesTerrainFilter(it, civ)
            )))
            return null;

        bool couldConstruct = unit.HasMovement()
            && !tile.IsCityCenter() && tile.ImprovementInProgress != Constants.Repair
            && !tile.IsEnemyTerritory(unit.Civ)
            // Are there any other improvement building problems that should block repair?
            && tile.ImprovementFunctions.GetImprovementBuildingProblems(improvement, new GameContext(civInfo: unit.Civ, unit: unit, tile: tile))
                .All(it => it != ImprovementBuildingProblem.OutsideBorders);

        int turnsToBuild = GetRepairTurns(unit);

        return new UnitAction(
            UnitActionType.Repair,
            title: $"{UnitActionType.Repair} [{unit.CurrentTile.GetImprovementToRepair().NotNull().Name}] - [{turnsToBuild}{Fonts.Turn}]",
            associatedUnique: uniques.First(),
            action: couldConstruct
                ? () =>
                {
                    tile.QueueImprovement(Constants.Repair, turnsToBuild);
                    unit.Action = null;
                }
        : null
        );
    }
}
