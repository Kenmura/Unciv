using System.Collections.Immutable;
using System.Globalization;
using Unciv.Logic;
using Unciv.Models.Metadata;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unique;

/// <summary>
/// Ordinal determines severity — ordered from least to most severe, so we can use <c>Severity &gt;=</c>.
/// Ported from <c>UniqueType.UniqueParameterErrorSeverity</c> in <c>UniqueType.kt</c>.
/// </summary>
public enum UniqueParameterErrorSeverity
{
    /// <summary>
    /// This is a warning, regardless of what ruleset we're in.
    /// This is for filters that can also potentially accept free text, like UnitFilter and TileFilter.
    /// </summary>
    PossibleFilteringUnique = 0,

    /// <summary>
    /// An error, but only because of other information in the current ruleset.
    /// This means that if this is an add-on to a different mod, it could be correct.
    /// This is a problem like "unit/resource/tech name doesn't exist in ruleset" — definite bug.
    /// </summary>
    RulesetSpecific = 1,

    /// <summary>
    /// An error, regardless of the ruleset we're in.
    /// This is a problem like "numbers don't parse", "stat isn't stat", "city filter not applicable".
    /// </summary>
    RulesetInvariant = 2,
}

public sealed record UniqueComplianceError(
    string ParameterName,
    IReadOnlyList<UniqueParameterType> AcceptableParameterTypes,
    UniqueParameterErrorSeverity ErrorSeverity);

/// <summary>
/// Covariant read-only map view over a ruleset dictionary (<c>LinkedHashMap&lt;String, T&gt;</c>).
/// Implemented via <see cref="RulesetMapView{TValue}"/> when <c>Ruleset</c> is ported in T034.
/// </summary>
public interface IRulesetMapView<out TValue> where TValue : class
{
    IEnumerable<string> Keys { get; }
    IEnumerable<TValue> Values { get; }
    bool ContainsKey(string key);
    TValue? this[string key] { get; }
}

public sealed class RulesetMapView<TValue>(IReadOnlyDictionary<string, TValue> inner) : IRulesetMapView<TValue>
    where TValue : class
{
    public static readonly RulesetMapView<TValue> Empty = new(new Dictionary<string, TValue>(StringComparer.Ordinal));

    public IEnumerable<string> Keys => inner.Keys;
    public IEnumerable<TValue> Values => inner.Values;
    public bool ContainsKey(string key) => inner.ContainsKey(key);
    public TValue? this[string key] => inner.TryGetValue(key, out var value) ? value : null;
}

/// <summary>
/// Minimal view of a ruleset object (<c>IHasUniques</c>) for <see cref="UniqueParameterType"/> validation.
/// Implemented by <c>IHasUniques</c> / <c>RulesetObject</c> in T027/T030.
/// </summary>
public interface IRulesetObjectParameterView
{
    string Name { get; }
    IReadOnlyCollection<string> Uniques { get; }
    bool HasTagUnique(string uniqueTag) => Uniques.Any(u => u.GetPlaceholderText() == uniqueTag);
    bool HasTagUniqueIgnoringConditionals(string uniqueTag) => HasTagUnique(uniqueTag);
}

public interface IRulesetUnitParameterView : IRulesetObjectParameterView
{
    bool HasGreatPersonUnique => HasTagUnique("Great Person - []");
}

public interface IRulesetTerrainParameterView : IRulesetObjectParameterView
{
    bool IsBaseTerrain { get; }
    bool IsTerrainFeature { get; }
    bool HasRegionUnique =>
        HasTagUnique("A Region is formed with at least []% [] tiles, with priority []") ||
        HasTagUnique("A Region is formed with at least []% [] tiles and [] tiles, with priority []");
}

public interface IRulesetResourceParameterView : IRulesetObjectParameterView
{
    bool IsStockpiled { get; }
}

/// <summary>
/// Minimal view of <c>Ruleset</c> needed by <see cref="UniqueParameterType"/>.
/// Implemented by <c>Ruleset</c> in T034.
/// </summary>
public interface IRulesetParameterView
{
    IRulesetMapView<IRulesetUnitParameterView> Units { get; }
    IRulesetMapView<IRulesetObjectParameterView> UnitTypes { get; }
    IRulesetMapView<IRulesetObjectParameterView> UnitPromotions { get; }
    IRulesetMapView<IRulesetObjectParameterView> Nations { get; }
    IRulesetMapView<IRulesetObjectParameterView> Buildings { get; }
    IRulesetMapView<IRulesetTerrainParameterView> Terrains { get; }
    IRulesetMapView<IRulesetResourceParameterView> TileResources { get; }
    IRulesetMapView<IRulesetObjectParameterView> TileImprovements { get; }
    IRulesetMapView<IRulesetObjectParameterView> Beliefs { get; }
    IRulesetMapView<IRulesetObjectParameterView> Technologies { get; }
    IRulesetMapView<IRulesetObjectParameterView> Policies { get; }
    IRulesetMapView<IRulesetObjectParameterView> UnitNameGroups { get; }
    IRulesetMapView<object> Specialists { get; }
    IRulesetMapView<object> GreatWorks { get; }
    IRulesetMapView<object> Eras { get; }
    IRulesetMapView<object> Speeds { get; }
    IRulesetMapView<object> Difficulties { get; }
    IRulesetMapView<object> Events { get; }
    IRulesetMapView<object> PolicyBranches { get; }
    IRulesetMapView<object> Victories { get; }
    IReadOnlyCollection<string> Religions { get; }
}

public sealed class EmptyRulesetParameterView : IRulesetParameterView
{
    public static readonly EmptyRulesetParameterView Instance = new();

    public IRulesetMapView<IRulesetUnitParameterView> Units => RulesetMapView<IRulesetUnitParameterView>.Empty;
    public IRulesetMapView<IRulesetObjectParameterView> UnitTypes => RulesetMapView<IRulesetObjectParameterView>.Empty;
    public IRulesetMapView<IRulesetObjectParameterView> UnitPromotions => RulesetMapView<IRulesetObjectParameterView>.Empty;
    public IRulesetMapView<IRulesetObjectParameterView> Nations => RulesetMapView<IRulesetObjectParameterView>.Empty;
    public IRulesetMapView<IRulesetObjectParameterView> Buildings => RulesetMapView<IRulesetObjectParameterView>.Empty;
    public IRulesetMapView<IRulesetTerrainParameterView> Terrains => RulesetMapView<IRulesetTerrainParameterView>.Empty;
    public IRulesetMapView<IRulesetResourceParameterView> TileResources => RulesetMapView<IRulesetResourceParameterView>.Empty;
    public IRulesetMapView<IRulesetObjectParameterView> TileImprovements => RulesetMapView<IRulesetObjectParameterView>.Empty;
    public IRulesetMapView<IRulesetObjectParameterView> Beliefs => RulesetMapView<IRulesetObjectParameterView>.Empty;
    public IRulesetMapView<IRulesetObjectParameterView> Technologies => RulesetMapView<IRulesetObjectParameterView>.Empty;
    public IRulesetMapView<IRulesetObjectParameterView> Policies => RulesetMapView<IRulesetObjectParameterView>.Empty;
    public IRulesetMapView<IRulesetObjectParameterView> UnitNameGroups => RulesetMapView<IRulesetObjectParameterView>.Empty;
    public IRulesetMapView<object> Specialists => RulesetMapView<object>.Empty;
    public IRulesetMapView<object> GreatWorks => RulesetMapView<object>.Empty;
    public IRulesetMapView<object> Eras => RulesetMapView<object>.Empty;
    public IRulesetMapView<object> Speeds => RulesetMapView<object>.Empty;
    public IRulesetMapView<object> Difficulties => RulesetMapView<object>.Empty;
    public IRulesetMapView<object> Events => RulesetMapView<object>.Empty;
    public IRulesetMapView<object> PolicyBranches => RulesetMapView<object>.Empty;
    public IRulesetMapView<object> Victories => RulesetMapView<object>.Empty;
    public IReadOnlyCollection<string> Religions => Array.Empty<string>();
}

/// <summary>
/// Hook interface for <see cref="UniqueParameterType.Countable"/> validation, wired by <c>Countables</c> in T028.
/// </summary>
public interface ICountablesParameterProvider
{
    bool IsKnownValue(string parameterText, IRulesetParameterView ruleset);
    IReadOnlySet<string> GetKnownValuesForAutocomplete(IRulesetParameterView ruleset);
    UniqueParameterErrorSeverity? GetErrorSeverity(string parameterText, IRulesetParameterView ruleset);
}

/// <summary>
/// Manages validation of parameters in Uniques.
/// Ported from <c>core/src/com/unciv/models/ruleset/unique/UniqueParameterType.kt</c>.
/// </summary>
public enum UniqueParameterType
{
    Number = 0,
    PositiveNumber = 1,
    NonNegativeNumber = 2,
    Fraction = 3,
    RelativeNumber = 4,
    Countable = 5,
    KeywordAll = 6,
    CombatantFilter = 7,
    MapUnitFilter = 8,
    BaseUnitFilter = 9,
    UnitTypeFilter = 10,
    UnitName = 11,
    GreatPerson = 12,
    Stats = 13,
    StatName = 14,
    CivWideStatName = 15,
    CivFilter = 16,
    NationFilter = 17,
    CityFilter = 18,
    BuildingName = 19,
    BuildingFilter = 20,
    PopulationFilter = 21,
    TerrainFilter = 22,
    TileFilter = 23,
    SimpleTerrain = 24,
    BaseTerrain = 25,
    TerrainFeature = 26,
    TerrainName = 27,
    RegionType = 28,
    TerrainQuality = 29,
    Promotion = 30,
    Era = 31,
    EraFilter = 32,
    Speed = 33,
    Difficulty = 34,
    ImprovementName = 35,
    ImprovementFilter = 36,
    Resource = 37,
    StockpiledResource = 38,
    Stockpile = 39,
    ResourceFilter = 40,
    BeliefTypeName = 41,
    SpyAction = 42,
    Belief = 43,
    ReligionFilter = 44,
    FoundingOrEnhancing = 45,
    Event = 46,
    Technology = 47,
    TechFilter = 48,
    Specialist = 49,
    Policy = 50,
    PolicyFilter = 51,
    VictoryT = 52,
    UnitNameGroup = 53,
    CostOrStrength = 54,
    UnitTriggerTarget = 55,
    ModName = 56,
    ValidationWarning = 57,
    CivilopediaLink = 58,
    Comment = 59,
    LeaderTitle = 60,
    Unknown = 61,
    GreatWork = 62,
}

public static class UniqueParameterTypeExtensions
{
    private const int MinimumSuppressionSelectivity = 12;
    private const string DeprecationWarningPattern = """unique "~" is deprecated as of ~, replace with""";
    private const string UntypedWarningPattern = """unique "~" not found in Unciv's unique types, and is not used as a filtering unique""";

    public const string SuppressionParameterDocDescription =
        "Suppresses one specific Ruleset validation warning. " +
        "This can specify the full text verbatim including correct upper/lower case, " +
        "or it can be a wildcard case-insensitive simple pattern starting and ending in an asterisk ('*'). " +
        "If the suppression unique is used within an object or as modifier (not ModOptions), " +
        "the wildcard symbols can be omitted, as selectivity is better due to the limited scope.";

    public const string SuppressionParameterDocExample =
        "Tinman is supposed to automatically upgrade at tech Clockwork, and therefore Servos for its upgrade Mecha may not yet be researched!" +
        " -or- *is supposed to automatically upgrade*";

    public static readonly IReadOnlyList<UniqueParameterType> Entries = Enum.GetValues<UniqueParameterType>();

    // ResourceType.entries in declaration order (ported as enum in T031).
    private static readonly ImmutableArray<string> ResourceTypeNames = ["Luxury", "Strategic", "Bonus"];

    private static readonly IReadOnlySet<string> EmptySet = new LinkedHashSet<string>();

    private static readonly IReadOnlySet<string> CombatantFilterStatic = SetOf("City");

    private static readonly IReadOnlySet<string> MapUnitFilterStatic = SetOf(
        Constants.Wounded, Constants.Barbarians, "Barbarian",
        "City-State", Constants.Embarked, "Non-City", "other");

    private static readonly IReadOnlySet<string> BaseUnitFilterStatic = UnionWith(
        SetOf(
            "Melee", "Ranged", "Civilian", "Military", "non-air",
            "Nuclear Weapon", "Great Person", "Religious",
            "relevant"),
        Constants.All);

    private static readonly IReadOnlySet<string> UnitTypeFilterStatic = SetOf("Land", "Water", "Air");

    private static readonly IReadOnlySet<string> CivWideStatNameStatic = SetOf(
        "Gold", "Science", "Culture", "Faith", "Happiness");

    private static readonly IReadOnlySet<string> CivFilterStatic = SetOf(
        Constants.AiPlayer, Constants.HumanPlayer, "Open Borders", "Friendly", "Hostile", "Known");

    private static readonly IReadOnlySet<string> NationFilterStatic = UnionWith(
        SetOf(Constants.CityStates, "City-State", "Major"),
        Constants.All);

    private static readonly IReadOnlySet<string> CityFilterStatic = UnionWith(
        SetOf(
            "in this city",
            "in all cities",
            "in your cities", "Your",
            "in all coastal cities", "Coastal",
            "in capital", "Capital",
            "in all non-occupied cities", "Non-occupied",
            "in all cities with a world wonder",
            "in all cities connected to capital",
            "in all cities with a garrison", "Garrisoned",
            "in all cities in which the majority religion is a major religion",
            "in all cities in which the majority religion is an enhanced religion",
            "in non-enemy foreign cities",
            "in enemy cities", "Enemy",
            "in foreign cities", "Foreign",
            "in annexed cities", "Annexed",
            "in puppeted cities", "Puppeted",
            "in resisting cities", "Resisting",
            "in cities being razed", "Razing",
            "in holy cities", "Holy",
            "in City-State cities",
            "in cities following this religion",
            "in cities following our religion"),
        Constants.All);

    private static readonly IReadOnlySet<string> BuildingFilterStatic = UnionWith(
        SetOf("Building", "Buildings", "Wonder", "Wonders", "National Wonder", "National", "World Wonder", "World"),
        StatExtensions.Names(),
        Constants.All);

    private static readonly IReadOnlySet<string> PopulationFilterStatic = SetOf(
        "Population", "Specialists", "Unemployed", "Followers of the Majority Religion", "Followers of this Religion");

    private static readonly IReadOnlySet<string> TerrainFilterStatic = UnionWith(
        SetOf(
            "Terrain",
            Constants.Coastal, Constants.River, "Open terrain", "Rough terrain", "Water resource",
            "resource", "Foreign Land", "Foreign", "Friendly Land", "Friendly", "Enemy Land", "Enemy", "your", "Unowned",
            "Terrain Feature", "Featureless", Constants.FreshWaterFilter, "non-fresh water", "Natural Wonder",
            Constants.Impassable, "Land", "Water"),
        ResourceTypeNames.Select(n => n + " resource"),
        Constants.All);

    private static readonly IReadOnlySet<string> TileFilterStatic = SetOf(
        "unimproved", "improved", "worked", "pillaged", Constants.AllRoad, "Great Improvement");

    private static readonly IReadOnlySet<string> SimpleTerrainStatic = SetOf("Elevated", "Water", "Land");

    private static readonly IReadOnlySet<string> RegionTypeStatic = SetOf("Hybrid");

    private static readonly IReadOnlySet<string> TerrainQualityStatic = SetOf(
        "Undesirable", "Food", "Desirable", "Production");

    private static readonly IReadOnlySet<string> EraFilterStatic = SetOf("any era", "Starting Era");

    private static readonly IReadOnlySet<string> ImprovementFilterStatic = UnionWith(
        SetOf("Improvement", Constants.AllRoad, "Great Improvement", "Great"),
        Constants.All);

    private static readonly IReadOnlySet<string> ResourceFilterStatic = UnionWith(
        SetOf("any"),
        Constants.All);

    // BeliefType.entries in declaration order (ported as enum in T030).
    private static readonly IReadOnlySet<string> BeliefTypeNameStatic = SetOf(
        "None", "Pantheon", "Founder", "Follower", "Enhancer", "Any");

    // SpyAction.entries.map { it.displayString } in declaration order.
    private static readonly IReadOnlySet<string> SpyActionStatic = SetOf(
        "None", "Moving", "Establishing Network", "Observing City",
        "Stealing Tech", "Rigging Elections", "Coup", "Counter-intelligence", "Dead");

    private static readonly IReadOnlySet<string> ReligionFilterStatic = SetOf(
        "any", "major", "enhanced", "your", "foreign", "enemy");

    private static readonly IReadOnlySet<string> FoundingOrEnhancingStatic = SetOf("founding", "enhancing");

    private static readonly IReadOnlySet<string> CostOrStrengthStatic = SetOf("Cost", "Strength");

    private static readonly IReadOnlySet<string> UnitTriggerTargetStatic = SetOf(
        Constants.ThisUnit, Constants.TargetUnit);

    public static ICountablesParameterProvider CountablesProvider { get; set; } = CountablesParameterProvider.Instance;

    public static string ParameterName(this UniqueParameterType type) => type switch
    {
        UniqueParameterType.Number => "amount",
        UniqueParameterType.PositiveNumber => "positiveAmount",
        UniqueParameterType.NonNegativeNumber => "nonNegativeAmount",
        UniqueParameterType.Fraction => "fraction",
        UniqueParameterType.RelativeNumber => "relativeAmount",
        UniqueParameterType.Countable => "countable",
        UniqueParameterType.KeywordAll => "'all'",
        UniqueParameterType.CombatantFilter => "combatantFilter",
        UniqueParameterType.MapUnitFilter => "mapUnitFilter",
        UniqueParameterType.BaseUnitFilter => "baseUnitFilter",
        UniqueParameterType.UnitTypeFilter => "unitType",
        UniqueParameterType.UnitName => "unit",
        UniqueParameterType.GreatPerson => "greatPerson",
        UniqueParameterType.Stats => "stats",
        UniqueParameterType.StatName => "stat",
        UniqueParameterType.CivWideStatName => "civWideStat",
        UniqueParameterType.CivFilter => "civFilter",
        UniqueParameterType.NationFilter => "nationFilter",
        UniqueParameterType.CityFilter => "cityFilter",
        UniqueParameterType.BuildingName => "buildingName",
        UniqueParameterType.BuildingFilter => "buildingFilter",
        UniqueParameterType.PopulationFilter => "populationFilter",
        UniqueParameterType.TerrainFilter => "terrainFilter",
        UniqueParameterType.TileFilter => "tileFilter",
        UniqueParameterType.SimpleTerrain => "simpleTerrain",
        UniqueParameterType.BaseTerrain => "baseTerrain",
        UniqueParameterType.TerrainFeature => "terrainFeature",
        UniqueParameterType.TerrainName => "terrainName",
        UniqueParameterType.RegionType => "regionType",
        UniqueParameterType.TerrainQuality => "terrainQuality",
        UniqueParameterType.Promotion => "promotion",
        UniqueParameterType.Era => "era",
        UniqueParameterType.EraFilter => "eraFilter",
        UniqueParameterType.Speed => "speed",
        UniqueParameterType.Difficulty => "difficulty",
        UniqueParameterType.ImprovementName => "improvementName",
        UniqueParameterType.ImprovementFilter => "improvementFilter",
        UniqueParameterType.Resource => "resource",
        UniqueParameterType.StockpiledResource => "stockpiledResource",
        UniqueParameterType.Stockpile => "stockpile",
        UniqueParameterType.ResourceFilter => "resourceFilter",
        UniqueParameterType.BeliefTypeName => "beliefType",
        UniqueParameterType.SpyAction => "spyAction",
        UniqueParameterType.Belief => "belief",
        UniqueParameterType.ReligionFilter => "religionFilter",
        UniqueParameterType.FoundingOrEnhancing => "foundingOrEnhancing",
        UniqueParameterType.Event => "event",
        UniqueParameterType.Technology => "tech",
        UniqueParameterType.TechFilter => "techFilter",
        UniqueParameterType.Specialist => "specialist",
        UniqueParameterType.Policy => "policy",
        UniqueParameterType.PolicyFilter => "policyFilter",
        UniqueParameterType.VictoryT => "victoryType",
        UniqueParameterType.UnitNameGroup => "unitNameGroup",
        UniqueParameterType.CostOrStrength => "costOrStrength",
        UniqueParameterType.UnitTriggerTarget => "unitTriggerTarget",
        UniqueParameterType.ModName => "modFilter",
        UniqueParameterType.ValidationWarning => "validationWarning",
        UniqueParameterType.CivilopediaLink => "pediaLink",
        UniqueParameterType.Comment => "comment",
        UniqueParameterType.LeaderTitle => "leaderTitle",
        UniqueParameterType.Unknown => "param",
        UniqueParameterType.GreatWork => "greatWork",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static string DocExample(this UniqueParameterType type) => type switch
    {
        UniqueParameterType.Number => "3",
        UniqueParameterType.PositiveNumber => "3",
        UniqueParameterType.NonNegativeNumber => "3",
        UniqueParameterType.Fraction => "0.5",
        UniqueParameterType.RelativeNumber => "+20",
        UniqueParameterType.Countable => "1000",
        UniqueParameterType.KeywordAll => "All",
        UniqueParameterType.CombatantFilter => "City",
        UniqueParameterType.MapUnitFilter => Constants.Wounded,
        UniqueParameterType.BaseUnitFilter => "Melee",
        UniqueParameterType.UnitTypeFilter => "Water",
        UniqueParameterType.UnitName => "Musketman",
        UniqueParameterType.GreatPerson => "Great General",
        UniqueParameterType.Stats => "+1 Gold, +2 Production",
        UniqueParameterType.StatName => "Culture",
        UniqueParameterType.CivWideStatName => "Gold",
        UniqueParameterType.CivFilter => Constants.CityStates,
        UniqueParameterType.NationFilter => Constants.CityStates,
        UniqueParameterType.CityFilter => "in all cities",
        UniqueParameterType.BuildingName => "Library",
        UniqueParameterType.BuildingFilter => "Culture",
        UniqueParameterType.PopulationFilter => "Followers of this Religion",
        UniqueParameterType.TerrainFilter => Constants.FreshWaterFilter,
        UniqueParameterType.TileFilter => "Farm",
        UniqueParameterType.SimpleTerrain => "Elevated",
        UniqueParameterType.BaseTerrain => Constants.Grassland,
        UniqueParameterType.TerrainFeature => Constants.Hill,
        UniqueParameterType.TerrainName => Constants.Forest,
        UniqueParameterType.RegionType => "Hybrid",
        UniqueParameterType.TerrainQuality => "Undesirable",
        UniqueParameterType.Promotion => "Shock I",
        UniqueParameterType.Era => "Ancient era",
        UniqueParameterType.EraFilter => "Ancient era",
        UniqueParameterType.Speed => "Quick",
        UniqueParameterType.Difficulty => "Prince",
        UniqueParameterType.ImprovementName => "Trading Post",
        UniqueParameterType.ImprovementFilter => Constants.AllRoad,
        UniqueParameterType.Resource => "Iron",
        UniqueParameterType.StockpiledResource => "Mana",
        UniqueParameterType.Stockpile => "Mana",
        UniqueParameterType.ResourceFilter => "Strategic",
        UniqueParameterType.BeliefTypeName => "Follower",
        UniqueParameterType.SpyAction => "Counter-intelligence",
        UniqueParameterType.Belief => "God of War",
        UniqueParameterType.ReligionFilter => "major",
        UniqueParameterType.FoundingOrEnhancing => "founding",
        UniqueParameterType.Event => "Inspiration",
        UniqueParameterType.Technology => "Agriculture",
        UniqueParameterType.TechFilter => "Agriculture",
        UniqueParameterType.Specialist => "Merchant",
        UniqueParameterType.Policy => "Oligarchy",
        UniqueParameterType.PolicyFilter => "Oligarchy",
        UniqueParameterType.VictoryT => "Domination",
        UniqueParameterType.UnitNameGroup => "Scientist",
        UniqueParameterType.CostOrStrength => "Cost",
        UniqueParameterType.UnitTriggerTarget => Constants.ThisUnit,
        UniqueParameterType.ModName => "DeCiv Redux",
        UniqueParameterType.ValidationWarning => SuppressionParameterDocExample,
        UniqueParameterType.CivilopediaLink => "Units/Settler",
        UniqueParameterType.Comment => "comment",
        UniqueParameterType.LeaderTitle => "Sovereign [leaderName] the Great",
        UniqueParameterType.Unknown => "Unknown",
        UniqueParameterType.GreatWork => "Parthenon Frieze",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static string? DocDescription(this UniqueParameterType type) => type switch
    {
        UniqueParameterType.Number =>
            "This indicates a whole number, possibly with a + or - sign, such as `2`, `+13`, or `-3`",
        UniqueParameterType.PositiveNumber =>
            "This indicates a positive whole number, larger than zero, a '+' sign is optional",
        UniqueParameterType.NonNegativeNumber =>
            "This indicates a non-negative whole number, larger than or equal to zero, a '+' sign is optional",
        UniqueParameterType.Fraction =>
            "Indicates a fractional number, which can be negative",
        UniqueParameterType.RelativeNumber =>
            "This indicates a number, usually with a + or - sign, such as `+25` (this kind of parameter is often followed by '%' which is nevertheless not part of the value)",
        UniqueParameterType.Countable =>
            "This indicates a number or a numeric variable." +
            "They can be tested in the developer console with `civ checkcountable` - for example, `civ checkcountable \"[Iron]+2\"`",
        UniqueParameterType.CombatantFilter =>
            "This indicates a combatant, which can either be a unit or a city (when bombarding). Must either be `City` or a `mapUnitFilter`",
        UniqueParameterType.UnitTypeFilter =>
            "Can be 'Land', 'Water', 'Air', any unit type, a filtering Unique on a unit type",
        UniqueParameterType.Stats =>
            "For example: `+2 Production, +3 Food`. Note that the stat names need to be capitalized!",
        UniqueParameterType.StatName =>
            "This is one of the 7 major stats in the game - `Gold`, `Science`, `Production`, `Food`, `Happiness`, `Culture` and `Faith`. Note that the stat names need to be capitalized!",
        UniqueParameterType.CivWideStatName =>
            "All the following stats have civ-wide fields: `Gold`, `Science`, `Culture`, `Faith`",
        UniqueParameterType.BuildingName =>
            "The name of any building",
        UniqueParameterType.TileFilter =>
            "Anything that can be used either in an improvementFilter or in a terrainFilter can be used here, plus 'unimproved'",
        UniqueParameterType.BaseTerrain =>
            "The name of any terrain that is a base terrain according to the json file",
        UniqueParameterType.TerrainFeature =>
            "The name of any terrain that is a terrain feature according to the json file",
        UniqueParameterType.Promotion =>
            "The name of any promotion",
        UniqueParameterType.Era =>
            "The name of any era",
        UniqueParameterType.EraFilter =>
            "The name of an era, `any era`, `Starting Era`, `pre-[era]`, `post-[era]`",
        UniqueParameterType.Speed =>
            "The name of any speed",
        UniqueParameterType.Difficulty =>
            "The name of any difficulty",
        UniqueParameterType.ImprovementName =>
            "The name of any improvement excluding 'Cancel improvement order'",
        UniqueParameterType.Resource =>
            "The name of any resource",
        UniqueParameterType.StockpiledResource =>
            "The name of any stockpiled resource",
        UniqueParameterType.Stockpile =>
            "The name of any stockpiled resource",
        UniqueParameterType.ResourceFilter =>
            "A resource name, type, 'all', or a Stat listed in the resource's improvementStats",
        UniqueParameterType.BeliefTypeName =>
            "'Pantheon', 'Follower', 'Founder' or 'Enhancer'",
        UniqueParameterType.SpyAction =>
            "A spy action display name, e.g. `Counter-intelligence`, `Stealing Tech`",
        UniqueParameterType.Belief =>
            "The name of any belief",
        UniqueParameterType.FoundingOrEnhancing =>
            "`founding` or `enhancing`",
        UniqueParameterType.Event =>
            "The name of any event",
        UniqueParameterType.Technology =>
            "The name of any tech",
        UniqueParameterType.Specialist =>
            "The name of any specialist",
        UniqueParameterType.GreatWork =>
            "The name of any Great Work found in GreatWorks.json",
        UniqueParameterType.Policy =>
            "The name of any policy",
        UniqueParameterType.PolicyFilter =>
            "The name of any policy, a filtering Unique, any branch (matching only the branch itself)," +
            " a branch name with \" Completed\" appended (matches if the branch is completed)," +
            " or a policy branch as `[branchName] branch` (matching all policies in that branch).",
        UniqueParameterType.VictoryT =>
            "The name of any victory type: 'Cultural', 'Diplomatic', 'Domination', 'Scientific', 'Time' or one of your mod's VictoryTypes.json names",
        UniqueParameterType.UnitNameGroup =>
            "The name of a unit name group found in UnitNameGroups.json, or one of their unique tags",
        UniqueParameterType.CostOrStrength =>
            "`Cost` or `Strength`",
        UniqueParameterType.UnitTriggerTarget =>
            $"`{Constants.ThisUnit}` or `{Constants.TargetUnit}`",
        UniqueParameterType.ModName =>
            "A Mod name, case-sensitive _or_ a simple wildcard filter beginning and ending in an Asterisk, case-insensitive.\n" +
            "Note that this must use the Mod name as Unciv displays it, not the Repository name.\n" +
            "There is a conversion affecting dashes and leading/trailing blanks. Please make sure not to get confused.",
        UniqueParameterType.ValidationWarning =>
            SuppressionParameterDocDescription,
        UniqueParameterType.CivilopediaLink =>
            "A Civilopedia link in the form category/entry",
        UniqueParameterType.LeaderTitle =>
            "Provides a leader title that includes the leader's name in parameters.",
        _ => null,
    };

    public static string DisplayName(this UniqueParameterType type) => type switch
    {
        UniqueParameterType.MapUnitFilter => "Map Unit Filters",
        UniqueParameterType.UnitTypeFilter => "Unit Type Filters",
        UniqueParameterType.CityFilter => "City filters",
        UniqueParameterType.PopulationFilter => "Population Filters",
        UniqueParameterType.TerrainFilter => "Terrain Filters",
        UniqueParameterType.TileFilter => "Tile Filters",
        UniqueParameterType.RegionType => "Region Types",
        UniqueParameterType.TerrainQuality => "Terrain Quality",
        UniqueParameterType.ImprovementFilter => "Improvement Filters",
        UniqueParameterType.FoundingOrEnhancing => "Prophet Action Filters",
        UniqueParameterType.ModName => "Mod name filter",
        UniqueParameterType.ValidationWarning => "Mod-check warning",
        UniqueParameterType.CivilopediaLink => "Unique Specials",
        UniqueParameterType.Comment => "Unique Specials",
        UniqueParameterType.LeaderTitle => "Leader Title",
        _ => type.ParameterName(),
    };

    internal static UniqueParameterErrorSeverity SeverityDefault(this UniqueParameterType type) => type switch
    {
        UniqueParameterType.KeywordAll
            or UniqueParameterType.Stats
            or UniqueParameterType.StatName
            or UniqueParameterType.CivWideStatName
            or UniqueParameterType.PopulationFilter
            or UniqueParameterType.TerrainQuality
            or UniqueParameterType.BeliefTypeName
            or UniqueParameterType.SpyAction
            or UniqueParameterType.FoundingOrEnhancing
            or UniqueParameterType.CostOrStrength
            or UniqueParameterType.ModName
            or UniqueParameterType.ValidationWarning
            or UniqueParameterType.CivilopediaLink => UniqueParameterErrorSeverity.RulesetInvariant,
        UniqueParameterType.ResourceFilter => UniqueParameterErrorSeverity.PossibleFilteringUnique,
        _ => UniqueParameterErrorSeverity.RulesetSpecific,
    };

    public static IReadOnlySet<string> StaticKnownValues(this UniqueParameterType type) => type switch
    {
        UniqueParameterType.KeywordAll => Constants.All,
        UniqueParameterType.CombatantFilter => CombatantFilterStatic,
        UniqueParameterType.MapUnitFilter => MapUnitFilterStatic,
        UniqueParameterType.BaseUnitFilter => BaseUnitFilterStatic,
        UniqueParameterType.UnitTypeFilter => UnitTypeFilterStatic,
        UniqueParameterType.CivWideStatName => CivWideStatNameStatic,
        UniqueParameterType.CivFilter => CivFilterStatic,
        UniqueParameterType.NationFilter => NationFilterStatic,
        UniqueParameterType.CityFilter => CityFilterStatic,
        UniqueParameterType.BuildingFilter => BuildingFilterStatic,
        UniqueParameterType.PopulationFilter => PopulationFilterStatic,
        UniqueParameterType.TerrainFilter => TerrainFilterStatic,
        UniqueParameterType.TileFilter => TileFilterStatic,
        UniqueParameterType.SimpleTerrain => SimpleTerrainStatic,
        UniqueParameterType.RegionType => RegionTypeStatic,
        UniqueParameterType.TerrainQuality => TerrainQualityStatic,
        UniqueParameterType.EraFilter => EraFilterStatic,
        UniqueParameterType.ImprovementFilter => ImprovementFilterStatic,
        UniqueParameterType.ResourceFilter => ResourceFilterStatic,
        UniqueParameterType.BeliefTypeName => BeliefTypeNameStatic,
        UniqueParameterType.SpyAction => SpyActionStatic,
        UniqueParameterType.ReligionFilter => ReligionFilterStatic,
        UniqueParameterType.FoundingOrEnhancing => FoundingOrEnhancingStatic,
        UniqueParameterType.TechFilter => Constants.All,
        UniqueParameterType.PolicyFilter => Constants.All,
        UniqueParameterType.CostOrStrength => CostOrStrengthStatic,
        UniqueParameterType.UnitTriggerTarget => UnitTriggerTargetStatic,
        _ => EmptySet,
    };

    public static UniqueParameterErrorSeverity? GetErrorSeverity(
        this UniqueParameterType type,
        string parameterText,
        IRulesetParameterView ruleset) => type switch
        {
            UniqueParameterType.Number =>
                GetInvariantSeverityUnless(ToIntOrNull(parameterText) != null),
            UniqueParameterType.PositiveNumber =>
                GetInvariantSeverityUnless(ToIntOrNull(parameterText) is > 0),
            UniqueParameterType.NonNegativeNumber =>
                GetInvariantSeverityUnless(ToIntOrNull(parameterText) is >= 0),
            UniqueParameterType.Fraction =>
                GetInvariantSeverityUnless(ToFloatOrNull(parameterText) != null),
            UniqueParameterType.RelativeNumber =>
                GetInvariantSeverityUnless(ToIntOrNull(parameterText) != null),
            UniqueParameterType.Countable =>
                CountablesProvider.GetErrorSeverity(parameterText, ruleset),
            UniqueParameterType.CombatantFilter
                or UniqueParameterType.MapUnitFilter
                or UniqueParameterType.BaseUnitFilter
                or UniqueParameterType.UnitTypeFilter
                or UniqueParameterType.CivFilter
                or UniqueParameterType.NationFilter
                or UniqueParameterType.CityFilter
                or UniqueParameterType.BuildingFilter
                or UniqueParameterType.TerrainFilter
                or UniqueParameterType.TileFilter
                or UniqueParameterType.EraFilter
                or UniqueParameterType.ImprovementFilter
                or UniqueParameterType.ResourceFilter
                or UniqueParameterType.TechFilter
                or UniqueParameterType.PolicyFilter =>
                type.GetErrorSeverityForFilter(parameterText, ruleset),
            UniqueParameterType.TerrainFeature =>
                ruleset.Terrains[parameterText]?.IsTerrainFeature == true
                    ? null
                    : UniqueParameterErrorSeverity.RulesetSpecific,
            UniqueParameterType.ImprovementName =>
                parameterText == Constants.CancelImprovementOrder
                    ? UniqueParameterErrorSeverity.RulesetInvariant
                    : type.GetErrorSeverityViaKnownValue(parameterText, ruleset),
            UniqueParameterType.Comment or UniqueParameterType.Unknown =>
                null,
            _ => type.GetErrorSeverityViaKnownValue(parameterText, ruleset),
        };

    public static bool IsKnownValue(
        this UniqueParameterType type,
        string parameterText,
        IRulesetParameterView ruleset) => type switch
        {
            UniqueParameterType.Countable =>
                CountablesProvider.IsKnownValue(parameterText, ruleset),
            UniqueParameterType.CombatantFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || UniqueParameterType.MapUnitFilter.IsKnownValue(parameterText, ruleset)
                || UniqueParameterType.CityFilter.IsKnownValue(parameterText, ruleset),
            UniqueParameterType.MapUnitFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || ruleset.UnitPromotions.ContainsKey(parameterText)
                || ruleset.UnitPromotions.Values.Any(it => it.HasTagUnique(parameterText))
                || UniqueParameterType.CivFilter.IsKnownValue(parameterText, ruleset)
                || UniqueParameterType.BaseUnitFilter.IsKnownValue(parameterText, ruleset),
            UniqueParameterType.BaseUnitFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || UniqueParameterType.UnitName.GetErrorSeverity(parameterText, ruleset) == null
                || IsFilteringUniqueIn(parameterText, ruleset.Units)
                || UniqueParameterType.UnitTypeFilter.IsKnownValue(parameterText, ruleset)
                || UniqueParameterType.TechFilter.IsKnownValue(parameterText, ruleset),
            UniqueParameterType.UnitTypeFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || ruleset.UnitTypes.ContainsKey(parameterText)
                || IsFilteringUniqueIn(parameterText, ruleset.UnitTypes),
            UniqueParameterType.Stats =>
                StatTypes.Stats.IsStats(parameterText),
            UniqueParameterType.CivFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || UniqueParameterType.NationFilter.IsKnownValue(parameterText, ruleset),
            UniqueParameterType.NationFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || ruleset.Nations.ContainsKey(parameterText)
                || ruleset.Nations.Values.Any(it => it.HasTagUnique(parameterText)),
            UniqueParameterType.BuildingFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || UniqueParameterType.BuildingName.IsKnownValue(parameterText, ruleset)
                || ruleset.Buildings.Values.Any(it => it.HasTagUnique(parameterText))
                || UniqueParameterType.TechFilter.IsKnownValue(parameterText, ruleset),
            UniqueParameterType.TerrainFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || ruleset.Terrains.ContainsKey(parameterText)
                || ruleset.TileResources.ContainsKey(parameterText)
                || IsFilteringUniqueIn(parameterText, ruleset.Terrains)
                || IsFilteringUniqueIn(parameterText, ruleset.TileResources),
            UniqueParameterType.TileFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || UniqueParameterType.ImprovementFilter.IsKnownValue(parameterText, ruleset)
                || UniqueParameterType.TerrainFilter.IsKnownValue(parameterText, ruleset)
                || UniqueParameterType.CivFilter.IsKnownValue(parameterText, ruleset),
            UniqueParameterType.ImprovementFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || UniqueParameterType.ImprovementName.IsKnownValue(parameterText, ruleset)
                || ruleset.TileImprovements.Values.Any(it => it.HasTagUnique(parameterText)),
            UniqueParameterType.ResourceFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || ruleset.TileResources.ContainsKey(parameterText)
                || ResourceTypeNames.Contains(parameterText)
                || StatExtensions.IsStat(parameterText),
            UniqueParameterType.ReligionFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || ruleset.Religions.Contains(parameterText)
                || ruleset.Beliefs.ContainsKey(parameterText)
                || ruleset.Beliefs.Values.Any(it => it.HasTagUnique(parameterText)),
            UniqueParameterType.TechFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || ruleset.Technologies.ContainsKey(parameterText)
                || ruleset.Technologies.Values.Any(it => it.HasTagUnique(parameterText))
                || UniqueParameterType.EraFilter.IsKnownValue(parameterText, ruleset),
            UniqueParameterType.PolicyFilter =>
                type.StaticKnownValues().Contains(parameterText)
                || ruleset.Policies.ContainsKey(parameterText)
                || (parameterText.StartsWith('[')
                    && parameterText.EndsWith("] branch", StringComparison.Ordinal)
                    && ruleset.PolicyBranches.ContainsKey(parameterText[1..^8]))
                || ruleset.Policies.Values.Any(it => it.HasTagUnique(parameterText)),
            UniqueParameterType.UnitNameGroup =>
                ruleset.UnitNameGroups.ContainsKey(parameterText)
                || ruleset.UnitNameGroups.Values.Any(it => it.HasTagUniqueIgnoringConditionals(parameterText)),
            UniqueParameterType.ModName =>
                IsKnownModName(parameterText),
            UniqueParameterType.ValidationWarning =>
                IsValidSuppressionFilter(parameterText),
            UniqueParameterType.CivilopediaLink =>
                IsKnownCivilopediaLink(parameterText),
            UniqueParameterType.LeaderTitle =>
                IsKnownLeaderTitle(parameterText),
            _ => type.GetKnownValuesForAutocomplete(ruleset).Contains(parameterText),
        };

    public static IReadOnlySet<string> GetKnownValuesForAutocomplete(
        this UniqueParameterType type,
        IRulesetParameterView ruleset) => type switch
        {
            UniqueParameterType.Countable =>
                CountablesProvider.GetKnownValuesForAutocomplete(ruleset),
            UniqueParameterType.CombatantFilter =>
                UnionWith(
                    type.StaticKnownValues(),
                    UniqueParameterType.MapUnitFilter.GetKnownValuesForAutocomplete(ruleset),
                    UniqueParameterType.CityFilter.GetKnownValuesForAutocomplete(ruleset)),
            UniqueParameterType.MapUnitFilter =>
                UnionWith(
                    type.StaticKnownValues(),
                    ruleset.UnitPromotions.Keys,
                    UniqueParameterType.CivFilter.GetKnownValuesForAutocomplete(ruleset),
                    UniqueParameterType.BaseUnitFilter.GetKnownValuesForAutocomplete(ruleset)),
            UniqueParameterType.BaseUnitFilter =>
                UnionWith(
                    type.StaticKnownValues(),
                    UniqueParameterType.UnitName.GetKnownValuesForAutocomplete(ruleset),
                    UniqueParameterType.UnitTypeFilter.GetKnownValuesForAutocomplete(ruleset),
                    UniqueParameterType.TechFilter.GetKnownValuesForAutocomplete(ruleset)),
            UniqueParameterType.UnitTypeFilter =>
                UnionWith(type.StaticKnownValues(), ruleset.UnitTypes.Keys),
            UniqueParameterType.UnitName =>
                ToLinkedSet(ruleset.Units.Keys),
            UniqueParameterType.GreatPerson =>
                ToLinkedSet(ruleset.Units.Values.Where(u => u.HasGreatPersonUnique).Select(u => u.Name)),
            UniqueParameterType.StatName =>
                ToLinkedSet(StatExtensions.Names()),
            UniqueParameterType.CivFilter =>
                UnionWith(
                    type.StaticKnownValues(),
                    UniqueParameterType.NationFilter.GetKnownValuesForAutocomplete(ruleset)),
            UniqueParameterType.NationFilter =>
                UnionWith(type.StaticKnownValues(), ruleset.Nations.Keys),
            UniqueParameterType.BuildingName =>
                ToLinkedSet(ruleset.Buildings.Keys),
            UniqueParameterType.BuildingFilter =>
                UnionWith(
                    type.StaticKnownValues(),
                    UniqueParameterType.BuildingName.GetKnownValuesForAutocomplete(ruleset),
                    UniqueParameterType.TechFilter.GetKnownValuesForAutocomplete(ruleset)),
            UniqueParameterType.PopulationFilter =>
                UnionWith(type.StaticKnownValues(), ruleset.Specialists.Keys),
            UniqueParameterType.TerrainFilter =>
                UnionWith(type.StaticKnownValues(), ruleset.Terrains.Keys, ruleset.TileResources.Keys),
            UniqueParameterType.TileFilter =>
                UnionWith(
                    type.StaticKnownValues(),
                    UniqueParameterType.ImprovementFilter.GetKnownValuesForAutocomplete(ruleset),
                    UniqueParameterType.TerrainFilter.GetKnownValuesForAutocomplete(ruleset)),
            UniqueParameterType.SimpleTerrain =>
                UnionWith(type.StaticKnownValues(), ruleset.Terrains.Keys),
            UniqueParameterType.BaseTerrain =>
                ToLinkedSet(ruleset.Terrains.Values.Where(t => t.IsBaseTerrain).Select(t => t.Name)),
            UniqueParameterType.TerrainName =>
                ToLinkedSet(ruleset.Terrains.Keys),
            UniqueParameterType.RegionType =>
                UnionWith(
                    type.StaticKnownValues(),
                    ruleset.Terrains.Values.Where(t => t.HasRegionUnique).Select(t => t.Name)),
            UniqueParameterType.Promotion =>
                ToLinkedSet(ruleset.UnitPromotions.Keys),
            UniqueParameterType.Era =>
                ToLinkedSet(ruleset.Eras.Keys),
            UniqueParameterType.EraFilter =>
                UnionWith(
                    type.StaticKnownValues(),
                    ruleset.Eras.Keys.SelectMany<string, string>(it => [it, $"pre-[{it}]", $"post-[{it}]"])),
            UniqueParameterType.Speed =>
                ToLinkedSet(ruleset.Speeds.Keys),
            UniqueParameterType.Difficulty =>
                ToLinkedSet(ruleset.Difficulties.Keys),
            UniqueParameterType.ImprovementName =>
                ToLinkedSet(ruleset.TileImprovements.Keys.Where(k => k != Constants.CancelImprovementOrder)),
            UniqueParameterType.ImprovementFilter =>
                UnionWith(
                    type.StaticKnownValues(),
                    UniqueParameterType.ImprovementName.GetKnownValuesForAutocomplete(ruleset)),
            UniqueParameterType.Resource =>
                ToLinkedSet(ruleset.TileResources.Keys),
            UniqueParameterType.StockpiledResource =>
                ToLinkedSet(ruleset.TileResources.Values.Where(r => r.IsStockpiled).Select(r => r.Name)),
            UniqueParameterType.Stockpile =>
                UnionWith(
                    ruleset.TileResources.Values.Where(r => r.IsStockpiled).Select(r => r.Name),
                    StatExtensions.Names(),
                    [SubStat.StoredFood.Text(), SubStat.GoldenAgePoints.Text()]),
            UniqueParameterType.ResourceFilter =>
                UnionWith(
                    type.StaticKnownValues(),
                    ruleset.TileResources.Keys,
                    ResourceTypeNames,
                    StatExtensions.Names()),
            UniqueParameterType.Belief =>
                ToLinkedSet(ruleset.Beliefs.Keys),
            UniqueParameterType.Event =>
                ToLinkedSet(ruleset.Events.Keys),
            UniqueParameterType.Technology =>
                ToLinkedSet(ruleset.Technologies.Keys),
            UniqueParameterType.TechFilter =>
                UnionWith(
                    type.StaticKnownValues(),
                    ruleset.Technologies.Keys,
                    UniqueParameterType.EraFilter.GetKnownValuesForAutocomplete(ruleset)),
            UniqueParameterType.Specialist => ToLinkedSet(ruleset.Specialists.Keys),
            UniqueParameterType.GreatWork => ToLinkedSet(ruleset.GreatWorks.Keys),
            UniqueParameterType.Policy =>
                ToLinkedSet(ruleset.Policies.Keys),
            UniqueParameterType.PolicyFilter =>
                UnionWith(
                    type.StaticKnownValues(),
                    ruleset.Policies.Keys,
                    ruleset.PolicyBranches.Keys.Select(it => $"[{it}] branch")),
            UniqueParameterType.VictoryT =>
                ToLinkedSet(ruleset.Victories.Keys),
            UniqueParameterType.UnitNameGroup =>
                ToLinkedSet(ruleset.UnitNameGroups.Keys),
            _ => type.StaticKnownValues(),
        };

    public static UniqueParameterType? SafeValueOf(string param)
    {
        foreach (var entry in Entries)
        {
            if (entry.ParameterName() == param)
                return entry;
        }
        return null;
    }

    private static UniqueParameterErrorSeverity? GetErrorSeverityForFilter(
        this UniqueParameterType type,
        string parameterText,
        IRulesetParameterView ruleset)
    {
        bool isKnown = MultiFilter.Filter(parameterText, it => type.IsKnownValue(it, ruleset), forUniqueValidityTests: true);
        if (isKnown) return null;
        return UniqueParameterErrorSeverity.PossibleFilteringUnique;
    }

    private static UniqueParameterErrorSeverity? GetErrorSeverityViaKnownValue(
        this UniqueParameterType type,
        string parameterText,
        IRulesetParameterView ruleset) =>
        type.IsKnownValue(parameterText, ruleset) ? null : type.SeverityDefault();

    private static UniqueParameterErrorSeverity? GetInvariantSeverityUnless(bool predicate) =>
        predicate ? null : UniqueParameterErrorSeverity.RulesetInvariant;

    private static bool IsFilteringUniqueIn(string parameterText, IRulesetMapView<IRulesetObjectParameterView> map) =>
        map.Values.Any(item => item.Uniques.Contains(parameterText));

    private static bool IsKnownModName(string parameterText)
    {
        foreach (var baseRuleset in Enum.GetValues<BaseRuleset>())
        {
            if (baseRuleset.FullName() == parameterText)
                return true;
        }
        if (parameterText is "*Civ V -*" or "*Civ V - *")
            return true;
        if (parameterText.Contains('-'))
            return false;
        return true;
    }

    /// <summary>
    /// Ported from <c>Suppression.isValidFilter</c> in <c>core/src/com/unciv/models/ruleset/validation/Suppression.kt</c>.
    /// </summary>
    public static bool IsValidSuppressionFilter(string parameterText)
    {
        if (parameterText.Contains('{') || parameterText.Contains('<'))
            return false;
        if (HasCommonSubstringLength(parameterText, DeprecationWarningPattern, MinimumSuppressionSelectivity))
            return false;
        if (HasCommonSubstringLength(parameterText, UntypedWarningPattern, MinimumSuppressionSelectivity))
            return false;
        if (parameterText.StartsWith('*') != parameterText.EndsWith('*'))
            return false;
        if (parameterText.Length < MinimumSuppressionSelectivity + 2)
            return false;
        return true;
    }

    private static bool HasCommonSubstringLength(string x, string y, int minCommonLength)
    {
        int xLimit = x.Length - minCommonLength;
        int yLimit = y.Length - minCommonLength;
        for (int xIndex = 0; xIndex < xLimit; xIndex++)
        {
            ReadOnlySpan<char> xSub = x.AsSpan(xIndex);
            for (int yIndex = 0; yIndex < yLimit; yIndex++)
            {
                ReadOnlySpan<char> ySub = y.AsSpan(yIndex);
                if (CommonPrefixLengthIgnoreCase(xSub, ySub) >= minCommonLength)
                    return true;
            }
        }
        return false;
    }

    private static int CommonPrefixLengthIgnoreCase(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        int limit = System.Math.Min(a.Length, b.Length);
        for (int i = 0; i < limit; i++)
        {
            char ca = a[i];
            char cb = b[i];
            if (ca == cb) continue;
            char ua = char.ToUpperInvariant(ca);
            char ub = char.ToUpperInvariant(cb);
            if (ua == ub || char.ToLowerInvariant(ua) == char.ToLowerInvariant(ub))
                continue;
            return i;
        }
        return limit;
    }

    private static bool IsKnownCivilopediaLink(string parameterText)
    {
        int firstSlash = parameterText.IndexOf('/');
        if (firstSlash <= 0 || firstSlash == parameterText.Length - 1)
            return false;
        return parameterText.IndexOf('/', firstSlash + 1) < 0;
    }

    private static bool IsKnownLeaderTitle(string parameterText)
    {
        var parameters = parameterText.GetPlaceholderParameters();
        return parameters.Count == 1 && parameters[0] == "leaderName";
    }

    /// <summary>
    /// Matches Kotlin's <c>String.toIntOrNull()</c> (radix 10): optional leading '+' or '-',
    /// no whitespace, Unicode decimal digits via <see cref="CharUnicodeInfo.GetDecimalDigitValue(char)"/>,
    /// exact 32-bit signed integer bounds.
    /// </summary>
    public static int? ToIntOrNull(string text)
    {
        int length = text.Length;
        if (length == 0) return null;

        int start;
        bool isNegative;
        int limit;
        char firstChar = text[0];
        if (firstChar < '0')
        {
            if (length == 1) return null;
            start = 1;
            if (firstChar == '-')
            {
                isNegative = true;
                limit = int.MinValue;
            }
            else if (firstChar == '+')
            {
                isNegative = false;
                limit = -int.MaxValue;
            }
            else
            {
                return null;
            }
        }
        else
        {
            start = 0;
            isNegative = false;
            limit = -int.MaxValue;
        }

        int multmin = limit / 10;
        int result = 0;
        for (int i = start; i < length; i++)
        {
            char c = text[i];
            int digit;
            if (c is >= '0' and <= '9')
            {
                digit = c - '0';
            }
            else if (char.GetUnicodeCategory(c) == UnicodeCategory.DecimalDigitNumber)
            {
                digit = CharUnicodeInfo.GetDecimalDigitValue(c);
                if (digit is < 0 or >= 10) return null;
            }
            else
            {
                return null;
            }

            if (result < multmin) return null;
            result *= 10;
            if (result < limit + digit) return null;
            result -= digit;
        }

        return isNegative ? result : -result;
    }

    /// <summary>
    /// Matches Kotlin's <c>String.toFloatOrNull()</c> on the JVM (<c>ScreenFloatValueRegEx</c> + <c>Float.parseFloat</c>).
    /// </summary>
    public static float? ToFloatOrNull(string text)
    {
        // Trim ASCII whitespace/control characters <= 0x20, matching Java's FloatingDecimal.readJavaFormatString
        int start = 0;
        int end = text.Length;
        while (start < end && text[start] <= ' ') start++;
        while (end > start && text[end - 1] <= ' ') end--;
        if (start >= end) return null;

        ReadOnlySpan<char> span = text.AsSpan(start, end - start);
        if (!IsValidJavaFloatFormat(span))
            return null;

        if (span is "NaN" or "+NaN" or "-NaN")
            return float.NaN;
        if (span is "Infinity" or "+Infinity")
            return float.PositiveInfinity;
        if (span is "-Infinity")
            return float.NegativeInfinity;

        // Strip optional trailing f/F/d/D suffix accepted by Java Float.parseFloat
        if (span[^1] is 'f' or 'F' or 'd' or 'D')
            span = span[..^1];

        if (IsHexFloatBody(StripSign(span)))
            return ParseHexFloat(span);

        if (float.TryParse(
                span,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out float value))
        {
            return value;
        }

        return null;
    }

    private static bool IsValidJavaFloatFormat(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty) return false;
        if (span[0] is '+' or '-')
            span = span[1..];
        if (span.IsEmpty) return false;

        if (span is "NaN" or "Infinity")
            return true;

        if (span[^1] is 'f' or 'F' or 'd' or 'D')
            span = span[..^1];
        if (span.IsEmpty) return false;

        if (IsHexFloatBody(span))
            return IsValidHexFloat(span[2..]);

        int expIdx = span.IndexOfAny('e', 'E');
        ReadOnlySpan<char> mantissa = expIdx >= 0 ? span[..expIdx] : span;
        if (expIdx >= 0)
        {
            ReadOnlySpan<char> exp = span[(expIdx + 1)..];
            if (!exp.IsEmpty && exp[0] is '+' or '-')
                exp = exp[1..];
            if (exp.IsEmpty) return false;
            foreach (char c in exp)
            {
                if (c is < '0' or > '9') return false;
            }
        }

        int dotIdx = mantissa.IndexOf('.');
        bool hasDigit = false;
        for (int i = 0; i < mantissa.Length; i++)
        {
            if (i == dotIdx) continue;
            if (mantissa[i] is < '0' or > '9') return false;
            hasDigit = true;
        }
        return hasDigit;
    }

    private static ReadOnlySpan<char> StripSign(ReadOnlySpan<char> span) =>
        !span.IsEmpty && span[0] is '+' or '-' ? span[1..] : span;

    /// <summary>Java's hex float literal prefix: <c>0x</c> / <c>0X</c> (sign and f/d suffix already removed).</summary>
    private static bool IsHexFloatBody(ReadOnlySpan<char> span) =>
        span.Length > 2 && span[0] == '0' && span[1] is 'x' or 'X';

    /// <summary>
    /// Kotlin's <c>ScreenFloatValueRegEx</c> hex branch: <c>HexDigits [.]</c> or <c>[HexDigits] . HexDigits</c>,
    /// then a mandatory <c>p</c>/<c>P</c> binary exponent with optional sign and decimal digits.
    /// </summary>
    private static bool IsValidHexFloat(ReadOnlySpan<char> body)
    {
        int pIdx = body.IndexOfAny('p', 'P');
        if (pIdx < 0) return false;

        ReadOnlySpan<char> exponent = StripSign(body[(pIdx + 1)..]);
        if (exponent.IsEmpty) return false;
        foreach (char c in exponent)
        {
            if (c is < '0' or > '9') return false;
        }

        bool hasDot = false;
        bool hasDigit = false;
        foreach (char c in body[..pIdx])
        {
            if (c == '.')
            {
                if (hasDot) return false;
                hasDot = true;
            }
            else if (char.IsAsciiHexDigit(c))
            {
                hasDigit = true;
            }
            else
            {
                return false;
            }
        }
        return hasDigit;
    }

    /// <summary>
    /// Value of an already validated hex float (optional sign, <c>0x</c>, no suffix).
    /// Exact for mantissas up to 53 significant bits; only validity matters to the engine.
    /// </summary>
    private static float ParseHexFloat(ReadOnlySpan<char> span)
    {
        bool isNegative = !span.IsEmpty && span[0] == '-';
        ReadOnlySpan<char> body = StripSign(span)[2..];
        int pIdx = body.IndexOfAny('p', 'P');

        double mantissa = 0;
        int fractionDigits = 0;
        bool afterDot = false;
        foreach (char c in body[..pIdx])
        {
            if (c == '.')
            {
                afterDot = true;
                continue;
            }
            mantissa = mantissa * 16 + Convert.ToInt32(c.ToString(), 16);
            if (afterDot) fractionDigits++;
        }

        ReadOnlySpan<char> exponentText = body[(pIdx + 1)..];
        bool exponentNegative = exponentText[0] == '-';
        long exponent = 0;
        foreach (char c in StripSign(exponentText))
        {
            exponent = System.Math.Min(exponent * 10 + (c - '0'), 100_000);
        }
        if (exponentNegative) exponent = -exponent;

        int binaryExponent = (int)(exponent - 4L * fractionDigits);
        float result = (float)System.Math.ScaleB(mantissa, binaryExponent);
        return isNegative ? -result : result;
    }

    private static IReadOnlySet<string> SetOf(params string[] items) =>
        new LinkedHashSet<string>(items);

    private static IReadOnlySet<string> ToLinkedSet(IEnumerable<string> items) =>
        items as LinkedHashSet<string> ?? new LinkedHashSet<string>(items);

    private static IReadOnlySet<string> UnionWith(params IEnumerable<string>[] sequences)
    {
        var result = new LinkedHashSet<string>();
        foreach (var seq in sequences)
        {
            foreach (var item in seq)
                result.Add(item);
        }
        return result;
    }
}
