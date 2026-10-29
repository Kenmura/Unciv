using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/ImprovementFunctions.kt (T054).
// Stripped: the purity annotations. The civilization is the IGameContextCivilization seam.

public static class ImprovementFunctions
{
    /// <summary>Generates a sequence of reasons that prevent building given <paramref name="improvement"/>.
    /// If the sequence is empty, improvement can be built immediately.</summary>
    public static IEnumerable<ImprovementBuildingProblem> GetImprovementBuildingProblems(TileImprovement improvement, GameContext gameContext, Tile? tile = null)
    {
        var improvementUniques = (IHasUniques)improvement;
        var civInfo = gameContext.CivInfo;
        if (civInfo != null)
        {
            if (improvement.UniqueTo != null && !civInfo.MatchesFilter(improvement.UniqueTo, null))
                yield return ImprovementBuildingProblem.WrongCiv;
            if (civInfo.UniqueImprovements.Any(it => it.Replaces == improvement.Name))
                yield return ImprovementBuildingProblem.Replaced;
            if (improvement.TechRequired != null && !civInfo.IsTechResearched(improvement.TechRequired))
                yield return ImprovementBuildingProblem.MissingTech;
            if (improvementUniques.GetMatchingUniques(UniqueType.Unbuildable, GameContext.IgnoreConditionalsState)
                    .Any(it => it.Modifiers.Count == 0))
                yield return ImprovementBuildingProblem.Unbuildable;
            else if (improvementUniques.HasUnique(UniqueType.Unbuildable, gameContext))
                yield return ImprovementBuildingProblem.ConditionallyUnbuildable;

            if (improvementUniques.HasUnique(UniqueType.Unavailable, gameContext))
                yield return ImprovementBuildingProblem.ConditionallyUnbuildable;

            if (improvementUniques.GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState)
                    .Any(it => !it.ConditionalsApply(gameContext)))
                yield return ImprovementBuildingProblem.UnmetConditional;

            if (improvementUniques.GetMatchingUniques(UniqueType.ObsoleteWith, gameContext)
                    .Any(it => civInfo.IsTechResearched(it.Params[0])))
                yield return ImprovementBuildingProblem.Obsolete;

            if (improvementUniques.GetMatchingUniques(UniqueType.ConsumesResources, gameContext)
                    .Any(it => civInfo.GetResourceAmount(it.Params[1]) < int.Parse(it.Params[0], System.Globalization.CultureInfo.InvariantCulture)))
                yield return ImprovementBuildingProblem.MissingResources;

            if (improvement.GetStockpiledResourceRequirements(gameContext)
                    .Any(it => civInfo.GetResourceAmount(it.Key) < it.Value))
                yield return ImprovementBuildingProblem.MissingResources;

            if (tile != null)
            {
                // B004 (bnw-spec 3.2): "Can only be built in City-State territory" (Feitoria) replaces the own-border rule,
                // "Can be built in any civ's territory" (Landmark, Archaeological Dig) lifts it.
                if (improvementUniques.HasUnique(UniqueType.OnlyInCityStateTerritory, gameContext))
                {
                    if (tile.GetOwner() is not { IsCityState: true })
                        yield return ImprovementBuildingProblem.OutsideBorders;
                }
                else if (tile.GetOwner() != civInfo
                    && !improvementUniques.HasUnique(UniqueType.IgnoresTileOwnership, gameContext)
                    && !improvementUniques.HasUnique(UniqueType.CanBuildOutsideBorders, gameContext)
                    && (!improvementUniques.HasUnique(UniqueType.CanBuildJustOutsideBorders, gameContext)
                        || tile.Neighbors.All(it => it.GetOwner() != civInfo)))
                    yield return ImprovementBuildingProblem.OutsideBorders;

                var knownFeatureRemovals = tile.Ruleset.NonRoadTileRemovals
                    .Where(rulesetImprovement =>
                        rulesetImprovement.TechRequired == null || civInfo.IsTechResearched(rulesetImprovement.TechRequired))
                    .ToList();

                if (!tile.ImprovementFunctions.CanImprovementBeBuiltHere(improvement, civInfo.CanSeeResource(tile.TileResource), knownFeatureRemovals, gameContext))
                    // There are way too many conditions in that functions, besides, they are not interesting
                    // at least for the current usecases. Improve if really needed.
                    yield return ImprovementBuildingProblem.Other;
            }
        }
        else
        {
            yield return ImprovementBuildingProblem.WrongCiv;
        }
    }
}
