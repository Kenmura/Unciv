using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityClass = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileImprovementObject = Unciv.Models.Ruleset.Tile.TileImprovement;

namespace Unciv.Logic.Map.Tiles;

// Ported from core/src/com/unciv/logic/map/tile/TileImprovementFunctions.kt (T054).
// Stripped: the purity annotations. Civilization, City and MapUnit are the seams of GameContextTileFunctions.cs.

/// <summary>Reason why an Improvement cannot be built by a given civ</summary>
public enum ImprovementBuildingProblem
{
    Replaced,
    WrongCiv,
    MissingTech,
    Unbuildable,
    ConditionallyUnbuildable,
    NotJustOutsideBorders,
    OutsideBorders,
    UnmetConditional,
    Obsolete,
    MissingResources,
    Other
}

public static class ImprovementBuildingProblemExtensions
{
    /// <summary><c>true</c> if this cannot change on the future of this game</summary>
    public static bool IsPermanent(this ImprovementBuildingProblem problem) => problem is
        ImprovementBuildingProblem.Replaced or ImprovementBuildingProblem.WrongCiv or ImprovementBuildingProblem.Unbuildable
        or ImprovementBuildingProblem.Obsolete;

    /// <summary><c>true</c> if the ImprovementPicker should report this problem</summary>
    public static bool IsReportable(this ImprovementBuildingProblem problem) => problem is
        ImprovementBuildingProblem.MissingTech or ImprovementBuildingProblem.NotJustOutsideBorders
        or ImprovementBuildingProblem.OutsideBorders or ImprovementBuildingProblem.MissingResources;
}

public class TileImprovementFunctions(Tile tile)
{
    public Tile Tile { get; } = tile;

    /// <summary>Returns true if the <paramref name="improvement"/> can be built on this <see cref="Tile"/></summary>
    public bool CanBuildImprovement(TileImprovementObject improvement, GameContext gameContext) =>
        !GetImprovementBuildingProblems(improvement, gameContext).Any();

    /// <summary>Generates a sequence of reasons that prevent building given <paramref name="improvement"/>.
    /// If the sequence is empty, improvement can be built immediately.</summary>
    public IEnumerable<ImprovementBuildingProblem> GetImprovementBuildingProblems(TileImprovementObject improvement, GameContext gameContext) =>
        ImprovementFunctions.GetImprovementBuildingProblems(improvement, gameContext, Tile);

    /// <summary>Without regards to what CivInfo it is (so no tech requirement check), a lot of the checks are just for the improvement on the tile.
    /// Doubles as a check for the map editor.</summary>
    /// <param name="improvement">the improvement</param>
    /// <param name="knownFeatureRemovals">feature removals the civ knows, or <c>null</c></param>
    /// <param name="isNormalizeCheck">called from <see cref="TileNormalizer"/></param>
    /// <param name="resourceIsVisible">Kotlin default: <c>tile.resource != null</c></param>
    /// <param name="gameContext">Kotlin default: <c>GameContext(tile=tile)</c></param>
    internal bool CanImprovementBeBuiltHere(
        TileImprovementObject improvement,
        bool? resourceIsVisible = null,
        IReadOnlyList<TileImprovementObject>? knownFeatureRemovals = null,
        GameContext? gameContext = null,
        bool isNormalizeCheck = false)
    {
        var visible = resourceIsVisible ?? Tile.Resource != null;
        var context = gameContext ?? new GameContext(tile: Tile);

        bool CanBeBuiltOnThisUnbuildableTerrain(IReadOnlyList<TileImprovementObject>? removals)
        {
            var topTerrain = Tile.LastTerrain;
            // We can build if we are specifically allowed to build on this terrain
            if (improvement.IsAllowedOnFeature(topTerrain)) return true;

            // Otherwise, we can if this improvement removes the top terrain
            if (!((IHasUniques)improvement).HasUnique(UniqueType.RemovesFeaturesIfBuilt, context)) return false;
            if (removals == null || removals.Count == 0) return false;
            var featureRemovals = Tile.TerrainFeatures
                .Select(feature => Tile.Ruleset.TileRemovals.FirstOrDefault(it => it.Name == Constants.Remove + feature))
                .Where(it => it != null).Select(it => it.NotNull()).ToList();
            if (featureRemovals.Count == 0) return false;
            if (featureRemovals.Any(it => !removals.Contains(it))) return false;
            var clonedTile = Tile.Clone(addUnits: false);
            clonedTile.SetTerrainFeatures(Tile.TerrainFeatures.Where(feature =>
                !featureRemovals.Any(it => RemoveRemovePrefix(it.Name) == feature)).ToList());
            return clonedTile.ImprovementFunctions.CanImprovementBeBuiltHere(improvement, visible, removals, context);
        }

        var roadStatuses = Enum.GetValues<RoadStatus>();

        // Can't build if the improvement is already here
        if (improvement.Name == Tile.Improvement && !isNormalizeCheck) return false;
        if (Tile.IsCityCenter()) return isNormalizeCheck && improvement.Name == Constants.CityCenter;

        // First we handle a few special improvements

        // Can only cancel if there is actually an improvement being built
        if (improvement.Name == Constants.CancelImprovementOrder) return Tile.ImprovementInProgress != null;
        // Can only remove roads if that road is actually there
        if (roadStatuses.Any(it => it.GetRemoveAction() == improvement.Name)) return Tile.RoadStatus.GetRemoveAction() == improvement.Name;
        // Can only remove features or improvement if that feature/improvement is actually there
        if (improvement.Name.StartsWith(Constants.Remove, StringComparison.Ordinal))
            return Tile.TerrainFeatures.Any(it => Constants.Remove + it == improvement.Name)
                || Constants.Remove + Tile.Improvement == improvement.Name;
        // Can only build roads if on land and they are better than the current road
        if (roadStatuses.Any(it => it.ToString() == improvement.Name))
            return !Tile.IsWater && Enum.Parse<RoadStatus>(improvement.Name) > Tile.RoadStatus;

        // Then we check if there is any reason to not allow this improvement to be built

        // Can't build if there is already an irremovable improvement here
        if (Tile.TileImprovement != null && ((IHasUniques)Tile.TileImprovement).HasUnique(UniqueType.Irremovable, context)) return false;

        // Can't build if this terrain is unbuildable, except when we are specifically allowed to
        if (Tile.LastTerrain.Unbuildable && !CanBeBuiltOnThisUnbuildableTerrain(knownFeatureRemovals)) return false;

        // Can't build if any terrain specifically prevents building this improvement
        var restricted = Tile.GetTerrainMatchingUniques(UniqueType.RestrictedBuildableImprovements, context).ToList();
        if (restricted.Count > 0 && !restricted.Any(unique => improvement.MatchesFilter(unique.Params[0], new GameContext(tile: Tile))))
            return false;

        // Can't build if the improvement specifically prevents building on some present feature
        if (((IHasUniques)improvement).GetMatchingUniques(UniqueType.CannotBuildOnTile, context)
                .Any(unique => Tile.MatchesFilter(unique.Params[0], context.CivInfo)))
            return false;

        // Can't build if an improvement is only allowed to be built on specific tiles and this is not one of them
        // If multiple uniques of this type exists, we want all to match (e.g. Hill _and_ Forest would be meaningful)
        var onlyOnTile = ((IHasUniques)improvement).GetMatchingUniques(UniqueType.CanOnlyBeBuiltOnTile, context).ToList();
        if (onlyOnTile.Count > 0 && onlyOnTile.Any(unique => !Tile.MatchesFilter(unique.Params[0], context.CivInfo)))
            return false;

        // Can't build if the improvement requires an adjacent terrain that is not present
        if (((IHasUniques)improvement).GetMatchingUniques(UniqueType.MustBeNextTo, context)
                .Any(it => !Tile.IsAdjacentTo(it.Params[0])))
            return false;

        // B004 (bnw-spec 3.2): Chateau must touch a luxury and no other improvement of its kind; some improvements need land neighbours.
        if (((IHasUniques)improvement).HasUnique(UniqueType.MustBeAdjacentToLuxury, context)
                && !Tile.Neighbors.Any(it => it.TileResource is { ResourceType: ResourceType.Luxury }))
            return false;
        if (((IHasUniques)improvement).GetMatchingUniques(UniqueType.NoTwoAdjacent, context)
                .Any(it => Tile.Neighbors.Any(neighbor => neighbor.TileImprovement is { } other && other.MatchesFilter(it.Params[0], context))))
            return false;
        if (((IHasUniques)improvement).GetMatchingUniques(UniqueType.RequiresAdjacentLand, context)
                .Any(it => Tile.Neighbors.Count(neighbor => neighbor.IsLand) < int.Parse(it.Params[0], System.Globalization.CultureInfo.InvariantCulture)))
            return false;

        // Can't build it if it is only allowed to improve resources and it doesn't improve this resource
        if (((IHasUniques)improvement).HasUnique(UniqueType.CanOnlyImproveResource, context)
                && (!visible || !Tile.TileResource.NotNull().IsImprovedBy(improvement.Name)))
            return false;

        // At this point we know this is a normal improvement and that there is no reason not to allow it to be built.

        // Lastly we check if the improvement may be built on this terrain or resource
        if (improvement.IsAllowedOnFeature(Tile.LastTerrain)) return true;
        if (Tile.IsLand && improvement.CanBeBuiltOn("Land")) return true;
        if (Tile.IsWater && improvement.CanBeBuiltOn("Water")) return true;
        // DO NOT reverse this &&. isAdjacentToFreshwater() is a lazy which calls a function, and reversing it breaks the tests.
        if (((IHasUniques)improvement).HasUnique(UniqueType.ImprovementBuildableByFreshWater, context)
                && Tile.IsAdjacentTo(Constants.FreshWater))
            return true;

        // I don't particularly like this check, but it is required to build mines on non-hill resources
        if (visible && Tile.TileResource.NotNull().IsImprovedBy(improvement.Name)
                && ExtendedDomainCheck(improvement))
            return true;
        // No reason this improvement should be built here, so can't build it
        return false;
    }

    /// <summary>Helper for the "build mines on non-hill resources" branch above:
    /// Verifies not to allow land improvements on water and vice versa.</summary>
    public bool ExtendedDomainCheck(TileImprovementObject improvement)
    {
        // Don't check when there's no terrain and CanOnlyImproveResource is the only reason allowing the improvement
        if (improvement.TerrainsCanBeBuiltOn.Count == 0) return true;
        // Resolve all terrainsCanBeBuiltOn entries to a TerrainType, using `occursOn` for TerrainFeatures
        var allowedTypes = new HashSet<TerrainType>();
        foreach (var canBuildOn in improvement.TerrainsCanBeBuiltOn)
        {
            var type = Enum.GetValues<TerrainType>().Cast<TerrainType?>().FirstOrDefault(it => it.ToString() == canBuildOn);
            if (type != null) allowedTypes.Add(type.Value);
            var terrain = Tile.Ruleset.Terrains.GetValueOrDefault(canBuildOn);
            if (terrain == null) continue;
            allowedTypes.Add(terrain.Type);
            foreach (var occursOn in terrain.OccursOn)
            {
                var baseTerrain = Tile.Ruleset.Terrains.GetValueOrDefault(occursOn);
                if (baseTerrain == null) continue;
                allowedTypes.Add(baseTerrain.Type);
            }
        }
        return (Tile.IsLand && allowedTypes.Contains(TerrainType.Land))
            || (Tile.IsWater && allowedTypes.Contains(TerrainType.Water));
    }

    /// <param name="improvementName">name of the improvement</param>
    /// <param name="civToActivateBroaderEffects">For road assignment and taking over tiles - DO NOT pass when simulating improvement effects!</param>
    /// <param name="unit">the unit building it, if any</param>
    public void SetImprovement(string improvementName, IGameContextCivilization? civToActivateBroaderEffects = null, IGameContextUnit? unit = null)
    {
        var improvementObject = Tile.Ruleset.TileImprovements.GetValueOrDefault(improvementName);
        SetImprovement(improvementObject, civToActivateBroaderEffects, unit);
    }

    /// <param name="improvement">the improvement, or <c>null</c> to remove it</param>
    /// <param name="civToActivateBroaderEffects">For road assignment and taking over tiles - DO NOT pass when simulating improvement effects!</param>
    /// <param name="unit">the unit building it, if any</param>
    public void SetImprovement(TileImprovementObject? improvement, IGameContextCivilization? civToActivateBroaderEffects = null, IGameContextUnit? unit = null)
    {
        void UpdateVisibility()
        {
            if (!Tile.TileMap.HasGameInfo()) return;
            // Update the separately-kept "what a civ sees" - unless in map editor where there are no civs
            foreach (var civ in Tile.TileMap.GameInfo.Civilizations)
            {
                if (civ.IsDefeated() || !civ.IsMajorCiv()) continue;
                if (civ == civToActivateBroaderEffects || Tile.IsVisible(civ))
                    civ.SetLastSeenImprovement(Tile.Position, improvement?.Name);
            }
        }

        void UpdateCity()
        {
            var city = Tile.OwningCity;
            if (city == null) return;
            if (civToActivateBroaderEffects == null) return;
            city.CityStats.Update();
            city.Civ.Cache.UpdateCivResources();
            city.ReassignPopulationDeferred();
        }

        if (improvement == null)
        {
            var wasEncampment = Tile.IsBarbarianEncampment();
            Tile.ImprovementIsPillaged = false;
            Tile.SetImprovementBasic((TileImprovementObject?)null);
            UpdateVisibility();
            UpdateCity();
            if (!wasEncampment) return;
            // Any barbarian encampment cleared outside MapUnit.clearEncampment should obsolete ClearBarbarianCamp quests
            if (Tile.TileMap.HasGameInfo()) // guard against removing encampments in map editor
                Tile.TileMap.GameInfo.HandleObsoleteGlobalQuestsOfAliveCityStates();
            return;
        }

        var improvementFieldHasChanged = false;
        if (improvement.Name.StartsWith(Constants.Remove, StringComparison.Ordinal))
            ActivateRemovalImprovement(improvement.Name, civToActivateBroaderEffects);
        else if (improvement.Name == RoadStatus.Road.ToString())
            Tile.SetRoadStatus(RoadStatus.Road, civToActivateBroaderEffects);
        else if (improvement.Name == RoadStatus.Railroad.ToString())
            Tile.SetRoadStatus(RoadStatus.Railroad, civToActivateBroaderEffects);
        else if (improvement.Name == Constants.Repair)
            Tile.SetRepaired();
        else
        {
            Tile.ImprovementIsPillaged = false;
            Tile.SetImprovementBasic(improvement);
            improvementFieldHasChanged = true;
            if (((IHasUniques)improvement).HasUnique(UniqueType.Irremovable) || Tile.IsMarkedForCreatesOneImprovement(improvement.Name))
            {
                // I'm not sure what would happen if we try to replace an irremovable improvement
                // Let's not cancel our "Districts" in progress unless when finishing it (don't mess it up with accidental worker movements etc.)
                RemoveCreatesOneImprovementMarker();
            }

            // relevant when the improvement was created instantly - such as Great Improvement
            Tile.ImprovementQueue.RemoveAll(it => Tile.Ruleset.TileImprovements.GetValueOrDefault(it.Improvement)?.IsRoad() == true);
        }

        if (improvementFieldHasChanged)
            UpdateVisibility();

        if (((IHasUniques)improvement).HasUnique(UniqueType.RemovesResourceWhenBuilt))
        {
            Tile.SetTileResource((string?)null);
            Tile.ResourceAmount = 0;
        }

        if (((IHasUniques)improvement).HasUnique(UniqueType.RemovesFeaturesIfBuilt))
        {
            // Remove terrainFeatures that a Worker can remove
            // and that aren't explicitly allowed under the improvement
            var removableTerrainFeatures = Tile.TerrainFeatureObjects.Where(feature =>
            {
                var removingAction = $"{Constants.Remove}{feature.Name}";

                return Tile.Ruleset.TileImprovements.ContainsKey(removingAction) // is removable
                    && !improvement.IsAllowedOnFeature(feature); // cannot coexist
            }).ToList();

            Tile.SetTerrainFeatures(Tile.TerrainFeatures.Where(feature => !removableTerrainFeatures.Any(it => it.Name == feature)).ToList());
        }

        if (civToActivateBroaderEffects != null)
            TriggerImprovementUniques(improvement, civToActivateBroaderEffects, unit);

        UpdateCity();
    }

    private void TriggerImprovementUniques(TileImprovementObject improvement, IGameContextCivilization civ, IGameContextUnit? unit = null)
    {
        var gameContext = new GameContext(civ, unit: unit, tile: Tile);

        foreach (var unique in improvement.UniqueObjects)
        {
            if (unique.HasTriggerConditional() || !unique.ConditionalsApply(gameContext)) continue;
            var multiplier = unique.GetUniqueMultiplier(gameContext); // evaluated once, like repeat()
            for (var i = 0; i < multiplier; i++)
                TriggerUnique(unique, civ, unit);
        }

        foreach (var unique in civ.GetTriggeredUniques(UniqueType.TriggerUponBuildingImprovement, gameContext,
                     it => improvement.MatchesFilter(it.Params[0], gameContext)))
            TriggerUnique(unique, civ, unit);

        if (unit == null) return;
        foreach (var unique in unit.GetTriggeredUniques(UniqueType.TriggerUponBuildingImprovement, gameContext,
                     it => improvement.MatchesFilter(it.Params[0], gameContext)))
            TriggerUnique(unique, civ, unit);
    }

    // The civ and unit arrive as their seams (Tile is ported before them); the only implementations are Civilization and MapUnit.
    private void TriggerUnique(Unique unique, IGameContextCivilization civ, IGameContextUnit? unit) =>
        UniqueTriggerActivation.TriggerUnique(unique, (Civilization.Civilization)civ, unit: (Unciv.Logic.Map.MapUnits.MapUnit?)unit, tile: Tile);

    private void ActivateRemovalImprovement(string improvementName, IGameContextCivilization? civToActivateBroaderEffects)
    {
        var removedFeatureName = RemoveRemovePrefix(improvementName);
        var currentTileImprovement = Tile.TileImprovement;
        // We removed a terrain (e.g. Forest) and the improvement (e.g. Lumber mill) requires it!
        if (currentTileImprovement != null && Tile.TerrainFeatures.Any(it =>
                currentTileImprovement.TerrainsCanBeBuiltOn.Contains(it) && it == removedFeatureName)
            && !currentTileImprovement.TerrainsCanBeBuiltOn.Contains(Tile.BaseTerrain)
        ) Tile.RemoveImprovement();

        if (Enum.GetValues<RoadStatus>().Any(it => improvementName == it.GetRemoveAction()))
        {
            Tile.RemoveRoad();
        }
        else if (Tile.Improvement == removedFeatureName) Tile.RemoveImprovement();
        else
        {
            var removedFeatureObject = Tile.Ruleset.Terrains.GetValueOrDefault(removedFeatureName);
            if (removedFeatureObject != null
                && civToActivateBroaderEffects != null
                && ((IHasUniques)removedFeatureObject).HasUnique(UniqueType.ProductionBonusWhenRemoved))
                TryProvideProductionToClosestCity(removedFeatureName, civToActivateBroaderEffects);

            Tile.RemoveTerrainFeature(removedFeatureName);
        }
    }

    private void TryProvideProductionToClosestCity(string removedTerrainFeature, IGameContextCivilization civ)
    {
        var cities = civ is CivilizationObject concreteCiv ? concreteCiv.Cities : civ.Cities.OfType<CityClass>();
        CityClass? closestCity = null;
        var closestDistance = 0;
        foreach (var city in cities) // minByOrNull: the first minimum wins
        {
            var d = city.GetCenterTile().AerialDistanceTo(Tile);
            if (closestCity == null || d < closestDistance)
            {
                closestCity = city;
                closestDistance = d;
            }
        }
        if (closestCity == null) return;
        var distance = closestCity.GetCenterTile().AerialDistanceTo(Tile);
        if (distance > 5) return;
        var stats = new Stats();
        foreach (var unique in Tile.GetTerrainMatchingUniques(UniqueType.ProductionBonusWhenRemoved))
        {
            var statsToAdd = unique.Stats;
            if (unique.IsModifiedByGameSpeed())
                statsToAdd *= civ.GameInfo.NotNull().Speed.Modifier;
            if (unique.IsModifiedByGameProgress())
                statsToAdd *= civ.GetGameProgressModifier(unique);
            stats.Add(statsToAdd);
        }
        if (stats.IsEmpty()) return;
        if (distance != 1) stats *= (6 - distance) / 4f;
        if (Tile.OwningCity == null || Tile.OwningCity.Civ != civ) stats *= 2 / 3f;
        foreach (var (stat, value) in stats)
        {
            closestCity.AddStat(stat, KotlinMath.FloatToInt(value));
            var locations = LocationAction.Of(Tile.Position, closestCity.Location);
            civ.AddNotification(
                $"Clearing a [{removedTerrainFeature}] has created [{stats.ToStringForNotifications()}] for [{closestCity.Name}]",
                locations, Notification.NotificationCategory.Production, NotificationIcon.Construction);
        }
    }

    /// <summary>Kotlin <c>removePrefix(Constants.remove)</c></summary>
    private static string RemoveRemovePrefix(string name) =>
        name.StartsWith(Constants.Remove, StringComparison.Ordinal) ? name[Constants.Remove.Length..] : name;

    /// <summary>Marks tile as target tile for a building with a <see cref="UniqueType.CreatesOneImprovement"/> unique</summary>
    public void MarkForCreatesOneImprovement(string improvement)
    {
        Tile.ImprovementQueue.Clear();
        Tile.QueueImprovement(improvement, -1);
    }

    /// <summary>Un-Marks a tile as target tile for a building with a <see cref="UniqueType.CreatesOneImprovement"/> unique.</summary>
    /// <param name="removeConstruction">whether to also remove the matching queued building.</param>
    public void RemoveCreatesOneImprovementMarker(bool removeConstruction = true)
    {
        if (!Tile.IsMarkedForCreatesOneImprovement()) return;
        var improvementInProgress = Tile.ImprovementInProgress
            ?? throw new InvalidOperationException(
                $"Cannot remove {nameof(UniqueType.CreatesOneImprovement)} marker from {Tile.Position} without an improvement in progress");
        Tile.ImprovementQueue.Clear();
        if (removeConstruction)
            Tile.OwningCity?.CityConstructions.RemoveCreateOneImprovementConstruction(improvementInProgress);
    }
}
