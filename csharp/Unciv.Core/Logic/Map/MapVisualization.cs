using Unciv.Models.Ruleset.Unique;

namespace Unciv.Logic.Map;

// Ported from core/src/com/unciv/logic/map/MapVisualization.kt (T040). Stripped: the purity annotations.
// GameInfo, Civilization and MapUnit are typed by their seams (T046/T055/T067 retype them).

/// <summary>
/// Helper class for making decisions about more abstract information that may be displayed on the world map (or fair to use in AI),
/// but which does not have any direct influence on save state, rules, or behaviour.
/// </summary>
public class MapVisualization(IGameContextGameInfo gameInfo, IGameContextCivilization viewingCiv)
{
    public IGameContextGameInfo GameInfo { get; } = gameInfo;
    public IGameContextCivilization ViewingCiv { get; } = viewingCiv;

    /// <returns>Whether a unit's past movements should be visible to the player.</returns>
    public bool IsUnitPastVisible(IGameContextUnit unit)
    {
        if (unit.Civ == ViewingCiv)
            return true;
        var checkPositions = unit.MovementMemoryPositions.Concat([unit.GetTile().Position]);
        return checkPositions.All(it => ViewingCiv.IsTileViewable(GameInfo.TileMap[it]))
            && unit.IsVisibleTo(ViewingCiv);
        // Past should always be visible for own units. Past should be visible for foreign units if the unit is visible and both its current tile and previous tiles are visible.
    }

    /// <returns>Whether an attack by a unit to a target should be visible to the player.</returns>
    public bool IsAttackVisible(IGameContextCivilization attacker, HexCoord source, HexCoord target) =>
        attacker == ViewingCiv || ViewingCiv.IsTileViewable(GameInfo.TileMap[source]) || ViewingCiv.IsTileViewable(GameInfo.TileMap[target]);
    // Attacks by the player civ should always be visible, and attacks by foreign civs should be visible if either the tile they targeted or the attacker's tile are visible. E.G. Civ V shows bombers coming out of the Fog of War.
}
