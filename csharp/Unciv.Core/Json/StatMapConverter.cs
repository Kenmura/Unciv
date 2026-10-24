using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Json;

public class StatMapConverter : JsonConverter<StatMap>
{
    public override StatMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject for StatMap, got {reader.TokenType}");

        var statMap = new StatMap();
        var statsConverter = (JsonConverter<Stats>)options.GetConverter(typeof(Stats));

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return statMap;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}");

            string? key = reader.GetString();
            reader.Read();

            var stats = statsConverter.Read(ref reader, typeof(Stats), options) ?? new Stats();
            statMap[key.NotNull()] = stats;
        }

        throw new JsonException("Unexpected end of JSON while reading StatMap");
    }

    public override void Write(Utf8JsonWriter writer, StatMap value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        var statsConverter = (JsonConverter<Stats>)options.GetConverter(typeof(Stats));
        foreach (var (key, stats) in value)
        {
            writer.WritePropertyName(key);
            statsConverter.Write(writer, stats, options);
        }
        writer.WriteEndObject();
    }
}
