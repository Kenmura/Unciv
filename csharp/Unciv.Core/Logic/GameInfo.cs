using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.Translations;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using DifficultyObject = Unciv.Models.Ruleset.Nation.Difficulty;
using TileClass = Unciv.Logic.Map.Tiles.Tile;
using TileResourceObject = Unciv.Models.Ruleset.Tile.TileResource;

namespace Unciv.Logic;

// Ported from core/src/com/unciv/logic/GameInfo.kt (T046): fields, serialization shape, setTransients, queries, difficulty/speed accessors.
//
// Turn handling (nextTurn, recordRankingStats, notifyOfCloseEnemyUnits, the enemy/bombard notifications, processDiplomaticVictory,
// checkForVictory, updateMinutesBeforeForceResign) is in GameInfoTurn.cs (T071); the BackwardCompatibility calls in setTransients go to
// BackwardCompatibility.cs (T071).
//
// Deviations from the Kotlin source:
// - Wall clock and randomness are inputs, not defaults: currentTurnStartTime starts at 0 (Kotlin: Clock.System.now()) and gameId at ""
//   (Kotlin: randomGameId() from a SecureRandom); the game starter sets both. GameInfoPreview.RandomGameId(rng) is the seeded replacement.
// - UncivGame.Current.settings / worldScreen reads are the GameClientState instance (ClientState), owned by the game and not serialized.
// - DebugUtils.SIMULATE_UNTIL_TURN is the game's EngineOptions.SimulateUntilTurn.
// - The second mods loop of setTransients ("-" to " ") iterates a snapshot: Kotlin removes and adds while iterating the LinkedHashSet, which always
//   throws ConcurrentModificationException when it takes effect.
// - Github.repoNameToFolderName (Won't port: the mod downloader) is a private helper here.
// - calculateChecksum hashes this port's JSON text, so the value differs from the Kotlin one (the Gdx Json text differs).
// - @Synchronized on getNextUnitId and the ConcurrentHashMap are not needed: engine logic is single-threaded; the cache stays a
//   ConcurrentDictionary because it is cheap and the client may query rejection reasons from its render thread.

/// <summary>The result of a game: who won how and when. Kotlin <c>data class VictoryData</c>.</summary>
public class VictoryData : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("winningCiv")]
    public string WinningCiv { get; set; } = "";
    [JsonPropertyName("victoryType")]
    public string VictoryType { get; set; } = "";
    [JsonPropertyName("victoryTurn")]
    public int VictoryTurn { get; set; }

    /// <summary>Kotlin <c>@Transient lateinit var</c>, set by <see cref="GameInfo.SetTransients"/>: reading before assignment throws.</summary>
    private Civilization.Civilization? winningCivObject;
    [JsonIgnore]
    public Civilization.Civilization WinningCivObject
    {
        get => winningCivObject ?? throw new InvalidOperationException("lateinit property winningCivObject has not been initialized");
        set => winningCivObject = value;
    }

    public VictoryData()
    {
    }

    public VictoryData(string winningCiv, string victoryType, int victoryTurn)
    {
        WinningCiv = winningCiv;
        VictoryType = victoryType;
        VictoryTurn = victoryTurn;
    }

    public VictoryData(Civilization.Civilization winningCiv, string victoryType, int victoryTurn)
        : this(winningCiv.CivId, victoryType, victoryTurn)
    {
        WinningCivObject = winningCiv;
    }

    /// <summary>Kotlin data class <c>copy()</c>: the transient is not copied.</summary>
    public VictoryData Copy() => new(WinningCiv, VictoryType, VictoryTurn);
}

/// <summary>The virtual world the users play in</summary>
public partial class GameInfo : IsPartOfGameInfoSerialization, IHasGameInfoSerializationVersion, IGameInfoState
{
    private static T Lateinit<T>(T? value, string name) where T : class =>
        value ?? throw new InvalidOperationException($"lateinit property {name} has not been initialized");

    #region Fields - Serialized

    /// <summary>Version that saved the game, set when the game is converted to a save string.</summary>
    [JsonPropertyName("version")]
    public CompatibilityVersion Version { get; set; } = CompatibilityVersion.FirstWithout;

    [JsonPropertyName("civilizations")]
    public List<Civilization.Civilization> Civilizations { get; set; } = [];

    [JsonPropertyName("barbarians")]
    public BarbarianManager Barbarians { get; set; } = new();

    [JsonPropertyName("religions")]
    public JavaHashMap<string, Religion> Religions { get; set; } = new();

    /// <summary>difficulty is game-wide, think what would happen if 2 human players could play on different difficulties?</summary>
    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = "Chieftain";

    [JsonPropertyName("tileMap")]
    public TileMap TileMap { get; set; } = new();

    [JsonPropertyName("gameParameters")]
    public GameParameters GameParameters { get; set; } = new();

    [JsonPropertyName("turns")]
    public int Turns { get; set; }

    [JsonPropertyName("oneMoreTurnMode")]
    public bool OneMoreTurnMode { get; set; }

    [JsonPropertyName("currentPlayer")]
    public string CurrentPlayer { get; set; } = "";

    /// <summary>Epoch milliseconds. An input, see the file header.</summary>
    [JsonPropertyName("currentTurnStartTime")]
    public long CurrentTurnStartTime { get; set; }

    [JsonPropertyName("gameId")]
    public string GameId { get; set; } = "";

    /// <summary>
    /// T230: the host's per-game secret, mixed into every state-based random seed (see <see cref="Randomness.SaltedSeed"/>), so a client
    /// cannot predict combat and other rolls from public state. 0 = none (Kotlin parity). Deliberately not serialized: it lives only
    /// in the host's game store and command-log header and is re-applied after a load (<c>HostSecret</c>).
    /// </summary>
    [JsonIgnore]
    public long RandomSalt { get; set; }

    [JsonPropertyName("checksum")]
    public string Checksum { get; set; } = "";

    [JsonInclude]
    [JsonPropertyName("lastUnitId")]
    private int lastUnitId;

    [JsonPropertyName("victoryData")]
    public VictoryData? VictoryData { get; set; }

    /// <summary>Maps a civ to the civ they voted for - <c>null</c> on the value side means they abstained</summary>
    [JsonPropertyName("diplomaticVictoryVotesCast")]
    public JavaHashMap<string, string?> DiplomaticVictoryVotesCast { get; set; } = new();

    /// <summary>
    /// T183: the state-affecting settings per civ id, see <see cref="PlayerSettings"/>. Null until a civ's settings are first set, so a save
    /// without them (every Kotlin save) loads and a game with default settings serialises exactly as before.
    /// </summary>
    [JsonPropertyName("playerSettings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, PlayerSettings>? PlayerSettingsByCiv { get; set; }

    /// <summary>The settings of <paramref name="civId"/>, <see cref="PlayerSettings.Default"/> when none were set.</summary>
    public PlayerSettings GetPlayerSettings(string civId) =>
        PlayerSettingsByCiv != null && PlayerSettingsByCiv.TryGetValue(civId, out var settings) ? settings : PlayerSettings.Default;

    /// <summary>Replaces the settings of <paramref name="civId"/> (the effect of the SetPlayerSettings command).</summary>
    public void SetPlayerSettings(string civId, PlayerSettings settings)
    {
        PlayerSettingsByCiv ??= new Dictionary<string, PlayerSettings>();
        PlayerSettingsByCiv[civId] = settings;
    }

    /// <summary>Set to false whenever the results still need te be processed</summary>
    [JsonPropertyName("diplomaticVictoryVotesProcessed")]
    public bool DiplomaticVictoryVotesProcessed { get; set; }

    /// <summary>
    /// The turn the replay history started recording.
    /// <list type="bullet">
    /// <item><c>-1</c> means the game was serialized with an older version without replay</item>
    /// <item><c>0</c> would be the normal value in any newer game (remember gameParameters.startingEra is not implemented through turns starting &gt; 0)</item>
    /// <item><c>&gt;0</c> would be set by compatibility migration (T071: BackwardCompatibility.migrateToTileHistory)</item>
    /// </list>
    /// </summary>
    [JsonPropertyName("historyStartTurn")]
    public int HistoryStartTurn { get; set; } = -1;

    /// <summary>Keep track of a custom location this game was saved to _or_ loaded from, using it as the default custom location for any further save/load attempts.</summary>
    [JsonPropertyName("customSaveLocation")]
    public string? CustomSaveLocation { get; set; }

    /// <summary>List of unit names that have been taken in this game from UnitNameGroups.json.</summary>
    [JsonPropertyName("unitNamesTaken")]
    public List<string> UnitNamesTaken { get; set; } = [];

    /// <summary>
    /// B020 (bnw-spec 2.2): every active trade route, global, in creation order. Null until the first route exists, so a save of a game
    /// without routes (every Kotlin save, every G&amp;K game) serialises exactly as before.
    /// </summary>
    [JsonPropertyName("tradeRoutes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TradeRoute>? TradeRoutes { get; set; }

    /// <summary>B020: the id the next trade route gets (ids are monotonic). 0 is omitted from the save.</summary>
    [JsonPropertyName("lastTradeRouteId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int LastTradeRouteId { get; set; }

    /// <summary>B020: routes that ended in the last <see cref="Trade.Routes.TradeRouteRules.RouteMemoryTurns"/> turns. Null while empty, like <see cref="TradeRoutes"/>.</summary>
    [JsonPropertyName("endedTradeRoutes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<EndedTradeRoute>? EndedTradeRoutes { get; set; }

    #endregion
    #region Fields - Transient

    private DifficultyObject? difficultyObject; // Since this is static game-wide, and was taking a large part of nextTurn

    /// <summary>
    /// <see cref="IHasUniques.IsUnavailableBySettings"/> is a pure function of game-start constants (game parameters, starting era, mod options) - it cannot
    /// change during a game - yet it is re-evaluated per building/unit on every construction-candidacy check. Memoize it per rule object.
    /// Kotlin keys the map by the rule object's equals; reference identity is the same for ruleset objects.
    /// </summary>
    private readonly ConcurrentDictionary<IHasUniques, bool> settingsUnavailability = new(ReferenceEqualityComparer.Instance);

    public bool IsUnavailableBySettingsCached(IHasUniques obj) =>
        settingsUnavailability.GetOrAdd(obj, it => it.IsUnavailableBySettings(this));

    private Speed? speed;
    /// <summary>Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Speed Speed
    {
        get => Lateinit(speed, "speed");
        set => speed = value;
    }

    private GlobalUniques? combinedGlobalUniques;

    private Civilization.Civilization? currentPlayerCiv; // this is called thousands of times, no reason to search for it with a find{} every time
    /// <summary>Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Civilization.Civilization CurrentPlayerCiv
    {
        get => Lateinit(currentPlayerCiv, "currentPlayerCiv");
        set => currentPlayerCiv = value;
    }

    /// <summary>This is used in multiplayer games, where I may have a saved game state on my phone that is inconsistent with the saved game on the cloud</summary>
    [JsonIgnore]
    public bool IsUpToDate { get; set; }

    private Ruleset? ruleset;
    /// <summary>Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Ruleset Ruleset
    {
        get => Lateinit(ruleset, "ruleset");
        set => ruleset = value;
    }

    /// <summary>
    /// Simulate until any player wins, or turns exceeds indicated number. Does not update World View until finished.
    /// Should be set manually on each new game start.
    /// </summary>
    [JsonIgnore]
    public int SimulateMaxTurns { get; set; } = 1000;
    [JsonIgnore]
    public bool SimulateUntilWin { get; set; }

    [JsonIgnore]
    public HashSet<string> SpaceResources { get; } = [];

    /// <summary>The DebugUtils flags of this game (T043). Game state: copied by <see cref="Clone"/>, but not saved in the file.</summary>
    [JsonIgnore]
    public EngineOptions EngineOptions { get; set; } = new();

    /// <summary>Client settings the rules read, see <see cref="GameClientState"/>. Shared by clones.</summary>
    [JsonIgnore]
    public GameClientState ClientState { get; set; } = new();

    private readonly KotlinLazy<Dictionary<string, Civilization.Civilization>> civMap;

    #endregion

    public GameInfo()
    {
        civMap = new KotlinLazy<Dictionary<string, Civilization.Civilization>>(() =>
        {
            var map = new Dictionary<string, Civilization.Civilization>();
            foreach (var civ in Civilizations) map[civ.CivId] = civ; // associateBy: the last one wins
            return map;
        });
    }

    #region Pure functions

    public GameInfo Clone()
    {
        var toReturn = new GameInfo
        {
            TileMap = TileMap.Clone(),
            Civilizations = Civilizations.Select(it => it.Clone()).ToList(),
            Barbarians = Barbarians.Clone(),
        };
        toReturn.Religions.PutAll(Religions.Select(it => new KeyValuePair<string, Religion>(it.Key, it.Value.Clone())));
        toReturn.CurrentPlayer = CurrentPlayer;
        toReturn.pendingPlayerIndex = pendingPlayerIndex;
        toReturn.turnStartedCivs.UnionWith(turnStartedCivs);
        toReturn.TurnState = TurnState?.Clone();
        toReturn.CurrentTurnStartTime = CurrentTurnStartTime;
        toReturn.Turns = Turns;
        toReturn.Difficulty = Difficulty;
        toReturn.GameParameters = GameParameters.Clone(); // T219: Kotlin shares the object
        // GameParameters.Clone resets godMode on purpose and makes the stats flags explicit; the game's own copy must stay exactly like its source (saved text, hash).
        toReturn.GameParameters.GodMode = GameParameters.GodMode;
        toReturn.GameParameters.ShowVictoryStats = GameParameters.ShowVictoryStats;
        toReturn.GameParameters.ShowCivilizationStatsRaw = GameParameters.ShowCivilizationStatsRaw;
        toReturn.GameId = GameId;
        toReturn.RandomSalt = RandomSalt;
        foreach (var vote in DiplomaticVictoryVotesCast) toReturn.DiplomaticVictoryVotesCast[vote.Key] = vote.Value;
        toReturn.OneMoreTurnMode = OneMoreTurnMode;
        toReturn.CustomSaveLocation = CustomSaveLocation;
        toReturn.VictoryData = VictoryData?.Copy();
        toReturn.HistoryStartTurn = HistoryStartTurn;
        toReturn.lastUnitId = lastUnitId;
        toReturn.UnitNamesTaken.AddRange(UnitNamesTaken);
        toReturn.TradeRoutes = TradeRoutes?.Select(it => it.Clone()).ToList();
        toReturn.LastTradeRouteId = LastTradeRouteId;
        toReturn.EndedTradeRoutes = EndedTradeRoutes?.ToList();
        toReturn.EngineOptions = EngineOptions.Clone();
        CloneGreatWorksTo(toReturn);
        CloneArchaeologyTo(toReturn);
        CloneLeagueTo(toReturn);
        if (PlayerSettingsByCiv != null) toReturn.PlayerSettingsByCiv = new Dictionary<string, PlayerSettings>(PlayerSettingsByCiv);
        toReturn.ClientState = ClientState;

        return toReturn;
    }

    // Important - duplicate unit ID's have been observed during debugging (Kotlin: @Synchronized, see the file header).
    public int GetNextUnitId() => unchecked(++lastUnitId);

    /// <summary>***Only*** for BackwardCompatibility.ensureUnitIds (T071)</summary>
    public void EnsureLastUnitId()
    {
        if (lastUnitId != 0) return;
        lastUnitId = Math.Max(TileMap.Values.SelectMany(it => it.GetUnits()).Select(it => (int?)it.Id).Max() ?? 0, 0);
    }

    public Civilization.Civilization GetPlayerToViewAs()
    {
        if (!GameParameters.IsOnlineMultiplayer) return GetCurrentPlayerCivilization(); // non-online, play as human player
        var userId = ClientState.UserId;

        // Iterating on all civs, starting from the the current player, gives us the one that will have the next turn
        // This allows multiple civs from the same UserID
        if (Civilizations.Any(it => it.PlayerId == userId))
        {
            var civIndex = Civilizations.Select(it => it.CivId).ToList().IndexOf(CurrentPlayer);
            while (true)
            {
                var civToCheck = Civilizations[civIndex % Civilizations.Count];
                if (civToCheck.PlayerId == userId) return civToCheck;
                civIndex++;
            }
        }

        // you aren't anyone. How did you even get this game? Can you spectate?
        return GetSpectator(userId);
    }

    /// <summary>Get a civ by name</summary>
    /// <exception cref="InvalidOperationException">if no civ of that name is in the game (alive or dead)!</exception>
    public Civilization.Civilization GetCivilization(string civId) =>
        civMap.Value.TryGetValue(civId, out var civ)
            ? civ
            : Civilizations.First(it => it.CivId == civId); // This is for spectators who are added in later, artificially

    /// <summary>Removes <paramref name="civ"/> from the game and from the id lookup (dev console <c>civ remove</c>, <c>game remove-spectator</c>).</summary>
    public void RemoveCivilization(Civilization.Civilization civ)
    {
        int removedIndex = Civilizations.IndexOf(civ);
        if (removedIndex < 0) return;
        Civilizations.RemoveAt(removedIndex);
        if (civMap.Value.TryGetValue(civ.CivId, out var mapped) && ReferenceEquals(mapped, civ)) civMap.Value.Remove(civ.CivId);

        // Turn bookkeeping indexes into Civilizations (the next civ of a pending sequential turn, the scan start of a resolving round) and names civs by id
        if (pendingPlayerIndex is { } pending)
        {
            if (removedIndex < pending) pending--;
            // The pending civ was the last one: the round is over, exactly as AdvancePlayerIndex does
            pendingPlayerIndex = pending >= Civilizations.Count ? AdvancePlayerIndex(Civilizations.Count - 1) : pending;
        }
        turnStartedCivs.Remove(civ.CivId);
        if (TurnState is { } state)
        {
            if (removedIndex < state.Next) state.Next--;
            state.Ended.RemoveAll(it => it == civ.CivId);
            state.Started.RemoveAll(it => it == civ.CivId);
            // The removed civ held the last pending step: the round is resolved, else nobody is active and the next step throws.
            // Only while Resolving: CompleteRoundIfDone in Playing would clear Ended.
            if (state.Phase == SimultaneousPhase.Resolving) CompleteRoundIfDone(state);
        }
    }

    public Civilization.Civilization? GetCivilizationOrNull(string civId) =>
        civMap.Value.TryGetValue(civId, out var civ) ? civ : Civilizations.FirstOrDefault(it => it.CivId == civId);

    public Civilization.Civilization GetCurrentPlayerCivilization() => CurrentPlayerCiv;

    public List<CivilizationInfoPreview> GetCivilizationsAsPreviews() => Civilizations.Select(it => it.AsPreview()).ToList();

    /// <summary>Get barbarian civ</summary>
    /// <exception cref="InvalidOperationException">in no-barbarians games!</exception>
    public Civilization.Civilization GetBarbarianCivilization() => GetCivilization(Constants.Barbarians);

    public DifficultyObject GetDifficulty() => Lateinit(difficultyObject, "difficultyObject");

    /// <summary>Access a cached <c>GlobalUniques</c> that combines the <see cref="Ruleset"/>'s globalUniques with the Uniques of the chosen <see cref="Speed"/> and <see cref="GetDifficulty"/></summary>
    public GlobalUniques GetGlobalUniques() => Lateinit(combinedGlobalUniques, "combinedGlobalUniques");

    /// <returns>Sequence of all cities in game, both major civilizations and city states</returns>
    public IEnumerable<CityObject> GetCities() => Civilizations.SelectMany(it => it.Cities);

    public List<Civilization.Civilization> GetAliveCityStates() => Civilizations.Where(it => it.IsAlive() && it.IsCityState).ToList();

    public List<Civilization.Civilization> GetAliveMajorCivs() => Civilizations.Where(it => it.IsAlive() && it.IsMajorCiv()).ToList();

    /// <summary>
    /// Gets civilizations in their commonly used order - City-states last, otherwise alphabetically by culture and translation.
    /// <paramref name="civToSortFirst"/> can be used to force a specific Civilization to be listed first.
    /// Barbarians and Spectators always excluded, other filter criteria are <paramref name="includeCityStates"/>,
    /// <paramref name="includeDefeated"/> and optionally an <paramref name="additionalFilter"/>.
    /// </summary>
    public IEnumerable<Civilization.Civilization> GetCivsSorted(
        bool includeCityStates = true,
        bool includeDefeated = false,
        Civilization.Civilization? civToSortFirst = null,
        Func<Civilization.Civilization, bool>? additionalFilter = null)
    {
        var collator = ClientState.Collator;
        return Civilizations
            .Where(it => !(it.IsBarbarian
                || it.IsSpectator()
                || (!includeDefeated && it.IsDefeated())
                || (!includeCityStates && it.IsCityState)
                || (additionalFilter != null && !additionalFilter(it))))
            .OrderBy(it => it != civToSortFirst)
            .ThenByDescending(it => it.IsMajorCiv())
            .ThenBy(it => it.CivName.Tr(hideIcons: true), collator);
    }

    /// <summary>Returns the first spectator for a <paramref name="playerId"/> or creates one if none found</summary>
    public Civilization.Civilization GetSpectator(string playerId)
    {
        var gameSpectatorCiv = Civilizations.FirstOrDefault(it => it.IsSpectator() && it.PlayerId == playerId);
        return gameSpectatorCiv ?? CreateTemporarySpectatorCiv(playerId);
    }

    private Civilization.Civilization CreateTemporarySpectatorCiv(string playerId)
    {
        var it = new Civilization.Civilization(Constants.Spectator)
        {
            PlayerType = PlayerType.Human,
            PlayerId = playerId,
            PlayerMinutesBeforeForceResign = GameParameters.MinutesUntilForceResign,
        };
        Civilizations.Add(it);
        it.GameInfo = this;
        it.SetNationTransient();
        it.Cache.UpdateState();
        it.Cache.UpdateViewableTiles();
        it.SetTransients();
        return it;
    }

    public bool IsReligionEnabled()
    {
        if (((IHasUniques)Ruleset.Eras[GameParameters.StartingEra].NotNull()).HasUnique(UniqueType.DisablesReligion)) return false;
        if (((IHasUniques)Ruleset.ModOptions).HasUnique(UniqueType.DisableReligion)) return false;
        return true;
    }

    public bool IsEspionageEnabled() => GameParameters.EspionageEnabled;

    private int GetEquivalentTurn()
    {
        var totalTurns = Speed.NumTotalTurns();
        var startPercent = Ruleset.Eras[GameParameters.StartingEra].NotNull().StartPercent;
        return unchecked(Turns + (totalTurns * startPercent / 100));
    }

    public int GetYear(int turnOffset = 0) => KotlinMath.FloatToInt(Speed.TurnToYear(unchecked(GetEquivalentTurn() + turnOffset)));

    public string CalculateChecksum()
    {
        var oldChecksum = Checksum;
        Checksum = ""; // Checksum calculation cannot include old checksum, obvs
        try
        {
            var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(UncivJson.Serialize(this)));
            return FileConversions.Encode(bytes);
        }
        finally
        {
            Checksum = oldChecksum;
        }
    }

    /// <summary>Do we automatically simulate until N turn?</summary>
    public bool IsSimulation() => EngineOptions.IsSimulation(Turns)
        || (Turns < SimulateMaxTurns && SimulateUntilWin);

    /// <summary>
    /// Generate and show a notification pointing out resources.
    /// maxDistance: from next City, 0 removes distance limitation; filter: optional tile filter predicate, e.g. to exclude foreign territory.
    /// </summary>
    /// <returns><c>false</c> if no resources were found and no notification was added.</returns>
    public bool NotifyExploredResources(Civilization.Civilization civInfo, string resourceName, int maxDistance = int.MaxValue, Func<TileClass, bool>? filter = null)
    {
        var notification = GetExploredResourcesNotification(civInfo, resourceName, maxDistance, filter);
        if (notification == null) return false;
        civInfo.Notifications.Add(notification);
        return true;
    }

    /// <inheritdoc cref="NotifyExploredResources(Civilization.Civilization, string, int, Func{TileClass, bool})"/>
    public bool NotifyExploredResources(Civilization.Civilization civInfo, TileResourceObject resource, int maxDistance = int.MaxValue, Func<TileClass, bool>? filter = null)
    {
        var notification = GetExploredResourcesNotification(civInfo, resource, maxDistance, filter);
        if (notification == null) return false;
        civInfo.Notifications.Add(notification);
        return true;
    }

    /// <summary>Generate a notification pointing out resources. Only researched Resources are considered.
    /// maxDistance: from next City, default removes distance limitation; filter: optional tile filter predicate, e.g. to exclude foreign territory.
    /// </summary>
    /// <returns><c>null</c> if no resources were found, otherwise a Notification instance.</returns>
    public Notification? GetExploredResourcesNotification(Civilization.Civilization civ, string resourceName, int maxDistance = int.MaxValue, Func<TileClass, bool>? filter = null) =>
        GetExploredResourcesNotification(civ, Ruleset.TileResources.GetValueOrDefault(resourceName), maxDistance, filter);

    /// <inheritdoc cref="GetExploredResourcesNotification(Civilization.Civilization, string, int, Func{TileClass, bool})"/>
    public Notification? GetExploredResourcesNotification(Civilization.Civilization civ, TileResourceObject? resource, int maxDistance = int.MaxValue, Func<TileClass, bool>? filter = null)
    {
        if (!civ.CanSeeResource(resource)) return null;
        resource = resource.NotNull();
        filter ??= _ => true;

        // Include your city-state allies' cities with your own for the purpose of showing the closest city
        var relevantCities = civ.Cities.Concat(
            civ.GetKnownCivs()
                .Where(it => it.IsCityState && it.AllyCiv == civ)
                .SelectMany(it => it.Cities)).ToList();

        // All sources of the resource on the map, using a city-state's capital center tile for the CityStateOnlyResource types
        IEnumerable<TileClass> exploredRevealTiles =
            ((IHasUniques)resource).HasUnique(UniqueType.CityStateOnlyResource)
                // Look for matching mercantile CS centers
                ? GetAliveCityStates()
                    .Where(it => it.CityStateResource == resource.Name)
                    .Select(it => it.GetCapital().NotNull().GetCenterTile())
                : TileMap.Values.Where(it => it.TileResource == resource);

        // Apply all filters to the above collection and sort them by distance to closest city
        var exploredRevealInfo = exploredRevealTiles
            .Where(civ.HasExplored)
            .SelectMany(tile => relevantCities
                // build a full cross join all revealed tiles * civ's cities (should rarely surpass a few hundred)
                // cache distance for each pair as sort will call it ~ 2n log n times
                // should still be cheaper than looking up 'the' closest city per reveal tile before sorting
                .Select(city => new CityTileAndDistance(city, tile, tile.AerialDistanceTo(city.GetCenterTile()))))
            .Where(it => it.Distance <= maxDistance && filter(it.Tile))
            // We still want to report tiles on our own territory before those of our cs-allies
            .OrderBy(it => it.City.Civ != civ).ThenBy(it => it.Distance)
            .DistinctBy(it => it.Tile)
            .ToList();

        var chosenCity = exploredRevealInfo.FirstOrDefault()?.City;
        if (chosenCity == null) return null;
        var positions = exploredRevealInfo
            // re-sort to a more pleasant display order
            .OrderBy(it => it.Tile.AerialDistanceTo(chosenCity.GetCenterTile()))
            .Select(it => it.Tile.Position)
            .ToList();

        var positionsCount = positions.Count;
        var text = positionsCount == 1
            ? $"[{resource.Name}] revealed near [{chosenCity.Name}]"
            : $"[{positionsCount}] sources of [{resource.Name}] revealed, e.g. near [{chosenCity.Name}]";

        return new Notification(text, [$"ResourceIcons/{resource.Name}"], LocationAction.Of(positions), Notification.NotificationCategory.General);
    }

    private sealed record CityTileAndDistance(CityObject City, TileClass Tile, int Distance);

    public GameInfoPreview AsPreview() => new(this);

    #endregion
    #region State changing functions

    // T200: which civs' turns have begun. Simultaneous rounds must not call SetTransients mid-round, it would drop the other civs' started flags.
    private void RestoreTurnStartedCivs()
    {
        if (TurnState is { Mode: TurnMode.Simultaneous } turnState) // the saved list says whose turn has begun
        {
            turnStartedCivs.Clear();
            turnStartedCivs.UnionWith(turnState.Started);
        }
        else if (pendingPlayerIndex == null) // a saved or new game always waits for its current player's input
        {
            turnStartedCivs.Clear();
            turnStartedCivs.Add(CurrentPlayer);
        }
    }

    /// <summary>
    /// All cross-game data which needs to be altered (e.g. when removing or changing a name of a building/tech)
    /// will be done here, and not in Civilization.setTransients or City
    /// </summary>
    public void SetTransients()
    {
        TileMap.GameInfo = this;

        // [TEMPORARY] Convert old saves to newer ones by moving base rulesets from the mod list to the base ruleset field
        ConvertOldSavesToNewSaves();

        // Cater for the mad modder using trailing '-' in their repo name - convert the mods list so
        // it requires our new, Windows-safe local name (no trailing blanks)
        foreach (var (oldName, newName) in GameParameters.Mods.Select(it => (it, RepoNameToFolderName(it, onlyOuterBlanks: true))).ToList())
        {
            if (newName == oldName) continue;
            GameParameters.Mods.Remove(oldName);
            GameParameters.Mods.Add(newName);
        }

        // Try to fix mods that are displayed in save with - but when downloaded it's replaced with a space
        if (!RulesetCache.ContainsKey(GameParameters.BaseRuleset) && RulesetCache.ContainsKey(GameParameters.BaseRuleset.Replace("-", " ")))
            GameParameters.BaseRuleset = GameParameters.BaseRuleset.Replace("-", " ");

        foreach (var mod in GameParameters.Mods.ToList())
        {
            if (!RulesetCache.ContainsKey(mod) && RulesetCache.ContainsKey(mod.Replace("-", " ")))
            {
                GameParameters.Mods.Remove(mod);
                GameParameters.Mods.Add(mod.Replace("-", " "));
            }
        }

        Ruleset = RulesetCache.GetComplexRuleset(GameParameters);

        // any mod the saved game lists that is currently not installed causes null pointer
        // exceptions in this routine unless it contained no new objects or was very simple.
        // Player's fault, so better complain early:
        var missingMods = new[] { GameParameters.BaseRuleset }.Concat(GameParameters.Mods)
            .Where(it => !Ruleset.Mods.Contains(it))
            .ToList();
        if (missingMods.Count != 0)
            throw new MissingModsException(missingMods);

        BackwardCompatibility.RemoveMissingModReferences(this);
        if (VictoryData != null) VictoryData.WinningCivObject = GetCivilization(VictoryData.WinningCiv);

        foreach (var baseUnit in Ruleset.Units.Values)
            baseUnit.SetRuleset(Ruleset);

        foreach (var building in Ruleset.Buildings.Values)
            building.Ruleset = Ruleset;

        // This needs to go before tileMap.setTransients, as units need to access
        // the nation of their civilization when setting transients
        foreach (var civInfo in Civilizations) civInfo.GameInfo = this;
        foreach (var civInfo in Civilizations)
        {
            civInfo.SetNationTransient();
            civInfo.Cache.UpdateState();
        }
        // must be done before updating tileMap, since unit uniques depend on civ uniques depend on allied city-state uniques depend on diplomacy
        foreach (var civInfo in Civilizations)
        {
            foreach (var diplomacyManager in civInfo.Diplomacy.Values)
            {
                diplomacyManager.CivInfo = civInfo;
                diplomacyManager.UpdateHasOpenBorders();
            }
        }

        // combinedGlobalUniques needs to be set before tileMap.setTransients!
        SetGlobalTransients();

        TileMap.SetTransients(Ruleset);
        BackwardCompatibility.EnsureUnitIds(this); // T236: before the sort, so units that get an id here are ordered like a running game orders them
        // T231: a load rebuilds unit lists tile by tile; use creation order instead (Kotlin keeps that order in a live game), see UnitManager.SortById.
        foreach (var civInfo in Civilizations) civInfo.Units.SortById();

        // Temporary - All games saved in 4.12.15 turned into 'hexagonal non world wrapped'
        // Here we attempt to fix that

        // How do we recognize a rectangle? By having many tiles at the lowest edge
        var tilesWithLowestRow = TileMap.TileList.GroupBy(it => it.GetRow()).MinBy(it => it.Key).NotNull().ToList();
        if (tilesWithLowestRow.Count > 2) TileMap.MapParameters.Shape = MapShape.Rectangular;

        if (CurrentPlayer == "")
        {
            CurrentPlayerCiv = GameParameters.IsOnlineMultiplayer
                ? Civilizations.First(it => it.IsHuman() && !it.IsSpectator()) // For MP, spectator doesn't get a 'turn'
                : Civilizations.First(it => it.IsHuman()); // for non-MP games, you can be a spectator of an AI-only match, and you *do* get a turn, sort of
            CurrentPlayer = CurrentPlayerCiv.CivId;
        }
        else CurrentPlayerCiv = GetCivilization(CurrentPlayer);
        // T200: simultaneous rounds must not call SetTransients mid-round, it would drop the other civs' started flags.
        RestoreTurnStartedCivs();

        foreach (var religion in Religions.Values) religion.SetTransients(this);

        foreach (var civInfo in Civilizations) civInfo.SetTransients();
        TileMap.SetNeutralTransients(); // has to happen after civInfo.setTransients() sets owningCity

        BackwardCompatibility.ConvertFortify(this);

        UpdateCivilizationState();

        SpaceResources.Clear();
        SpaceResources.UnionWith(Ruleset.Buildings.Values.Where(it => ((IHasUniques)it).HasUnique(UniqueType.SpaceshipPart))
            .SelectMany(it => it.GetResourceRequirementsPerTurn().Keys));
        // Spaceship parts are UNITS in the base ruleset (SS Booster etc., each requiring Aluminum).
        SpaceResources.UnionWith(Ruleset.Units.Values.Where(it => ((IHasUniques)it).HasUnique(UniqueType.SpaceshipPart))
            .SelectMany(it => it.GetResourceRequirementsPerTurn().Keys));
        SpaceResources.UnionWith(Ruleset.Victories.Values.SelectMany(it => it.RequiredSpaceshipParts));

        Barbarians.SetTransients(this);

        BackwardCompatibility.MigrateToTileHistory(this);
        BackwardCompatibility.MigrateGreatGeneralPools(this);
    }

    public void SetGlobalTransients()
    {
        static float EstimateDifficulty(DifficultyObject d) =>
            d.BuildingCostModifier + d.PolicyCostModifier + d.ResearchCostModifier + d.UnhappinessModifier
            - d.AiBuildingCostModifier - d.AiUnitCostModifier - d.AiUnhappinessModifier;

        difficultyObject = Ruleset.Difficulties.GetValueOrDefault(Difficulty)
            ?? MaxByOrFirst(Ruleset.Difficulties.Values, EstimateDifficulty)
            ?? throw new UncivShowableException("Invalid game: Specifies a ruleset with no entries for [Difficulty]");
        speed = Ruleset.Speeds.GetValueOrDefault(GameParameters.Speed)
            ?? MaxByOrFirst(Ruleset.Speeds.Values, it => it.Modifier)
            ?? throw new UncivShowableException("Invalid game: Specifies a ruleset with no entries for [Speed]");

        combinedGlobalUniques = GlobalUniques.Combine(Ruleset.GlobalUniques, speed, difficultyObject);
    }

    /// <summary>Kotlin <c>maxByOrNull</c>: the first element with the maximal selector value, <c>null</c> when empty.</summary>
    private static T? MaxByOrFirst<T>(IEnumerable<T> source, Func<T, float> selector) where T : class
    {
        T? best = null;
        var bestValue = 0f;
        foreach (var item in source)
        {
            var value = selector(item);
            if (best == null || value > bestValue)
            {
                best = item;
                bestValue = value;
            }
        }
        return best;
    }

    private void UpdateCivilizationState()
    {
        foreach (var civInfo in Civilizations
            // update city-state resource first since the happiness of major civ depends on it.
            // See issue: https://github.com/yairm210/Unciv/issues/7781
            .OrderByDescending(it => it.IsCityState).ToList())
        {
            using (TurnProbe.Measure("UCS.UnitVisibleTiles"))
                foreach (var unit in civInfo.Units.GetCivUnits().ToList())
                    unit.UpdateVisibleTiles(false); // this needs to be done after all the units are assigned to their civs and all other transients are set
            if (civInfo.PlayerType == PlayerType.Human)
                civInfo.ExploredRegion.SetMapParameters(TileMap.MapParameters); // Required for the correct calculation of the explored region on world wrap maps

            using (TurnProbe.Measure("UCS.UpdateOurTiles")) civInfo.Cache.UpdateOurTiles();
            using (TurnProbe.Measure("UCS.UpdateSightAndResources")) civInfo.Cache.UpdateSightAndResources(); // only run ONCE and not for each unit - this is a huge performance saver!

            // Since this depends on the cities of ALL civilizations,
            // we need to wait until we've set the transients of all the cities before we can run this.
            // Hence why it's not in CivInfo.setTransients().
            using (TurnProbe.Measure("UCS.CitiesConnectedToCapital")) civInfo.Cache.UpdateCitiesConnectedToCapital(true);

            // We need to determine the GLOBAL happiness state in order to determine the city stats
            using (TurnProbe.Measure("UCS.TileStatsAndHappiness"))
                foreach (var city in civInfo.Cities)
                {
                    city.CityStats.UpdateTileStats(); // Some nat wonders can give happiness!
                    city.CityStats.UpdateCityHappiness(city.CityConstructions.GetStats());
                }

            using (TurnProbe.Measure("UCS.CityStatsUpdate"))
                foreach (var city in civInfo.Cities)
                {
                    // We remove constructions from the queue that aren't defined in the ruleset.
                    // This can lead to situations where the city is puppeted and had its construction removed, and there's no way to user-set it
                    // So for cities like those, we'll auto-set the construction
                    // Also set construction for human players who have automate production turned on
                    if (city.CityConstructions.ConstructionQueue.Count == 0)
                        city.CityConstructions.ChooseNextConstruction();

                    // We also remove resources that the city may be demanding but are no longer in the ruleset
                    if (!Ruleset.TileResources.ContainsKey(city.DemandedResource))
                        city.DemandedResource = "";

                    // No uniques have changed since the cache was created, so we can still use it
                    // T262: the tile stats of the loop above are still what Update would compute again unless the civ's happiness moved since.
                    city.CityStats.Update(updateTileStats: !city.CityStats.TileStatsCurrent);
                }
        }
    }

    private void ConvertOldSavesToNewSaves()
    {
        var baseRulesetInMods = GameParameters.Mods.FirstOrDefault(it => RulesetCache.Get(it)?.ModOptions.IsBaseRuleset == true);
        if (baseRulesetInMods != null)
        {
            GameParameters.BaseRuleset = baseRulesetInMods;
            GameParameters.Mods = new LinkedHashSet<string>(GameParameters.Mods.Where(it => it != baseRulesetInMods));
        }
    }

    /// <summary>Github.repoNameToFolderName: outer blanks (a trailing or leading '-') become '=' so the folder name is Windows-safe.</summary>
    private static string RepoNameToFolderName(string name, bool onlyOuterBlanks = false)
    {
        const char outerBlankReplacement = '=';
        var result = onlyOuterBlanks ? name : name.Replace('-', ' ');
        if (result.EndsWith(' ')) result = result.TrimEnd() + outerBlankReplacement;
        if (result.StartsWith(' ')) result = outerBlankReplacement + result.TrimStart();
        return result;
    }

    #endregion
    #region Seams

    // IGameContextGameInfo
    IGameContextRuleset IGameContextGameInfo.Ruleset => Ruleset;
    IGameContextSpeed IGameContextGameInfo.Speed => Speed;
    string? IGameContextGameInfo.GameId => GameId;
    long? IGameContextGameInfo.MapSeed => TileMap.MapParameters.Seed;
    string IGameContextGameInfo.GetDifficultyName() => GetDifficulty().Name;
    IReadOnlyList<IGameContextCivilization> IGameContextGameInfo.Civilizations => Civilizations;
    IEnumerable<IGameContextCity> IGameContextGameInfo.GetCities() => GetCities();
    IEnumerable<IGameContextTile>? IGameContextGameInfo.TileList => TileMap.TileList;
    IGameContextCivilization IGameContextGameInfo.GetCivilization(string civId) => GetCivilization(civId);
    BaseUnit IGameContextGameInfo.GetBaseUnit(string unitName) => Ruleset.Units[unitName].NotNull();
    ModConstants IGameContextGameInfo.GetModConstants() => Ruleset.ModOptions.Constants;
    void IGameContextGameInfo.HandleObsoleteGlobalQuestsOfAliveCityStates()
    {
        foreach (var civ in GetAliveCityStates()) civ.QuestManager.HandleObsoleteGlobalQuests();
    }

    // IGameInfoState
    Civilization.Civilization? IGameInfoState.CurrentPlayerCiv => currentPlayerCiv;
    IReadOnlyList<Civilization.Civilization> IGameInfoState.Civilizations => Civilizations;
    JavaHashMap<string, string?> IGameInfoState.DiplomaticVictoryVotesCast => DiplomaticVictoryVotesCast;
    IReadOnlySet<string> IGameInfoState.SpaceResources => SpaceResources;
    void IGameInfoState.NotifyExploredResources(Civilization.Civilization civInfo, TileResourceObject resource, int maxDistance) =>
        NotifyExploredResources(civInfo, resource, maxDistance);

    IComparer<string> IGameInfoState.Collator => ClientState.Collator;
    bool IGameInfoState.IsFullAutoPlayAI => ClientState.IsFullAutoPlayAI;
    bool IGameInfoState.AlternateUnitCycleOrder => ClientState.AlternateUnitCycleOrder;
    void IGameInfoState.AddCompletedTutorialTask(string civId, string task) => ClientState.AddCompletedTutorialTask(civId, task);

    #endregion
}

/// <summary>
/// Reduced variant of GameInfo used for load preview and multiplayer saves.
/// Contains additional data for multiplayer settings.
/// </summary>
public class GameInfoPreview()
{
    public List<CivilizationInfoPreview> Civilizations { get; set; } = [];
    public string Difficulty { get; set; } = "Chieftain";
    public GameParameters GameParameters { get; set; } = new();
    public int Turns { get; set; }
    public string GameId { get; set; } = "";
    public string CurrentPlayer { get; set; } = "";
    /// <summary>Epoch milliseconds, an input (Kotlin: the wall clock when created).</summary>
    public long CurrentTurnStartTime { get; set; }

    /// <summary>
    /// Converts a GameInfo object (can be uninitialized) into a GameInfoPreview object.
    /// Sets all multiplayer settings to default.
    /// </summary>
    public GameInfoPreview(GameInfo gameInfo) : this()
    {
        Civilizations = gameInfo.GetCivilizationsAsPreviews();
        Difficulty = gameInfo.Difficulty;
        GameParameters = gameInfo.GameParameters;
        Turns = gameInfo.Turns;
        GameId = gameInfo.GameId;
        CurrentPlayer = gameInfo.CurrentPlayer;
        CurrentTurnStartTime = gameInfo.CurrentTurnStartTime;
    }

    public CivilizationInfoPreview GetCivilization(string civId) => Civilizations.First(it => it.CivId == civId);
    public CivilizationInfoPreview GetCurrentPlayerCiv() => GetCivilization(CurrentPlayer);
    public CivilizationInfoPreview? GetPlayerCiv(string playerId) => Civilizations.FirstOrDefault(it => it.PlayerId == playerId);

    /// <summary>
    /// Kotlin <c>randomGameId()</c> draws a UUID from a SecureRandom; the engine takes the generator as an input instead.
    /// </summary>
    public static string RandomGameId(GameRandom rng) => Randomness.PseudoRandomUuid(rng).ToString();
}
