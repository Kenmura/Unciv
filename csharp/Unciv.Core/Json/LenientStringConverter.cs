using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unciv.Json;

/// <summary>
/// libGDX <c>Json</c> reads any primitive JSON value into a <c>String</c> field via <c>JsonValue.asString()</c>, so a save or fixture holding
/// <c>"data1":0</c> loads as <c>"0"</c>. System.Text.Json rejects numbers and booleans for strings; this reads them as their raw JSON text (T151).
/// Writing is unchanged.
/// </summary>
public class LenientStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray<byte>() : reader.ValueSpan),
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        JsonTokenType.Null => null,
        _ => throw new JsonException($"Cannot read {reader.TokenType} as a string"),
    };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
