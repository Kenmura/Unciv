using Unciv.Logic.Map;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/MotivationToAttackAutomation.kt (T088), part 2: getAttackPathsModifier.
// - Kotlin `groupBy { it.first }` keeps first-occurrence order of the (reference-identity) City keys: LinkedHashMap.
// - useAStarPathfinding (default false, client setting): the A* branches call City.getLandAttackPath / getAmphibiousAttackPath, which
//   are PathingMap users. They are reached through GameClientState.AttackPathing; null is the setting off (MapPathing).

/// <summary>What <c>MotivationToAttackAutomation</c> calls on <c>City</c> when <c>settings.useAStarPathfinding</c> is on.</summary>
public interface ICityAttackPathing
{
    /// <summary><c>from.getLandAttackPath(destination, maxTurns)</c></summary>
    List<TileClass>? GetLandAttackPath(CityObject from, CityObject destination, int maxTurns);

    /// <summary><c>from.getAmphibiousAttackPath(destination, maxTurns)</c></summary>
    List<TileClass>? GetAmphibiousAttackPath(CityObject from, CityObject destination, int maxTurns);
}

public static partial class MotivationToAttackAutomation
{
    /// <summary>
    /// Checks the routes of attack against <paramref name="otherCiv"/> using <paramref name="targetCitiesWithOurCity"/>.
    ///
    /// The more routes of attack and shorter the path the higher a motivation will be returned.
    /// Sea attack routes are less valuable
    /// </summary>
    /// <returns>The motivation ranging from -30 to around +10</returns>
    private static float GetAttackPathsModifier(CivilizationClass civInfo, CivilizationClass otherCiv, List<(CityObject, CityObject)> targetCitiesWithOurCity)
    {
        bool IsTileCanMoveThrough(CivilizationClass civ, TileClass tile)
        {
            var owner = tile.GetOwner();
            return !tile.IsImpassible()
                && (owner == otherCiv || owner == null || civ.DiplomacyFunctions.CanPassThroughTiles((CivilizationClass)owner));
        }

        bool IsLandTileCanMoveThrough(CivilizationClass civ, TileClass tile) => tile.IsLand && IsTileCanMoveThrough(civ, tile);

        var attackPaths = new List<List<TileClass>>();
        var attackPathModifiers = -3f;
        var attackPathing = civInfo.GameInfo.ClientState.AttackPathing;

        // groupBy { it.first }: keys in first-occurrence order
        var attacksGroupedByCity = new LinkedHashMap<CityObject, List<CityObject>>();
        foreach (var (from, to) in targetCitiesWithOurCity)
        {
            if (!attacksGroupedByCity.TryGetValue(from, out var group))
                attacksGroupedByCity[from] = group = [];
            group.Add(to);
        }

        // For each city, we want to calculate if there is an attack path to the enemy
        foreach (var (cityToAttackFrom, citiesToAttack) in attacksGroupedByCity)
        {
            var cityAttackValue = 0f;

            // We only want to calculate the best attack path and use it's value
            // Land routes are clearly better than sea routes
            foreach (var cityToAttack in citiesToAttack)
            {
                var landAttackPath = attackPathing != null
                    ? attackPathing.GetLandAttackPath(cityToAttackFrom, cityToAttack, 17)
                    : MapPathing.GetConnection(civInfo, cityToAttackFrom.GetCenterTile(), cityToAttack.GetCenterTile(), IsLandTileCanMoveThrough);
                if (landAttackPath != null && landAttackPath.Count < 16)
                {
                    attackPaths.Add(landAttackPath);
                    cityAttackValue = 3f;
                    break;
                }

                if (cityAttackValue > 0) continue;

                var landAndSeaAttackPath = attackPathing != null
                    ? attackPathing.GetAmphibiousAttackPath(cityToAttackFrom, cityToAttack, 17)
                    : MapPathing.GetConnection(civInfo, cityToAttackFrom.GetCenterTile(), cityToAttack.GetCenterTile(), IsTileCanMoveThrough);
                if (landAndSeaAttackPath != null && landAndSeaAttackPath.Count < 16)
                {
                    attackPaths.Add(landAndSeaAttackPath);
                    cityAttackValue += 1;
                }
            }
            attackPathModifiers += cityAttackValue;
        }

        if (attackPaths.Count == 0)
        {
            // Do an expensive BFS to find any possible attack path
            var reachableEnemyCitiesBfs = new BFS(civInfo.GetCapital(true).NotNull().GetCenterTile(), it => IsTileCanMoveThrough(civInfo, it));
            reachableEnemyCitiesBfs.StepToEnd();
            var reachableEnemyCities = otherCiv.Cities.Where(it => reachableEnemyCitiesBfs.HasReachedTile(it.GetCenterTile())).ToList();
            if (reachableEnemyCities.Count == 0) return -50f; // Can't even reach the enemy city, no point in war.
            var minAttackDistance = reachableEnemyCities.Min(it => reachableEnemyCitiesBfs.GetPathTo(it.GetCenterTile()).Count());

            // Longer attack paths are worse, but if the attack path is too far away we shouldn't completely discard the possibility
            attackPathModifiers -= Math.Clamp(minAttackDistance - 10, 0, 30);
        }
        return attackPathModifiers;
    }
}
