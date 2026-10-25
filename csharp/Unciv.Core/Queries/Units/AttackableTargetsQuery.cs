using Unciv.View;

namespace Unciv.Queries.Units;

// X: Tile x of the target
// Y: Tile y of the target
// Name: What the viewer sees on the tile (unit or city name), null for a tile without a visible combatant (nuke targets)
// Civ: Owner of that combatant
// IsCity: The visible combatant is a city
// AttackFrom: Tiles the attack can be made from (the unit's own tile for ranged attackers that need not move), with the movement left after moving there
public sealed record AttackTarget(int X, int Y, string? Name, string? Civ, bool IsCity, IReadOnlyList<AttackOrigin> AttackFrom);

public sealed record AttackOrigin(int X, int Y, float MovementLeft);

// Kind: "attack", "nuke", "airsweep" or "bombard"
// CanAttack: Whether the attacker can attack at all right now (movement, attacks left)
public sealed record AttackableTargetsResult(string Kind, bool CanAttack, IReadOnlyList<AttackTarget> Targets);

// T195: what a unit (UnitId) or a city (CityId, bombard) can attack. Wraps TargetHelper.GetAttackableEnemies /
// GetBombardableTiles through the views. The names come from the tile as the viewer sees it (TileView.GetCombatant), never
// from the engine's AttackableTile.Combatant, so a hidden submarine on a nuke target tile is not named.
[QueryType("units.AttackableTargets")]
public sealed record AttackableTargetsQuery(int? UnitId = null, string? CityId = null) : IQuery<AttackableTargetsResult>
{
    public string Type => "units.AttackableTargets";

    public AttackableTargetsResult Run(GameView view)
    {
        UnitQueryHelpers.RequireOneAttacker(UnitId, CityId);
        if (CityId != null)
        {
            var city = UnitQueryHelpers.OwnCity(view, CityId);
            var center = city.GetCenterTile();
            var origin = new AttackOrigin(center.Position().X, center.Position().Y, 0f);
            var targets = city.GetBombardableTiles()
                .Where(it => it.IsExplored())
                .Select(it => Describe(it, [origin]))
                .OrderBy(it => it.X).ThenBy(it => it.Y).ToList();
            return new AttackableTargetsResult("bombard", city.AsCombatant().CanAttack(), targets);
        }

        var unit = UnitQueryHelpers.OwnUnit(view, UnitId!.Value);
        var grouped = new SortedDictionary<(int, int), (TileView Tile, List<AttackOrigin> From)>();
        foreach (var attackable in unit.GetAttackableEnemies())
        {
            var tile = attackable.GetTileToAttack();
            if (!tile.IsExplored()) continue;
            var key = (tile.Position().X, tile.Position().Y);
            if (!grouped.TryGetValue(key, out var entry)) grouped[key] = entry = (tile, []);
            var from = attackable.GetTileToAttackFrom().Position();
            entry.From.Add(new AttackOrigin(from.X, from.Y, attackable.GetMovementLeftAfterMovingToAttackTile()));
        }
        var result = grouped.Values
            .Select(it => Describe(it.Tile, it.From.OrderBy(o => o.X).ThenBy(o => o.Y).ToList()))
            .ToList();
        var kind = unit.IsNuclearWeapon() ? "nuke" : unit.IsPreparingAirSweep() ? "airsweep" : "attack";
        return new AttackableTargetsResult(kind, unit.AsCombatant().CanAttack(), result);
    }

    private static AttackTarget Describe(TileView tile, IReadOnlyList<AttackOrigin> from)
    {
        var combatant = tile.GetCombatant();
        return new AttackTarget(tile.Position().X, tile.Position().Y, combatant?.GetCombatantName(),
            combatant?.GetCivInfo().Unwrap().CivName, combatant?.IsCity() ?? false, from);
    }
}
