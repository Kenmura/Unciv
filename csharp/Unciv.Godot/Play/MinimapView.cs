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
    private static readonly Color FrameColour = ToColour(PlaceholderPalette.PanelBorder);
    private static readonly Color ViewColour = ToColour(PlaceholderPalette.Light);
    private static readonly Color BackColour = ToColour(PlaceholderPalette.Unexplored);
    private const float FrameWidth = 2f;

    private MapView map = null!; // JUSTIFY: C014 assigned by Attach before the minimap is used
    private MinimapRaster? raster;
    private ImageTexture? texture;
    private Vector2 lastFocus = new(float.NaN, float.NaN);
    private float lastZoom = float.NaN;
    private bool jumping;

    public MinimapRaster? Raster => raster;

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
        raster = MinimapRaster.Build(tiles, ownerColour);
        texture = ProceduralAssets.ToTexture(raster.Image);
        CustomMinimumSize = new Vector2(raster.Image.Width, raster.Image.Height);
        Size = CustomMinimumSize;
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
        var picture = new Rect2(Vector2.Zero, Size);
        return new Rect2(min, max - min).Intersection(picture);
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
        if (texture != null) DrawTexture(texture, Vector2.Zero);
        if (ViewportRect() is { } view) DrawRect(view, ViewColour, false, FrameWidth);
        DrawRect(new Rect2(Vector2.Zero, Size), FrameColour, false, FrameWidth);
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
        if (raster?.TileAt((int)local.X, (int)local.Y) is not { } tile) return false;
        var elevation = map.TileNodeAt(tile)?.Elevation ?? 1f;
        map.FocusOn(MapLayout.TileTop(tile.X, tile.Y, elevation));
        Jumped?.Invoke(tile);
        QueueRedraw();
        return true;
    }

    private static Color ToColour(Rgb rgb) => new(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);
}
