using Unciv.Assets;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Assets;

public class MinimapRasterTests
{
    private static readonly Rgb Red = new(200, 20, 20);

    private static TileDto Tile(int x, int y, string terrain = "Grassland", bool visible = true, string? owner = null, string? feature = null, string? wonder = null) => new()
    {
        X = x,
        Y = y,
        Visible = visible,
        BaseTerrain = terrain,
        Features = feature == null ? [] : [feature],
        NaturalWonder = wonder,
        RiverBottom = false,
        RiverBottomLeft = false,
        RiverBottomRight = false,
        Owner = owner,
    };

    private static List<TileDto> Block(Func<int, int, TileDto> make)
    {
        var tiles = new List<TileDto>();
        for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= 3; y++) tiles.Add(make(x, y));
        return tiles;
    }

    private static Rgb PixelAt(MinimapRaster raster, int x, int y)
    {
        var i = (y * raster.Image.Width + x) * 4;
        return new Rgb(raster.Image.Rgba[i], raster.Image.Rgba[i + 1], raster.Image.Rgba[i + 2]);
    }

    private static Rgb? Colour(string civ) => civ == "Red" ? Red : null;

    [Fact]
    public void EveryTileMapsBackToItselfFromItsCentreAndCorners()
    {
        var raster = MinimapRaster.Build(Block((x, y) => Tile(x, y)), Colour);
        Assert.Equal(49, raster.TileCount);
        for (var x = -3; x <= 3; x++)
        {
            for (var y = -3; y <= 3; y++)
            {
                var coord = new HexCoord(x, y);
                var (px, py) = raster.CentreOf(coord);
                Assert.Equal(coord, raster.TileAt(px, py));
            }
        }
    }

    [Fact]
    public void OutsideAndUnexploredPixelsPickNothing()
    {
        var raster = MinimapRaster.Build([Tile(0, 0)], Colour);
        Assert.Null(raster.TileAt(-50, -50));
        Assert.Null(raster.TileAt(raster.Image.Width + 50, 3));
        var (px, py) = raster.CentreOf(new HexCoord(0, 0));
        Assert.Null(raster.TileAt(px + 40, py));
    }

    [Fact]
    public void ImageIsOpaqueAndUnexploredSpaceIsBlack()
    {
        var raster = MinimapRaster.Build([Tile(0, 0), Tile(5, 0)], Colour);
        Assert.Equal(raster.Image.Width * raster.Image.Height * 4, raster.Image.Rgba.Length);
        for (var i = 3; i < raster.Image.Rgba.Length; i += 4) Assert.Equal(255, raster.Image.Rgba[i]);
        Assert.Equal(PlaceholderPalette.Unexplored, PixelAt(raster, 0, 0));
    }

    [Fact]
    public void TerrainFeatureWonderAndHillColours()
    {
        Rgb Centre(TileDto tile)
        {
            var raster = MinimapRaster.Build([tile], Colour);
            var (px, py) = raster.CentreOf(new HexCoord(tile.X, tile.Y));
            return PixelAt(raster, px, py);
        }
        Assert.Equal(PlaceholderPalette.ForTerrain("Plains"), Centre(Tile(0, 0, "Plains")));
        Assert.Equal(PlaceholderPalette.ForTerrain("Forest"), Centre(Tile(0, 0, "Plains", feature: "Forest")));
        Assert.Equal(PlaceholderPalette.ForTerrain("Hill"), Centre(Tile(0, 0, "Plains", feature: "Hill")));
        Assert.Equal(PlaceholderPalette.ForTerrain("Krakatoa"), Centre(Tile(0, 0, "Plains", wonder: "Krakatoa")));
    }

    [Fact]
    public void FoggedTilesAreDimmerThanVisibleOnes()
    {
        var visible = MinimapRaster.Build([Tile(0, 0)], Colour);
        var fogged = MinimapRaster.Build([Tile(0, 0, visible: false)], Colour);
        var (px, py) = visible.CentreOf(new HexCoord(0, 0));
        Assert.Equal(PlaceholderPalette.ForTerrain("Grassland").Scale(60), PixelAt(fogged, px, py));
        Assert.NotEqual(PixelAt(visible, px, py), PixelAt(fogged, px, py));
    }

    [Fact]
    public void BorderTilesCarryTheOwnerColourMoreStronglyThanInteriorOnes()
    {
        var raster = MinimapRaster.Build(Block((x, y) => Tile(x, y, owner: System.Math.Abs(x) <= 1 && System.Math.Abs(y) <= 1 ? "Red" : null)), Colour);
        var grass = PlaceholderPalette.ForTerrain("Grassland");
        var (ix, iy) = raster.CentreOf(new HexCoord(0, 0));
        var (bx, by) = raster.CentreOf(new HexCoord(1, 1));
        var (ox, oy) = raster.CentreOf(new HexCoord(3, 3));
        Assert.Equal(grass, PixelAt(raster, ox, oy));
        var interior = PixelAt(raster, ix, iy);
        var border = PixelAt(raster, bx, by);
        Assert.NotEqual(grass, interior);
        Assert.True(System.Math.Abs(border.R - Red.R) < System.Math.Abs(interior.R - Red.R));
    }

    [Fact]
    public void UnknownOwnerIsDrawnUntintedAndEmptyMapIsTiny()
    {
        var raster = MinimapRaster.Build([Tile(0, 0, owner: "Nobody")], Colour);
        var (px, py) = raster.CentreOf(new HexCoord(0, 0));
        Assert.Equal(PlaceholderPalette.ForTerrain("Grassland"), PixelAt(raster, px, py));
        var empty = MinimapRaster.Build([], Colour);
        Assert.Equal(0, empty.TileCount);
        Assert.Null(empty.TileAt(0, 0));
    }

    [Fact]
    public void WorldPositionsMapNorthUpAndRoundTripThroughPixels()
    {
        var raster = MinimapRaster.Build(Block((x, y) => Tile(x, y)), Colour, cell: 6);
        var north = HexMath.Hex2WorldCoords(new HexCoord(3, 3));
        var south = HexMath.Hex2WorldCoords(new HexCoord(-3, -3));
        Assert.True(north.y > south.y);
        Assert.True(raster.PixelOfWorld(north.x, north.y).Y < raster.PixelOfWorld(south.x, south.y).Y);
        var (px, py) = raster.PixelOfWorld(north.x, north.y);
        Assert.Equal(new HexCoord(3, 3), raster.TileAt(px, py));
    }
}
