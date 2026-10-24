using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Logic.Map;
using Unciv.Mathematics;
using Unciv.Models;
using Unciv.Models.StatTypes;

namespace Unciv.Json;

public static class UncivJson
{
    private static readonly JsonSerializerOptions defaultOptions = CreateDefaultOptions();

    public static JsonSerializerOptions DefaultOptions => defaultOptions;

    public static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            WriteIndented = false
        };

        options.Converters.Add(new HexCoordConverter());
        options.Converters.Add(new CounterConverterFactory());
        options.Converters.Add(new StatsConverter());
        options.Converters.Add(new StatMapConverter());
        options.Converters.Add(new Vector2Converter());
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    public static string Serialize<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(value, options ?? defaultOptions);

    public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<T>(json, options ?? defaultOptions);
}
