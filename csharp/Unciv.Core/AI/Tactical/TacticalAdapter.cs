using Unciv.Logic.Battle;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.AI.Tactical;

/// <summary>
/// Game adapter for the tactical AI (SPEC.md sections 6.1 and 6.5): scans a <see cref="Civilization"/>'s visible tiles into a
/// <see cref="TacticalTargetList"/>, lists its units as <see cref="TacticalAttacker"/>s, estimates damage with the engine's own
/// (deterministic) combat formula, and carries out the attacks an <see cref="OverkillPlan"/> recruited.
/// The spec does not recover how targets are classified into high/medium/low ([?]), so the split used here is this port's own:
/// enemy units next to or near our cities are high, ranged and siege units medium, the rest low; settlers and great people are
/// very high priority civilians, other civilians high. Within a move, weaker targets (lower health) rank higher.
/// Zone postures (SPEC 6.3) are not modelled.
/// </summary>
public static class TacticalAdapter
{
    /// <summary>Enemy units this close to one of our cities count as high priority.</summary>
    public const int HighUnitCityRange = 3;

    /// <summary>Finds every target the civilization can see, in a stable order (visible tiles sorted by position).</summary>
    public static TacticalTargetList ScanTargets(Civilization civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        var targets = new TacticalTargetList();
        var cityPositions = civ.Cities.Select(c => c.GetCenterTile().Position).ToList();
        foreach (var tile in civ.ViewableTiles.OrderBy(t => t.Position.X).ThenBy(t => t.Position.Y))
        {
            var city = tile.GetCity();
            if (city != null && tile.IsCityCenter() && !city.HasJustBeenConquered && civ.IsAtWarWith(city.Civ))
            {
                int hp = city.Health;
                targets.Add(new TacticalTarget(TacticalMoveType.CaptureCity, tile.Position, PriorityOf(hp), hp));
            }

            var military = tile.MilitaryUnit;
            if (military != null && civ.IsAtWarWith(military.Civ))
                targets.Add(new TacticalTarget(ClassifyMilitary(military, tile.Position, cityPositions), tile.Position, PriorityOf(military.Health), military.Health));

            var civilian = tile.CivilianUnit;
            if (civilian != null && military == null && civ.IsAtWarWith(civilian.Civ))
            {
                bool veryHigh = civilian.IsGreatPerson() || civilian.HasUnique(UniqueType.FoundCity);
                var move = veryHigh ? TacticalMoveType.AttackVeryHighPriorityCivilian : TacticalMoveType.AttackHighPriorityCivilian;
                targets.Add(new TacticalTarget(move, tile.Position, PriorityOf(civilian.Health), civilian.Health));
            }
        }
        return targets;
    }

    private static int PriorityOf(int health) => 100 - health;

    private static TacticalMoveType ClassifyMilitary(MapUnit unit, HexCoord position, List<HexCoord> cityPositions)
    {
        if (cityPositions.Any(c => HexMath.GetDistance(c, position) <= HighUnitCityRange)) return TacticalMoveType.DestroyHighUnit;
        return unit.GetRange() > 1 ? TacticalMoveType.DestroyMediumUnit : TacticalMoveType.DestroyLowUnit;
    }

    /// <summary>The civilization's units that can attack this turn, in unit-list order.</summary>
    public static List<TacticalAttacker> GetAttackers(Civilization civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return civ.Units.GetCivUnits()
            .Where(u => u.IsMilitary() && u.CanAttack())
            .Select(u => new TacticalAttacker(u.Id, u.CurrentTile.Position, u.GetRange() > 1))
            .ToList();
    }

    /// <summary>
    /// Damage a unit would deal to whatever stands on the target tile, using Unciv's turn-and-location based randomness
    /// (no RNG draw). Zero when the unit is gone, the tile is empty, or the defender is not an enemy.
    /// </summary>
    public sealed class DamageEstimator(Civilization civ) : ITacticalDamageEstimator
    {
        public int EstimateDamage(TacticalAttacker attacker, TacticalTarget target)
        {
            ArgumentNullException.ThrowIfNull(attacker);
            ArgumentNullException.ThrowIfNull(target);
            var unit = civ.Units.GetUnitById(attacker.Id);
            if (unit == null) return 0;
            var defender = Battle.GetMapCombatantOfTile(civ.GameInfo.TileMap[target.Position]);
            if (defender == null || !civ.IsAtWarWith(defender.GetCivInfo())) return 0;
            return BattleDamage.CalculateDamageToDefender(new MapUnitCombatant(unit), defender);
        }
    }

    /// <summary>
    /// Carries out the recruited attacks, assignment by assignment in order, each attacker in acting order. A unit that has
    /// vanished, cannot attack any more, or cannot reach an attack position for the target is skipped; an assignment stops
    /// once its target tile no longer holds an enemy. Returns the number of attacks made.
    /// </summary>
    public static int ExecuteAttacks(Civilization civ, OverkillPlan plan)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(plan);
        int attacks = 0;
        foreach (var assignment in plan.Assignments)
        {
            var tile = civ.GameInfo.TileMap[assignment.Target.Position];
            foreach (var recruit in assignment.Attackers)
            {
                var defender = Battle.GetMapCombatantOfTile(tile);
                if (defender == null || !civ.IsAtWarWith(defender.GetCivInfo())) break;
                var unit = civ.Units.GetUnitById(recruit.Id);
                if (unit == null || !unit.CanAttack()) continue;
                var option = FindAttack(unit, tile);
                if (option == null) continue;
                var combatant = new MapUnitCombatant(unit);
                if (!Battle.MovePreparingAttack(combatant, option, true)) continue;   // MoveAndAttack, but only counting real attacks
                Battle.AttackOrNuke(combatant, option);
                attacks++;
            }
        }
        return attacks;
    }

    private static AttackableTile? FindAttack(MapUnit unit, TileClass target) =>
        TargetHelper.GetAttackableEnemies(unit, unit.Movement.GetDistanceToTiles())
            .FirstOrDefault(a => a.TileToAttack == target);

    /// <summary>One tactical turn for the attack moves: scan, recruit by move priority, then attack.</summary>
    public static int DoTurn(Civilization civ, TacticalRecruiter recruiter, TacticalMoveQueue queue, TacticalFlavors flavors)
    {
        ArgumentNullException.ThrowIfNull(queue);
        return DoTurn(civ, recruiter, queue.Build(flavors));
    }

    /// <summary><see cref="DoTurn(Civilization, TacticalRecruiter, TacticalMoveQueue, TacticalFlavors)"/> with a queue built by the caller (e.g. with jitter).</summary>
    public static int DoTurn(Civilization civ, TacticalRecruiter recruiter, IReadOnlyList<TacticalQueueEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(recruiter);
        ArgumentNullException.ThrowIfNull(entries);
        var targets = ScanTargets(civ);
        var assigner = new OverkillAssigner(recruiter, new DamageEstimator(civ));
        var free = GetAttackers(civ);
        int attacks = 0;
        TacticalMoveQueue.Run(entries, targets, (move, list) =>
        {
            if (!IsAttackMove(move) || list.Count == 0 || free.Count == 0) return;
            var plan = assigner.Assign(list, free);
            attacks += ExecuteAttacks(civ, plan);
            free = plan.Unassigned.ToList();
        });
        return attacks;
    }

    private static bool IsAttackMove(TacticalMoveType move) => move is TacticalMoveType.CaptureCity or TacticalMoveType.DestroyHighUnit
        or TacticalMoveType.DestroyMediumUnit or TacticalMoveType.DestroyLowUnit
        or TacticalMoveType.AttackVeryHighPriorityCivilian or TacticalMoveType.AttackHighPriorityCivilian;
}
