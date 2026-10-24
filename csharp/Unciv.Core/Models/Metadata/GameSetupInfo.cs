using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Logic.Map;

namespace Unciv.Models.Metadata;

/// <summary>
/// Ported from <c>core/src/com/unciv/models/metadata/GameSetupInfo.kt</c>.
/// </summary>
public class GameSetupInfo
{
    [JsonPropertyName("gameParameters")]
    public GameParameters GameParameters { get; init; }

    [JsonPropertyName("mapParameters")]
    public MapParameters MapParameters { get; init; }

    /// <summary>
    /// Transient map file handle / path; client concern.
    /// </summary>
    [JsonIgnore]
    public string? MapFile { get; set; }

    [JsonConstructor]
    public GameSetupInfo(GameParameters? gameParameters = null, MapParameters? mapParameters = null)
    {
        GameParameters = gameParameters ?? new GameParameters();
        MapParameters = mapParameters ?? new MapParameters();
    }

    /// <summary>
    /// Cloning constructor used for recreating or reseeding a game setup from an existing one.
    /// </summary>
    public GameSetupInfo(GameSetupInfo setup)
        : this(setup.GameParameters.Clone(), setup.MapParameters.Clone())
    {
        // MapFile is not copied: upstream's cloning constructor leaves the transient mapFile at null
    }

    public GameSetupInfo(GameInfo gameInfo)
        : this(gameInfo.GameParameters.Clone(), gameInfo.TileMap.MapParameters.Clone())
    {
    }

    /// <summary>
    /// Creates a deep copy of this <see cref="GameSetupInfo"/>.
    /// </summary>
    public GameSetupInfo Clone() => new(this);

    /// <summary>
    /// Creates a default setup with quick-start defaults, matching the default branch of upstream's <c>fromSettings</c>:
    /// <list type="bullet">
    /// <item><c>mapParameters.shape = MapShape.Rectangular</c></item>
    /// <item><c>mapParameters.worldWrap = true</c></item>
    /// <item><c>gameParameters.espionageEnabled = true</c></item>
    /// </list>
    /// </summary>
    /// <param name="defaultDifficulty">Overrides difficulty when specified.</param>
    public static GameSetupInfo CreateDefault(string? defaultDifficulty = null)
    {
        var setup = new GameSetupInfo();
        if (defaultDifficulty != null)
        {
            setup.GameParameters.Difficulty = defaultDifficulty;
        }

        setup.MapParameters.Shape = MapShape.Rectangular;
        setup.MapParameters.WorldWrap = true;
        setup.GameParameters.EspionageEnabled = true;
        return setup;
    }
}
