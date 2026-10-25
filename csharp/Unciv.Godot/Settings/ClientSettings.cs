using System.Text.Json.Serialization;
using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Logic;

namespace Unciv.GodotClient.Settings;

/// <summary>
/// C043: the client's own settings (the Kotlin GameSettings minus what is engine state). Everything here is per machine and never reaches the
/// engine except <see cref="Player"/>, whose values travel as a <see cref="SetPlayerSettings"/> command (see <see cref="ToCommand"/>) so they are
/// part of the replayable log. Immutable: change with <c>with</c>, persist with <see cref="ClientSettingsStore"/>.
/// Deliberate deviation from Kotlin: one versioned JSON file, and a file that cannot be read yields the defaults instead of an exception.
/// </summary>
public sealed record ClientSettings
{
    /// <summary>The newest layout this build writes; a file with an older number is migrated on load, a newer one is not trusted.</summary>
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyName("language")]
    public string Language { get; init; } = "English";

    /// <summary>Action name to key text (e.g. "EndTurn" to "Space"). Empty means every action uses its default key. Ordered by name for a stable file.</summary>
    [JsonPropertyName("keyBindings")]
    public SortedDictionary<string, string> KeyBindings { get; init; } = [];

    [JsonPropertyName("uiScale")]
    public float UiScale { get; init; } = 1f;

    [JsonPropertyName("assetPack")]
    public string AssetPack { get; init; } = "Default";

    /// <summary>Autosave every this many turns; 0 turns autosave off.</summary>
    [JsonPropertyName("autosaveTurns")]
    public int AutosaveTurns { get; init; } = 1;

    [JsonPropertyName("tutorialsSeen")]
    public List<string> TutorialsSeen { get; init; } = [];

    [JsonPropertyName("playerName")]
    public string PlayerName { get; init; } = "Player";

    /// <summary>The engine-side options of the human civ; sent to the game with <see cref="ToCommand"/>.</summary>
    [JsonPropertyName("player")]
    public PlayerSettings Player { get; init; } = PlayerSettings.Default;

    public static ClientSettings Default { get; } = new();

    /// <summary>The settings made safe: values a hand-edited or damaged file could hold that would break the UI are replaced by the default.</summary>
    public ClientSettings Sanitized()
    {
        return this with
        {
            Language = string.IsNullOrWhiteSpace(Language) ? Default.Language : Language,
            KeyBindings = KeyBindings ?? [],
            UiScale = float.IsFinite(UiScale) ? Math.Clamp(UiScale, 0.5f, 4f) : Default.UiScale,
            AssetPack = string.IsNullOrWhiteSpace(AssetPack) ? Default.AssetPack : AssetPack,
            AutosaveTurns = Math.Max(0, AutosaveTurns),
            TutorialsSeen = TutorialsSeen?.Distinct(StringComparer.Ordinal).ToList() ?? [],
            PlayerName = string.IsNullOrWhiteSpace(PlayerName) ? Default.PlayerName : PlayerName,
            Player = Player is null ? PlayerSettings.Default : Player with { NotificationsLogMaxTurns = Math.Max(0, Player.NotificationsLogMaxTurns) },
        };
    }

    public ClientSettings WithTutorialSeen(string id) =>
        TutorialsSeen.Contains(id, StringComparer.Ordinal) ? this : this with { TutorialsSeen = [.. TutorialsSeen, id] };

    /// <summary>The command that makes the game use <see cref="Player"/>; sets every member, so applying twice is idempotent.</summary>
    public SetPlayerSettings ToCommand() => Build(Player, null);

    /// <summary>The command that changes only what differs between <paramref name="before"/> and <see cref="Player"/>; null when nothing differs.</summary>
    public SetPlayerSettings? ToDiffCommand(PlayerSettings before) => Player == before ? null : Build(Player, before);

    private static SetPlayerSettings Build(PlayerSettings now, PlayerSettings? before)
    {
        static T? Changed<T>(T now, T? before) where T : struct => before is { } b && EqualityComparer<T>.Default.Equals(now, b) ? null : now;
        return new SetPlayerSettings
        {
            AutoAssignCityProduction = Changed(now.AutoAssignCityProduction, before?.AutoAssignCityProduction),
            AutoAssignSpecialistsInNewCities = Changed(now.AutoAssignSpecialistsInNewCities, before?.AutoAssignSpecialistsInNewCities),
            AutomatedUnitsMoveOnTurnStart = Changed(now.AutomatedUnitsMoveOnTurnStart, before?.AutomatedUnitsMoveOnTurnStart),
            CitiesAutoBombardAtEndOfTurn = Changed(now.CitiesAutoBombardAtEndOfTurn, before?.CitiesAutoBombardAtEndOfTurn),
            NotificationsLogMaxTurns = Changed(now.NotificationsLogMaxTurns, before?.NotificationsLogMaxTurns),
            AutomatedUnitsChoosePromotions = Changed(now.AutomatedUnitsChoosePromotions, before?.AutomatedUnitsChoosePromotions),
            AutoBuildingRoads = Changed(now.AutoBuildingRoads, before?.AutoBuildingRoads),
            AutomatedWorkersReplaceImprovements = Changed(now.AutomatedWorkersReplaceImprovements, before?.AutomatedWorkersReplaceImprovements),
            StopAutomatedWorkersRemoveVegetation = Changed(now.StopAutomatedWorkersRemoveVegetation, before?.StopAutomatedWorkersRemoveVegetation),
            HumanConquestPrompt = Changed(now.HumanConquestPrompt, before?.HumanConquestPrompt),
        };
    }
}

/// <summary>Reads and writes <see cref="ClientSettings"/> as indented JSON at one path (in the game: the globalized <c>user://settings.json</c>).</summary>
public sealed class ClientSettingsStore(string path)
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Path { get; } = path;

    /// <summary>The stored settings; the defaults when the file is missing, unreadable, damaged or written by a newer version.</summary>
    public ClientSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return ClientSettings.Default;
            var loaded = System.Text.Json.JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(Path), Options);
            if (loaded == null || loaded.Version > ClientSettings.CurrentVersion || loaded.Version < 1) return ClientSettings.Default;
            return Migrate(loaded).Sanitized();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
        {
            return ClientSettings.Default;
        }
    }

    /// <summary>Writes the settings; false when the file could not be written (the caller keeps the values in memory). Written to a temp file first, so a crash never leaves half a file.</summary>
    public bool Save(ClientSettings settings)
    {
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string temp = Path + ".tmp";
            File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(settings with { Version = ClientSettings.CurrentVersion }, Options));
            File.Move(temp, Path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Version 1 is the first layout; the next layout change adds its step here.
    private static ClientSettings Migrate(ClientSettings loaded) => loaded;
}

/// <summary>
/// The live settings of the running client: holds the current value, persists every change and tells the game about changed engine options.
/// <paramref name="submit"/> sends a command as the human (false = the game refused it, the setting is still kept for the next game).
/// </summary>
public sealed class ClientSettingsService(ClientSettingsStore store, Func<GameCommand, bool>? submit = null)
{
    public ClientSettings Current { get; private set; } = store.Load();

    public event Action<ClientSettings>? Changed;

    /// <summary>Applies <paramref name="change"/>, saves, submits the engine options that changed and raises <see cref="Changed"/>. Returns whether the file was written.</summary>
    public bool Update(Func<ClientSettings, ClientSettings> change)
    {
        var before = Current;
        Current = change(before).Sanitized();
        bool saved = store.Save(Current);
        if (Current.ToDiffCommand(before.Player) is { } command) submit?.Invoke(command);
        Changed?.Invoke(Current);
        return saved;
    }

    /// <summary>At game start: makes the game use the stored engine options (one command, skipped when they equal the engine defaults the game starts with).</summary>
    public void ApplyToNewGame()
    {
        if (Current.Player != PlayerSettings.Default) submit?.Invoke(Current.ToCommand());
    }
}
