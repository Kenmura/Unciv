using Unciv.Models.Ruleset.Tile;
using Unciv.Utils;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Unit;

// T158: the two companion-object members of Kotlin `WorkerAutomation` (owner: T092) that ConstructionAutomation's workboat check calls.
// They are small and pure, so they are ported faithfully here (and tested); T092 moves them into WorkerAutomation and deletes this file.
internal static class WorkerAutomationSeams
{
    /// <summary>
    /// Kotlin <c>WorkerAutomation.hasWorkableSeaResource(tile, civInfo)</c>. TODO(T092)
    /// Does not check tile ownership - the caller must do so. A tile without resource gives false.
    /// </summary>
    internal static bool HasWorkableSeaResource(TileClass tile, CivilizationClass civInfo)
    {
        var resource = tile.TileResource;
        if (!tile.IsWater) return false;
        if (resource == null || !civInfo.CanSeeResource(resource)) return false; // Kotlin: canSeeResource(null) is false
        if (tile.Improvement != null && resource.IsImprovedBy(tile.Improvement)) return false;
        return resource.GetImprovements().Any(it =>
        {
            var improvement = civInfo.GameInfo.Ruleset.TileImprovements[it];
            return tile.ImprovementFunctions.CanBuildImprovement(improvement, civInfo.State);
        });
    }

    /// <summary>
    /// Kotlin <c>WorkerAutomation.isNotBonusResourceOrWorkable(tile, civInfo)</c>. TODO(T092)
    /// Throws on tiles without a resource, like the Kotlin <c>!!</c>.
    /// </summary>
    internal static bool IsNotBonusResourceOrWorkable(TileClass tile, CivilizationClass civInfo) =>
        tile.TileResource.NotNull().ResourceType != ResourceType.Bonus
        || civInfo.Cities.Any(it => it.TilesInRange.Contains(tile));
}
