using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C014: the minimap. A texture generated from the viewer's snapshot tiles (<see cref="MinimapRaster"/>: terrain colour, fog dimming, civ borders), the
/// main camera's visible ground drawn as a rectangle over it, and click or drag to jump the camera to the tile under the cursor. It reads only the
/// snapshot and the map view and sends no command.
/// </summary>
public partial class MinimapView : Control
{
    private static readonly Color ViewColour = ToColour(PlaceholderPalette.Light);
    private static readonly Color BackColour = ToColour(PlaceholderPalette.Unexplored);
    private const float FrameWidth = 2f;
    private const int MaxCell = 8;

    private MapView map = null!; // JUSTIFY: C014 assigned by Attach before the minimap is used
    private MinimapRaster? raster;
    private ImageTexture? texture;
    private Vector2 lastFocus = new(float.NaN, float.NaN);
    private float lastZoom = float.NaN;
    private bool jumping;
    private Vector2 slot;

    public MinimapRaster? Raster => raster;

    /// <summary>Where the picture's top left corner lies inside this control (the picture is centred in the slot; a click maps through it).</summary>
    public Vector2 PictureOffset => raster == null ? Vector2.Zero : ((Size - new Vector2(raster.Image.Width, raster.Image.Height)) / 2f).Floor();

    /// <summary>C067: the size of the dock this minimap fills (per breakpoint, never below 140x100). The picture is rebuilt to fit it on the next <see cref="Update"/>.</summary>
    public void SetSlot(Vector2 size)
    {
        slot = size;
        ClipContents = true; // a map too big even for one-pixel cells is cropped to the dock, not drawn over the HUD
        CustomMinimumSize = size;
        Size = size;
        if (lastTiles != null) Update(lastTiles, lastOwnerColour!);
    }

    private IReadOnlyList<TileDto>? lastTiles;
    private Func<string, Rgb?>? lastOwnerColour;

    /// <summary>Raised after every jump with the tile jumped to.</summary>
    public event Action<HexCoord>? Jumped;

    public void Attach(MapView mapView)
    {
        map = mapView;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>Redraws the picture from <paramref name="tiles"/>; <paramref name="ownerColour"/> gives each civ's border colour.</summary>
    public void Update(IReadOnlyList<TileDto> tiles, Func<string, Rgb?> ownerColour)
    {
        lastTiles = tiles;
        lastOwnerColour = ownerColour;
        raster = MinimapRaster.Build(tiles, ownerColour);
        // a map bigger than the dock is drawn with smaller cells (down to one pixel) so the whole of it fits; a smaller one keeps the default cell and is centred
        if (slot != default && (raster.Image.Width > slot.X || raster.Image.Height > slot.Y))
        {
            int cell = MinimapRaster.DefaultCell;
            while (cell > 1 && (raster.Image.Width > slot.X || raster.Image.Height > slot.Y))
                raster = MinimapRaster.Build(tiles, ownerColour, --cell);
        }
        else if (slot != default)
        {
            // a small map is drawn larger (up to 8 px a tile) so it fills the dock instead of floating in it
            int cell = MinimapRaster.DefaultCell;
            while (cell < MaxCell)
            {
                var bigger = MinimapRaster.Build(tiles, ownerColour, cell + 1);
                if (bigger.Image.Width > slot.X || bigger.Image.Height > slot.Y) break;
                raster = bigger;
                cell++;
            }
        }
        texture = ProceduralAssets.ToTexture(raster.Image);
        QueueRedraw();
    }

    /// <summary>The camera's visible ground as a rectangle in minimap pixels, clipped to the picture; null before the first update.</summary>
    public Rect2? ViewportRect()
    {
        if (raster == null || map == null) return null;
        var screen = GetViewportRect().Size;
        Vector2[] corners = [Vector2.Zero, new(screen.X, 0f), screen, new(0f, screen.Y)];
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var corner in corners)
        {
            if (map.WorldAt(corner) is not { } world) return null;
            var (px, py) = raster.PixelOfWorld(world.X, world.Y);
            min = new Vector2(Math.Min(min.X, px), Math.Min(min.Y, py));
            max = new Vector2(Math.Max(max.X, px), Math.Max(max.Y, py));
        }
        var picture = new Rect2(Vector2.Zero, new Vector2(raster.Image.Width, raster.Image.Height));
        var clipped = new Rect2(min, max - min).Intersection(picture);
        return new Rect2(clipped.Position + PictureOffset, clipped.Size);
    }

    public override void _Process(double delta)
    {
        if (map == null) return;
        var focus = new Vector2(map.Focus.X, map.Focus.Z);
        if (focus == lastFocus && map.Zoom == lastZoom) return;
        lastFocus = focus;
        lastZoom = map.Zoom;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), BackColour);
        if (texture != null) DrawTexture(texture, PictureOffset);
        if (ViewportRect() is { } view) DrawRect(view, ViewColour, false, FrameWidth);
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                jumping = button.Pressed;
                if (button.Pressed) JumpTo(button.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when jumping:
                JumpTo(motion.Position);
                AcceptEvent();
                break;
        }
    }

    /// <summary>Centres the main camera on the tile under a minimap-local position; does nothing over unexplored space.</summary>
    public bool JumpTo(Vector2 local)
    {
        local -= PictureOffset;
        if (raster?.TileAt((int)local.X, (int)local.Y) is not { } tile) return false;
        var elevation = map.TileNodeAt(tile)?.Elevation ?? 1f;
        map.FocusOn(MapLayout.TileTop(tile.X, tile.Y, elevation));
        Jumped?.Invoke(tile);
        QueueRedraw();
        return true;
    }

    private static Color ToColour(Rgb rgb) => new(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);
}
