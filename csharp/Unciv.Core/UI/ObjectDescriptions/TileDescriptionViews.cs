using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.UI.ObjectDescriptions;

// The parts of TileView, CivView, CityView, and MapUnitView that TileDescription reads. Kept as interfaces so that the description
// text can be tested without a game (the tests fake them); the view classes implement them.

public interface ITileView
{
    ICivView? GetCivView();
    bool IsCityCenter();
    ICityView? OwningCity();
    string BaseTerrain { get; }
    IEnumerable<string> TerrainFeatures { get; }
    TileResource? GetViewableResource(ICivView? viewingCiv);
    string? Resource { get; }
    int ResourceAmount { get; }
    string? NaturalWonder { get; }
    RoadStatus RoadStatus { get; }
    bool RoadIsPillaged { get; }
    string? GetShownImprovement();
    bool ImprovementIsPillaged { get; }
    string? ImprovementInProgress { get; }
    int TurnsToImprovement { get; }
    IMapUnitView? CivilianUnit { get; }
    IMapUnitView? MilitaryUnit { get; }
    float GetDefensiveBonus();
    bool IsImpassible();
    bool IsLand { get; }
    bool IsAdjacentTo(string terrainFilter);
    Ruleset GetRuleset();
    bool IsVisibleMap { get; }
}

public interface ICivView
{
    bool CanSeeTile(ITileView tileView);
    bool CanBuildImprovementOn(TileImprovement improvement, ITileView tileView);
    IEnumerable<ImprovementBuildingProblem> GetImprovementBuildingProblems(TileImprovement improvement, ITileView tileView);
    Technology? TechnologyByName(string? name);
    bool IsResearched(string techName);
}

public interface ICityView
{
    string Name { get; }
    int GetHealth();
    bool IsOwnedByViewer();
    FormattedLine GetProductionMarkup();
}

public interface IMapUnitView
{
    string Name { get; }
    string CivName { get; }
    int UnitHealth { get; }
}
