using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unciv.Json;

public static class UncivJson
{
    private static readonly JsonSerializerOptions SharedOptions = CreateDefaultOptions();

    public static JsonSerializerOptions DefaultOptions => SharedOptions;

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
        options.Converters.Add(new StatsSubclassConverterFactory());
        options.Converters.Add(new StatMapConverter());
        options.Converters.Add(new Vector2Converter());
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    public static string Serialize<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(value, options ?? SharedOptions);

    public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<T>(json, options ?? SharedOptions);
}
