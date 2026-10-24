using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Models.StatTypes;

namespace Unciv.Json;

/// <summary>
/// Reads the classes that extend <see cref="Stats"/> (<c>NamedStats</c>, so <c>Specialist</c>, <c>Building</c>, <c>TileImprovement</c>...) from a flat json object:
/// Kotlin reads the stat fields (<c>"production": 2</c>) and the subclass's own fields from the same object.
/// System.Text.Json would otherwise treat these as collections, because <see cref="Stats"/> is enumerable.
/// </summary>
/// <remarks>Read-only: nothing writes ruleset objects. Unknown properties are skipped, like the rest of the ruleset reading.</remarks>
public sealed class StatsSubclassConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert != typeof(Stats) && typeof(Stats).IsAssignableFrom(typeToConvert)
        && typeToConvert.GetConstructor(Type.EmptyTypes) != null;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(StatsSubclassConverter<>).MakeGenericType(typeToConvert))!;
}

internal sealed class StatsSubclassConverter<T> : JsonConverter<T> where T : Stats
{
    private static readonly Dictionary<string, PropertyInfo> Properties = typeof(T)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(it => it.CanWrite && it.GetIndexParameters().Length == 0 && it.GetCustomAttribute<JsonIgnoreAttribute>() == null
            && it.DeclaringType != typeof(Stats))
        .ToDictionary(it => it.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? it.Name, StringComparer.OrdinalIgnoreCase);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject for {typeof(T).Name}, got {reader.TokenType}");
        using var document = JsonDocument.ParseValue(ref reader);
        var result = Activator.CreateInstance<T>();
        foreach (var member in document.RootElement.EnumerateObject())
        {
            if (TryGetStat(member.Name, out var stat))
                result[stat] = ReadFloat(member.Value, member.Name);
            else if (Properties.TryGetValue(member.Name, out var property))
                property.SetValue(result, member.Value.Deserialize(property.PropertyType, options));
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        throw new NotSupportedException($"{typeof(T).Name} is only read from rulesets, never written");

    private static bool TryGetStat(string name, out Stat stat)
    {
        foreach (var candidate in Enum.GetValues<Stat>())
        {
            if (!string.Equals(candidate.ToString(), name, StringComparison.OrdinalIgnoreCase)) continue;
            stat = candidate;
            return true;
        }
        stat = default;
        return false;
    }

    private static float ReadFloat(JsonElement element, string name) => element.ValueKind switch
    {
        JsonValueKind.Number => element.GetSingle(),
        JsonValueKind.String when float.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) => value,
        _ => throw new JsonException($"Expected a number for stat '{name}', got {element.ValueKind}"),
    };
}
