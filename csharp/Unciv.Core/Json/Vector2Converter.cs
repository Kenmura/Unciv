using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Mathematics;

namespace Unciv.Json;

public class Vector2Converter : JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject for Vector2, got {reader.TokenType}");

        float x = 0f;
        float y = 0f;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return new Vector2(x, y);

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}");

            string? propName = reader.GetString();
            reader.Read();

            float val = ReadValueAsFloat(ref reader);

            if (string.Equals(propName, "x", StringComparison.OrdinalIgnoreCase))
                x = val;
            else if (string.Equals(propName, "y", StringComparison.OrdinalIgnoreCase))
                y = val;
        }

        throw new JsonException("Unexpected end of JSON while reading Vector2");
    }

    private static float ReadValueAsFloat(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetSingle();
            case JsonTokenType.String:
                return float.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
            case JsonTokenType.StartObject:
                float result = 0f;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        break;
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        string? innerProp = reader.GetString();
                        reader.Read();
                        if (string.Equals(innerProp, "value", StringComparison.OrdinalIgnoreCase))
                            result = ReadValueAsFloat(ref reader);
                        else
                            reader.Skip();
                    }
                }
                return result;
            default:
                throw new JsonException($"Unsupported token type {reader.TokenType} for Vector2 coordinate");
        }
    }

    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.x);
        writer.WriteNumber("y", value.y);
        writer.WriteEndObject();
    }
}
