using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Models;
using Unciv.Utils;

namespace Unciv.Json;

public class CounterConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsGenericType &&
               typeToConvert.GetGenericTypeDefinition() == typeof(Counter<>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type keyType = typeToConvert.GetGenericArguments()[0];
        // Kotlin keeps the raw String name whatever K is (unchecked cast), so only Counter<String> can round-trip
        if (keyType != typeof(string))
            // JUSTIFY: T118 Kotlin keeps raw String names, only string keys round-trip (permanent)
            throw new NotSupportedException($"Json (de)serialization of {typeToConvert.Name}<{keyType.Name}> is not supported; only string keys");
        Type converterType = typeof(CounterConverter<>).MakeGenericType(keyType);
        return (JsonConverter)Activator.CreateInstance(converterType).NotNull();
    }
}

public class CounterConverter<K> : JsonConverter<Counter<K>> where K : notnull
{
    public override Counter<K> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject for Counter<{typeof(K).Name}>, got {reader.TokenType}");

        var counter = new Counter<K>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return counter;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}");

            string? propName = reader.GetString();
            reader.Read();

            int val = JsonValueReader.ReadInt(ref reader, "Counter value");

            counter[(K)(object)propName.NotNull()] = val;
        }

        throw new JsonException("Unexpected end of JSON while reading Counter");
    }

    public override void Write(Utf8JsonWriter writer, Counter<K> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (k, v) in value)
        {
            writer.WriteNumber(k.ToString().NotNull(), v);
        }
        writer.WriteEndObject();
    }
}
