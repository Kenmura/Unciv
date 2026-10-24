using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/TargetHelper.kt (T082). Purity annotations and timeThis dropped (T027 policy).
// Nuke.mayUseNuke and Battle.getMapCombatantOfTile are ported with Battle (T083).
public static class TargetHelper
{
    public static List<AttackableTile> GetAttackableEnemies(
        MapUnitClass unit,
        PathsToTilesWithinTurn unitDistanceToTiles,
        List<TileClass>? tilesToCheck = null,
        bool stayOnTile = false)
    {
        int rangeOfAttack = unit.GetRange();
        var attackableTiles = new List<AttackableTile>();

        bool unitMustBeSetUp = unit.HasUnique(UniqueType.MustSetUp);
        IEnumerable<(TileClass Tile, float MovementLeft)> tilesToAttackFrom = stayOnTile || unit.BaseUnit.IsAirUnit()
            ? [(unit.CurrentTile, unit.CurrentMovement)]
            : GetTilesToAttackFromWhenUnitMoves(unitDistanceToTiles, unitMustBeSetUp, unit);

        var tilesWithEnemies = new HashSet<TileClass>();
        var tilesWithoutEnemies = new HashSet<TileClass>();
        foreach (var (reachableTile, movementLeft) in tilesToAttackFrom) // tiles we'll still have energy after we reach there
        {
            // If we are a melee unit that is escorting, we only want to be able to attack from this
            // tile if the escorted unit can also move into the tile we are attacking if we kill the enemy unit.
            if (unit.BaseUnit.IsMelee() && unit.IsEscorting())
            {
                var escortingUnit = unit.GetOtherEscortUnit().NotNull();
                if (!escortingUnit.Movement.CanReachInCurrentTurn(reachableTile)
                    || escortingUnit.CurrentMovement - escortingUnit.Movement.GetDistanceToTiles()[reachableTile].TotalMovement <= 0f)
                    continue;
            }

            // Nukes ignore line of sight when checking range, and don't require a visible enemy on the target tile
            IEnumerable<TileClass> tilesInAttackRange =
                unit.BaseUnit.IsMelee() ? reachableTile.Neighbors
                : unit.BaseUnit.IsAirUnit() || unit.IsNuclearWeapon() || unit.HasUnique(UniqueType.IndirectFire, null, true)
                    ? reachableTile.GetTilesInDistance(rangeOfAttack)
                    : reachableTile.TileMap.GetViewableTiles(reachableTile.Position, rangeOfAttack, true);

            foreach (var tile in tilesInAttackRange)
            {
                // Since military units can technically enter tiles with enemy civilians,
                // some try to move to to the tile and then attack the unit it contains, which is silly
                if (tile == reachableTile) continue;

                if (unit.IsNuclearWeapon())
                {
                    if (Nuke.MayUseNuke(new MapUnitCombatant(unit), tile))
                        attackableTiles.Add(new AttackableTile(reachableTile, tile, movementLeft, Battle.GetMapCombatantOfTile(tile)));
                }
                else if (tilesWithEnemies.Contains(tile))
                    attackableTiles.Add(new AttackableTile(reachableTile, tile, movementLeft, Battle.GetMapCombatantOfTile(tile)));
                else if (tilesWithoutEnemies.Contains(tile))
                    continue; // avoid checking the same empty tile multiple times
                else if (TileContainsAttackableEnemy(unit, tile, tilesToCheck) || unit.IsPreparingAirSweep())
                {
                    tilesWithEnemies.Add(tile);
                    attackableTiles.Add(new AttackableTile(reachableTile, tile, movementLeft, Battle.GetMapCombatantOfTile(tile)));
                }
                else
                    tilesWithoutEnemies.Add(tile);
            }
        }
        return attackableTiles;
    }

    private static IEnumerable<(TileClass Tile, float MovementLeft)> GetTilesToAttackFromWhenUnitMoves(
        PathsToTilesWithinTurn unitDistanceToTiles, bool unitMustBeSetUp, MapUnitClass unit) =>
        // OrderBy is stable, like Kotlin sortedWith
        unitDistanceToTiles.OrderBy(it => it.Value.TotalMovement, Comparer<float>.Default)
            .Select(it =>
            {
                var (tile, distance) = (it.Key, it.Value);
                int movementPointsToExpendAfterMovement = unitMustBeSetUp ? 1 : 0;
                int movementPointsToExpendHere = unitMustBeSetUp && !unit.IsSetUpForSiege() ? 1 : 0;
                int movementPointsToExpendBeforeAttack = tile == unit.CurrentTile ? movementPointsToExpendHere : movementPointsToExpendAfterMovement;
                float movementLeft = unit.CurrentMovement - distance.TotalMovement - movementPointsToExpendBeforeAttack;
                return (tile, movementLeft);
            })
            // still got leftover movement points after all that, to attack
            .Where(it => it.movementLeft > Constants.MinimumMovementEpsilon)
            .Where(it => it.tile == unit.GetTile() || unit.Movement.CanMoveTo(it.tile));

    private static bool TileContainsAttackableEnemy(MapUnitClass unit, TileClass tile, List<TileClass>? tilesToCheck)
    {
        bool visible = tilesToCheck != null ? tilesToCheck.Contains(tile) : unit.Civ.ViewableTiles.Contains(tile);
        if (!visible || !ContainsAttackableEnemy(tile, new MapUnitCombatant(unit)))
            return false;
        var mapCombatant = Battle.GetMapCombatantOfTile(tile);

        return !unit.BaseUnit.IsMelee() || mapCombatant is not MapUnitCombatant mapUnitCombatant || !mapUnitCombatant.Unit.IsCivilian()
            || unit.Movement.CanPassThrough(tile);
    }

    public static bool ContainsAttackableEnemy(TileClass tile, ICombatant combatant)
    {
        if (combatant is MapUnitCombatant embarkedCombatant && embarkedCombatant.Unit.IsEmbarked() && !embarkedCombatant.HasUnique(UniqueType.AttackOnSea))
        {
            // Can't attack water units while embarked, only land
            if (tile.IsWater || combatant.IsRanged())
                return false;
        }

        var tileCombatant = Battle.GetMapCombatantOfTile(tile);
        if (tileCombatant == null) return false;
        if (tileCombatant.GetCivInfo() == combatant.GetCivInfo()) return false;
        // If the user automates units, one may capture the city before the user had a chance to decide what to do with it,
        //  and then the next unit should not attack that city
        if (tileCombatant is CityCombatant cityCombatant && cityCombatant.City.HasJustBeenConquered) return false;
        if (!combatant.GetCivInfo().IsAtWarWith(tileCombatant.GetCivInfo())) return false;

        if (combatant is MapUnitCombatant mapUnitCombatant && combatant.IsLandUnit() && combatant.IsMelee() && tile.IsWater &&
            !combatant.GetCivInfo().Tech.UnitsCanEmbark && !mapUnitCombatant.Unit.Cache.CanMoveOnWater)
            return false;

        if (combatant is MapUnitCombatant attackerCombatant)
        {
            var gameContext = new GameContext(
                unit: attackerCombatant.Unit, tile: tile,
                ourCombatant: attackerCombatant, theirCombatant: tileCombatant, combatAction: CombatAction.Attack);

            if (attackerCombatant.HasUnique(UniqueType.CannotAttack, gameContext))
                return false;

            var onlyUnits = attackerCombatant.Unit.GetMatchingUniques(UniqueType.CanOnlyAttackUnits, gameContext).ToList();
            if (onlyUnits.Count > 0 && !onlyUnits.Any(it => tileCombatant.MatchesFilter(it.Params[0])))
                return false;

            var onlyTiles = attackerCombatant.Unit.GetMatchingUniques(UniqueType.CanOnlyAttackTiles, gameContext).ToList();
            if (onlyTiles.Count > 0 && !onlyTiles.Any(it => tile.MatchesFilter(it.Params[0])))
                return false;
        }

        // Only units with the right unique can view submarines (or other invisible units) from more then one tile away.
        // Garrisoned invisible units can be attacked by anyone, as else the city will be in invincible.
        if (!tile.IsCityCenter())
            return tileCombatant.IsVisibleTo(combatant.GetCivInfo());

        return true;
    }

    /// <summary>Get a list of visible tiles which have something attackable</summary>
    public static IEnumerable<TileClass> GetBombardableTiles(CityObject city) =>
        city.GetCenterTile().GetTilesInDistance(city.GetBombardRange())
            .Where(it => it.IsVisible(city.Civ) && ContainsAttackableEnemy(it, new CityCombatant(city)));
}
