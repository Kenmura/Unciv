using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Net.Visibility;

namespace Unciv.Assets;

/// <summary>
/// T378: keeps a viewer's <see cref="TileOwnerMemory"/> in a file per game and viewer, so fogged territory keeps its border after the client restarts, loads the save or reconnects to a host.
/// Client-only: never a command, never hashed, never sent anywhere, and filled only from tiles that were in sight, so it cannot reveal more than the civ saw. The file records the game turn
/// its contents last changed; a game that is now at an earlier turn (an older save was loaded) must not show borders from its future, so such a file is ignored.
/// A file also carries a signature of the terrain under the remembered tiles; it is used only when every remembered tile is still on the map and unchanged, so a different game that reuses the
/// same key (a host starting a new game under the same host key and civ) can never inherit it. A damaged, oversized or unknown-version file reads as no memory; a failed write is reported and the memory simply stays in RAM.
/// </summary>
public sealed class TileOwnerMemoryStore(string folder)
{
    private const int FileVersion = 1;
    private const long MaxFileBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new();

    public string Folder { get; } = folder;

    /// <summary>The file for one game key and viewer. The name is a hash, so neither id can ever act as a path.</summary>
    public string PathFor(string gameKey, string viewerCivId) =>
        Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gameKey + "\n" + viewerCivId)))[..32].ToLowerInvariant() + ".json");

    private static string Signature(IEnumerable<RememberedOwner> entries, IReadOnlyList<TileDto> tiles)
    {
        var terrain = new Dictionary<(int, int), string>();
        foreach (var tile in tiles) terrain[(tile.X, tile.Y)] = tile.BaseTerrain;
        var text = new StringBuilder();
        foreach (var entry in entries) text.Append(entry.X).Append(',').Append(entry.Y).Append(',').Append(terrain.GetValueOrDefault((entry.X, entry.Y), "?")).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>
    /// Fills <paramref name="memory"/> from the file; false (memory left empty) when there is none, it is damaged, it dates from a turn after <paramref name="currentTurn"/>, or the map in
    /// <paramref name="tiles"/> (the viewer's current snapshot) does not hold the remembered tiles with the terrain they had.
    /// </summary>
    public bool Load(string gameKey, string viewerCivId, int currentTurn, IReadOnlyList<TileDto> tiles, TileOwnerMemory memory)
    {
        memory.Clear();
        try
        {
            string path = PathFor(gameKey, viewerCivId);
            if (!File.Exists(path) || new FileInfo(path).Length > MaxFileBytes) return false;
            var file = JsonSerializer.Deserialize<StoredMemory>(File.ReadAllText(path), Options);
            if (file == null || file.Version != FileVersion || file.Turn > currentTurn || file.Entries == null) return false;
            var entries = file.Entries.Select(it => new RememberedOwner(it.X, it.Y, it.Owner)).ToList();
            if (file.Signature != Signature(entries, tiles)) return false;
            memory.Restore(entries);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            memory.Clear();
            return false;
        }
    }

    /// <summary>Writes the memory (temp file then move, so a crash never leaves half a file); false when it could not be written.</summary>
    public bool Save(string gameKey, string viewerCivId, int turn, IReadOnlyList<TileDto> tiles, TileOwnerMemory memory)
    {
        string path = PathFor(gameKey, viewerCivId);
        string temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Folder);
            var entries = memory.Entries();
            var stored = new StoredMemory { Version = FileVersion, Turn = turn, Signature = Signature(entries, tiles), Entries = [.. entries.Select(it => new StoredOwner { X = it.X, Y = it.Y, Owner = it.Owner })] };
            File.WriteAllText(temp, JsonSerializer.Serialize(stored, Options));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); }
            catch (Exception d) when (d is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }

    private sealed class StoredMemory
    {
        [JsonPropertyName("version")] public int Version { get; init; }
        [JsonPropertyName("turn")] public int Turn { get; init; }
        [JsonPropertyName("terrain")] public string? Signature { get; init; }
        [JsonPropertyName("tiles")] public List<StoredOwner>? Entries { get; init; }
    }

    private sealed class StoredOwner
    {
        [JsonPropertyName("x")] public int X { get; init; }
        [JsonPropertyName("y")] public int Y { get; init; }
        [JsonPropertyName("o")] public string? Owner { get; init; }
    }
}
