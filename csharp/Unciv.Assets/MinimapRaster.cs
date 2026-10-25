using Unciv.Logic.Map;
using Unciv.Mathematics;
using Unciv.Net.Visibility;

namespace Unciv.Assets;

/// <summary>
/// C014: the minimap picture and its coordinate mapping, engine-independent (Unciv.Godot turns <see cref="Image"/> into an ImageTexture). One square cell per
/// explored tile of the viewer's snapshot at the tile's engine world position (north up), terrain colour from <see cref="PlaceholderPalette"/>, fogged tiles
/// dimmed, owned visible tiles tinted with the owner's colour and the tiles on the edge of a territory drawn in it nearly solid. Unexplored area is the
/// unexplored colour. Pixel to tile and tile to pixel use <see cref="HexMath"/>, so a click maps to the tile the engine would pick.
/// </summary>
public sealed class MinimapRaster
{
    public const int DefaultCell = 4;
    private const int Margin = 2;
    private const int OwnerTintPercent = 40;
    private const int BorderPercent = 85;
    private const int FogPercent = 60;
    private const string HillFeature = "Hill";
    private static readonly float TileSpacing = (float)Math.Sqrt(3.0);

    private readonly HashSet<HexCoord> drawn;
    private readonly float minX;
    private readonly float maxY;
    private readonly float pixelsPerWorld;
    private readonly int offset;

    private MinimapRaster(PixelImage image, HashSet<HexCoord> drawn, float minX, float maxY, float pixelsPerWorld, int cell)
    {
        offset = Margin + cell / 2;
        Image = image;
        this.drawn = drawn;
        this.minX = minX;
        this.maxY = maxY;
        this.pixelsPerWorld = pixelsPerWorld;
    }

    public PixelImage Image { get; }

    public int TileCount => drawn.Count;

    /// <param name="tiles">The viewer's explored tiles.</param>
    /// <param name="ownerColour">Colour of a civ by id, null when unknown (the tile is then drawn untinted).</param>
    /// <param name="cell">Pixels per tile edge.</param>
    public static MinimapRaster Build(IReadOnlyList<TileDto> tiles, Func<string, Rgb?> ownerColour, int cell = DefaultCell)
    {
        var scale = cell / TileSpacing;
        var drawn = new HashSet<HexCoord>();
        var byCoord = new Dictionary<HexCoord, TileDto>();
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var tile in tiles)
        {
            var coord = new HexCoord(tile.X, tile.Y);
            byCoord[coord] = tile;
            drawn.Add(coord);
            var world = HexMath.Hex2WorldCoords(coord);
            minX = Math.Min(minX, world.x);
            maxX = Math.Max(maxX, world.x);
            minY = Math.Min(minY, world.y);
            maxY = Math.Max(maxY, world.y);
        }
        if (tiles.Count == 0) minX = maxX = minY = maxY = 0f;

        var width = (int)MathF.Ceiling((maxX - minX) * scale) + cell + 2 * Margin + 1;
        var height = (int)MathF.Ceiling((maxY - minY) * scale) + cell + 2 * Margin + 1;
        var data = new byte[width * height * 4];
        var back = PlaceholderPalette.Unexplored;
        for (var i = 0; i < data.Length; i += 4) Set(data, i, back);

        var raster = new MinimapRaster(new PixelImage(width, height, data), drawn, minX, maxY, scale, cell);
        foreach (var (coord, tile) in byCoord)
        {
            var colour = TileColour(tile, IsBorder(coord, tile, byCoord), ownerColour);
            var (cx, cy) = raster.CentreOf(coord);
            var left = cx - cell / 2;
            var top = cy - cell / 2;
            for (var y = top; y < top + cell; y++)
            {
                for (var x = left; x < left + cell; x++)
                {
                    if (x >= 0 && x < width && y >= 0 && y < height) Set(data, (y * width + x) * 4, colour);
                }
            }
        }
        return raster;
    }

    /// <summary>Pixel at the centre of the tile's cell.</summary>
    public (int X, int Y) CentreOf(HexCoord coord)
    {
        var world = HexMath.Hex2WorldCoords(coord);
        return PixelOfWorld(world.x, world.y);
    }

    /// <summary>Pixel for an engine world position (north up on the picture).</summary>
    public (int X, int Y) PixelOfWorld(float worldX, float worldY) => (
        (int)MathF.Round((worldX - minX) * pixelsPerWorld, MidpointRounding.AwayFromZero) + offset,
        (int)MathF.Round((maxY - worldY) * pixelsPerWorld, MidpointRounding.AwayFromZero) + offset);

    /// <summary>The drawn tile under a pixel, or null for unexplored space and positions outside the picture.</summary>
    public HexCoord? TileAt(int pixelX, int pixelY)
    {
        var worldX = (pixelX - offset) / pixelsPerWorld + minX;
        var worldY = maxY - (pixelY - offset) / pixelsPerWorld;
        var hex = HexMath.RoundHexCoords(HexMath.World2HexCoords(new Vector2(worldX, worldY)));
        var coord = new HexCoord((int)hex.x, (int)hex.y);
        return drawn.Contains(coord) ? coord : null;
    }

    private static bool IsBorder(HexCoord coord, TileDto tile, Dictionary<HexCoord, TileDto> byCoord)
    {
        if (tile.Owner == null) return false;
        foreach (var near in HexMath.GetHexCoordsInDistance(coord, 1, false))
        {
            if (near == coord) continue;
            if (!byCoord.TryGetValue(near, out var other) || other.Owner != tile.Owner) return true;
        }
        return false;
    }

    private static Rgb TileColour(TileDto tile, bool border, Func<string, Rgb?> ownerColour)
    {
        var overlay = tile.NaturalWonder ?? tile.Features.FirstOrDefault(it => it != HillFeature);
        var colour = PlaceholderPalette.ForTerrain(overlay ?? (tile.Features.Contains(HillFeature) ? HillFeature : tile.BaseTerrain));
        if (!tile.Visible) return colour.Scale(FogPercent);
        if (tile.Owner == null || ownerColour(tile.Owner) is not { } owner) return colour;
        return Mix(colour, owner, border ? BorderPercent : OwnerTintPercent);
    }

    private static Rgb Mix(Rgb from, Rgb to, int percentTo) => new(
        (byte)((from.R * (100 - percentTo) + to.R * percentTo) / 100),
        (byte)((from.G * (100 - percentTo) + to.G * percentTo) / 100),
        (byte)((from.B * (100 - percentTo) + to.B * percentTo) / 100));

    private static void Set(byte[] data, int index, Rgb colour)
    {
        data[index] = colour.R;
        data[index + 1] = colour.G;
        data[index + 2] = colour.B;
        data[index + 3] = 255;
    }
}
