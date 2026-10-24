using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Json;

// core/src/com/unciv/json/LastSeenImprovement.kt
// Dedicated map with HexCoord keys, serialized with "(x,y)" field names. Kotlin wraps a HashMap by delegation, so
// iteration (and thus the serialized field order) is java.util.HashMap order: this wraps a JavaHashMap and exposes
// the map operations the Kotlin MutableMap delegation gave callers. Equality is content equality, like HashMap.equals.
[JsonConverter(typeof(LastSeenImprovementConverter))]
public class LastSeenImprovement : IEnumerable<KeyValuePair<HexCoord, string>>, IsPartOfGameInfoSerialization
{
    private readonly JavaHashMap<HexCoord, string> map;

    public LastSeenImprovement() => map = new JavaHashMap<HexCoord, string>();

    public LastSeenImprovement(JavaHashMap<HexCoord, string> map) => this.map = map;

    public int Count => map.Count;

    public string this[HexCoord key]
    {
        get => map[key];
        set => map[key] = value;
    }

    public bool ContainsKey(HexCoord key) => map.ContainsKey(key);

    public bool TryGetValue(HexCoord key, out string value) => map.TryGetValue(key, out value!); // JUSTIFY: T119 out value is unspecified when TryGetValue returns false

    public bool Remove(HexCoord key) => map.Remove(key);

    public void Clear() => map.Clear();

    public IEnumerator<KeyValuePair<HexCoord, string>> GetEnumerator() => map.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override bool Equals(object? obj) =>
        obj is LastSeenImprovement other && Count == other.Count
        && this.All(entry => other.TryGetValue(entry.Key, out var value) && value == entry.Value);

    // java.util.AbstractMap.hashCode: sum of (key.hashCode xor value.hashCode)
    public override int GetHashCode()
    {
        int hash = 0;
        foreach (var (key, value) in this)
            hash = unchecked(hash + (key.GetHashCode() ^ JavaHash.HashCode(value)));
        return hash;
    }
}

internal sealed class LastSeenImprovementConverter : JsonConverter<LastSeenImprovement>
{
    public override LastSeenImprovement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject for LastSeenImprovement, got {reader.TokenType}");
        var result = new LastSeenImprovement();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return result;
            string name = reader.GetString().NotNull();
            reader.Read();
            var key = ToHexCoord(name);
            string value;
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    value = reader.GetString().NotNull();
                    break;
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                    using (var doc = JsonDocument.ParseValue(ref reader))
                        value = doc.RootElement.GetRawText();
                    break;
                case JsonTokenType.StartObject:
                    value = ReadWrappedString(ref reader);
                    break;
                default:
                    throw new JsonException($"Unsupported token type {reader.TokenType} for LastSeenImprovement value");
            }
            result[key] = value;
        }
        throw new JsonException("Unexpected end of JSON while reading LastSeenImprovement");
    }

    // entry.getString("value") on the verbose {"class":..., "value":...} form; throws when absent
    private static string ReadWrappedString(ref Utf8JsonReader reader)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (!property.NameEquals("value")) continue;
            return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString().NotNull() : property.Value.GetRawText();
        }
        throw new JsonException("Missing \"value\" member in LastSeenImprovement entry");
    }

    // removeSurrounding("(", ")").split(',') destructured to x, y; each Float.toInt()
    private static HexCoord ToHexCoord(string text)
    {
        string inner = text.Length >= 2 && text.StartsWith('(') && text.EndsWith(')') ? text[1..^1] : text;
        var parts = inner.Split(',');
        if (parts.Length < 2) throw new JsonException($"Cannot parse HexCoord key '{text}'");
        return HexCoord.Of(ParseFloatToInt(parts[0]), ParseFloatToInt(parts[1]));
    }

    private static int ParseFloatToInt(string text) =>
        KotlinMath.FloatToInt(float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));

    public override void Write(Utf8JsonWriter writer, LastSeenImprovement value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, improvement) in value)
            writer.WriteString(key.ToPrettyString(), improvement);
        writer.WriteEndObject();
    }
}
