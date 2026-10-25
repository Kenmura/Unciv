using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;

namespace Unciv.Models.Ruleset;

/// <summary>
/// The parts of <c>Ruleset</c> that <see cref="Unit.BaseUnit"/>, <see cref="Unit.UnitType"/>, <see cref="Unit.UnitNameGroup"/> and <see cref="Building"/> read.
/// T034's <c>Ruleset</c> implements this. Methods rather than maps, so the ruleset can also implement <see cref="IRulesetParameterView"/>.
/// </summary>
public interface IRulesetConstructionView
{
    /// <summary><c>ruleset.globalUniques</c></summary>
    GlobalUniques GlobalUniques { get; }
    /// <summary><c>ruleset.unitTypes[name]</c></summary>
    UnitType? GetUnitType(string name);
    /// <summary><c>ruleset.units[name]</c></summary>
    Unit.BaseUnit? GetUnit(string name);
    /// <summary><c>ruleset.units.values</c>, in ruleset order.</summary>
    IEnumerable<Unit.BaseUnit> GetAllUnits();
    /// <summary><c>ruleset.unitTypes.values</c>, in ruleset order.</summary>
    IEnumerable<UnitType> GetAllUnitTypes();
    /// <summary><c>ruleset.tileImprovements[name]</c></summary>
    Tile.TileImprovement? GetTileImprovement(string name);
    /// <summary><c>ruleset.unitPromotions[name]</c></summary>
    Promotion? GetPromotion(string name);
    /// <summary><c>ruleset.technologies[techName]?.matchesFilter(filter, state, multiFilter)</c>; <c>null</c> when there is no such technology.</summary>
    bool? TechnologyMatchesFilter(string techName, string filter, GameContext? state, bool multiFilter);
}
