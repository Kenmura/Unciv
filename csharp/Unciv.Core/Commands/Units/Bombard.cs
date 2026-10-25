using Unciv.Logic.Battle;
using Unciv.Logic.Map;
using Unciv.Utils;
using BattleClass = Unciv.Logic.Battle.Battle;

namespace Unciv.Commands.Units;

/// <summary>
/// A city bombards a tile. Kotlin: BattleTable (<c>canAttack()</c> = <c>City.canBombard()</c> and the tile among
/// <c>getBombardableTiles</c>), then <c>CityView.tryBombard</c> = <c>Battle.attackOrNuke</c> with the attackable tile built by
/// <c>CombatantView.buildBombardAttackableTile</c> (no movement, attack from the city centre).
/// </summary>
[CommandType("bombard-with-city")]
public sealed class BombardWithCity : GameCommand
{
    public string CityId { get; init; } = "";
    public HexCoord Target { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnCity(actor, CityId).TryGet(out var city, out failure)) return failure;
        if (!context.TileAt(Target).TryGet(out var tile, out failure)) return failure;
        if (!city.CanBombard()) return CommandValidation.Fail(CommandErrors.NotAllowed, "The city cannot bombard now");
        return TargetHelper.GetBombardableTiles(city).Contains(tile)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "That tile cannot be bombarded");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.OwnCity(context.Actor, CityId).Value.NotNull();
        var tile = context.TileAt(Target).Value.NotNull();
        BattleClass.AttackOrNuke(new CityCombatant(city), new AttackableTile(city.GetCenterTile(), tile, 0f, BattleClass.GetMapCombatantOfTile(tile)));
        return [];
    }
}
