using Unciv.Queries.Cities;
using Unciv.Queries.Units;
using Unciv.View;

namespace Unciv.Queries.Tiles;

// Problems: ImprovementBuildingProblem names; empty means the unit can build it here now
// StatDiff: Change of the tile's yields (and cost of upkeep) if built, only when there is no problem
public sealed record ImprovementOption(string Name, IReadOnlyList<string> Problems, IReadOnlyList<StatAmount> StatDiff);

public sealed record TileUnit(int Id, string Name, string Civ);

public sealed record TileInfoResult(
    TilePos Position, string BaseTerrain, IReadOnlyList<string> TerrainFeatures, string? NaturalWonder,
    string? Resource, int ResourceAmount, string? Improvement, bool ImprovementIsPillaged, string? ImprovementInProgress, int TurnsToImprovement,
    string Road, bool RoadIsPillaged, bool HasRiver,
    string? Owner, string? OwningCity, string? WorkedBy, bool IsLocked, bool IsCityCenter, bool IsVisible,
    IReadOnlyList<StatAmount> Yields, IReadOnlyList<StatSource> YieldBreakdown,
    IReadOnlyList<TileUnit> Units, IReadOnlyList<ImprovementOption>? ImprovementOptions);

// T196: the tile info panel. Yields are what the tile yields for the viewer, worked by its working city when the viewer owns it.
// The resource is shown only if the viewer's techs reveal it. With UnitId (a worker of the viewer) the improvements the unit
// could place are listed with the engine's building problems; permanently unbuildable ones are left out.
[QueryType("tiles.TileInfo")]
public sealed record TileInfoQuery(int X, int Y, int? UnitId = null) : IQuery<TileInfoResult>
{
    public string Type => "tiles.TileInfo";

    public TileInfoResult Run(GameView view)
    {
        var tile = UnitQueryHelpers.ExploredTile(view, X, Y);
        var cityView = tile.GetWorkingCity()?.TryGetCityView();
        var yields = tile.GetTileStats(view.CivView, cityView);
        var breakdown = tile.GetTileStatsBreakdown(view.CivView, cityView)
            .Select(it => new StatSource(it.Cause, CityQueryHelpers.StatList(it.Stats))).ToList();
        var units = tile.GetVisibleUnits().Select(it => new TileUnit(it.Id, it.Name, it.CivName)).ToList();
        bool locked = cityView != null && tile.IsLocked();

        IReadOnlyList<ImprovementOption>? options = null;
        if (UnitId != null)
        {
            var unit = UnitQueryHelpers.OwnUnit(view, UnitId.Value);
            options = tile.GetRuleset().TileImprovements.Values.Select(improvement =>
                {
                    var problems = tile.GetImprovementBuildingProblems(improvement, unit);
                    return (improvement, problems);
                })
                .Where(it => !it.problems.Contains(Logic.Map.Tiles.ImprovementBuildingProblem.Unbuildable))
                .Select(it => new ImprovementOption(it.improvement.Name,
                    it.problems.Select(p => p.ToString()).OrderBy(p => p, StringComparer.Ordinal).ToList(),
                    it.problems.Count == 0 ? CityQueryHelpers.StatList(tile.GetStatDiffForImprovement(it.improvement, view.CivView)) : []))
                .ToList();
        }

        return new TileInfoResult(UnitQueryHelpers.Pos(tile.Position()), tile.BaseTerrain, tile.TerrainFeatures.ToList(), tile.NaturalWonder,
            tile.GetViewableResource(view.CivView)?.Name, tile.ResourceAmount, tile.GetShownImprovement(), tile.ImprovementIsPillaged,
            tile.ImprovementInProgress, tile.TurnsToImprovement,
            tile.RoadStatus.ToString(), tile.RoadIsPillaged, tile.HasBottomRiver || tile.HasBottomLeftRiver || tile.HasBottomRightRiver,
            tile.GetOwner()?.CivName, tile.OwningCity()?.Name, tile.GetWorkingCity()?.Name, locked, tile.IsCityCenter(), tile.IsVisible(),
            CityQueryHelpers.StatList(yields), breakdown, units, options);
    }
}
