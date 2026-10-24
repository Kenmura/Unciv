using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Unciv.Json;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;

namespace Unciv.Logic.Civilization;

// core/src/com/unciv/logic/civilization/CivRankingHistory.kt
// Kotlin extends HashMap<Int, Map<RankingType, Int>>; this wraps a JavaHashMap so iteration (and the serialized turn
// order) is java.util.HashMap order (Int keys have a deterministic JVM hashCode). The inner maps are insertion-ordered
// (Kotlin: associateWith / mutableMapOf / mapOf are LinkedHashMaps). Equality is content equality, like HashMap.equals.
// Not ported: recordRankingStats(Civilization), which needs Civilization.getStatForRanking and gameInfo.turns.

/// <summary>Records for each turn (key of outer map) what the score (value of inner map) was for each RankingType.</summary>
[JsonConverter(typeof(CivRankingHistoryConverter))]
public class CivRankingHistory : IEnumerable<KeyValuePair<int, IReadOnlyDictionary<RankingType, int>>>, IsPartOfGameInfoSerialization
{
    private readonly JavaHashMap<int, IReadOnlyDictionary<RankingType, int>> map = new();

    public int Count => map.Count;

    public IReadOnlyDictionary<RankingType, int> this[int turn]
    {
        get => map[turn];
        set => map[turn] = value;
    }

    public bool ContainsKey(int turn) => map.ContainsKey(turn);

    public bool TryGetValue(int turn, out IReadOnlyDictionary<RankingType, int> value) => map.TryGetValue(turn, out value!); // JUSTIFY: T119 out value is unspecified when TryGetValue returns false

    public bool Remove(int turn) => map.Remove(turn);

    public IEnumerator<KeyValuePair<int, IReadOnlyDictionary<RankingType, int>>> GetEnumerator() => map.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns a shallow copy of this <see cref="CivRankingHistory"/> instance.
    /// The inner maps are not cloned, only their references are copied.
    /// </summary>
    public CivRankingHistory Clone()
    {
        var toReturn = new CivRankingHistory();
        toReturn.map.PutAll(map);
        return toReturn;
    }

    public override bool Equals(object? obj) =>
        obj is CivRankingHistory other && Count == other.Count
        && this.All(entry => other.TryGetValue(entry.Key, out var value) && InnerEquals(entry.Value, value));

    private static bool InnerEquals(IReadOnlyDictionary<RankingType, int> a, IReadOnlyDictionary<RankingType, int> b) =>
        a.Count == b.Count && a.All(entry => b.TryGetValue(entry.Key, out var value) && value == entry.Value);

    // java.util.AbstractMap.hashCode shape; enum hashCodes are identity hashes on the JVM, so the ordinal stands in.
    public override int GetHashCode()
    {
        int hash = 0;
        foreach (var (turn, rankings) in this)
        {
            int inner = 0;
            foreach (var (type, value) in rankings)
                inner = unchecked(inner + ((int)type ^ value));
            hash = unchecked(hash + (turn ^ inner));
        }
        return hash;
    }
}

internal sealed class CivRankingHistoryConverter : JsonConverter<CivRankingHistory>
{
    // Rankings can be negative, so we can't just \D. ASCII digits only, like Java's \d.
    private static readonly Regex NonNumber = new("[^0-9-]", RegexOptions.CultureInvariant);

    /// <summary>
    /// Output looked like this: <c>statsHistory:{0:{S:50,G:120,...},1:{S:55,G:80,...}}</c> (old format, still read).
    /// New format looks like this: <c>statsHistory:{0:"S50G120,...",1:"S55G80,..."}</c>
    /// </summary>
    public override void Write(Utf8JsonWriter writer, CivRankingHistory value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (turn, rankings) in value)
        {
            var builder = new StringBuilder();
            foreach (var (type, ranking) in rankings)
                builder.Append(type.IdForSerialization()).Append(ranking.ToString(CultureInfo.InvariantCulture));
            writer.WriteString(turn.ToString(CultureInfo.InvariantCulture), builder.ToString());
        }
        writer.WriteEndObject();
    }

    public override CivRankingHistory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject for CivRankingHistory, got {reader.TokenType}");
        var result = new CivRankingHistory();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return result;
            int turn = int.Parse(reader.GetString().NotNull(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            reader.Read();
            var rankings = new Dictionary<RankingType, int>();

            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    ReadNewFormat(reader.GetString().NotNull(), rankings);
                    break;
                case JsonTokenType.StartObject:
                    ReadOldFormat(ref reader, rankings);
                    break;
                default:
                    // Kotlin iterates the children of any other value, of which a scalar has none
                    reader.Skip();
                    break;
            }

            result[turn] = rankings;
        }
        throw new JsonException("Unexpected end of JSON while reading CivRankingHistory");
    }

    private static void ReadNewFormat(string text, Dictionary<RankingType, int> rankings)
    {
        // split into key-value pairs by adding a space before every non-digit, and splitting by spaces
        var pairs = NonNumber.Replace(text, " $0").Split(' ').Where(pair => pair.Length != 0);

        foreach (var pair in pairs)
        {
            var rankingType = RankingTypes.FromIdForSerialization(pair[0]);
            if (rankingType == null) continue;
            if (!int.TryParse(pair.AsSpan(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)) continue;
            rankings[rankingType.Value] = value;
        }
    }

    private static void ReadOldFormat(ref Utf8JsonReader reader, Dictionary<RankingType, int> rankings)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString().NotNull();
            reader.Read();
            // Unknown ranking types are silently dropped, without looking at their value
            var rankingType = name.Length == 1 ? RankingTypes.FromIdForSerialization(name[0]) : null;
            if (rankingType == null)
            {
                reader.Skip();
                continue;
            }
            rankings[rankingType.Value] = JsonValueReader.ReadInt(ref reader, "ranking value");
        }
    }
}
