using System.Globalization;
using System.Text;
using System.Text.Json;
using Unciv.Utils;

namespace Unciv.Json;

// Reads a numeric json value the way libGDX does for Unciv: a bare number, a string, or the verbose
// {"class": "java.lang.Float", "value": 23} object form. Float to Int conversion truncates (Kotlin toInt()).
internal static class JsonValueReader
{
    // JsonValue.asInt(): integer literals are longs narrowed with (int), others are doubles narrowed with saturating (int).
    public static int ReadInt(ref Utf8JsonReader reader, string what)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt64(out long l) ? unchecked((int)l) : KotlinMath.DoubleToInt(reader.GetDouble());
            case JsonTokenType.String:
                return int.Parse(reader.GetString()!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            case JsonTokenType.True:
                return 1;
            case JsonTokenType.False:
                return 0;
            case JsonTokenType.StartObject:
                var wrapped = ReadWrappedValue(ref reader, what);
                return ReadInt(ref wrapped, what);
            default:
                throw new JsonException($"Unsupported token type {reader.TokenType} for {what}");
        }
    }

    // json.readValue(name, Float::class.java, ...): Float parsing, no narrowing.
    public static float ReadFloat(ref Utf8JsonReader reader, string what)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetSingle();
            case JsonTokenType.String:
                return float.Parse(reader.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture);
            case JsonTokenType.StartObject:
                var wrapped = ReadWrappedValue(ref reader, what);
                return ReadFloat(ref wrapped, what);
            default:
                throw new JsonException($"Unsupported token type {reader.TokenType} for {what}");
        }
    }

    // Float read then Kotlin Float.toInt() (HexCoord.Serializer reads x/y as Float, then truncates).
    public static int ReadFloatAsInt(ref Utf8JsonReader reader, string what) =>
        KotlinMath.FloatToInt(ReadFloat(ref reader, what));

    // Positioned on StartObject: consumes the object and returns a reader positioned on its "value" member.
    // Throws if there is none (libGDX getInt("value") throws; the member name is case-sensitive).
    private static Utf8JsonReader ReadWrappedValue(ref Utf8JsonReader reader, string what)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!property.NameEquals("value")) continue;
            var inner = new Utf8JsonReader(Encoding.UTF8.GetBytes(property.Value.GetRawText()));
            inner.Read();
            return inner;
        }
        throw new JsonException($"Missing \"value\" member in object for {what}");
    }
}
