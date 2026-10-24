using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Models;

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
        Type converterType = typeof(CounterConverter<>).MakeGenericType(keyType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
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

            int val = ReadValueAsInt(ref reader);

            K key = ConvertKey(propName!);
            counter[key] = val;
        }

        throw new JsonException("Unexpected end of JSON while reading Counter");
    }

    private static K ConvertKey(string keyString)
    {
        if (typeof(K) == typeof(string))
            return (K)(object)keyString;
        return (K)Convert.ChangeType(keyString, typeof(K));
    }

    private static int ReadValueAsInt(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt32(out int i) ? i : (int)Math.Round(reader.GetDouble());
            case JsonTokenType.String:
                return int.Parse(reader.GetString()!);
            case JsonTokenType.StartObject:
                int result = 0;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        break;
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        string? innerProp = reader.GetString();
                        reader.Read();
                        if (string.Equals(innerProp, "value", StringComparison.OrdinalIgnoreCase))
                            result = ReadValueAsInt(ref reader);
                        else
                            reader.Skip();
                    }
                }
                return result;
            default:
                throw new JsonException($"Unsupported token type {reader.TokenType} for Counter value");
        }
    }

    public override void Write(Utf8JsonWriter writer, Counter<K> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (k, v) in value)
        {
            writer.WriteNumber(k.ToString()!, v);
        }
        writer.WriteEndObject();
    }
}
