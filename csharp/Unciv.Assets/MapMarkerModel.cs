using System.Text.Json.Serialization;
using Unciv.GodotClient.Settings;

namespace Unciv.Assets;

/// <summary>C089: one private tack on a tile (the tile, one of <see cref="MapMarkerModel.Icons"/>, a short label). Lives in the client settings only.</summary>
public sealed record MapMarker
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = MapMarkerModel.DefaultIcon;

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";
}

/// <summary>
/// C089: the rules of the player's private map markers, Godot-free so they are unit-tested. Markers belong to the player's machine: they are stored in
/// <see cref="ClientSettings.MapMarkers"/> under a key made of the game id and the viewing civ (a hotseat opponent never sees them), are never a game command,
/// never part of any state hash and never sent to a host. A game without an id (a joined network game: the host does not tell the client the id yet) cannot
/// store markers, so its markers last for the session only (<see cref="Key"/> returns null; the screen keeps them in memory).
/// </summary>
public static class MapMarkerModel
{
    public const string DefaultIcon = "Pin";

    /// <summary>The longest label, after trimming.</summary>
    public const int MaxLabelLength = 24;

    /// <summary>The most markers one game holds (a hand-edited file cannot grow the map layer without bound).</summary>
    public const int MaxPerGame = 200;

    private const string ForbiddenChars = "[]{}\"\\<>";

    private static readonly (string Name, string Glyph, Rgb Colour)[] IconTable =
    [
        ("Pin", "", new Rgb(215, 60, 55)),
        ("Star", "*", new Rgb(235, 190, 45)),
        ("Danger", "!", new Rgb(235, 120, 30)),
        ("Question", "?", new Rgb(70, 130, 215)),
        ("Target", "+", new Rgb(150, 80, 190)),
        ("Home", "H", new Rgb(70, 165, 90)),
    ];

    /// <summary>The icon names a marker can have, in the order the editor offers them.</summary>
    public static IReadOnlyList<string> Icons { get; } = [.. IconTable.Select(it => it.Name)];

    public static Rgb ColourOf(string icon) => IconTable.FirstOrDefault(it => it.Name == icon, IconTable[0]).Colour;

    /// <summary>The character drawn on the tack's head ("" for the plain pin).</summary>
    public static string GlyphOf(string icon) => IconTable.FirstOrDefault(it => it.Name == icon, IconTable[0]).Glyph;

    /// <summary>The settings key of a game as seen by one civ, or null when the game has no id (nothing can be stored).</summary>
    public static string? Key(string? gameId, string viewerCivId) =>
        string.IsNullOrWhiteSpace(gameId) ? null : gameId + "/" + viewerCivId;

    /// <summary>The label that would be stored for what was typed: trimmed, control and markup characters removed, at most <see cref="MaxLabelLength"/> characters.</summary>
    public static string NormalizeLabel(string? typed)
    {
        var clean = new string((typed ?? "").Where(it => !char.IsControl(it) && !ForbiddenChars.Contains(it)).ToArray()).Trim();
        return clean.Length > MaxLabelLength ? clean[..MaxLabelLength].TrimEnd() : clean;
    }

    /// <summary>The markers of a game, in the order they were placed (empty for no key or an unknown game).</summary>
    public static IReadOnlyList<MapMarker> For(ClientSettings settings, string? key) =>
        key != null && settings.MapMarkers.TryGetValue(key, out var list) ? list : [];

    public static MapMarker? At(ClientSettings settings, string? key, int x, int y) =>
        For(settings, key).FirstOrDefault(it => it.X == x && it.Y == y);

    /// <summary>The settings with a marker on the tile (it replaces the one already there, keeping its place in the order). A full game refuses a new tile (the settings come back unchanged).</summary>
    public static ClientSettings Place(ClientSettings settings, string key, int x, int y, string icon, string? label)
    {
        var marker = new MapMarker { X = x, Y = y, Icon = Icons.Contains(icon) ? icon : DefaultIcon, Label = NormalizeLabel(label) };
        var list = For(settings, key).ToList();
        int at = list.FindIndex(it => it.X == x && it.Y == y);
        if (at >= 0) list[at] = marker;
        else if (list.Count >= MaxPerGame) return settings;
        else list.Add(marker);
        return settings with { MapMarkers = With(settings.MapMarkers, key, list) };
    }

    /// <summary>The settings without the tile's marker (unchanged when it has none).</summary>
    public static ClientSettings Remove(ClientSettings settings, string key, int x, int y)
    {
        var list = For(settings, key).Where(it => it.X != x || it.Y != y).ToList();
        return list.Count == For(settings, key).Count ? settings : settings with { MapMarkers = With(settings.MapMarkers, key, list) };
    }

    /// <summary>The marker map made safe for a hand-edited or damaged file: unknown icons become the pin, labels are normalized, a tile holds one marker (the last wins), at most <see cref="MaxPerGame"/> per game, empty games and empty keys dropped.</summary>
    public static SortedDictionary<string, List<MapMarker>> Sanitize(IDictionary<string, List<MapMarker>>? raw)
    {
        var result = new SortedDictionary<string, List<MapMarker>>(StringComparer.Ordinal);
        if (raw == null) return result;
        foreach (var (key, list) in raw)
        {
            if (string.IsNullOrEmpty(key) || list == null) continue;
            var kept = new List<MapMarker>();
            foreach (var marker in list.Where(it => it != null))
            {
                var clean = marker with { Icon = Icons.Contains(marker.Icon) ? marker.Icon : DefaultIcon, Label = NormalizeLabel(marker.Label) };
                int at = kept.FindIndex(it => it.X == clean.X && it.Y == clean.Y);
                if (at >= 0) kept[at] = clean;
                else if (kept.Count < MaxPerGame) kept.Add(clean);
            }
            if (kept.Count > 0) result[key] = kept;
        }
        return result;
    }

    private static SortedDictionary<string, List<MapMarker>> With(SortedDictionary<string, List<MapMarker>> all, string key, List<MapMarker> list)
    {
        var copy = new SortedDictionary<string, List<MapMarker>>(all, StringComparer.Ordinal);
        if (list.Count == 0) copy.Remove(key);
        else copy[key] = list;
        return copy;
    }
}
