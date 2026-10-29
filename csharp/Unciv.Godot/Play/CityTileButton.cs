using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Ui;
using Unciv.GodotClient.Localization;
using System.Globalization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C069: one tile of the city screen's work range as a flat-top hexagon (the shape of the map's tiles). Its colours come from the theme variation of its state
/// (<see cref="UiVariants.CityTile"/>: "hex_fill", "hex_border"), so no stylebox is made per cell. Each yield is drawn as the stat's icon and its value; a locked tile
/// gets a thick accent outline and a lock. Only the hexagon takes the pointer (<see cref="_HasPoint"/>), so neighbouring cells whose rectangles overlap do not steal clicks.
/// </summary>
public partial class CityTileButton : Button
{
    private const float Sqrt3Half = 0.8660254f;
    private readonly UiTheme theme;
    private readonly CityTileCell cell;
    private readonly List<(Texture2D Icon, string Text)> yields = [];
    private readonly Texture2D lockIcon;

    public CityTileButton(UiTheme uiTheme, CityTileCell tile)
    {
        theme = uiTheme;
        cell = tile;
        Name = $"Tile_{tile.X}_{tile.Y}";
        ThemeTypeVariation = UiVariants.CityTile(tile.State);
        TooltipText = tile.Tooltip.TrText();
        foreach (var yield in tile.Yields)
            yields.Add((uiTheme.Icon(AssetKey.Of(RulesetAssetKeys.StatIcon, yield.Stat)), yield.Value.ToString("0.#", CultureInfo.InvariantCulture)));
        lockIcon = uiTheme.Icon(AssetKey.Of(RulesetAssetKeys.UiIcon, "LockSmall"));
    }

    public CityTileCell Cell => cell;

    /// <summary>The height of a hexagon that is <paramref name="width"/> wide (corner to corner).</summary>
    public static float HeightFor(float width) => width * Sqrt3Half;

    public override bool _HasPoint(Vector2 point)
    {
        float dx = Math.Abs(point.X - Size.X / 2f) / (Size.X / 2f);
        float dy = Math.Abs(point.Y - Size.Y / 2f) / (Size.Y / 2f);
        return dy <= 1f && dx <= 1f - dy / 2f;
    }

    public override void _Draw()
    {
        float cx = Size.X / 2f, cy = Size.Y / 2f, rx = cx, ry = cy;
        Vector2[] hex =
        [
            new(cx + rx, cy), new(cx + rx / 2f, cy + ry), new(cx - rx / 2f, cy + ry), new(cx - rx, cy), new(cx - rx / 2f, cy - ry), new(cx + rx / 2f, cy - ry),
        ];
        var fill = GetThemeColor("hex_fill");
        if (IsHovered() && !Disabled) fill = fill.Lightened(0.15f);
        DrawColoredPolygon(hex, fill);
        var outline = new List<Vector2>(hex) { hex[0] };
        DrawPolyline([.. outline], GetThemeColor("hex_border"), theme.Px(UiTokens.BorderWidth) * 1.5f, true);
        if (cell.Locked) DrawPolyline([.. outline], ProceduralAssets.ToColor(UiTokens.Accent), theme.Px(3), true);
        if (HasFocus()) DrawPolyline([.. outline], ProceduralAssets.ToColor(UiTokens.AccentHi), theme.Px(UiTokens.RingWidth), true);

        var font = GetThemeFont("font", "Label");
        int fontSize = Math.Max(8, (int)(Size.X * 0.17f));
        float icon = Math.Max(8f, Size.X * 0.2f);
        if (cell.Locked) DrawTextureRect(lockIcon, new Rect2(cx - icon / 2f, 2f, icon, icon), false, ProceduralAssets.ToColor(UiTokens.Accent));
        const int columns = 2;
        int rows = (yields.Count + columns - 1) / columns;
        float lineHeight = icon + 1f;
        float top = cy - rows * lineHeight / 2f + (cell.Locked ? icon / 2f : 0f);
        for (int row = 0; row < rows; row++)
        {
            var items = yields.Skip(row * columns).Take(columns).ToList();
            float[] widths = items.Select(it => icon + 1f + font.GetStringSize(it.Text, HorizontalAlignment.Left, -1, fontSize).X).ToArray();
            float total = widths.Sum() + (items.Count - 1) * 3f;
            float x = cx - total / 2f;
            for (int i = 0; i < items.Count; i++)
            {
                float y = top + row * lineHeight;
                DrawTextureRect(items[i].Icon, new Rect2(x, y, icon, icon), false);
                DrawString(font, new Vector2(x + icon + 1f, y + icon * 0.85f), items[i].Text, HorizontalAlignment.Left, -1, fontSize, ProceduralAssets.ToColor(UiTokens.TextPrimary));
                x += widths[i] + 3f;
            }
        }
    }
}
