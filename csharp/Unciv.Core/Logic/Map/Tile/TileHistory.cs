using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Json;

namespace Unciv.Logic.Map.Tiles;

/// <summary>
/// What <see cref="TileHistory"/> reads of the Kotlin <c>Tile</c> (<c>tile.getOwner()?.civID</c>, <c>tile.isCityCenter()</c>,
/// <c>tile.getCity()?.isCapital()</c>, <c>tile.tileMap.gameInfo.turns</c>). The Tile class (still to be ported) implements it.
/// </summary>
public interface ITileHistorySource
{
    /// <summary><c>tile.tileMap.gameInfo.turns</c></summary>
    int Turns { get; }
    /// <summary><c>tile.getOwner()?.civID</c></summary>
    string? OwnerCivID { get; }
    /// <summary><c>tile.isCityCenter()</c></summary>
    bool IsCityCenter { get; }
    /// <summary><c>tile.getCity()?.isCapital() == true</c></summary>
    bool IsCityCapital { get; }
}

/// <summary>
/// Records events throughout the game related to a tile, used for end of game replay.
/// Ported from <c>core/src/com/unciv/logic/map/tile/TileHistory.kt</c>.
/// </summary>
[JsonConverter(typeof(TileHistoryConverter))]
public class TileHistory : IsPartOfGameInfoSerialization, IEnumerable<KeyValuePair<int, TileHistory.TileHistoryState>>
{
    public enum CityCenterType
    {
        None,
        Regular,
        Capital,
    }

    /// <remarks>Like the Kotlin class this has reference equality (it is not a data class).</remarks>
    public class TileHistoryState : IsPartOfGameInfoSerialization
    {
        /// <summary>The name of the civilization owning this tile or <c>null</c> if there is no owner.</summary>
        public string? OwningCivName { get; }
        /// <summary>Which type of city center this tile is, if any.</summary>
        public CityCenterType CityCenterTypeValue { get; }

        public TileHistoryState(string? owningCivName = null, CityCenterType cityCenterType = CityCenterType.None)
        {
            OwningCivName = owningCivName;
            CityCenterTypeValue = cityCenterType;
        }

        public TileHistoryState(ITileHistorySource tile) : this(
            tile.OwnerCivID,
            !tile.IsCityCenter ? CityCenterType.None
                : tile.IsCityCapital ? CityCenterType.Capital
                : CityCenterType.Regular)
        { }
    }

    public static string SerializedRepresentation(CityCenterType type) => type switch
    {
        CityCenterType.None => "N",
        CityCenterType.Regular => "R",
        CityCenterType.Capital => "C",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static CityCenterType Deserialize(string s) => s switch
    {
        "R" => CityCenterType.Regular,
        "C" => CityCenterType.Capital,
        _ => CityCenterType.None,
    };

    /// <summary>History records by turn (a TreeMap in Kotlin: sorted by turn).</summary>
    private readonly SortedList<int, TileHistoryState> history;

    public TileHistory() => history = new SortedList<int, TileHistoryState>();

    private TileHistory(SortedList<int, TileHistoryState> history) => this.history = history;

    public void RecordTakeOwnership(ITileHistorySource tile) =>
        history[tile.Turns] = new TileHistoryState(tile);

    public void RecordRelinquishOwnership(ITileHistorySource tile) =>
        history[tile.Turns] = new TileHistoryState();

    /// <summary><c>history.floorEntry(turn)?.value ?: TileHistoryState()</c></summary>
    public TileHistoryState GetState(int turn)
    {
        var keys = history.Keys;
        int lo = 0, hi = keys.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (keys[mid] <= turn) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found >= 0 ? history.Values[found] : new TileHistoryState();
    }

    public TileHistory Clone() => new(new SortedList<int, TileHistoryState>(history));

    /// <remarks>Kotlin <c>@VisibleForTesting</c>.</remarks>
    public void AddTestEntry(int turn, TileHistoryState entry) => history[turn] = entry;

    /// <remarks>Kotlin <c>@VisibleForTesting</c>.</remarks>
    public IEnumerator<KeyValuePair<int, TileHistoryState>> GetEnumerator() => history.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Value equality of the maps, where the values compare by reference (TileHistoryState is no data class)
    public override bool Equals(object? obj)
    {
        if (obj is not TileHistory other) return false;
        if (history.Count != other.history.Count) return false;
        foreach (var (turn, state) in history)
        {
            if (!other.history.TryGetValue(turn, out var otherState) || !ReferenceEquals(state, otherState)) return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        int hash = 0;
        foreach (var (turn, state) in history)
            hash = unchecked(hash + (turn ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(state)));
        return hash;
    }
}

/// <summary>
/// Output looked like this: <c>history:{"0":["Spain","C"],"12":["China","R"]}</c>.
/// Reading: a non-string (or empty) first element means no owner; a missing second element throws.
/// </summary>
internal sealed class TileHistoryConverter : JsonConverter<TileHistory>
{
    public override TileHistory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject for TileHistory, got {reader.TokenType}");
        var result = new TileHistory();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return result;
            int turn = int.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException($"Expected StartArray for TileHistory entry, got {reader.TokenType}");
            using var doc = JsonDocument.ParseValue(ref reader);
            var elements = doc.RootElement.EnumerateArray().ToList();
            string? owner = elements.Count > 0 && elements[0].ValueKind == JsonValueKind.String ? elements[0].GetString() : "";
            if (string.IsNullOrEmpty(owner)) owner = null;
            if (elements.Count < 2) throw new JsonException("Missing city center type in TileHistory entry");
            string typeText = elements[1].ValueKind == JsonValueKind.String ? elements[1].GetString()! : elements[1].GetRawText();
            result.AddTestEntry(turn, new TileHistory.TileHistoryState(owner, TileHistory.Deserialize(typeText)));
        }
        throw new JsonException("Unexpected end of JSON while reading TileHistory");
    }

    public override void Write(Utf8JsonWriter writer, TileHistory value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (turn, state) in value)
        {
            writer.WriteStartArray(turn.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (state.OwningCivName == null) writer.WriteNullValue();
            else writer.WriteStringValue(state.OwningCivName);
            writer.WriteStringValue(TileHistory.SerializedRepresentation(state.CityCenterTypeValue));
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }
}
