using System.Globalization;
using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;

namespace Unciv.Commands.Log;

/// <summary>
/// Line 1 of a command log (design/COMMANDS-IMPLEMENTATION.md section 6): everything needed to rebuild the game the log starts from.
/// A log is only valid for the engine version that wrote it (<see cref="EngineVersion"/>) and the ruleset it was played with (<see cref="RulesetHash"/>).
/// </summary>
public sealed record CommandLogHeader
{
    public const int CurrentFormatVersion = 1;

    public const string SequentialTurns = "Sequential";

    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    [JsonPropertyName("engineVersion")]
    public string EngineVersion { get; init; } = CurrentEngineVersion;

    [JsonPropertyName("rulesetHash")]
    public string RulesetHash { get; init; } = "";

    /// <summary>The setup as it was before <c>GameStarter</c> normalised it in place.</summary>
    [JsonPropertyName("gameSetup")]
    public GameSetupInfo GameSetup { get; init; } = new();

    [JsonPropertyName("gameSeed")]
    public long GameSeed { get; init; }

    /// <summary>
    /// T230: the host's per-game secret for state-based randomness (0 = none). HOST-SIDE ONLY: it is the one place besides the game store
    /// where it is kept, and replay needs it. Never hand a header to a client; use <see cref="WithoutSecret"/> for any export.
    /// </summary>
    [JsonPropertyName("hostSecret")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long HostSecret { get; init; }

    /// <summary>The same header with the host secret removed (it can no longer replay a salted game).</summary>
    public CommandLogHeader WithoutSecret() => this with { HostSecret = 0 };

    [JsonPropertyName("startClockMillis")]
    public long StartClockMillis { get; init; }

    /// <summary>"Sequential" (the only path T194 replays itself); the simultaneous modes are carried for T203.</summary>
    [JsonPropertyName("turnMode")]
    public string TurnMode { get; init; } = SequentialTurns;

    /// <summary>The state-affecting settings per civ at game start; authoritative on replay (no UI code runs).</summary>
    [JsonPropertyName("playerSettings")]
    public SortedDictionary<string, PlayerSettings> PlayerSettings { get; init; } = new(StringComparer.Ordinal);

    public static string CurrentEngineVersion { get; } =
        typeof(GameInfo).Assembly.GetName().Version?.ToString() ?? "0";

    /// <summary>Copies <paramref name="setup"/> (the starter rewrites its parameters) and fingerprints the ruleset it names.</summary>
    public static CommandLogHeader Create(GameSetupInfo setup, long gameSeed, long startClockMillis,
        IDictionary<string, PlayerSettings>? playerSettings = null, long hostSecret = 0) =>
        new()
        {
            RulesetHash = RulesetHashOf(setup.GameParameters),
            GameSetup = Copy(setup),
            GameSeed = gameSeed,
            StartClockMillis = startClockMillis,
            HostSecret = hostSecret,
            PlayerSettings = new SortedDictionary<string, PlayerSettings>(playerSettings ?? new Dictionary<string, PlayerSettings>(), StringComparer.Ordinal),
        };

    /// <summary>
    /// A deep copy through json. Not <see cref="GameSetupInfo(GameSetupInfo)"/>: its parameters clone follows Kotlin and re-derives showCivilizationStats from
    /// showVictoryStats, so the copy would start a different game than the original.
    /// </summary>
    public static GameSetupInfo Copy(GameSetupInfo setup) =>
        UncivJson.Deserialize<GameSetupInfo>(UncivJson.Serialize(setup)) ?? throw new CommandLogException("The game setup cannot be copied");

    /// <summary>FNV hash over the names of the rules the combined ruleset (base plus mods) defines, in their loaded order.</summary>
    public static string RulesetHashOf(GameParameters parameters)
    {
        var ruleset = RulesetCache.GetComplexRuleset(parameters);
        var hash = StateHasher.ComputeHash(h =>
        {
            h.Add(ruleset.Name);
            foreach (var mod in parameters.Mods) h.Add(mod);
            foreach (var section in new IEnumerable<string>[] { ruleset.Technologies.Keys, ruleset.Units.Keys, ruleset.Buildings.Keys, ruleset.Policies.Keys, ruleset.Nations.Keys })
            {
                h.Add(section.Count());
                foreach (var name in section) h.Add(name);
            }
        });
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}

/// <summary>A log that cannot be read, or cannot be replayed by this engine (wrong format, engine version or ruleset).</summary>
public sealed class CommandLogException(string message) : Exception(message);
