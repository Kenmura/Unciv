using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using Unciv.View;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/UnitAutomation.kt (exploration, fog busting, wandering, ruins, encampments).
public static partial class UnitAutomation
{
    private static int TilesInHexRadius(int radius) => 1 + 3 * radius * (radius + 1);

    private static bool IsGoodTileToExplore(MapUnitView unitView, AutomationCache cache, TileView tile, int unitVisibilityRange)
    {
        var civ = unitView.Civ();
        // These should be ordered by increasing computational cost
        return tile.GetOwner()?.IsCityState != true
            && (!civ.IsCityState || tile.GetExploredTilesAtDistance(1).Any(it => it.GetOwner()?.Equals(civ) == true))
            && unitView.GetDamageFromTerrain(tile) <= 0
            && tile.GetExploredTilesInDistance(unitVisibilityRange).Count() < TilesInHexRadius(unitVisibilityRange)
            && ThreatManager.GetDistanceToClosestEnemyUnit(civ, cache, tile, 3) > 3
            && unitView.CanMoveTo(tile)
            && unitView.CanReach(tile);
    }

    internal static bool TryExplore(MapUnitView unitView, AutomationCache cache)
    {
        if (TryGoToRuin(unitView) && (!unitView.HasMovement() || unitView.IsDestroyed())) return true;

        int unitVisibilityRange = unitView.GetVisibilityRange();
        var explorableTilesThisTurn = unitView.GetDistanceToTiles().Keys
            .Where(it => IsGoodTileToExplore(unitView, cache, it, unitVisibilityRange))
            .ToList();
        if (explorableTilesThisTurn.Count != 0)
        {
            var bestTile = explorableTilesThisTurn
                .MaxBy(it => it.TileHeight + it.EstimateUnexploredTilesAtDistance(unitVisibilityRange));
            if (bestTile != null)
                return unitView.TryHeadTowards(bestTile);
        }

        // Nothing immediate, lets look further. Number increases exponentially with distance - at 10 this took a looong time
        foreach (var tile in unitView.GetTile().GetExploredTilesInDistance(5))
        {
            if (IsGoodTileToExplore(unitView, cache, tile, unitVisibilityRange))
                return unitView.TryHeadTowards(tile);
        }
        return false;
    }

    private static bool TryGoToRuin(MapUnitView unitView)
    {
        if (!unitView.Civ().IsMajorCiv()) return false; // barbs don't have anything to do in ruins

        var tileWithRuin = unitView.GetViewableTiles()
            .FirstOrDefault(it =>
                it.Improvement != null
                && it.GetRuleset().TileImprovements.TryGetValue(it.Improvement, out var impl)
                && impl.IsAncientRuinsEquivalent()
                && unitView.CanMoveTo(it)
                && unitView.CanReach(it));
        if (tileWithRuin == null) return false;

        unitView.TryHeadTowards(tileWithRuin);
        return true;
    }

    private static bool TryFogBust(MapUnitView unitView)
    {
        if (!Automation.AfraidOfBarbarians(unitView.Civ())) return false; // Not if we're not afraid

        var unitTile = unitView.GetTile();
        var tilesInRange = unitTile.GetExploredTilesInDistance(5).ToList();
        if (tilesInRange.Count == TilesInHexRadius(5) && tilesInRange.All(it => it.IsVisible())) return false;

        var rng = unitView.StateBasedRandom("UnitAutomation.tryFogBust");
        var reachableTilesThisTurn = unitView.GetDistanceToTiles().Keys
            .Where(it => IsGoodTileForFogBusting(unitView, it))
            .ToList();
        if (reachableTilesThisTurn.Count != 0)
        {
            unitView.TryHeadTowards(reachableTilesThisTurn.Random(rng)); // Just pick one
            return true;
        }

        // Nothing immediate, lets look further. Number increases exponentially with distance - at 10 this took a looong time
        foreach (var tile in unitTile.GetExploredTilesInDistance(5))
        {
            if (IsGoodTileForFogBusting(unitView, tile))
            {
                unitView.TryHeadTowards(tile);
                return true;
            }
        }
        return false;
    }

    private static bool IsGoodTileForFogBusting(MapUnitView unitView, TileView tile)
    {
        var civ = unitView.Civ();
        return unitView.CanMoveTo(tile)
            && tile.GetOwner() == null
            && tile.GetVisibleNeighbors().All(it => it.GetOwner() == null)
            && civ.HasExplored(tile)
            && tile.GetExploredTilesInDistance(2).Any(it => it.GetOwner()?.Equals(civ) == true)
            && unitView.GetDamageFromTerrain(tile) <= 0
            && unitView.CanReach(tile);
    }

    public static void Wander(MapUnitView unitView, bool stayInTerritory = false, IReadOnlySet<TileView>? tilesToAvoid = null)
    {
        if (!unitView.HasMovement()) return; // return in case we can't move anyways
        var unitTile = unitView.GetTile();
        var unitDistanceToTiles = unitTile.GetExploredTilesAtDistance(1);
        var unitDistanceToTilesMap = unitView.GetDistanceToTiles();

        var reachableTiles = unitDistanceToTiles
            .Where(it =>
                (tilesToAvoid == null || !tilesToAvoid.Contains(it))
                && unitView.CanMoveTo(it)
                && unitDistanceToTilesMap.ContainsKey(it) // Yes this is required despite the above line - see #14461
                && unitView.GetDamageFromTerrain(it) <= 0 // Don't end turn on damaging terrain for no good reason
                && (!stayInTerritory || it.GetOwner()?.Equals(unitView.Civ()) == true || unitTile.GetOwner()?.Equals(unitView.Civ()) != true))
            .ToList();

        var rng = unitView.StateBasedRandom("UnitAutomation.wander");
        if (reachableTiles.Count != 0)
            unitView.TryMoveToTile(reachableTiles.Random(rng));
    }

    private static bool TryHeadTowardsEncampment(MapUnitView unitView)
    {
        if (unitView.HasUnique(UniqueType.SelfDestructs)) return false; // don't use single-use units against barbarians...
        var knownEncampments = unitView.Civ().Cities()
            .SelectMany(it => it.GetCenterTile().GetExploredTilesInDistance(6))
            .Where(it => it.IsBarbarianEncampment())
            .Distinct();
        var encampmentsCloseToCities = knownEncampments
            .OrderBy(it => it.AerialDistanceTo(unitView.GetTile()));
        var encampmentToHeadTowards = encampmentsCloseToCities.FirstOrDefault(it => unitView.CanReach(it));
        if (encampmentToHeadTowards == null) return false;
        unitView.TryHeadTowards(encampmentToHeadTowards);
        return true;
    }

    public static void AutomatedExplore(MapUnitView unitView, AutomationCache? cache = null)
    {
        cache ??= new AutomationCache();
        var unit = unitView.Unwrap();
        if (TryGoToRuin(unitView) && (!unitView.HasMovement() || unit.IsDestroyed)) return;
        if (unitView.UnitHealth < 80 && TryHealUnit(unitView, cache)) return;
        if (TryExplore(unitView, cache)) return;
        unit.Civ.AddNotification($"{unit.ShortDisplayName()} finished exploring.", MapUnitAction.Of(unit), Notification.NotificationCategory.Units, unit.Name, "OtherIcons/Sleep");
        unit.Action = null;
    }
}
