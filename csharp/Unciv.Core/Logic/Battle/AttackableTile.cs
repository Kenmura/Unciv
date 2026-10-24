using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/AttackableTile.kt (a Kotlin data class: memberwise equality, which a record gives).
public sealed record AttackableTile(
    TileClass TileToAttackFrom,
    TileClass TileToAttack,
    float MovementLeftAfterMovingToAttackTile,
    ICombatant? Combatant);
