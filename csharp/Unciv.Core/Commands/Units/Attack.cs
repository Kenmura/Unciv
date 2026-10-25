using Unciv.Logic.Battle;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Utils;
using BattleClass = Unciv.Logic.Battle.Battle;

namespace Unciv.Commands.Units;

/// <summary>
/// Attack the tile with one of the actor's units, moving into position first. Kotlin: WorldMapHolder right-click attack
/// (<c>canAttack()</c> and an attackable tile from <c>getAttackableEnemies</c> whose tileToAttack is the target), then
/// <c>tryMovePreparingAttack</c> and, if still possible, <c>attackOrNuke</c>. The defender is whatever <c>TargetHelper</c> and
/// <c>Battle.GetMapCombatantOfTile</c> find at apply time; all randomness is drawn inside <c>Apply</c>.
/// Nuclear weapons and air units preparing an air sweep have their own commands.
/// </summary>
[CommandType("attack-with-unit")]
public sealed class AttackWithUnit : GameCommand
{
    public int UnitId { get; init; }
    public HexCoord Target { get; init; }

    private static AttackableTile? Find(MapUnit unit, HexCoord target) =>
        TargetHelper.GetAttackableEnemies(unit, unit.Movement.GetDistanceToTiles())
            .FirstOrDefault(it => it.TileToAttack.Position == target && it.Combatant != null);

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        if (!context.TileAt(Target).TryGet(out _, out failure)) return failure;
        if (!unit.CanAttack()) return CommandValidation.Fail(CommandErrors.NotAllowed, "The unit cannot attack now");
        if (unit.IsNuclearWeapon() || unit.IsPreparingAirSweep())
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Use the nuke or air sweep command for this unit");
        return Find(unit, Target) != null
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "There is nothing to attack on that tile");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        var attackable = Find(unit, Target).NotNull();
        var attacker = new MapUnitCombatant(unit);
        if (BattleClass.MovePreparingAttack(attacker, attackable)) BattleClass.AttackOrNuke(attacker, attackable);
        return [];
    }
}
