using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Logic.Map;

namespace Unciv.Json;

public class HexCoordConverter : JsonConverter<HexCoord>
{
    public override HexCoord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject for HexCoord, got {reader.TokenType}");

        int x = 0;
        int y = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return HexCoord.Of(x, y);

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}");

            string? propName = reader.GetString();
            reader.Read();

            int val = JsonValueReader.ReadFloatAsInt(ref reader, "HexCoord coordinate");

            if (string.Equals(propName, "x", StringComparison.OrdinalIgnoreCase))
                x = val;
            else if (string.Equals(propName, "y", StringComparison.OrdinalIgnoreCase))
                y = val;
        }

        throw new JsonException("Unexpected end of JSON while reading HexCoord");
    }

    public override void Write(Utf8JsonWriter writer, HexCoord value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.x != 0) writer.WriteNumber("x", value.x);
        if (value.y != 0) writer.WriteNumber("y", value.y);
        writer.WriteEndObject();
    }
}
