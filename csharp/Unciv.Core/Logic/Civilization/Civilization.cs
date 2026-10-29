using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapGenerator.Mapregions;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Logic.Trade;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.UI.Components.Extensions;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CityStateTypeObject = Unciv.Models.Ruleset.Nation.CityStateType;
using DifficultyObject = Unciv.Models.Ruleset.Nation.Difficulty;
using EraObject = Unciv.Models.Ruleset.Tech.Era;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using PersonalityObject = Unciv.Models.Ruleset.Nation.Personality;
using ProximityLevel = Unciv.Logic.Civilization.Proximity;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Civilization;

// Ported from core/src/com/unciv/logic/civilization/Civilization.kt (T055).
//
// Civilization is ported BEFORE its managers, City, MapUnit and GameInfo (seam rule, T053 notes): every field of those types is typed by
// its contract in CivilizationContracts.cs, and the manager fields are lateinit-style properties (reading before assignment throws) because
// the Kotlin initializer (`var tech = TechManager()`) has nothing to construct yet. The ticket porting a manager replaces its property
// by `= new XManager()` (and wires the constructor), then retypes the field to the concrete class.
//
// Stripped / deferred:
// - timeThis profiling wrappers and the purity annotations.
// - addCity (CityFounder): its two client-settings inputs of resetSpecialistsControl are explicit parameters (T061).
// - isAIOrAutoPlaying: the UncivGame.Current.worldScreen auto-play check is UI state; a headless engine has none, so it is `false`.
// - Kotlin HashMap/HashSet fields keep java.util iteration order (JavaHashMap/JavaHashSet), except neutralRoads, whose seam
//   (IGameContextCivilization.NeutralRoads, T053) is an ISet<HexCoord>; its only Kotlin iteration (CivInfoStatsForNextTurn, T058) sums.
// - GameResource (an interface implemented by the enums Stat and SubStat and the class TileResource) is passed as `object`.
// - The `Civilization.NotificationsLog` / `HistoricalAttackMemory` nested classes keep their Kotlin shape.
// Kotlin Civilization has no equals/hashCode: this class keeps reference identity.

public enum Proximity
{
    None, // ie no cities
    Neighbors,
    Close,
    Far,
    Distant,
}

public partial class Civilization : IsPartOfGameInfoSerialization, IConstructionCivilization, IGameProgressCivilization, ILocalUniqueCacheCivilization, IMapRegionsCivilization
{
    private static T Lateinit<T>(T? value, string name) where T : class =>
        value ?? throw new InvalidOperationException($"lateinit property {name} has not been initialized");

    #region Transient fields
    private IGameInfoState? gameInfo;
    /// <summary>Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public IGameInfoState GameInfo
    {
        get => Lateinit(gameInfo, "gameInfo");
        set => gameInfo = value;
    }

    private bool IsGameInfoInitialized => gameInfo != null;

    /// <summary>
    /// Context for <see cref="UniqueType.StartBias"/> during map gen / start placement.
    /// Passes <see cref="IGameInfoState"/> only - never this <see cref="Civilization"/>, which may be only partially initialized.
    /// Conditionals that need tiles/cities/units are therefore unsupported here.
    /// </summary>
    public GameContext GetGameContextForStartBias() =>
        IsGameInfoInitialized ? new GameContext(gameInfo: gameInfo) : GameContext.IgnoreConditionalsState;

    // IMapRegionsCivilization (T075 seam, implemented by T041): what MapRegions / MinorCivPlacer read from a Civilization
    List<string> IMapRegionsCivilization.GetStartBias(Ruleset ruleset) =>
        ruleset.Nations[CivName].GetStartBias(ruleset, GetGameContextForStartBias()).ToList();

    GameRandom IMapRegionsCivilization.StateBasedRandom(string caller) => State.StateBasedRandom(caller);

    private NationObject? nation;
    /// <summary>Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public NationObject Nation
    {
        get => Lateinit(nation, "nation");
        set => nation = value;
    }

    [JsonIgnore]
    public GameContext State { get; set; } = GameContext.EmptyState;

    /// <summary>Transient. Kotlin <c>val units = UnitManager(this)</c> (assigned in the constructor).</summary>
    [JsonIgnore]
    public UnitManager Units { get; set; }

    /// <summary>Transient. Kotlin <c>var diplomacyFunctions = DiplomacyFunctions(this)</c> (assigned in the constructor).</summary>
    [JsonIgnore]
    public DiplomacyFunctions DiplomacyFunctions { get; set; }

    [JsonIgnore]
    public IReadOnlySet<Tile> ViewableTiles { get; set; } = new HashSet<Tile>();

    /// <summary>
    /// For each tile some detector unit of ours can see, the unit filters (<see cref="UniqueType.CanSeeInvisibleUnits"/>)
    /// it could detect there - independent of what (if anything) currently occupies the tile.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<Tile, IReadOnlySet<string>> ViewableInvisibleUnitsTiles { get; set; } =
        new Dictionary<Tile, IReadOnlySet<string>>();

    /// <summary>This is for performance since every movement calculation depends on this, see MapUnit comment</summary>
    [JsonIgnore]
    public bool HasActiveEnemyMovementPenalty { get; set; }

    /// <summary>Same as above variable</summary>
    [JsonIgnore]
    public IEnumerable<UniqueObject>? EnemyMovementPenaltyUniques { get; set; }

    [JsonIgnore]
    public ResourceSupplyList DetailedCivResources { get; set; } = new();

    [JsonIgnore]
    public ResourceSupplyList SummarizedCivResourceSupply { get; set; } = new();

    [JsonIgnore]
    public UniqueMap CivResourcesUniqueMap { get; set; } = new();

    private ICityStateFunctions? cityStateFunctions;
    /// <summary>
    /// Transient. Kotlin <c>val cityStateFunctions = CityStateFunctions(this)</c>: the real <see cref="Diplomacy.CityStateFunctions"/> unless a test
    /// (or a later seam owner) assigns another <see cref="ICityStateFunctions"/>.
    /// </summary>
    [JsonIgnore]
    public ICityStateFunctions CityStateFunctions
    {
        get => cityStateFunctions ??= new Diplomacy.CityStateFunctions(this);
        set => cityStateFunctions = value;
    }

    /// <summary>Cached Boolean equal to passableImpassables.isNotEmpty()</summary>
    [JsonIgnore]
    public bool PassThroughImpassableUnlocked { get; set; }

    [JsonIgnore]
    public HashSet<HexCoord> NeutralRoads { get; set; } = [];

    [JsonIgnore]
    public ModConstants ModConstants => GameInfo.Ruleset.ModOptions.Constants;

    [JsonIgnore]
    public bool HasLongCountDisplayUnique { get; set; }

    /// <summary>Transient. Kotlin <c>val stats = CivInfoStatsForNextTurn(this)</c> (assigned in the constructor).</summary>
    [JsonIgnore]
    public CivInfoStatsForNextTurn Stats { get; }

    /// <summary>Transient. Kotlin <c>val cache = CivInfoTransientCache(this)</c> (assigned in the constructor).</summary>
    [JsonIgnore]
    public CivInfoTransientCache Cache { get; }

    private int cachedMilitaryMight = -1;
    #endregion

    #region Serialized fields
    [JsonPropertyName("playerType")]
    public PlayerType PlayerType { get; set; } = PlayerType.AI;

    /// <summary>Used in online multiplayer for human players</summary>
    [JsonPropertyName("playerId")]
    public string PlayerId { get; set; } = "";
    /// <summary>Used in online multiplayer, if a player exceed this time to complete their turn, others can force them to resign</summary>
    [JsonPropertyName("playerMinutesBeforeForceResign")]
    public int PlayerMinutesBeforeForceResign { get; set; } = 3 * 24 * 60;
    /// <summary>Total number of minutes spent on their turn. Recorded at the end of each turn.</summary>
    [JsonPropertyName("totalTurnTimeSeconds")]
    public int TotalTurnTimeSeconds { get; set; }
    /// <summary>To calculate average turn time, we only count turns when controlled by a human. Additionally, offers backward compatibility.</summary>
    [JsonPropertyName("turnsPlayedAsHuman")]
    public int TurnsPlayedAsHuman { get; set; }
    [JsonPropertyName("lastTurnProcessedWithVersion")]
    public Version? LastTurnProcessedWithVersion { get; set; }

    /// <summary>The Civ's gold reserves. Public get, private set - please use <see cref="AddGold"/> method to modify.</summary>
    [JsonPropertyName("gold")]
    [JsonInclude]
    public int Gold { get; private set; }

    /// <summary>
    /// The Civ's name
    /// - must always be equal to Nation.name (except in the unit test code, where only local consistency is needed)
    /// - Displayed and translated as-is
    /// </summary>
    [JsonPropertyName("civName")]
    [JsonInclude]
    public string CivName { get; private set; } = "";

    /// <summary>
    /// The Civ's json key
    /// - is currently defaulted to Nation.name (except in the unit test code, where only local consistency is needed)
    /// - used as uniquely identifying key, so no two players can have the same id
    /// Kotlin <c>civID</c>.
    /// </summary>
    [JsonPropertyName("civID")]
    public string CivId { get; set; } = "";

    [JsonPropertyName("tech")]
    public TechManager Tech { get; set; } = new();

    [JsonPropertyName("policies")]
    public PolicyManager Policies { get; set; } = new();

    /// <summary>Kotlin <c>var civConstructions = CivConstructions()</c>.</summary>
    [JsonPropertyName("civConstructions")]
    public CivConstructions CivConstructions { get; set; } = new();

    /// <summary>Kotlin <c>var questManager = QuestManager()</c>.</summary>
    [JsonPropertyName("questManager")]
    public QuestManager QuestManager { get; set; } = new();

    [JsonPropertyName("religionManager")]
    public ReligionManager ReligionManager { get; set; } = new();

    [JsonPropertyName("goldenAges")]
    public GoldenAgeManager GoldenAges { get; set; } = new();

    [JsonPropertyName("greatPeople")]
    public GreatPersonManager GreatPeople { get; set; } = new();

    [JsonPropertyName("espionageManager")]
    public EspionageManager EspionageManager { get; set; } = new();

    [JsonPropertyName("victoryManager")]
    public VictoryManager VictoryManager { get; set; } = new();

    [JsonPropertyName("ruinsManager")]
    public RuinsManager RuinsManager { get; set; } = new();

    /// <summary>Kotlin <c>HashMap&lt;String, DiplomacyManager&gt;</c>: java.util.HashMap iteration order.</summary>
    [JsonPropertyName("diplomacy")]
    public JavaHashMap<string, DiplomacyManager> Diplomacy { get; set; } = new();
    [JsonPropertyName("proximity")]
    public JavaHashMap<string, Proximity> Proximity { get; set; } = new();
    [JsonPropertyName("popupAlerts")]
    [JsonInclude]
    public List<PopupAlert> PopupAlerts { get; private set; } = [];
    /// <summary>Serialization field for <see cref="AllyCiv"/>. Is equivalent to <c>allyCiv.civName</c></summary>
    [JsonPropertyName("allyCivName")]
    [JsonInclude]
    private string? allyCivName;
    [JsonPropertyName("naturalWonders")]
    public List<string> NaturalWonders { get; set; } = [];

    [JsonPropertyName("notifications")]
    public List<Notification> Notifications { get; set; } = [];
    /// <summary>Used by NotificationsOverviewTable to highlight new entries</summary>
    [JsonPropertyName("notificationCountAtStartTurn")]
    public int? NotificationCountAtStartTurn { get; set; }

    [JsonPropertyName("notificationsLog")]
    public List<NotificationsLog> NotificationsLogs { get; set; } = [];

    public class NotificationsLog(int turn = 0) : IsPartOfGameInfoSerialization
    {
        [JsonPropertyName("turn")]
        public int Turn { get; } = turn;
        [JsonPropertyName("notifications")]
        public List<Notification> Notifications { get; set; } = [];
    }

    /// <summary>for trades here, ourOffers is the current civ's offers, and theirOffers is what the requesting civ offers.</summary>
    [JsonPropertyName("tradeRequests")]
    [JsonInclude]
    public List<TradeRequest> TradeRequests { get; private set; } = [];

    /// <summary>See DiplomacyManager.flagsCountdown for why this does not map Enums to ints</summary>
    [JsonPropertyName("flagsCountdown")]
    public JavaHashMap<string, int> FlagsCountdown { get; set; } = new();

    [JsonPropertyName("resourceStockpiles")]
    public Counter<string> ResourceStockpiles { get; set; } = new();

    /// <summary>
    /// List instead of HashMap as the same unique might appear multiple times.
    /// This can also contain NON-temporary uniques but I can't be bothered to do the deprecation dance with this one
    /// </summary>
    [JsonPropertyName("temporaryUniques")]
    [JsonInclude]
    public List<TemporaryUnique> TemporaryUniques { get; private set; } = [];

    /// <summary>
    /// if we only use lists, and change the list each time the cities are changed,
    /// we won't get concurrent modification exceptions.
    /// This is basically a way to ensure our lists are immutable.
    /// </summary>
    [JsonPropertyName("cities")]
    public IReadOnlyList<CityObject> Cities { get; set; } = [];
    [JsonPropertyName("citiesCreated")]
    public int CitiesCreated { get; set; }

    /// <summary>
    /// Constructions (e.g. buildings) that will be disabled when a new city is founded
    /// They are moved to the "Disabled" section in the build menu, and will not be built during automation.
    /// </summary>
    [JsonPropertyName("disabledCityConstructions")]
    [JsonInclude]
    public JavaHashSet<string> DisabledCityConstructions { get; private set; } = new();

    /// <summary>Limit camera within explored region</summary>
    [JsonPropertyName("exploredRegion")]
    public ExploredRegion ExploredRegion { get; set; } = new();

    public bool HasExplored(Tile tile) => tile.IsExplored(this);

    [JsonPropertyName("lastSeenImprovement")]
    [JsonInclude]
    public LastSeenImprovement LastSeenImprovement { get; private set; } = new();

    /// <summary>To correctly determine "game over" condition as clarified in #4707</summary>
    [JsonPropertyName("hasEverOwnedOriginalCapital")]
    public bool HasEverOwnedOriginalCapital { get; set; }

    /// <summary>For Carthage-like uniques</summary>
    [JsonPropertyName("passableImpassables")]
    [JsonInclude]
    public JavaHashSet<string> PassableImpassables { get; private set; } = new();

    /// <summary>For Aggressor, Warmonger status</summary>
    [JsonPropertyName("numMinorCivsAttacked")]
    [JsonInclude]
    internal int NumMinorCivsAttacked { get; set; }

    [JsonPropertyName("totalCultureForContests")]
    public int TotalCultureForContests { get; set; }
    [JsonPropertyName("totalFaithForContests")]
    public int TotalFaithForContests { get; set; }

    /// <summary>
    /// The title for the Civilization's leader.
    /// When empty, will display the nation's display name, otherwise will parse it with leaderName. For example: <c>"King [leaderName]"</c>
    /// </summary>
    [JsonPropertyName("leaderTitle")]
    public string LeaderTitle { get; set; } = "";

    /// <summary>Container class to represent a historical attack recently performed by this civilization.</summary>
    public class HistoricalAttackMemory() : IsPartOfGameInfoSerialization
    {
        public HistoricalAttackMemory(string? attackingUnit, HexCoord source, HexCoord target) : this()
        {
            AttackingUnit = attackingUnit;
            Source = source;
            Target = target;
        }

        /// <summary>Name key of BaseUnit type that performed the attack, or null (E.G. for city bombardments).</summary>
        [JsonPropertyName("attackingUnit")]
        public string? AttackingUnit { get; set; }
        /// <summary>Position of the tile from which the attack was made. Kotlin <c>lateinit var</c>.</summary>
        [JsonPropertyName("source")]
        public HexCoord Source { get; set; }
        /// <summary>Position of the tile targeted by the attack. Kotlin <c>lateinit var</c>.</summary>
        [JsonPropertyName("target")]
        public HexCoord Target { get; set; }

        public HistoricalAttackMemory Clone() => new(AttackingUnit, Source, Target);
    }

    /// <summary>
    /// List of attacks that this civilization has performed since the start of its most recent turn. Does not include attacks already
    /// tracked in MapUnit.attacksSinceTurnStart of living units. Used in movement arrow overlay.
    /// </summary>
    [JsonPropertyName("attacksSinceTurnStart")]
    public List<HistoricalAttackMemory> AttacksSinceTurnStart { get; set; } = [];

    [JsonPropertyName("hasMovedAutomatedUnits")]
    public bool HasMovedAutomatedUnits { get; set; }

    [JsonPropertyName("statsHistory")]
    public CivRankingHistory StatsHistory { get; set; } = new();

    [JsonPropertyName("cityStatePersonality")]
    public CityStatePersonality CityStatePersonality { get; set; } = CityStatePersonality.Neutral;
    /// <summary>Kotlin <c>cityStateResource</c></summary>
    [JsonPropertyName("cityStateResource")]
    public string? CityStateResource { get; set; }
    /// <summary>Unique unit for militaristic city state. Might still be null if there are no appropriate units</summary>
    [JsonPropertyName("cityStateUniqueUnit")]
    public string? CityStateUniqueUnit { get; set; }
    #endregion

    private readonly KotlinLazy<CityStateTypeObject> cityStateType;
    [JsonIgnore]
    public CityStateTypeObject CityStateType => cityStateType.Value;

    public Civilization()
    {
        Stats = new CivInfoStatsForNextTurn(this);
        Cache = new CivInfoTransientCache(this);
        DiplomacyFunctions = new DiplomacyFunctions(this);
        Units = new UnitManager(this);
        // PublicationOnly: Kotlin `by lazy` does not cache a failed initializer (e.g. gameInfo not yet set), unlike ExecutionAndPublication
        cityStateType = new KotlinLazy<CityStateTypeObject>(() => GameInfo.Ruleset.CityStateTypes[Nation.CityStateType.NotNull()]);
    }

    public Civilization(string civName) : this()
    {
        CivName = civName;
        CivId = civName;
    }

    public Civilization(NationObject nation) : this()
    {
        this.nation = nation;
        CivName = nation.Name;
        CivId = nation.Name;
    }

    public Civilization(string civName, string civId) : this()
    {
        CivName = civName;
        CivId = civId;
    }

    public Civilization(NationObject nation, string civId) : this()
    {
        this.nation = nation;
        CivName = nation.Name;
        CivId = civId;
    }

    public Civilization Clone()
    {
        var toReturn = new Civilization
        {
            Gold = Gold,
            PlayerType = PlayerType,
            PlayerId = PlayerId,
            PlayerMinutesBeforeForceResign = PlayerMinutesBeforeForceResign,
            TotalTurnTimeSeconds = TotalTurnTimeSeconds,
            TurnsPlayedAsHuman = TurnsPlayedAsHuman,
            LastTurnProcessedWithVersion = LastTurnProcessedWithVersion,
            CivName = CivName,
            CivId = CivId,
            Tech = Tech.Clone(),
            Policies = Policies.Clone(),
        };
        toReturn.CivConstructions = CivConstructions.Clone();
        toReturn.CivConstructions.SetTransients(toReturn);
        toReturn.ReligionManager = ReligionManager.Clone();
        toReturn.QuestManager = QuestManager.Clone();
        toReturn.GoldenAges = GoldenAges.Clone();
        toReturn.GreatPeople = GreatPeople.Clone();
        toReturn.RuinsManager = RuinsManager.Clone();
        toReturn.EspionageManager = EspionageManager.Clone();
        toReturn.VictoryManager = VictoryManager.Clone();
        toReturn.allyCivName = allyCivName;
        foreach (var diplomacyManager in Diplomacy.Values.Select(it => it.Clone()).ToList())
            toReturn.Diplomacy[diplomacyManager.OtherCivName] = diplomacyManager;
        toReturn.Proximity.PutAll(Proximity);
        toReturn.Cities = Cities.Select(it => it.Clone()).ToList();
        toReturn.NeutralRoads = [.. NeutralRoads]; // T219: copies, not the source's own collections
        foreach (var construction in DisabledCityConstructions) toReturn.DisabledCityConstructions.Add(construction);
        toReturn.ExploredRegion = ExploredRegion.Clone();
        foreach (var (position, improvement) in LastSeenImprovement) toReturn.LastSeenImprovement[position] = improvement;
        toReturn.LeaderTitle = LeaderTitle;
        toReturn.Notifications.AddRange(Notifications.Select(it => it.Clone()));
        foreach (var log in NotificationsLogs)
            toReturn.NotificationsLogs.Add(new NotificationsLog(log.Turn) { Notifications = [.. log.Notifications.Select(it => it.Clone())] });
        toReturn.NotificationCountAtStartTurn = NotificationCountAtStartTurn;
        toReturn.CitiesCreated = CitiesCreated;
        toReturn.PopupAlerts.AddRange(PopupAlerts.Select(it => new PopupAlert(it.Type, it.Value)));
        toReturn.TradeRequests.AddRange(TradeRequests.Select(it => new TradeRequest(it.RequestingCiv, it.Trade.Clone())));
        toReturn.NaturalWonders.AddRange(NaturalWonders);
        toReturn.CityStatePersonality = CityStatePersonality;
        toReturn.CityStateResource = CityStateResource;
        toReturn.CityStateUniqueUnit = CityStateUniqueUnit;
        toReturn.FlagsCountdown.PutAll(FlagsCountdown);
        toReturn.TemporaryUniques.AddRange(TemporaryUniques.Select(it => it.Clone()));
        toReturn.HasEverOwnedOriginalCapital = HasEverOwnedOriginalCapital;
        foreach (var impassable in PassableImpassables) toReturn.PassableImpassables.Add(impassable);
        toReturn.NumMinorCivsAttacked = NumMinorCivsAttacked;
        toReturn.TotalCultureForContests = TotalCultureForContests;
        toReturn.TotalFaithForContests = TotalFaithForContests;
        toReturn.AttacksSinceTurnStart = AttacksSinceTurnStart.Select(it => it.Clone()).ToList();
        toReturn.HasMovedAutomatedUnits = HasMovedAutomatedUnits;
        toReturn.StatsHistory = StatsHistory.Clone();
        toReturn.ResourceStockpiles = ResourceStockpiles.Clone();
        return CopyCultureTo(CopyCiv5AiTo(toReturn));
    }

    #region pure functions
    public DifficultyObject GetDifficulty()
    {
        if (IsHuman()) return GameInfo.GetDifficulty();
        var aiDifficultyLevel = GameInfo.GetDifficulty().AiDifficultyLevel;
        if (aiDifficultyLevel != null && GameInfo.Ruleset.Difficulties.TryGetValue(aiDifficultyLevel, out var aiDifficulty))
            return aiDifficulty;
        if (GameInfo.Ruleset.Difficulties.TryGetValue("Chieftain", out var chieftainDifficulty)) return chieftainDifficulty;
        return GameInfo.Ruleset.Difficulties.Values.First();
    }

    /// <summary>Makes this civilization meet <paramref name="civInfo"/> and returns the DiplomacyManager</summary>
    public DiplomacyManager GetDiplomacyManagerOrMeet(Civilization civInfo)
    {
        if (!Knows(civInfo)) DiplomacyFunctions.MakeCivilizationsMeet(civInfo);
        return GetDiplomacyManager(civInfo.CivId).NotNull();
    }
    public DiplomacyManager? GetDiplomacyManager(Civilization civInfo) => GetDiplomacyManager(civInfo.CivId);
    public DiplomacyManager? GetDiplomacyManager(string civId) => Diplomacy.TryGetValue(civId, out var manager) ? manager : null;

    public Proximity GetProximity(Civilization civInfo) => GetProximity(civInfo.CivId);
    public Proximity GetProximity(string civId) => Proximity.TryGetValue(civId, out var proximity) ? proximity : ProximityLevel.None;

    /// <summary>
    /// Returns only undefeated civs, aka the ones we care about
    ///
    /// Note: Currently the implementation of <c>updateAllyCivForCityState</c> will cause the diplomacy map of
    /// city-states to contain the barbarians. Therefore, <see cref="GetKnownCivs"/> will **not** list the barbarians
    /// for major civs, but **will** do so for city-states after some gameplay.
    ///
    /// <c>ForEachKnownCiv</c> is faster, for cases that require high perf
    /// </summary>
    public IEnumerable<Civilization> GetKnownCivs() => Diplomacy.Values.Select(it => it.OtherCiv)
        .Where(it => !it.IsDefeated() && !it.IsSpectator());

    /// <summary><c>ForEachKnownCiv</c> is faster, for cases that require high perf</summary>
    public IEnumerable<Civilization> GetKnownCivsWithSpectators() => Diplomacy.Values.Select(it => it.OtherCiv)
        .Where(it => !it.IsDefeated());

    public void ForEachKnownCiv(bool withSpectators, Action<Civilization> op)
    {
        foreach (var manager in Diplomacy.Values)
        {
            if (!manager.OtherCiv.IsDefeated() && (withSpectators || !manager.OtherCiv.IsSpectator()))
                op(manager.OtherCiv);
        }
    }

    public void ForEachKnownCiv(Action<Civilization> op) => ForEachKnownCiv(false, op);

    public bool Knows(string otherCivName) => Diplomacy.ContainsKey(otherCivName);
    public bool Knows(Civilization otherCiv) => Knows(otherCiv.CivId);
    public CityObject? GetCapital(bool firstCityIfNoCapital = true) =>
        Cities.FirstOrDefault(it => it.IsCapital()) ?? (firstCityIfNoCapital ? Cities.FirstOrDefault() : null);

    public bool IsHuman() => PlayerType == PlayerType.Human;
    public bool IsAI() => PlayerType == PlayerType.AI;
    public bool IsAIOrAutoPlaying()
    {
        if (PlayerType == PlayerType.AI) return true;
        if (GameInfo.IsSimulation()) return true;
        // Kotlin: worldScreen.selectedGameView.civView.getCiv() == this && worldScreen.autoPlay.isAutoPlaying(); the auto-played civ is the one the command named (T228).
        return GameInfo.ClientState.AutoPlay?.IsAutoPlayingFor(CivId) == true;
    }

    public bool IsOneCityChallenger() => PlayerType == PlayerType.Human && GameInfo.GameParameters.OneCityChallenge;
    /// <summary>Always false for AI since currentPlayerCiv is only set for human players</summary>
    public bool IsCurrentPlayer() => ReferenceEquals(GameInfo.CurrentPlayerCiv, this);
    public bool IsMajorCiv() => Nation.IsMajorCiv;
    public bool IsMinorCiv() => Nation.IsCityState || Nation.IsBarbarian;

    [JsonIgnore]
    public bool IsCityState => Nation.IsCityState;
    [JsonIgnore]
    public bool IsBarbarian => Nation.IsBarbarian;

    public bool IsSpectator() => Nation.IsSpectator;
    public bool IsAlive() => !IsDefeated();

    /// <summary>
    /// A human player who lost in singleplayer keeps playing as a de-facto spectator, and should get the same map visibility.
    /// GameInfo.turns &gt; 0 guards against the setup phase, where every civ is briefly "defeated" (zero units) before starting units are placed.
    /// </summary>
    public bool HasSpectatorVision() => IsSpectator() || TurnState.DefeatedSeesWholeMap(this);

    public bool HasMetCivTerritory(Civilization otherCiv) =>
        otherCiv.GetCivTerritory().Any(it => GameInfo.TileMap[it].IsExplored(this));
    public int GetCompletedPolicyBranchesCount() => Policies.AdoptedPolicies.Count(Policy.IsBranchCompleteByName);
    private IEnumerable<HexCoord> GetCivTerritory() => Cities.SelectMany(it => it.Tiles);

    public List<string> GetPreferredVictoryTypes()
    {
        // A victory this civilization cannot achieve is not worth working towards
        var victoryTypes = VictoryManager.GetAvailableVictories().Select(it => it.Name).ToList();
        if (victoryTypes.Count == 1)
            return [victoryTypes[0]]; // That is the most relevant one
        var victoryType = new List<string> { Nation.PreferredVictoryType, GetPersonality().PreferredVictoryType }
            .Where(it => victoryTypes.Contains(it)).ToList();
        return victoryType.Count != 0 ? victoryType : [Constants.NeutralVictoryType];
    }

    public PersonalityObject GetPersonality()
    {
        if (!IsAIOrAutoPlaying()) return PersonalityObject.NeutralPersonality;
        return Nation.Personality != null && GameInfo.Ruleset.Personalities.TryGetValue(Nation.Personality, out var personality)
            ? personality
            : PersonalityObject.NeutralPersonality;
    }

    public void UpdateStatsForNextTurn()
    {
        int previousHappiness = Stats.Happiness;
        // Kotlin Iterable<Float>.sum() accumulates in Float
        float happinessSum = 0f;
        foreach (var (_, value) in Stats.GetHappinessBreakdown()) happinessSum += value;
        Stats.Happiness = KotlinMath.RoundToInt(happinessSum);
        if (Stats.Happiness != previousHappiness && GameInfo.Ruleset.AllHappinessLevelsThatAffectUniques.Any(it =>
                Stats.Happiness < it != previousHappiness < it)) // If move from being below them to not, or vice versa
            foreach (var city in Cities) city.CityStats.Update(updateCivStats: false);

        var newStats = new Models.StatTypes.Stats();
        foreach (var statsForTurn in Stats.GetStatMapForNextTurn().Values) newStats.Add(statsForTurn);
        Stats.StatsForNextTurn = Policies.InAnarchy ? Policies.WithoutAnarchyStats(newStats) : newStats; // B014: anarchy stops culture and science
    }

    public int GetHappiness() => Stats.Happiness;

    public bool CanSeeResource(TileResource? resource)
    {
        if (resource == null) return false;
        return Tech.IsRevealed(resource);
    }

    /// <summary>Note that for stockpiled resources, this gives by how much it grows per turn, not current amount</summary>
    public ResourceSupplyList GetCivResourceSupply() => SummarizedCivResourceSupply;

    /// <summary>
    /// Preserves some origins for resources so we can separate them for trades
    /// Stockpiled uniques cannot be traded currently
    /// </summary>
    public ResourceSupplyList GetPerTurnResourcesWithOriginsForTrade()
    {
        var newResourceSupplyList = new ResourceSupplyList(keepZeroAmounts: true);

        foreach (var resourceSupply in DetailedCivResources)
        {
            if (resourceSupply.Resource.IsStockpiled) continue;
            if (((IHasUniques)resourceSupply.Resource).HasUnique(UniqueType.CannotBeTraded, State)) continue;
            // If we got it from another trade or from a CS, preserve the origin
            if (resourceSupply.IsCityStateOrTradeOrigin())
            {
                newResourceSupplyList.Add(new ResourceSupplyList.ResourceSupply(resourceSupply.Resource, resourceSupply.Origin, resourceSupply.Amount));
                newResourceSupplyList.Add(resourceSupply.Resource, Constants.Tradable, 0); // Still add an empty "tradable" entry so it shows up in the list
            }
            else
                newResourceSupplyList.Add(resourceSupply.Resource, Constants.Tradable, resourceSupply.Amount);
        }
        return newResourceSupplyList;
    }

    public ResourceSupplyList GetStockpiledResourcesForTrade()
    {
        var newResourceSupplyList = new ResourceSupplyList(keepZeroAmounts: false);

        foreach (var resourceSupply in DetailedCivResources)
        {
            var resource = resourceSupply.Resource;
            if (!resource.IsStockpiled) continue;
            var resourceUniques = (IHasUniques)resource;
            if (resourceUniques.HasUnique(UniqueType.CannotBeTraded, State)) continue;
            if (!resourceUniques.HasUnique(UniqueType.AiWillSellAt, State) && !resourceUniques.HasUnique(UniqueType.AiWillBuyAt, State)) continue;

            newResourceSupplyList.Add(resource, Constants.Tradable, resourceSupply.Amount);
        }
        return newResourceSupplyList;
    }

    public bool IsCapitalConnectedToCity(CityObject city) => Cache.CitiesConnectedToCapitalToMediums.ContainsKey(city);

    /// <summary>
    /// Returns a dictionary of ALL resource names, and the amount that the civ has of each
    /// Stockpiled resources return the stockpiled amount
    /// </summary>
    public JavaHashMap<string, int> GetCivResourcesByName()
    {
        var hashMap = new JavaHashMap<string, int>(GameInfo.Ruleset.TileResources.Count);
        foreach (var resource in GameInfo.Ruleset.TileResources.Keys) hashMap[resource] = 0;
        foreach (var entry in GetCivResourceSupply())
            if (!entry.Resource.IsStockpiled)
                hashMap[entry.Resource.Name] = entry.Amount;
        foreach (var (key, value) in ResourceStockpiles)
            hashMap[key] = value;
        return hashMap;
    }

    /// <summary>
    /// Gets the number of resources available to this city
    /// Does not include city-wide resources
    /// Returns 0 for undefined resources
    /// </summary>
    public int GetResourceAmount(string resourceName)
    {
        int stockpileValue = ResourceStockpiles[resourceName];
        if (stockpileValue != 0) return stockpileValue;
        return GetCivResourceSupply().FirstOrDefault(it => !it.Resource.IsStockpiled && it.Resource.Name == resourceName)?.Amount ?? 0;
    }

    /// <summary>
    /// Gets the number of resources available to this city
    /// Does not include city-wide resources
    /// Returns 0 for undefined resources
    /// </summary>
    public int GetResourceAmount(TileResource resource)
    {
        if (resource.IsStockpiled) return ResourceStockpiles[resource.Name];
        return GetCivResourceSupply().FirstOrDefault(it => it.Resource == resource)?.Amount ?? 0;
    }

    /// <summary>
    /// Returns the resource production modifier as a multiplier.
    /// For example: 1.0f means no change, 2.0f results in double production.
    /// </summary>
    public float GetResourceModifier(TileResource resource)
    {
        float finalModifier = 1f;

        ForEachMatchingUnique(UniqueType.PercentResourceProduction, unique =>
        {
            if (resource.MatchesFilter(unique.Params[1]))
                finalModifier += ParseFloat(unique.Params[0]) / 100f;
        });

        return finalModifier;
    }

    private static float ParseFloat(string text) =>
        UniqueParameterTypeExtensions.ToFloatOrNull(text) ?? throw new FormatException($"Invalid float: \"{text}\"");

    public bool HasResource(string resourceName) => GetResourceAmount(resourceName) > 0;
    public bool HasResource(TileResource resource) => GetResourceAmount(resource) > 0;

    public bool HasUnique(UniqueType uniqueType, GameContext? gameContext = null) =>
        GetMatchingUniques(uniqueType, gameContext).Any();

    /// <summary>Does not return local uniques, only global ones. <see cref="ForEachMatchingUnique(UniqueType, Action{UniqueObject})"/> is faster. If not viable, then this can still be used.</summary>
    public IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType, GameContext? gameContext = null)
    {
        var context = gameContext ?? State;
        // Kotlin `sequence {}`: lazily evaluated in this order
        foreach (var unique in Nation.UniqueMap.GetMatchingUniques(uniqueType, context)) yield return unique;
        foreach (var city in Cities)
            foreach (var unique in city.GetMatchingUniquesWithNonLocalEffects(uniqueType, context)) yield return unique;
        foreach (var unique in Policies.PolicyUniques.GetMatchingUniques(uniqueType, context)) yield return unique;
        foreach (var unique in Tech.TechUniques.GetMatchingUniques(uniqueType, context)) yield return unique;
        foreach (var unique in TemporaryUniques.GetMatchingTagUniques(uniqueType, context)) yield return unique;
        foreach (var unique in GetEra().UniqueMap.GetMatchingUniques(uniqueType, context)) yield return unique;
        foreach (var unique in CityStateFunctions.GetUniquesProvidedByCityStates(uniqueType, context)) yield return unique;
        if (ReligionManager.Religion != null)
            foreach (var unique in ReligionManager.Religion.FounderBeliefUniqueMap.GetMatchingUniques(uniqueType, context)) yield return unique;

        foreach (var unique in CivResourcesUniqueMap.GetMatchingUniques(uniqueType, context)) yield return unique;
        foreach (var unique in GameInfo.GetGlobalUniques().UniqueMap.GetMatchingUniques(uniqueType, context)) yield return unique;
    }

    public void ForEachMatchingUnique(UniqueType uniqueType, Action<UniqueObject> op) => ForEachMatchingUnique(uniqueType, State, op);

    public void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op)
    {
        Nation.UniqueMap.ForEachMatchingUnique(uniqueType, gameContext, op);
        for (int i = 0; i < Cities.Count; i++)
            Cities[i].ForEachMatchingUniqueWithNonLocalEffects(uniqueType, gameContext, op);
        Policies.PolicyUniques.ForEachMatchingUnique(uniqueType, gameContext, op);
        Tech.TechUniques.ForEachMatchingUnique(uniqueType, gameContext, op);
        TemporaryUniques.ForEachMatchingUnique(uniqueType, gameContext, op);
        GetEra().UniqueMap.ForEachMatchingUnique(uniqueType, gameContext, op);
        CityStateFunctions.ForEachUniqueProvidedByCityStates(uniqueType, gameContext, op);
        ReligionManager.Religion?.FounderBeliefUniqueMap.ForEachMatchingUnique(uniqueType, gameContext, op);
        CivResourcesUniqueMap.ForEachMatchingUnique(uniqueType, gameContext, op);
        GameInfo.GetGlobalUniques().UniqueMap.ForEachMatchingUnique(uniqueType, gameContext, op);
    }

    public IEnumerable<UniqueObject> GetTriggeredUniques(UniqueType trigger, GameContext gameContext, Func<UniqueObject, bool>? triggerFilter = null) =>
        GetTriggeredUniques(trigger, gameContext, triggerFilter ?? (_ => true), false);

    public IEnumerable<UniqueObject> GetTriggeredUniques(UniqueType trigger, GameContext gameContext, Func<UniqueObject, bool> triggerFilter, bool ignoreCities)
    {
        // Gathering all uniques into a list first since triggers can add e.g. buildings
        // which contain triggers, causing concurrent modification errors.
        // Cannot use getTriggeredUniques from uniqueMaps since we don't want to check conditionals yet
        var uniques = new List<UniqueObject>();
        uniques.AddRange(Nation.UniqueMap.GetAllUniques());
        if (!ignoreCities)
            foreach (var city in Cities) uniques.AddRange(city.CityConstructions.BuiltBuildingUniqueMap.GetAllUniques());
        if (ReligionManager.Religion != null)
            uniques.AddRange(ReligionManager.Religion.FounderBeliefUniqueMap.GetAllUniques());
        uniques.AddRange(Policies.PolicyUniques.GetAllUniques());
        uniques.AddRange(Tech.TechUniques.GetAllUniques());
        uniques.AddRange(GetEra().UniqueMap.GetAllUniques());
        uniques.AddRange(GameInfo.GetGlobalUniques().UniqueMap.GetAllUniques());
        // Then check conditionals when triggering rather than before triggering
        return uniques
            .Where(it => it.GetModifiers(trigger).Any(triggerFilter) && it.ConditionalsApply(gameContext))
            .SelectMany(it => it.GetMultiplied(gameContext));
    }

    public void ForEachTriggeredUnique(UniqueType trigger, GameContext gameContext, Func<UniqueObject, bool> triggerFilter, Action<UniqueObject> op) =>
        ForEachTriggeredUnique(trigger, gameContext, triggerFilter, false, op);

    public void ForEachTriggeredUnique(UniqueType trigger, GameContext gameContext, Func<UniqueObject, bool> triggerFilter, bool ignoreCities, Action<UniqueObject> op)
    {
        // Gathering all uniques into a list first since triggers can add e.g. buildings
        // which contain triggers, causing concurrent modification errors.
        // Trigger conditions like [trigger] are modifiers on other uniques, not uniques of
        // their own type, so we must scan all uniques (as getTriggeredUniques does) rather
        // than look [trigger] up as if it were a unique's own type.
        bool UniqueFilter(UniqueObject unique) => unique.GetModifiers(trigger).Any(triggerFilter) && unique.ConditionalsApply(gameContext);
        var uniqueList = new List<UniqueObject>(100);
        void ListOp(UniqueObject unique) => uniqueList.Add(unique);
        Nation.UniqueMap.ForEachUnique(UniqueFilter, ListOp);
        if (!ignoreCities)
        {
            foreach (var city in Cities)
                city.CityConstructions.BuiltBuildingUniqueMap.ForEachUnique(UniqueFilter, ListOp);
        }
        ReligionManager.Religion?.FounderBeliefUniqueMap.ForEachUnique(UniqueFilter, ListOp);
        Policies.PolicyUniques.ForEachUnique(UniqueFilter, ListOp);
        Tech.TechUniques.ForEachUnique(UniqueFilter, ListOp);
        GetEra().UniqueMap.ForEachUnique(UniqueFilter, ListOp);
        GameInfo.GetGlobalUniques().UniqueMap.ForEachUnique(UniqueFilter, ListOp);
        // now its safe to do the op, which might mutate the lists
        foreach (var unique in uniqueList) unique.ForEachMultiplied(gameContext, op);
    }

    /// <summary>Implements UniqueParameterType.CivFilter</summary>
    public bool MatchesFilter(string filter) => MatchesFilter(filter, State, true);

    public bool MatchesFilter(string filter, GameContext? state) => MatchesFilter(filter, state, true);

    public bool MatchesFilter(string filter, GameContext? state, bool multiFilter) =>
        multiFilter ? MultiFilter.Filter(filter, it => MatchesSingleFilter(it, state)) : MatchesSingleFilter(filter, state);

    public bool MatchesSingleFilter(string filter, GameContext? state)
    {
        // The state's civ is a Civilization (the only implementer of the IGameContextCivilization seam in the engine)
        var otherCiv = state?.CivInfo as Civilization;
        switch (filter)
        {
            case Constants.HumanPlayer: return IsHuman();
            case Constants.AiPlayer: return IsAI();
            case "Open Borders":
                return otherCiv != null && otherCiv.Diplomacy.TryGetValue(CivId, out var borders) && borders.HasOpenBorders;
            case "Friendly":
                return otherCiv != null && (otherCiv.CivId == CivId
                    || (otherCiv.Diplomacy.TryGetValue(CivId, out var friend) && friend.IsRelationshipLevelGE(RelationshipLevel.Friend)));
            case "Hostile": return otherCiv != null && IsAtWarWith(otherCiv);
            case "Known": return otherCiv != null && (ReferenceEquals(otherCiv, this) || Knows(otherCiv));
            default: return Nation.MatchesFilter(filter, state, false);
        }
    }

    public bool MatchesSingleFilter(string filter) => MatchesSingleFilter(filter, State);

    public bool ShouldOpenTechPicker()
    {
        if (!Tech.CanResearchTech()) return false;
        if (Tech.FreeTechs != 0) return true;
        return Tech.CurrentTechnology() == null && Cities.Count != 0;
    }

    public Building GetEquivalentBuilding(string buildingName)
    {
        if (!GameInfo.Ruleset.Buildings.TryGetValue(buildingName, out var building))
            throw new Exception($"No building by the name of {buildingName} exists!");
        return GetEquivalentBuilding(building);
    }

    public Building GetEquivalentBuilding(Building baseBuilding)
    {
        if (baseBuilding.Replaces != null
                && GameInfo.Ruleset.Buildings.ContainsKey(baseBuilding.Replaces))
            return GetEquivalentBuilding(baseBuilding.Replaces);

        foreach (var building in Cache.UniqueBuildings)
            if (building.Replaces == baseBuilding.Name)
                return building;
        return baseBuilding;
    }

    public TileImprovement GetEquivalentTileImprovement(string tileImprovementName)
    {
        if (!GameInfo.Ruleset.TileImprovements.TryGetValue(tileImprovementName, out var tileImprovement))
            throw new UncivShowableException($"Improvement {tileImprovementName} doesn't seem to exist!");
        return GetEquivalentTileImprovement(tileImprovement);
    }

    public TileImprovement GetEquivalentTileImprovement(TileImprovement tileImprovement)
    {
        if (tileImprovement.Replaces != null)
            return GetEquivalentTileImprovement(tileImprovement.Replaces);

        foreach (var improvement in Cache.UniqueImprovements)
            if (improvement.Replaces == tileImprovement.Name)
                return improvement;
        return tileImprovement;
    }

    public BaseUnit GetEquivalentUnit(string baseUnitName)
    {
        if (!GameInfo.Ruleset.Units.TryGetValue(baseUnitName, out var baseUnit))
            throw new UncivShowableException($"Unit {baseUnitName} doesn't seem to exist!");
        return GetEquivalentUnit(baseUnit);
    }

    public BaseUnit GetEquivalentUnit(BaseUnit baseUnit)
    {
        if (baseUnit.Replaces != null && GameInfo.Ruleset.Units.ContainsKey(baseUnit.Replaces))
            return GetEquivalentUnit(baseUnit.Replaces); // Equivalent of unique unit is the equivalent of the replaced unit

        foreach (var unit in Cache.UniqueUnits)
            if (unit.Replaces == baseUnit.Name)
                return unit;
        return baseUnit;
    }

    public Building? CapitalCityIndicator(CityObject? city = null)
    {
        var gameContext = city != null && ReferenceEquals(city.Civ, this) ? city.State
            : city == null ? State
            : new GameContext(civInfo: this, city: city);
        var indicatorBuildings = GameInfo.Ruleset.Buildings.Values
            .Where(it => ((IHasUniques)it).HasUnique(UniqueType.IndicatesCapital, gameContext));

        var civSpecificBuilding = indicatorBuildings.FirstOrDefault(it => it.UniqueTo != null && MatchesFilter(it.UniqueTo, gameContext));
        return civSpecificBuilding ?? indicatorBuildings.FirstOrDefault();
    }

    public override string ToString() => CivId; // for debug

    /// <summary>
    /// Determine loss conditions.
    ///
    /// If the civ has never controlled an original capital, it stays 'alive' as long as it has units (irrespective of non-original-capitals owned)
    /// Otherwise, it stays 'alive' as long as it has cities (irrespective of settlers owned)
    /// </summary>
    public bool IsDefeated()
    {
        if (IsBarbarian || IsSpectator()) return false; // Barbarians and voyeurs can't lose
        if (HasEverOwnedOriginalCapital) return Cities.Count == 0;
        return Units.GetCivUnitsSize() == 0;
    }

    public EraObject GetEra() => Tech.Era;
    public int GetEraNumber() => GetEra().EraNumber;
    public bool IsAtWarWith(Civilization otherCiv) => DiplomacyFunctions.IsAtWarWith(otherCiv);
    public bool IsAtWar() => Diplomacy.Values.Any(it => it.DiplomaticStatus == DiplomaticStatus.War && !it.OtherCiv.IsDefeated());
    public List<Civilization> GetCivsAtWarWith() =>
        Diplomacy.Values.Where(it => it.DiplomaticStatus == DiplomaticStatus.War && !it.OtherCiv.IsDefeated()).Select(it => it.OtherCiv).ToList();

    /// <summary>
    /// Returns a civilization caption suitable for greetings including player type info:
    /// Like "Milan" if the nation is a city state, "Caesar of Rome" otherwise, with an added
    /// " (AI)", " (Human - Hotseat)", or " (Human - Multiplayer)" if the game is multiplayer.
    /// </summary>
    public string GetLeaderDisplayName()
    {
        bool severalHumans = GameInfo.Civilizations.Count(it => it.PlayerType == PlayerType.Human) > 1;
        bool online = GameInfo.GameParameters.IsOnlineMultiplayer;
        string suffix;
        if (!online && !severalHumans) suffix = ""; // offline single player will know everybody else is AI
        else if (PlayerType == PlayerType.AI) suffix = $" ({"AI".Tr()})";
        else if (online) suffix = $" ({"Human".Tr()} - {"Multiplayer".Tr()})";
        else suffix = $" ({"Human".Tr()} - {"Hotseat".Tr()})";
        return Nation.GetLeaderDisplayName(LeaderTitle).Tr(hideIcons: true) + suffix;
    }

    public int GetStatForRanking(RankingType category)
    {
        if (IsDefeated()) return 0;
        switch (category)
        {
            case RankingType.Score: return KotlinMath.DoubleToInt(CalculateTotalScore());
            case RankingType.Population: return Cities.Sum(it => it.Population.Population);
            case RankingType.Growth: return KotlinMath.RoundToInt(Stats.StatsForNextTurn.Food);
            case RankingType.Production: return KotlinMath.RoundToInt(Stats.StatsForNextTurn.Production);
            case RankingType.Gold: return Gold;
            case RankingType.Territory: return Cities.Sum(it => it.Tiles.Count);
            case RankingType.Force: return GetMilitaryMight();
            case RankingType.Happiness: return GetHappiness();
            case RankingType.Technologies: return Tech.ResearchedTechnologies.Count;
            case RankingType.Culture: return Policies.AdoptedPolicies.Count(it => !Policy.IsBranchCompleteByName(it));

            // Non vanilla ranking types
            case RankingType.TilesExplored: return GameInfo.TileMap.Values.Count(it => it.IsExplored(this));
            default: throw new ArgumentOutOfRangeException(nameof(category));
        }
    }

    private int GetMilitaryMight()
    {
        if (cachedMilitaryMight < 0)
            cachedMilitaryMight = CalculateMilitaryMight();
        return cachedMilitaryMight;
    }

    public void ResetMilitaryMightCache() { cachedMilitaryMight = -1; }

    private int CalculateMilitaryMight()
    {
        int sum = 1; // minimum value, so we never end up with 0
        foreach (var unit in Units.GetCivUnits())
        {
            sum += unit.BaseUnit.IsWaterUnit
                ? unit.GetForceEvaluation() / 2 // Really don't value water units highly
                : unit.GetForceEvaluation();
        }
        float goldBonus = ((float)Math.Sqrt(Math.Max(0f, (float)Gold))).ToPercent(); // 2f if gold == 10000
        sum = KotlinMath.FloatToInt(sum * Math.Min(goldBonus, 2f)); // 2f is max bonus
        return sum;
    }

    public bool IsMinorCivAggressor() => NumMinorCivsAttacked >= 2;
    public bool IsMinorCivWarmonger() => NumMinorCivsAttacked >= 4;

    public bool IsLongCountActive()
    {
        var unique = GetMatchingUniques(UniqueType.MayanGainGreatPerson).FirstOrDefault();
        if (unique == null) return false;
        return Tech.IsResearched(unique.Params[1]);
    }
    public bool IsLongCountDisplay() => HasLongCountDisplayUnique && IsLongCountActive();

    /// <summary>Kotlin <c>HashMap&lt;String, Double&gt;</c>: java.util.HashMap order, which the double sum in <see cref="CalculateTotalScore"/> depends on.</summary>
    public JavaHashMap<string, double> CalculateScoreBreakdown()
    {
        var scoreBreakdown = new JavaHashMap<string, double>();
        // 1276 is the number of tiles in a medium sized map. The original uses 4160 for this,
        // but they have bigger maps
        double mapSizeModifier = 1276 / (double)GameInfo.TileMap.MapParameters.NumberOfTiles();
        if (mapSizeModifier > 1)
            mapSizeModifier = (mapSizeModifier - 1) / 3 + 1;

        var modConstants = GameInfo.Ruleset.ModOptions.Constants;
        scoreBreakdown["Cities"] = Cities.Count * 10 * mapSizeModifier;
        scoreBreakdown["Population"] = Cities.Sum(it => it.Population.Population) * modConstants.ScoreFromPopulation * mapSizeModifier;
        scoreBreakdown["Tiles"] = Cities.Sum(city => city.GetTiles().Count(it => !it.IsWater)) * 1 * mapSizeModifier;
        scoreBreakdown["Wonders"] = modConstants.ScoreFromWonders * (double)Cities
            .Sum(city => city.CityConstructions.GetBuiltBuildings().Count(it => it.IsWonder));
        scoreBreakdown["Technologies"] = Tech.TechsResearched.Count * 4.0;
        scoreBreakdown["Future Tech"] = Tech.RepeatingTechsResearched * 10.0;

        return scoreBreakdown;
    }

    public double CalculateTotalScore()
    {
        // Kotlin Iterable<Double>.sum() accumulates in Double, in map order
        double sum = 0.0;
        foreach (var value in CalculateScoreBreakdown().Values) sum += value;
        return sum;
    }
    #endregion

    #region state-changing functions
    /// <summary>Kotlin <c>@VisibleForTesting setNameForUnitTests</c>.</summary>
    public void SetNameForUnitTests(string name)
    {
        CivName = name;
    }

    /// <summary>
    /// This is separate because the REGULAR setTransients updates the viewable ties,
    /// and updateVisibleTiles tries to meet civs...
    /// And if the civs don't yet know who they are then they don't know if they're barbarians =\
    /// </summary>
    public void SetNationTransient()
    {
        nation = GameInfo.Ruleset.Nations.TryGetValue(CivName, out var found)
            ? found
            : throw new MissingNationException($"Nation {CivName} is not found!", GameInfo.Ruleset.Mods.ToList());
    }

    public void SetTransients()
    {
        GoldenAges.CivInfo = this;
        GreatPeople.CivInfo = this;
        CivConstructions.SetTransients(this);
        Policies.SetTransients(this);
        QuestManager.SetTransients(this);
        ReligionManager.SetTransients(this); // needs to be before tech, since tech setTransients looks at all uniques
        Tech.SetTransients(this);
        RuinsManager.SetTransients(this);
        EspionageManager.SetTransients(this);
        VictoryManager.CivInfo = this;

        foreach (var diplomacyManager in Diplomacy.Values)
        {
            diplomacyManager.CivInfo = this;
            diplomacyManager.UpdateHasOpenBorders();
        }

        foreach (var city in Cities)
        {
            city.SetTransients(this); // must be before the city's setTransients because it depends on the tilemap, that comes from the currentPlayerCivInfo
        }

        // Now that all tile transients have been updated, clean "worked" tiles that are not under the Civ's control
        foreach (var city in Cities)
            foreach (var tile in city.GetWorkedTiles().ToList())
                if (!ReferenceEquals(tile.GetOwner(), this))
                    city.StopWorkingTile(tile);

        PassThroughImpassableUnlocked = PassableImpassables.Count != 0;

        HasLongCountDisplayUnique = HasUnique(UniqueType.MayanCalendarDisplay);

        Cache.SetTransients();
    }

    public void AddFlag(string flag, int count) => FlagsCountdown[flag] = count;
    public void RemoveFlag(string flag) => FlagsCountdown.Remove(flag);
    public bool HasFlag(string flag) => FlagsCountdown.ContainsKey(flag);

    private int? GetFlag(CivFlags flag) => FlagsCountdown.TryGetValue(flag.ToString(), out var value) ? value : null;

    public int GetTurnsBetweenDiplomaticVotes() => KotlinMath.FloatToInt(15 * GameInfo.Speed.Modifier); // Dunno the exact calculation, hidden in Lua files
    public int? GetTurnsTillNextDiplomaticVote() => GetFlag(CivFlags.TurnsTillNextDiplomaticVote);

    public int? GetRecentBullyingCountdown() => GetFlag(CivFlags.RecentlyBullied);
    public int? GetTurnsTillCallForBarbHelp() => GetFlag(CivFlags.TurnsTillCallForBarbHelp);

    public bool MayVoteForDiplomaticVictory() =>
        // Does not need checks for Barbarians or dead civs because the callers already ensure that
        // (NextTurnAutomation.tryVoteForDiplomaticVictory and NextTurnAction.WorldCongressVote)
        !IsSpectator()
        && GetTurnsTillNextDiplomaticVote() == 0
        && !GameInfo.DiplomaticVictoryVotesCast.ContainsKey(CivId)
        // Only vote if there is someone to vote for, may happen in one-more-turn mode
        && GameInfo.Civilizations.Any(it => it.IsMajorCiv() && !it.IsDefeated() && !ReferenceEquals(it, this));

    public void DiplomaticVoteForCiv(string? chosenCivId)
    {
        GameInfo.DiplomaticVictoryVotesCast[CivId] = chosenCivId;
    }

    public bool ShouldShowDiplomaticVotingResults() =>
        GetFlag(CivFlags.ShowDiplomaticVotingResults) == 0
        && GameInfo.Civilizations.Any(it => it.IsMajorCiv() && !it.IsDefeated() && !ReferenceEquals(it, this));

    /// <summary>Modify gold by a given amount making sure it does neither overflow nor underflow.</summary>
    /// <param name="delta">the amount to add (can be negative)</param>
    public void AddGold(int delta)
    {
        // not using Long.coerceIn - this stays in 32 bits
        if (delta > 0 && Gold > int.MaxValue - delta) Gold = int.MaxValue;
        else if (delta < 0 && Gold < int.MinValue - delta) Gold = int.MinValue;
        else Gold = unchecked(Gold + delta);
    }

    public bool HasStatToBuy(Stat stat, int price)
    {
        if (GameInfo.GameParameters.GodMode) return true;
        if (price == 0) return true;
        return GetStatReserve(stat) >= price;
    }

    public void AddStats(Models.StatTypes.Stats stats)
    {
        foreach (var (stat, amount) in stats) AddStat(stat, KotlinMath.FloatToInt(amount));
    }

    public void AddStat(Stat stat, int amount)
    {
        switch (stat)
        {
            case Stat.Culture:
                Policies.AddCulture(amount);
                if (amount > 0) TotalCultureForContests += amount;
                break;
            case Stat.Science: Tech.AddScience(amount); break;
            case Stat.Gold: AddGold(amount); break;
            case Stat.Faith:
                ReligionManager.StoredFaith += amount;
                if (amount > 0) TotalFaithForContests += amount;
                break;
                // Food and Production wouldn't make sense to be added nationwide
                // Happiness cannot be added as it is recalculated again, use a unique instead
        }
    }

    /// <param name="stat">A <see cref="Stat"/>, a <see cref="SubStat"/> or a <see cref="TileResource"/> (Kotlin <c>GameResource</c>).</param>
    /// <param name="amount">The amount to add.</param>
    public void AddGameResource(object stat, int amount)
    {
        if (stat is TileResource { IsStockpiled: true } stockpiled) GainStockpiledResource(stockpiled, amount);
        switch (stat)
        {
            case Stat.Culture:
                Policies.AddCulture(amount);
                if (amount > 0) TotalCultureForContests += amount;
                break;
            case Stat.Science: Tech.AddScience(amount); break;
            case Stat.Gold: AddGold(amount); break;
            case Stat.Faith:
                ReligionManager.StoredFaith += amount;
                if (amount > 0) TotalFaithForContests += amount;
                break;
            case SubStat.GoldenAgePoints: GoldenAges.AddHappiness(amount); break;
                // Food and Production wouldn't make sense to be added nationwide
                // Happiness cannot be added as it is recalculated again, use a unique instead
        }
    }

    /// <param name="gameResource">A <see cref="Stat"/>, a <see cref="SubStat"/> or a <see cref="TileResource"/> (Kotlin <c>GameResource</c>).</param>
    public int GetGameResource(object gameResource) => gameResource switch
    {
        TileResource resource => GetResourceAmount(resource),
        Stat stat => GetStatReserve(stat),
        SubStat.GoldenAgePoints => GoldenAges.StoredHappiness,
        _ => throw new Exception($"Unrecognized gameResource {(gameResource as IGameResource)?.Name ?? gameResource.ToString()}"),
    };

    public void GainStockpiledResource(TileResource resource, int amount)
    {
        if (resource.IsCityWide) return;
        ResourceStockpiles.Add(resource.Name, amount);
    }

    public int GetStatReserve(Stat stat)
    {
        switch (stat)
        {
            case Stat.Culture: return Policies.StoredCulture;
            case Stat.Science:
                var currentTechnology = Tech.CurrentTechnology();
                return currentTechnology == null ? 0 : Tech.ResearchOfTech(currentTechnology.Name);
            case Stat.Gold: return Gold;
            case Stat.Faith: return ReligionManager.StoredFaith;
            case Stat.Happiness: return Stats.Happiness;
            default: return 0;
        }
    }

    #region addNotification
    // Add a Notification to this civ's notifications. Ignored for AI civ's. There are overloads accepting zero, one, a Sequence or
    // Iterable of NotificationActions between text and category; another overload accepts a HexCoord as shorthand for a LocationAction -
    // for several use LocationAction.Of(positions). notificationIcons: Zero or more icons to decorate the notification with - see NotificationIcon.

    /// <summary>No-action version.</summary>
    public void AddNotification(string text, NotificationCategory category, params string[] notificationIcons) =>
        AddNotification(text, (IEnumerable<NotificationAction>?)null, category, notificationIcons);

    /// <summary>Single-location version.</summary>
    public void AddNotification(string text, HexCoord location, NotificationCategory category, params string[] notificationIcons) =>
        AddNotification(text, new LocationAction(location), category, notificationIcons);

    /// <summary>Single-action version.</summary>
    public void AddNotification(string text, NotificationAction action, NotificationCategory category, params string[] notificationIcons) =>
        AddNotification(text, new[] { action }, category, notificationIcons);

    /// <summary>Iterable (and Sequence) version.</summary>
    public void AddNotification(string text, IEnumerable<NotificationAction>? actions, NotificationCategory category, params string[] notificationIcons)
    {
        if (PlayerType == PlayerType.AI) return; // no point in lengthening the saved game info if no one will read it
        Notifications.Add(GameEventHooks.NotificationAdded(this, new Notification(text, notificationIcons, actions, category)));
    }
    #endregion

    /// <summary>
    /// Kotlin <c>addCity(location, unit)</c>. <c>CityFounder</c> reads this civ's <see cref="Unciv.Logic.PlayerSettings"/> for <c>City.resetSpecialistsControl</c>.
    /// </summary>
    public CityObject AddCity(HexCoord location, MapUnit? unit = null)
    {
        var newCity = new CityFounder().FoundCity(this, location, unit);
        newCity.CityConstructions.ChooseNextConstruction();
        return newCity;
    }

    /// <summary>
    /// Destroy what's left of a Civilization
    ///
    /// - function expects cities.isEmpty()
    /// - remaining units are destroyed and diplomacy cleaned up
    /// </summary>
    /// <param name="notificationLocation">if given *and* the civ receiving the notification can see the tile or knows there was a city there, then the notification can show this location on click</param>
    public void Destroy(HexCoord? notificationLocation = null)
    {
        // At the moment, the "last unit down" callers do not pass a location, the city ones do - because the former isn't interesting
        RevealMapWhenDefeated();
        string destructionText = IsMajorCiv() ? $"The civilization of [{CivName}] has been destroyed!"
            : $"The City-State of [{CivName}] has been destroyed!";
        foreach (var civ in GameInfo.Civilizations)
        {
            if (civ.IsDefeated()) continue; // addNotification will ignore barbarians and other AI
            HexCoord? location = null;
            if (notificationLocation != null)
            {
                var tile = GameInfo.TileMap[notificationLocation.Value];
                if (tile.IsVisible(civ) || (tile.IsExplored(civ) && tile.GetShownImprovement(civ) == Constants.CityCenter))
                    location = notificationLocation;
            }
            civ.AddNotification(destructionText, LocationAction.Of(location), NotificationCategory.General, CivName, NotificationIcon.Death);
        }
        foreach (var unit in Units.GetCivUnits().ToList()) unit.Destroy();
        TradeRequests.Clear(); // if we don't do this then there could be resources taken by "pending" trades forever
        foreach (var diplomacyManager in Diplomacy.Values)
        {
            diplomacyManager.Trades.Clear();
            diplomacyManager.OtherCivDiplomacy().Trades.Clear();
            foreach (var tradeRequest in diplomacyManager.OtherCiv.TradeRequests.Where(it => it.RequestingCiv == CivId).ToList())
                diplomacyManager.OtherCiv.TradeRequests.Remove(tradeRequest); // it  would be really weird to get a trade request from a dead civ
        }
        if (GameInfo.IsEspionageEnabled())
            EspionageManager.RemoveAllSpies();
    }

    /// <summary>Reveals the entire map to a human player defeated in a singleplayer game, so they can watch the game play out.</summary>
    public void RevealMapWhenDefeated()
    {
        if (TurnState.IsSimultaneousGame(this) ? !TurnState.DefeatedSeesWholeMap(this) : GameInfo.GameParameters.IsOnlineMultiplayer || !IsCurrentPlayer()) return;
        foreach (var tile in GameInfo.TileMap.Values) tile.SetExplored(this, true);
    }

    public Proximity UpdateProximity(Civilization otherCiv, Proximity? preCalculated = null) => Cache.UpdateProximity(otherCiv, preCalculated);

    /// <summary>Removes current capital then moves capital to argument city if not null</summary>
    public void MoveCapitalTo(CityObject? city, CityObject? oldCapital)
    {
        // Add new capital first so the civ doesn't get stuck in a state where it has cities but no capital
        var newCapitalIndicator = city == null ? null : CapitalCityIndicator(city);
        if (newCapitalIndicator != null)
        {
            // move new capital
            city.NotNull().CityConstructions.AddBuilding(newCapitalIndicator);
            city.IsBeingRazed = false; // stop razing the new capital if it was being razed

            // move the buildings with MovedToNewCapital unique
            if (oldCapital != null)
            {
                // Get the Set of the buildings to move
                var buildingsToMove = oldCapital.CityConstructions.GetBuiltBuildings()
                    .Where(it => ((IHasUniques)it).HasUnique(UniqueType.MovesToNewCapital))
                    .Distinct().ToList();

                foreach (var building in buildingsToMove) oldCapital.CityConstructions.RemoveBuilding(building);

                // Add the buildings to new capital
                foreach (var building in buildingsToMove) city.CityConstructions.AddBuilding(building);
            }
        }

        var oldCapitalIndicator = oldCapital == null ? null : CapitalCityIndicator(oldCapital);
        if (oldCapitalIndicator != null) oldCapital.NotNull().CityConstructions.RemoveBuilding(oldCapitalIndicator);
    }

    /// <param name="oldCapital"><c>null</c> when destroying, otherwise old capital</param>
    public void MoveCapitalToNextLargest(CityObject? oldCapital)
    {
        var availableCities = Cities.Where(it => !it.IsCapital()).ToList();
        if (availableCities.Count == 0)
        {
            MoveCapitalTo(null, oldCapital);
            return;
        }

        // Kotlin maxByOrNull: the first element with the maximum population wins
        static CityObject? LargestOrNull(IEnumerable<CityObject> candidates)
        {
            CityObject? best = null;
            foreach (var candidate in candidates)
                if (best == null || candidate.Population.Population > best.Population.Population) best = candidate;
            return best;
        }

        var newCapital = LargestOrNull(availableCities.Where(it => !it.IsPuppet));

        if (newCapital == null)
        {
            // No non-puppets, take largest puppet and annex
            newCapital = LargestOrNull(availableCities).NotNull();
            newCapital.AnnexCity();
        }

        // Slight "Easter egg": see #11486: In the rare case a City-state loses their last city but it's not their original capital, the notification names the Nation which confuses players.
        // Rename the newly conquered city when the conquering Nation's first-city name is equal to the nation name (meaning Babylon too) and the civ has lost that...
        var currentCapital = GetCapital();
        if (IsCityState && currentCapital != null && currentCapital.IsOriginalCapital && CivName == currentCapital.Name)
            newCapital.Name = $"New [{CivName}]\n(formerly known as [{newCapital.Name}])";

        MoveCapitalTo(newCapital, oldCapital);
    }

    private Civilization? allyCiv;
    /// <summary>
    /// Current ally (assuming this is a city-state)
    /// Setting this also sets its backing serialization field <c>allyCivName</c>
    /// </summary>
    [JsonIgnore]
    public Civilization? AllyCiv
    {
        get
        {
            if (allyCiv == null && allyCivName != null)
                allyCiv = (Civilization)GameInfo.GetCivilization(allyCivName);
            return allyCiv;
        }
        set
        {
            allyCivName = value?.CivId;
            allyCiv = value;
        }
    }

    /// <summary>
    /// Determine if this civ (typically as human player) is allowed to know how many major civs there are
    ///
    /// Can only be <c>true</c> if GameParameters.randomNumberOfPlayers is <c>true</c>, but in that case
    /// we try to see if the player _could_ be certain with a modicum of cleverness...
    /// </summary>
    public bool ShouldHideCivCount()
    {
        if (!GameInfo.GameParameters.RandomNumberOfPlayers) return false;
        int knownCivs = 1 + GetKnownCivs().Count(it => it.IsMajorCiv());
        if (knownCivs >= GameInfo.GameParameters.MaxNumberOfPlayers) return false;
        if (HasUnique(UniqueType.OneTimeRevealEntireMap)) return false;
        // Other ideas? viewableTiles.size == gameInfo.tileMap.tileList.size seems not quite useful...
        return true;
    }

    public CivilizationInfoPreview AsPreview() => new(this);

    public string? GetLastSeenImprovement(HexCoord position)
    {
        if (IsAI() || IsSpectator()) return null;
        return LastSeenImprovement.TryGetValue(position, out var improvement) ? improvement : null;
    }

    public void SetLastSeenImprovement(HexCoord position, string? improvement)
    {
        if (IsAI() || IsSpectator()) return;
        if (improvement == null)
            LastSeenImprovement.Remove(position);
        else
            LastSeenImprovement[position] = improvement;
    }
    #endregion

    #region Seam members (IGameContextCivilization, IConstructionCivilization, IGameProgressCivilization, ILocalUniqueCacheCivilization)
    IGameContextGameInfo? IGameContextCivilization.GameInfo => gameInfo;
    IConstructionGameInfo IConstructionCivilization.GameInfo => GameInfo;
    IReadOnlyList<IGameContextCity> IGameContextCivilization.Cities => Cities;
    string ILocalUniqueCacheCivilization.CivName => CivName;
    int IGameContextCivilization.Happiness => GetHappiness();
    bool IGameContextCivilization.IsTileViewable(Tile tile) => ViewableTiles.Contains(tile);
    bool IGameContextCivilization.Knows(IGameContextCivilization otherCiv) => Knows((Civilization)otherCiv);
    bool IGameContextCivilization.IsAtWarWith(IGameContextCivilization otherCiv) => IsAtWarWith((Civilization)otherCiv);
    bool IGameContextCivilization.IsConsideredFriendlyTerritoryOf(IGameContextCivilization otherCiv) =>
        GetDiplomacyManager((Civilization)otherCiv).NotNull().IsConsideredFriendlyTerritory();
    bool IGameContextCivilization.CanPassThroughTiles(IGameContextCivilization otherCiv) => DiplomacyFunctions.CanPassThroughTiles((Civilization)otherCiv);
    bool IGameContextCivilization.ForestsAndJunglesAreRoads => Nation.ForestsAndJunglesAreRoads;
    ISet<HexCoord> IGameContextCivilization.NeutralRoads => NeutralRoads;
    void IGameContextCivilization.CheckExploredRegionTilePosition(HexCoord tilePosition, HexCoord? explorerPosition) =>
        ExploredRegion.CheckTilePosition(tilePosition, explorerPosition);
    IGameContextUnit IGameContextCivilization.NewMapUnit(BaseUnit baseUnit, int? unitId) => baseUnit.NewMapUnit(this, unitId);
    void IGameContextCivilization.RemoveUnit(IGameContextUnit unit, bool updateCivInfo) => Units.RemoveUnit((MapUnit)unit, updateCivInfo);
    void IGameContextCivilization.UpdateCivResources() => Cache.UpdateCivResources();
    IGameContextCity? IGameContextCivilization.GetCapital() => GetCapital();
    IEnumerable<IGameContextCivilization> IGameContextCivilization.GetKnownCivs() => GetKnownCivs();
    bool IGameContextCivilization.IsTechResearched(string techName) => Tech.IsResearched(techName);
    bool IGameContextCivilization.AnyResearchedTechnologyMatchesFilter(string filter) => Tech.ResearchedTechnologies.Any(it => it.MatchesFilter(filter));
    bool IGameContextCivilization.CurrentTechnologyMatchesFilter(string filter) => Tech.CurrentTechnology()?.MatchesFilter(filter) == true;
    IEnumerable<string> IGameContextCivilization.TechsResearched => Tech.TechsResearched;
    bool IGameContextCivilization.IsPolicyAdopted(string policyName) => Policies.IsAdopted(policyName);
    int IGameContextCivilization.CountAdoptedPoliciesMatching(string filter, GameContext state) => Policies.GetAdoptedPoliciesMatching(filter, state).Count();
    IGameContextReligion? IGameContextCivilization.Religion => ReligionManager.Religion;
    Managers.ReligionState IGameContextCivilization.ReligionState => ReligionManager.ReligionState;
    int IGameContextCivilization.GreatProphetsEarned() => ReligionManager.GreatProphetsEarned();
    bool IGameContextCivilization.IsGoldenAge() => GoldenAges.IsGoldenAge();
    int IGameContextCivilization.GetCivUnitsSize() => Units.GetCivUnitsSize();
    IEnumerable<IGameContextUnit> IGameContextCivilization.GetCivUnits() => Units.GetCivUnits();
    IEnumerable<float> IGameContextCivilization.GetStatMapForNextTurnValues(Stat stat) => Stats.GetStatMapForNextTurn().Values.Select(it => it[stat]);
    int IGameContextCivilization.GetCivResourceSupplyAmount(string resourceName) => GetCivResourceSupply().SumBy(resourceName);
    IEnumerable<TileImprovement> IGameContextCivilization.UniqueImprovements => Cache.UniqueImprovements;
    float IGameContextCivilization.GetGameProgressModifier(UniqueObject unique) => unique.GetGameProgressModifier(this);
    int IConstructionCivilization.GetBuiltItemsWithIncreasingCost(string name) => CivConstructions.GetBuiltItemsWithIncreasingCost(name);
    int IConstructionCivilization.GetBoughtItemsWithIncreasingPrice(string name) => CivConstructions.GetBoughtItemsWithIncreasingPrice(name);
    int IGameProgressCivilization.ResearchedTechnologiesCount => Tech.ResearchedTechnologies.Count;
    int IGameProgressCivilization.RulesetTechnologiesCount => GameInfo.Ruleset.Technologies.Count;
    int IGameProgressCivilization.AdoptedPoliciesCount => Policies.AdoptedPolicies.Count;
    int IGameProgressCivilization.RulesetPoliciesCount => GameInfo.Ruleset.Policies.Count;
    #endregion
}

/// <summary>Reduced variant of CivilizationInfo used for load preview.</summary>
public class CivilizationInfoPreview()
{
    public string CivName { get; set; } = "";
    public string CivId { get; set; } = "";
    public PlayerType PlayerType { get; set; } = PlayerType.AI;
    public string PlayerId { get; set; } = "";
    public int PlayerMinutesBeforeForceResign { get; set; } = 60 * 24 * 3;
    public int TotalTurnTimeSeconds { get; set; }
    public int TurnsPlayedAsHuman { get; set; }
    public bool? IsAlive { get; set; }

    public bool IsPlayerCivilization() => PlayerType == PlayerType.Human;

    /// <summary>Converts a CivilizationInfo object (can be uninitialized) into a CivilizationInfoPreview object.</summary>
    public CivilizationInfoPreview(Civilization civilization) : this()
    {
        CivName = civilization.CivName;
        CivId = civilization.CivId;
        PlayerType = civilization.PlayerType;
        PlayerId = civilization.PlayerId;
        PlayerMinutesBeforeForceResign = civilization.PlayerMinutesBeforeForceResign;
        TotalTurnTimeSeconds = civilization.TotalTurnTimeSeconds;
        TurnsPlayedAsHuman = civilization.TurnsPlayedAsHuman;
        IsAlive = civilization.IsAlive();
    }
}

public enum CivFlags
{
    CityStateGreatPersonGift,
    TurnsTillCityStateElection,
    TurnsTillNextDiplomaticVote,
    ShowDiplomaticVotingResults,
    ShouldResetDiplomaticVotes,
    RecentlyBullied,
    TurnsTillCallForBarbHelp,
    RevoltSpawning,
    CityRevolt,
}
