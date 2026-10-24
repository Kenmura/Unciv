using Unciv.Logic.Battle;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/AttackableTileView.kt (T085). Stripped: the purity annotations.

/// <summary>View of an <see cref="AttackableTile"/> from the perspective of the viewer via the <see cref="GameView"/>.</summary>
public class AttackableTileView(AttackableTile wrapped, CivilizationClass viewer, bool spectatorMode, GameView gameView)
    : GameBasedView<AttackableTile>(wrapped, viewer, spectatorMode, gameView)
{
    private readonly AttackableTile attackableTile = wrapped;

    public TileView GetTileToAttackFrom() => GameView.GetTile(attackableTile.TileToAttackFrom);

    public TileView GetTileToAttack() => GameView.GetTile(attackableTile.TileToAttack);

    public float GetMovementLeftAfterMovingToAttackTile() => attackableTile.MovementLeftAfterMovingToAttackTile;

    public CombatantView? GetCombatant() => attackableTile.Combatant switch
    {
        null => null,
        MapUnitCombatant combatant => GameView.GetForeignMapUnitView(combatant.Unit).AsCombatant(),
        CityCombatant combatant => GameView.GetForeignCityView(combatant.City).AsCombatant(),
        _ => null,
    };
}
