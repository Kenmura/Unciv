using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Utils;

namespace Unciv.Json;

// Gdx Json writes a Kotlin HashSet<T> as a JSON array and a HashMap<String, V> as a JSON object, both in java.util
// iteration order (JavaHashSet/JavaHashMap iterate in that order). Reading inserts in file order into a fresh
// java.util-faithful table, as Gdx does, so the resulting iteration order is the JVM one whatever the file order was.
public class JavaHashSetConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(JavaHashSet<>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type elementType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(typeof(JavaHashSetConverter<>).MakeGenericType(elementType)).NotNull();
    }
}

public class JavaHashSetConverter<T> : JsonConverter<JavaHashSet<T>> where T : notnull
{
    public override JavaHashSet<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected StartArray for JavaHashSet<{typeof(T).Name}>, got {reader.TokenType}");
        var set = new JavaHashSet<T>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return set;
            var item = JsonSerializer.Deserialize<T>(ref reader, options) ?? throw new JsonException("Null element in JavaHashSet");
            set.Add(item);
        }
        throw new JsonException("Unexpected end of JSON while reading JavaHashSet");
    }

    public override void Write(Utf8JsonWriter writer, JavaHashSet<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
            JsonSerializer.Serialize(writer, item, options);
        writer.WriteEndArray();
    }
}

public class JavaHashMapConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(JavaHashMap<,>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var args = typeToConvert.GetGenericArguments();
        // Gdx writes map keys as their toString and reads them back as raw Strings, so only String keys round-trip
        if (args[0] != typeof(string))
            // JUSTIFY: T118 Gdx writes map keys as toString, only string keys round-trip (permanent)
            throw new NotSupportedException($"Json (de)serialization of {typeToConvert.Name}<{args[0].Name},...> is not supported; only string keys");
        return (JsonConverter)Activator.CreateInstance(typeof(JavaHashMapConverter<>).MakeGenericType(args[1])).NotNull();
    }
}

public class JavaHashMapConverter<TValue> : JsonConverter<JavaHashMap<string, TValue>>
{
    public override JavaHashMap<string, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject for JavaHashMap<string,{typeof(TValue).Name}>, got {reader.TokenType}");
        var map = new JavaHashMap<string, TValue>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return map;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}");
            string key = reader.GetString().NotNull();
            reader.Read();
            TValue value = typeof(TValue) == typeof(int)
                ? (TValue)(object)JsonValueReader.ReadInt(ref reader, "JavaHashMap value")
                : JsonSerializer.Deserialize<TValue>(ref reader, options)!; // JUSTIFY: T119 TValue is unconstrained; JSON null maps to default (Java null)
            map.Put(key, value);
        }
        throw new JsonException("Unexpected end of JSON while reading JavaHashMap");
    }

    public override void Write(Utf8JsonWriter writer, JavaHashMap<string, TValue> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (k, v) in value)
        {
            writer.WritePropertyName(k);
            JsonSerializer.Serialize(writer, v, options);
        }
        writer.WriteEndObject();
    }
}
