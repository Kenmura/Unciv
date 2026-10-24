using Unciv.Models.Ruleset.Unique;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Logic.Map;

// Ported from core/src/com/unciv/logic/map/MapVisualization.kt (T040). Stripped: the purity annotations.
// Civilization and MapUnit retyped in T134; GameInfo remains IGameContextGameInfo until T046.

/// <summary>
/// Helper class for making decisions about more abstract information that may be displayed on the world map (or fair to use in AI),
/// but which does not have any direct influence on save state, rules, or behaviour.
/// </summary>
public class MapVisualization(IGameContextGameInfo gameInfo, CivilizationClass viewingCiv)
{
    public IGameContextGameInfo GameInfo { get; } = gameInfo;
    public CivilizationClass ViewingCiv { get; } = viewingCiv;

    /// <returns>Whether a unit's past movements should be visible to the player.</returns>
    public bool IsUnitPastVisible(MapUnitClass unit)
    {
        if (unit.Civ == ViewingCiv)
            return true;
        var checkPositions = unit.MovementMemories.Select(it => it.Position).Concat([unit.GetTile().Position]);
        return checkPositions.All(it => ViewingCiv.ViewableTiles.Contains(GameInfo.TileMap[it]))
            && unit.IsVisibleTo(ViewingCiv);
        // Past should always be visible for own units. Past should be visible for foreign units if the unit is visible and both its current tile and previous tiles are visible.
    }

    /// <returns>Whether an attack by a unit to a target should be visible to the player.</returns>
    public bool IsAttackVisible(CivilizationClass attacker, HexCoord source, HexCoord target) =>
        attacker == ViewingCiv || ViewingCiv.ViewableTiles.Contains(GameInfo.TileMap[source]) || ViewingCiv.ViewableTiles.Contains(GameInfo.TileMap[target]);
    // Attacks by the player civ should always be visible, and attacks by foreign civs should be visible if either the tile they targeted or the attacker's tile are visible. E.G. Civ V shows bombers coming out of the Fog of War.
}
