using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Utils;

namespace Unciv.Models.Metadata;

// core/src/com/unciv/models/metadata/GameParameters.kt. Default values are the default new game.
//
// Json: property names are the Kotlin field names, in declaration order. Kotlin (libGDX) leaves out fields equal to their
// default when writing; this writes every field, which reads back identically. Reading accepts both.
public class GameParameters : IIsPartOfGameInfoSerialization
{
    private const string DefaultSpeed = Unciv.Models.Ruleset.Speed.Default;

    private bool? showCivilizationStatsField;

    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = "Prince";

    /// <summary>The speed name, not an instance of Speed.</summary>
    [JsonPropertyName("speed")]
    public string Speed { get; set; } = DefaultSpeed;

    [JsonPropertyName("randomNumberOfPlayers")]
    public bool RandomNumberOfPlayers { get; set; }

    [JsonPropertyName("minNumberOfPlayers")]
    public int MinNumberOfPlayers { get; set; } = 3;

    [JsonPropertyName("maxNumberOfPlayers")]
    public int MaxNumberOfPlayers { get; set; } = 3;

    [JsonPropertyName("players")]
    public List<Player> Players { get; set; } =
    [
        new Player(playerType: PlayerType.Human),
        new Player(),
        new Player(),
        new Player()
    ];

    [JsonPropertyName("randomNumberOfCityStates")]
    public bool RandomNumberOfCityStates { get; set; }

    [JsonPropertyName("minNumberOfCityStates")]
    public int MinNumberOfCityStates { get; set; } = 6;

    [JsonPropertyName("maxNumberOfCityStates")]
    public int MaxNumberOfCityStates { get; set; } = 6;

    [JsonPropertyName("numberOfCityStates")]
    public int NumberOfCityStates { get; set; } = 6;

    [JsonPropertyName("enableRandomNationsPool")]
    public bool EnableRandomNationsPool { get; set; }

    [JsonPropertyName("randomNationsPool")]
    public List<string> RandomNationsPool { get; set; } = [];

    [JsonPropertyName("noCityRazing")]
    public bool NoCityRazing { get; set; }

    [JsonPropertyName("noBarbarians")]
    public bool NoBarbarians { get; set; }

    [JsonPropertyName("ragingBarbarians")]
    public bool RagingBarbarians { get; set; }

    [JsonPropertyName("oneCityChallenge")]
    public bool OneCityChallenge { get; set; }

    [JsonPropertyName("godMode")]
    public bool GodMode { get; set; }

    [JsonPropertyName("nuclearWeaponsEnabled")]
    public bool NuclearWeaponsEnabled { get; set; } = true;

    /// <summary>T197: lets a player ask the Query API whether an AI civ would accept a trade (bucketed hint, see TradeHintQuery). Off: it would let a player probe the AI valuation.</summary>
    [JsonPropertyName("tradeHintsEnabled")]
    public bool TradeHintsEnabled { get; set; }

    [JsonPropertyName("espionageEnabled")]
    public bool EspionageEnabled { get; set; }

    [JsonPropertyName("noStartBias")]
    public bool NoStartBias { get; set; }

    [JsonPropertyName("shufflePlayerOrder")]
    public bool ShufflePlayerOrder { get; set; }

    [JsonPropertyName("victoryTypes")]
    public List<string> VictoryTypes { get; set; } = [];

    [JsonPropertyName("startingEra")]
    public string StartingEra { get; set; } = "Ancient era";

    /// <summary>Deprecated since Kotlin 4.21.0, use <see cref="ShowCivilizationStats"/>; still the fallback of its getter.</summary>
    [JsonPropertyName("showVictoryStats")]
    public bool ShowVictoryStats { get; set; } = true;

    /// <summary>
    /// The stored value of <see cref="ShowCivilizationStats"/>, which libGDX reads and writes directly (bypassing the setter's
    /// side effect on <see cref="ShowVictoryStats"/>); null in games saved before the field existed.
    /// </summary>
    [JsonPropertyName("showCivilizationStats")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ShowCivilizationStatsRaw
    {
        get => showCivilizationStatsField;
        set => showCivilizationStatsField = value;
    }

    /// <summary>Kotlin property: the getter falls back to <see cref="ShowVictoryStats"/>, a non-null setter updates both.</summary>
    [JsonIgnore]
    public bool? ShowCivilizationStats
    {
        get => showCivilizationStatsField ?? ShowVictoryStats;
        set
        {
            showCivilizationStatsField = value;
            if (value != null) ShowVictoryStats = value.Value;
        }
    }

    [JsonPropertyName("showDemographics")]
    public bool ShowDemographics { get; set; }

    [JsonPropertyName("showRankings")]
    public bool ShowRankings { get; set; } = true;

    [JsonPropertyName("showCharts")]
    public bool ShowCharts { get; set; } = true;

    [JsonPropertyName("hideOtherCivilizationStats")]
    public bool HideOtherCivilizationStats { get; set; }

    [JsonPropertyName("showAdditionalRankingTypes")]
    public bool ShowAdditionalRankingTypes { get; set; }

    // Multiplayer parameters
    [JsonPropertyName("isOnlineMultiplayer")]
    public bool IsOnlineMultiplayer { get; set; }

    [JsonPropertyName("multiplayerServerUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MultiplayerServerUrl { get; set; }

    [JsonPropertyName("anyoneCanSpectate")]
    public bool AnyoneCanSpectate { get; set; } = true;

    /// <summary>After this amount of minutes, anyone can choose to 'skip turn' of the current player to keep the game going.</summary>
    [JsonPropertyName("minutesUntilSkipTurn")]
    public int MinutesUntilSkipTurn { get; set; } = 60 * 24;

    /// <summary>Initial players' timer to play before they can be forced to resign permanently.</summary>
    [JsonPropertyName("minutesUntilForceResign")]
    public int MinutesUntilForceResign { get; set; } = 3 * 24 * 60;

    /// <summary>Time a player recovers on their timer before they can be forced to resign. Not added if their turn gets skipped.</summary>
    [JsonPropertyName("minutesRecoveredPerTurn")]
    public int MinutesRecoveredPerTurn { get; set; } = 60 * 24;

    [JsonPropertyName("baseRuleset")]
    public string BaseRuleset { get; set; } = Metadata.BaseRuleset.Civ_V_BNW.FullName();

    [JsonPropertyName("mods")]
    public LinkedHashSet<string> Mods { get; set; } = [];

    [JsonPropertyName("maxTurns")]
    public int MaxTurns { get; set; } = 500;

    /// <summary>
    /// C#-only (T227): AI civs take their military, tactical and homeland decisions from the Civ 5 AI of <c>Unciv.AI</c> (grand strategy, tactical
    /// and homeland move queues) instead of only the ported Kotlin unit automation. Saved with the game, so a reload plays on the same way. T235: on by default;
    /// false keeps the Kotlin AI only, which the Kotlin golden tests are written against (they opt out).
    /// </summary>
    [JsonPropertyName("useCiv5Ai")]
    public bool UseCiv5Ai { get; set; } = true;

    [JsonPropertyName("acceptedModCheckErrors")]
    public string AcceptedModCheckErrors { get; set; } = "";

    public GameParameters Clone()
    {
        var parameters = new GameParameters
        {
            Difficulty = Difficulty,
            Speed = Speed,
            RandomNumberOfPlayers = RandomNumberOfPlayers,
            MinNumberOfPlayers = MinNumberOfPlayers,
            MaxNumberOfPlayers = MaxNumberOfPlayers,
            Players = Players.Select(it => it.Clone()).ToList(), // T219: Kotlin shares the Player objects
            RandomNumberOfCityStates = RandomNumberOfCityStates,
            MinNumberOfCityStates = MinNumberOfCityStates,
            MaxNumberOfCityStates = MaxNumberOfCityStates,
            NumberOfCityStates = NumberOfCityStates,
            EnableRandomNationsPool = EnableRandomNationsPool,
            RandomNationsPool = new List<string>(RandomNationsPool),
            NoCityRazing = NoCityRazing,
            NoBarbarians = NoBarbarians,
            RagingBarbarians = RagingBarbarians,
            OneCityChallenge = OneCityChallenge,
            // godMode intentionally reset on clone
            NuclearWeaponsEnabled = NuclearWeaponsEnabled,
            TradeHintsEnabled = TradeHintsEnabled,
            EspionageEnabled = EspionageEnabled,
            NoStartBias = NoStartBias,
            ShufflePlayerOrder = ShufflePlayerOrder,
            VictoryTypes = new List<string>(VictoryTypes),
            StartingEra = StartingEra
        };
        parameters.ShowCivilizationStats = ShowCivilizationStats; // the setter also fixes showVictoryStats
        parameters.ShowDemographics = ShowDemographics;
        parameters.ShowRankings = ShowRankings;
        parameters.ShowCharts = ShowCharts;
        parameters.HideOtherCivilizationStats = HideOtherCivilizationStats;
        parameters.ShowAdditionalRankingTypes = ShowAdditionalRankingTypes;
        parameters.IsOnlineMultiplayer = IsOnlineMultiplayer;
        parameters.MultiplayerServerUrl = MultiplayerServerUrl;
        parameters.AnyoneCanSpectate = AnyoneCanSpectate;
        parameters.MinutesUntilSkipTurn = MinutesUntilSkipTurn;
        parameters.MinutesUntilForceResign = MinutesUntilForceResign;
        parameters.MinutesRecoveredPerTurn = MinutesRecoveredPerTurn;
        parameters.BaseRuleset = BaseRuleset;
        parameters.Mods = new LinkedHashSet<string>(Mods);
        parameters.MaxTurns = MaxTurns;
        parameters.UseCiv5Ai = UseCiv5Ai;
        parameters.AcceptedModCheckErrors = AcceptedModCheckErrors;
        return parameters;
    }

    // For debugging and GameStarter console output
    public override string ToString() => "(" + string.Join(", ", ToStringParts()) + ")";

    private IEnumerable<string> ToStringParts()
    {
        yield return $"{Difficulty} {Speed} {StartingEra}";
        yield return $"{Players.Count(it => it.PlayerType == PlayerType.Human)} {PlayerType.Human}";
        yield return $"{Players.Count(it => it.PlayerType == PlayerType.AI)} {PlayerType.AI}";
        if (RandomNumberOfPlayers) yield return $"Random number of Players: {MinNumberOfPlayers}..{MaxNumberOfPlayers}";
        if (RandomNumberOfCityStates) yield return $"Random number of City-States: {MinNumberOfCityStates}..{MaxNumberOfCityStates}";
        else yield return $"{NumberOfCityStates} CS";
        if (IsOnlineMultiplayer) yield return "Online Multiplayer";
        if (NoBarbarians) yield return "No barbs";
        if (RagingBarbarians) yield return "Raging barbs";
        if (OneCityChallenge) yield return "OCC";
        if (!NuclearWeaponsEnabled) yield return "No nukes";
        if (GodMode) yield return "God mode";
        yield return "Enabled Victories: " + string.Join(", ", VictoryTypes);
        yield return BaseRuleset;
        // Kotlin mods.joinToString(",", "mods=(", ")", 6): at most six, then ",..."
        yield return Mods.Count == 0 ? "no mods" : "mods=(" + string.Join(",", Mods.Take(6)) + (Mods.Count > 6 ? ",..." : "") + ")";
    }

    /// <summary>
    /// All mods including the base ruleset, base first, then in the order stored in a save.
    /// Creates a fresh instance that the caller may mutate.
    /// </summary>
    public LinkedHashSet<string> GetModsAndBaseRuleset()
    {
        var result = new LinkedHashSet<string>();
        result.Add(BaseRuleset);
        result.AddAll(Mods);
        return result;
    }
}
