using Unciv.Models.StatTypes;
using Unciv.Queries.Cities;
using Unciv.Queries.Units;
using Unciv.View;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Queries.Tiles;

/// <summary>Why a tile cannot take a city (<see cref="CitySiteProblem.Code"/>).</summary>
public static class CitySiteProblemCodes
{
    public const string Water = "Water";
    public const string Impassable = "Impassable";
    /// <summary>Subject is the name of the (known) city that is too near.</summary>
    public const string TooCloseToCity = "TooCloseToCity";
    public const string ForeignTerritory = "ForeignTerritory";
}

public sealed record CitySiteProblem(string Code, string? Subject);

// CanSettle: no problem the viewer can know of; a city the viewer has not seen may still forbid the site, and the engine decides when the city is founded
// Radius: the city's work range; Tiles: the tiles in range that the viewer has explored and that count (not known to belong to another civ)
// Yields: the land's yields on those tiles (no improvements, see TileView.GetLandStats) plus the city centre's minimum; Resources: tiles per resource the viewer can see
// UnexploredTiles: tiles in range the viewer has not explored, shown as unknown and never counted
public sealed record CitySiteResult(
    TilePos Position, bool CanSettle, IReadOnlyList<CitySiteProblem> Problems, int Radius, IReadOnlyList<TilePos> Tiles,
    IReadOnlyList<StatAmount> Yields, IReadOnlyList<NamedAmount> Resources, IReadOnlyList<string> NaturalWonders, int UnexploredTiles);

// C088: what a city founded on the tile would have to work with, and whether it could be founded there. Read-only. Fog rules: the tile itself must be explored
// (else the same refusal as an unknown tile); only explored tiles are counted, their resources only when the viewer's techs reveal them, their yields without
// improvements (an improvement built out of sight would show through); a foreign owner and the problems that depend on cities only count what the viewer can see
// (cities on explored tiles, owners of visible tiles), so an answer never changes with what lies in the unexplored dark.
[QueryType("tiles.CitySite")]
public sealed record CitySiteQuery(int X, int Y) : IQuery<CitySiteResult>
{
    public string Type => "tiles.CitySite";

    public CitySiteResult Run(GameView view)
    {
        var tileView = UnitQueryHelpers.ExploredTile(view, X, Y);
        var tile = tileView.Unwrap();
        var game = view.Unwrap();
        var me = view.CivView.Unwrap();
        var constants = game.Ruleset.ModOptions.Constants;

        var problems = new List<CitySiteProblem>();
        if (tile.IsWater) problems.Add(new CitySiteProblem(CitySiteProblemCodes.Water, null));
        else if (tileView.IsImpassible()) problems.Add(new CitySiteProblem(CitySiteProblemCodes.Impassable, null));
        foreach (var city in KnownCities(view).OrderBy(it => it.Tile.AerialDistanceTo(tile)).ThenBy(it => it.Name, StringComparer.Ordinal))
        {
            int limit = city.Tile.GetContinent() == tile.GetContinent() ? constants.MinimalCityDistance : constants.MinimalCityDistanceOnDifferentContinents;
            if (city.Tile.AerialDistanceTo(tile) <= limit) problems.Add(new CitySiteProblem(CitySiteProblemCodes.TooCloseToCity, city.Name));
        }
        if (OwnedByOther(tileView, me)) problems.Add(new CitySiteProblem(CitySiteProblemCodes.ForeignTerritory, null));

        int radius = constants.CityWorkRange;
        var counted = new List<TilePos>();
        var yields = new Stats();
        var resources = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var wonders = new SortedSet<string>(StringComparer.Ordinal);
        int unexplored = 0;
        foreach (var inRange in tile.GetTilesInDistance(radius).OrderBy(it => it.Position.X).ThenBy(it => it.Position.Y))
        {
            var seen = view.TileMapView.GetTile(inRange.Position);
            if (seen == null)
            {
                unexplored++;
                continue;
            }
            if (inRange != tile && OwnedByOther(seen, me)) continue;
            counted.Add(UnitQueryHelpers.Pos(inRange.Position));
            var land = seen.GetLandStats(view.CivView);
            if (inRange == tile) // the city centre is never worse than the minimum a new city gets
                foreach (var stat in StatExtensions.Entries) land[stat] = Math.Max(land[stat], Stats.DefaultCityCenterMinimum[stat]);
            yields.Add(land);
            if (seen.GetViewableResource(view.CivView) is { } resource) resources[resource.Name] = resources.GetValueOrDefault(resource.Name) + 1;
            if (seen.NaturalWonder != null) wonders.Add(seen.NaturalWonder);
        }

        return new CitySiteResult(UnitQueryHelpers.Pos(tile.Position), problems.Count == 0, problems, radius, counted, CityQueryHelpers.StatList(yields),
            resources.Select(it => new NamedAmount(it.Key, it.Value)).ToList(), wonders.ToList(), unexplored);
    }

    // Owners are live state: only a visible tile tells who owns it (SnapshotBuilder.BuildTile does the same)
    private static bool OwnedByOther(TileView tile, CivilizationClass me) =>
        tile.IsVisible() && tile.Unwrap().GetOwner() is { } owner && owner != me;

    private static IEnumerable<(string Name, Logic.Map.Tiles.Tile Tile)> KnownCities(GameView view) =>
        view.Unwrap().Civilizations
            .SelectMany(civ => view.GetForeignCivView(civ).Cities())
            .Select(it => (it.Name, it.GetCenterTile().Unwrap()));
}
