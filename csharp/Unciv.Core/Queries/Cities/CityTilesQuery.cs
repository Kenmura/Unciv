using Unciv.Queries.Units;
using Unciv.View;

namespace Unciv.Queries.Cities;

// State: Center (the city tile), Worked (by this city), Taken (yields to somebody without this city's citizen: another city works it, or it yields without population),
//        Available (a free citizen could work it now), Unavailable (the city may use it but the engine refuses: blockaded or nothing to yield),
//        Foreign (another civ owns it), Unowned (nobody does; the city could buy it)
// Locked: Whether a worked tile is pinned against reassignment
// Stats: What working it yields for this city (nonzero stats)
public sealed record CityTile(TilePos Position, string State, bool Locked, IReadOnlyList<StatAmount> Stats);

public sealed record CityTilesResult(string CityId, TilePos Center, int WorkRange, int FreePopulation, IReadOnlyList<CityTile> Tiles);

// C017: every tile the viewer has explored within the city's work range, classified exactly as the work-tile / stop-working-tile commands judge them
// (GetWorkableTiles, IsBlockaded, ProvidesYield, empty stats), so the city screen draws its grid without recomputing a rule. Ordered by coordinates.
[QueryType("cities.CityTiles")]
public sealed record CityTilesQuery(string CityId) : IQuery<CityTilesResult>
{
    public string Type => "cities.CityTiles";

    public CityTilesResult Run(GameView view)
    {
        var city = UnitQueryHelpers.OwnCity(view, CityId);
        var engineCity = city.Unwrap();
        var locked = engineCity.LockedTiles;
        var workable = engineCity.GetWorkableTiles().ToHashSet();
        var tiles = new List<CityTile>();
        foreach (var tile in engineCity.TilesInRange)
        {
            var tileView = view.TileMapView.GetTile(tile.Position);
            if (tileView == null) continue;
            var stats = tileView.GetTileStats(view.CivView, city);
            string state;
            if (tile.IsCityCenter() && ReferenceEquals(tile.GetCity(), engineCity)) state = "Center";
            else if (engineCity.IsWorked(tile)) state = "Worked";
            else if (!workable.Contains(tile)) state = tile.GetOwner() == null ? "Unowned" : "Foreign";
            else if (tile.IsBlockaded() || stats.IsEmpty()) state = "Unavailable";
            else state = tile.ProvidesYield() ? "Taken" : "Available";
            tiles.Add(new CityTile(UnitQueryHelpers.Pos(tile.Position), state, locked.Contains(tile.Position), CityQueryHelpers.StatList(stats)));
        }
        return new CityTilesResult(CityId, UnitQueryHelpers.Pos(city.Location()), city.GetWorkRange(), city.GetFreePopulation(),
            tiles.OrderBy(it => it.Position.X).ThenBy(it => it.Position.Y).ToList());
    }
}
