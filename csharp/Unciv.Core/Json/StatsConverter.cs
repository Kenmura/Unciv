using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Models.StatTypes;

namespace Unciv.Json;

public class StatsConverter : JsonConverter<Stats>
{
    public override Stats Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject for Stats, got {reader.TokenType}");

        var stats = new Stats();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return stats;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}");

            string? propName = reader.GetString();
            reader.Read();

            float val = JsonValueReader.ReadFloat(ref reader, "Stats value");

            if (propName != null)
            {
                var stat = StatExtensions.SafeValueOf(propName);
                if (stat.HasValue)
                {
                    stats[stat.Value] = val;
                }
                else
                {
                    // Case-insensitive match on stat name (e.g. "production" -> Stat.Production)
                    foreach (var s in StatExtensions.Entries)
                    {
                        if (string.Equals(s.ToString(), propName, StringComparison.OrdinalIgnoreCase))
                        {
                            stats[s] = val;
                            break;
                        }
                    }
                }
            }
        }

        throw new JsonException("Unexpected end of JSON while reading Stats");
    }

    public override void Write(Utf8JsonWriter writer, Stats value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Production != 0f) writer.WriteNumber("production", value.Production);
        if (value.Food != 0f) writer.WriteNumber("food", value.Food);
        if (value.Gold != 0f) writer.WriteNumber("gold", value.Gold);
        if (value.Science != 0f) writer.WriteNumber("science", value.Science);
        if (value.Culture != 0f) writer.WriteNumber("culture", value.Culture);
        if (value.Happiness != 0f) writer.WriteNumber("happiness", value.Happiness);
        if (value.Faith != 0f) writer.WriteNumber("faith", value.Faith);
        writer.WriteEndObject();
    }
}
