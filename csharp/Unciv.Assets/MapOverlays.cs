using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.Assets;

/// <summary>One tile side that lies on a territory border: the tile, the direction to the neighbour across the side, and the owner whose border it is.</summary>
public readonly record struct BorderEdge(int X, int Y, int Dx, int Dy, string Owner);

/// <summary>A road piece: from the tile centre to the neighbour at (<see cref="Dx"/>, <see cref="Dy"/>), or a lone stub when both are 0. <see cref="Railroad"/> when both ends have rail.</summary>
public readonly record struct RoadLink(int X, int Y, int Dx, int Dy, bool Railroad);

/// <summary>An improvement standing on a tile (roads excluded).</summary>
public readonly record struct ImprovementMarker(int X, int Y, string Name, bool Pillaged);

/// <summary>The text of a city banner: a title line and optional detail lines.</summary>
public sealed record BannerText(string Title, IReadOnlyList<string> Details);

/// <summary>
/// C041: the engine-independent half of the flat map's city, border, road and improvement overlays. Reads only the viewer's snapshot DTOs and answers
/// where to draw what; the Godot side (<c>Unciv.Godot/Map/MapOverlayLayer.cs</c>) turns the answers into meshes. Output order follows the snapshot order, so it is deterministic.
/// </summary>
public static class MapOverlays
{
    /// <summary>The six neighbour offsets of a tile, in a fixed order.</summary>
    public static readonly IReadOnlyList<(int Dx, int Dy)> Neighbours = [(1, 0), (1, 1), (0, 1), (-1, 0), (-1, -1), (0, -1)];

    private const string CityCentre = "City center";
    private const string RailroadName = "Railroad";

    /// <summary>
    /// Every side of an owned, visible tile whose neighbour has a different owner. A neighbour that is missing (map edge), fogged (ownership unknown) or unowned counts as different,
    /// so a territory is always fully outlined; two tiles of one owner never draw a line between them.
    /// </summary>
    public static IReadOnlyList<BorderEdge> BorderEdges(IReadOnlyList<TileDto> tiles)
    {
        var byCoord = Index(tiles);
        var edges = new List<BorderEdge>();
        foreach (var tile in tiles)
        {
            if (!tile.Visible || tile.Owner == null) continue;
            foreach (var (dx, dy) in Neighbours)
            {
                var other = byCoord.GetValueOrDefault(new HexCoord(tile.X + dx, tile.Y + dy));
                if (other is { Visible: true } && other.Owner == tile.Owner) continue;
                edges.Add(new BorderEdge(tile.X, tile.Y, dx, dy, tile.Owner));
            }
        }
        return edges;
    }

    /// <summary>
    /// The road network: one link per pair of adjacent road tiles (listed once, from the tile that comes first in snapshot order), plus a stub for every road tile with no road neighbour.
    /// A city centre counts as a road tile (the engine connects cities by road). A pillaged road is not a road.
    /// </summary>
    public static IReadOnlyList<RoadLink> RoadLinks(IReadOnlyList<TileDto> tiles, IReadOnlyList<CityDto> cities)
    {
        var byCoord = Index(tiles);
        var cityTiles = cities.Select(it => new HexCoord(it.X, it.Y)).ToHashSet();
        // The snapshot names the tile's RoadStatus, so "None" is an absent road and only a visible tile has one
        static bool Paved(TileDto tile) => (tile.Road is "Road" or RailroadName) && tile.RoadPillaged != true;
        bool HasRoad(TileDto tile) => Paved(tile) || cityTiles.Contains(new HexCoord(tile.X, tile.Y));
        bool HasRail(TileDto tile) => tile.Road == RailroadName && tile.RoadPillaged != true;
        var order = new Dictionary<HexCoord, int>();
        for (var i = 0; i < tiles.Count; i++) order[new HexCoord(tiles[i].X, tiles[i].Y)] = i;

        var links = new List<RoadLink>();
        foreach (var tile in tiles)
        {
            if (!HasRoad(tile)) continue;
            var linked = false;
            foreach (var (dx, dy) in Neighbours)
            {
                var coord = new HexCoord(tile.X + dx, tile.Y + dy);
                if (byCoord.GetValueOrDefault(coord) is not { } other || !HasRoad(other)) continue;
                linked = true;
                if (order[coord] > order[new HexCoord(tile.X, tile.Y)]) links.Add(new RoadLink(tile.X, tile.Y, dx, dy, HasRail(tile) && HasRail(other)));
            }
            if (!linked && Paved(tile)) links.Add(new RoadLink(tile.X, tile.Y, 0, 0, HasRail(tile)));
        }
        return links;
    }

    /// <summary>The improvements to mark: every visible or fogged tile that has one, except the city centre.</summary>
    public static IReadOnlyList<ImprovementMarker> Improvements(IReadOnlyList<TileDto> tiles) =>
        [.. tiles.Where(it => it.Improvement != null && it.Improvement != CityCentre).Select(it => new ImprovementMarker(it.X, it.Y, it.Improvement!, it.ImprovementPillaged == true))];

    /// <summary>What a city banner says: the name (capital marked) with its size, then, for the viewer's own cities, what it is building, and the health when it is hurt.</summary>
    public static BannerText Banner(CityDto city)
    {
        var title = (city.IsCapital ? "* " : "") + city.Name + " (" + city.Population.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        var details = new List<string>();
        if (city.Own is { } own) details.Add(own.ConstructionQueue.Count > 0 ? "Building " + own.ConstructionQueue[0] : "Nothing in production");
        if (city.IsPuppet) details.Add("Puppet");
        if (city.IsBeingRazed) details.Add("Razing");
        if (city.Health < city.MaxHealth)
            details.Add("Health " + city.Health.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + city.MaxHealth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new BannerText(title, details);
    }

    /// <summary>Height of the city marker block for a population: grows a little with size, capped so a metropolis does not hide its neighbours.</summary>
    public static float CityBlockHeight(int population) => 0.35f + 0.05f * Math.Clamp(population, 1, 12);

    /// <summary>The ground-plane ends (world x, world z) of the side of tile (x, y) that faces the neighbour at (dx, dy). World z is minus the engine's y, as in the client's map layout.</summary>
    public static ((float X, float Z) A, (float X, float Z) B) EdgeEnds(int x, int y, int dx, int dy)
    {
        var centre = HexMath.Hex2WorldCoords(new HexCoord(x, y));
        var toNeighbour = HexMath.Hex2WorldCoords(new HexCoord(dx, dy));
        var length = MathF.Sqrt(toNeighbour.x * toNeighbour.x + toNeighbour.y * toNeighbour.y);
        // Midpoint of the side is half way to the neighbour; the side runs perpendicular to that direction and is one tile radius (1) long
        var midX = centre.x + toNeighbour.x / 2f;
        var midY = centre.y + toNeighbour.y / 2f;
        var halfX = -toNeighbour.y / length / 2f;
        var halfY = toNeighbour.x / length / 2f;
        return ((midX - halfX, -(midY - halfY)), (midX + halfX, -(midY + halfY)));
    }

    private static Dictionary<HexCoord, TileDto> Index(IReadOnlyList<TileDto> tiles)
    {
        var byCoord = new Dictionary<HexCoord, TileDto>(tiles.Count);
        foreach (var tile in tiles) byCoord[new HexCoord(tile.X, tile.Y)] = tile;
        return byCoord;
    }
}
