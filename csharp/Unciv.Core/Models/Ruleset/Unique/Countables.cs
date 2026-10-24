using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Unciv.Models.Ruleset.Unique.Expressions;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unique;

/// <summary>Kotlin <c>@Deprecated(message, ReplaceWith(...), DeprecationLevel.ERROR)</c> on a <see cref="Countables"/> entry.</summary>
public sealed record CountableDeprecation(string Message, string ReplaceWith);

/// <summary>
///  Contains all knowledge about how to check and evaluate countable Unique parameters (<see cref="UniqueParameterType.Countable"/>).
///  Ported from core/src/com/unciv/models/ruleset/unique/Countables.kt.
///
///  Kotlin is an <c>enum class</c> whose entries override <c>matches</c>, <c>eval</c>, <c>getErrorSeverity</c> and
///  <c>getKnownValuesForAutocomplete</c>. C# has no such enum, so each entry is an instance of this class that holds the overrides as
///  delegates (null = "not overridden", which selects the Kotlin default). <see cref="Entries"/> is in declaration order.
///  Expansion instructions: see Countables.kt.
/// </summary>
/// <remarks>
/// Deviations: <c>Ruleset</c> is <see cref="IRulesetParameterView"/> (and <see cref="IGameContextRuleset"/> through the game state);
/// the autocomplete cache is keyed by ruleset identity in a <see cref="ConditionalWeakTable{TKey, TValue}"/> instead of leaking;
/// <see cref="GetDeprecationAnnotation"/> replaces the Java reflection on the enum field.
/// </remarks>
public sealed class Countables
{
    private readonly Func<string, bool>? matchesNoRuleset;
    private readonly Func<string, IRulesetParameterView, bool>? matchesRuleset;
    private readonly Func<string, GameContext, int?> eval;
    private readonly Func<string, IRulesetParameterView, UniqueParameterErrorSeverity?>? errorSeverity;
    private readonly Func<IRulesetParameterView, IReadOnlySet<string>>? knownValues;
    private readonly string? documentationHeaderOverride;
    private readonly string? exampleOverride;

    private Countables(
        string name,
        Func<string, GameContext, int?> eval,
        string text = "",
        string shortDocumentation = "",
        IReadOnlyList<string>? documentationStrings = null,
        string? documentationHeader = null,
        string? example = null,
        bool noPlaceholders = true,
        bool noPlaceholdersIsExplicit = false,
        Func<string, bool>? matches = null,
        Func<string, IRulesetParameterView, bool>? matchesWithRuleset = null,
        Func<string, IRulesetParameterView, UniqueParameterErrorSeverity?>? getErrorSeverity = null,
        Func<IRulesetParameterView, IReadOnlySet<string>>? getKnownValuesForAutocomplete = null,
        CountableDeprecation? deprecation = null)
    {
        Name = name;
        Text = text;
        this.eval = eval;
        ShortDocumentation = shortDocumentation;
        DocumentationStrings = documentationStrings ?? [];
        documentationHeaderOverride = documentationHeader;
        exampleOverride = example;
        matchesNoRuleset = matches;
        matchesRuleset = matchesWithRuleset;
        errorSeverity = getErrorSeverity;
        knownValues = getKnownValuesForAutocomplete;
        Deprecation = deprecation;
        PlaceholderText = text.GetPlaceholderText();
        MatchesWithRuleset = matchesWithRuleset != null;
        NoPlaceholders = noPlaceholdersIsExplicit ? noPlaceholders : !text.Contains('[', StringComparison.Ordinal);
    }

    /// <summary>Kotlin enum <c>name</c>.</summary>
    public string Name { get; }

    /// <summary>The "key" to recognize this countable. If not empty, it will be included in translations. Placeholders should match a <see cref="UniqueParameterType"/> by its parameter type.</summary>
    public string Text { get; }

    private string ShortDocumentation { get; }

    public IReadOnlyList<string> DocumentationStrings { get; }

    public string PlaceholderText { get; }

    public bool MatchesWithRuleset { get; }

    public bool NoPlaceholders { get; }

    /// <summary>True when this entry supplies its own <c>matches(text)</c> (the overload without a ruleset).</summary>
    public bool OverridesMatches => matchesNoRuleset != null;
    /// <summary>True when this entry supplies its own <c>matches(text, ruleset)</c>.</summary>
    public bool OverridesMatchesWithRuleset => matchesRuleset != null;
    /// <summary>True when this entry supplies its own <c>getErrorSeverity</c>.</summary>
    public bool OverridesGetErrorSeverity => errorSeverity != null;

    private CountableDeprecation? Deprecation { get; }

    public override string ToString() => Name;

    // Leave these in place only for the really simple cases
    public bool Matches(string parameterText) =>
        matchesNoRuleset != null ? matchesNoRuleset(parameterText)
        : NoPlaceholders ? parameterText == Text
        : parameterText.EqualsPlaceholderText(PlaceholderText);

    /// <summary>
    /// This indicates whether a parameter *is of this countable type*, not *whether its parameters are correct*.
    /// E.g. "[fakeBuilding] Buildings" is obviously a countable of type "[buildingFilter] Buildings", therefore matches will return true.
    /// But it has another problem, which is that the building filter is bad, so its getErrorSeverity will return "ruleset specific".
    /// </summary>
    public bool Matches(string parameterText, IRulesetParameterView ruleset) =>
        matchesRuleset != null && matchesRuleset(parameterText, ruleset);

    public int? Eval(string parameterText, GameContext gameContext) => eval(parameterText, gameContext);

    /// <summary>Needs to return the ENTIRE countable, not just parameters.</summary>
    public IReadOnlySet<string> GetKnownValuesForAutocomplete(IRulesetParameterView ruleset) =>
        knownValues != null ? knownValues(ruleset) : new LinkedHashSet<string>([Text]);

    /// <summary>Leave this only for Countables without any parameters - they can rely on <see cref="Matches(string)"/> having validated enough</summary>
    public UniqueParameterErrorSeverity? GetErrorSeverity(string parameterText, IRulesetParameterView ruleset) =>
        errorSeverity?.Invoke(parameterText, ruleset);

    public string DocumentationHeader =>
        documentationHeaderOverride ?? ("`" + Text + "`" + (ShortDocumentation.Length == 0 ? "" : " - " + ShortDocumentation));

    public string Example
    {
        get
        {
            if (exampleOverride != null) return exampleOverride;
            if (NoPlaceholders) return Text;
            var placeholderParams = Text.GetPlaceholderParameters()
                .Select(it => UniqueParameterTypeExtensions.SafeValueOf(it)?.DocExample())
                .Where(it => it != null).Select(it => it.NotNull()).ToArray();
            return Text.FillPlaceholders(placeholderParams);
        }
    }

    public CountableDeprecation? GetDeprecationAnnotation() => Deprecation;

    /// <summary>Joins a list with <c>,</c> while having an or for the last entry. Useful for the documentation headers.</summary>
    private static string NiceJoinList(IEnumerable<string> list)
    {
        string joined = "`" + string.Join("`, `", list) + "`";
        int index = joined.LastIndexOf("`, `", StringComparison.Ordinal);
        return joined[..index] + "` or `" + joined[(index + 4)..];
    }

    private static UniqueParameterErrorSeverity? GetTranslatedErrorSeverity(UniqueParameterType type, string parameterText, IRulesetParameterView ruleset) =>
        type.GetErrorSeverity(parameterText.GetPlaceholderParameters()[0], ruleset);

    /// <summary>Kotlin <c>sumOf { Int }</c>: wraps on overflow.</summary>
    private static int SumOf<T>(IEnumerable<T> source, Func<T, int> selector)
    {
        int sum = 0;
        foreach (var item in source) sum = unchecked(sum + selector(item));
        return sum;
    }

    private static IReadOnlySet<string> MapToSet(IEnumerable<string> source, Func<string, string> map) =>
        new LinkedHashSet<string>(source.Select(map));

    private static IReadOnlySet<string> EmptySet() => new LinkedHashSet<string>();

    // Since each filter matches a small subset of buildings, and there can be lots of cities with lots of buildings,
    // it's faster to *pre-check* the filter *once* to find matching buildings and then use that "list of matching buildings" for the count.
    // Like Kotlin, the cache is keyed by the filter alone (not by ruleset) and never invalidated.
    private static readonly ConcurrentDictionary<string, IReadOnlySet<string>> FilterToMatchingBuildingsCache = new(StringComparer.Ordinal);

    //region Entries
    public static readonly Countables Integer = new("Integer",
        documentationHeader: "Integer constant - any positive or negative integer number",
        matches: parameterText => UniqueParameterTypeExtensions.ToIntOrNull(parameterText) != null,
        eval: (parameterText, _) => UniqueParameterTypeExtensions.ToIntOrNull(parameterText),
        getKnownValuesForAutocomplete: _ => EmptySet(),
        example: "123");

    public static readonly Countables Turns = new("Turns", text: "turns", shortDocumentation: "Number of turns played",
        documentationStrings: ["Always starts at zero irrespective of game speed or start era"],
        eval: (_, gameContext) => gameContext.GameInfo?.Turns);

    public static readonly Countables Year = new("Year", text: "year", shortDocumentation: "The current year",
        documentationStrings: ["Depends on game speed or start era, negative for years BC"],
        eval: (_, gameContext) => gameContext.GameInfo?.GetYear(0));

    public static readonly Countables Cities = new("Cities", text: "Cities", shortDocumentation: "The number of cities the relevant Civilization owns",
        eval: (_, gameContext) => gameContext.CivInfo?.Cities.Count);

    public static readonly Countables Units = new("Units", text: "Units", shortDocumentation: "The number of units the relevant Civilization owns",
        eval: (_, gameContext) => gameContext.CivInfo?.GetCivUnitsSize());

    public static readonly Countables Stats = new("Stats",
        documentationHeader: $"Stat name ({NiceJoinList(StatExtensions.Names())})",
        documentationStrings: ["Gets the stat *reserve*, not the amount per turn (can be city stats or civilization stats, depending on where the unique is used)"],
        matches: StatExtensions.IsStat,
        eval: (parameterText, gameContext) =>
        {
            var relevantStat = StatExtensions.SafeValueOf(parameterText);
            if (relevantStat == null) return null;
            var city = gameContext.City;
            return city != null ? city.GetStatReserve(relevantStat.Value) : gameContext.CivInfo?.GetStatReserve(relevantStat.Value);
        },
        example: "Science",
        getKnownValuesForAutocomplete: _ => new LinkedHashSet<string>(StatExtensions.Names()));

    public static readonly Countables StatOrResourcePerTurn = new("StatOrResourcePerTurn", text: "[stat/resource] Per Turn",
        shortDocumentation: "The amount of a stat or resource gained per turn",
        documentationHeader: "Stat/Resource Per Turn",
        documentationStrings: ["Gets the amount of a stat or resource the civilization gains per turn"],
        matchesWithRuleset: (parameterText, ruleset) =>
        {
            if (!parameterText.StartsWith('[') || !parameterText.EndsWith("] Per Turn", StringComparison.Ordinal)) return false;
            var parameters = parameterText.GetPlaceholderParameters();
            if (parameters.Count == 0) return false;
            string param = parameters[0];
            return StatExtensions.IsStat(param) || ruleset.TileResources.ContainsKey(param); // second term = Countables.TileResources.matches
        },
        eval: (parameterText, gameContext) =>
        {
            var parameters = parameterText.GetPlaceholderParameters();
            if (parameters.Count == 0) return null;
            string param = parameters[0];
            var civ = gameContext.CivInfo;
            if (civ == null) return null;
            var city = gameContext.City;

            var stat = StatExtensions.SafeValueOf(param);
            if (stat != null)
            {
                if (city != null && stat.Value.IsCityWide())
                    return KotlinMath.FloatToInt(city.GetCurrentCityStat(stat.Value));
                float sum = 0f;
                foreach (float value in civ.GetStatMapForNextTurnValues(stat.Value)) sum += value;
                return KotlinMath.FloatToInt(sum);
            }

            // Kotlin: gameContext.gameInfo!!.ruleset.tileResources[param] ?: return null
            var resource = gameContext.GameInfo.NotNull().Ruleset.TileResources[param];
            if (resource == null) return null;
            if (city != null)
                return city.GetResourcesGeneratedByCityAmount(resource.Name);
            return civ.GetCivResourceSupplyAmount(resource.Name);
        },
        getErrorSeverity: (parameterText, ruleset) =>
        {
            var parameters = parameterText.GetPlaceholderParameters();
            if (parameters.Count == 0) return UniqueParameterErrorSeverity.RulesetInvariant;
            if (StatExtensions.IsStat(parameters[0]))
                return GetTranslatedErrorSeverity(UniqueParameterType.StatName, parameterText, ruleset);
            return GetTranslatedErrorSeverity(UniqueParameterType.Resource, parameterText, ruleset);
        },
        getKnownValuesForAutocomplete: ruleset => MapToSet(
            UniqueParameterType.StatName.GetKnownValuesForAutocomplete(ruleset)
                .Concat(UniqueParameterType.Resource.GetKnownValuesForAutocomplete(ruleset)),
            it => "[stat/resource] Per Turn".FillPlaceholders(it)),
        example: "[Culture] Per Turn");

    public static readonly Countables PolicyBranches = new("PolicyBranches", text: "Completed Policy branches",
        eval: (_, gameContext) => gameContext.CivInfo?.GetCompletedPolicyBranchesCount());

    public static readonly Countables FilteredCities = new("FilteredCities", text: "[cityFilter] Cities",
        eval: (parameterText, gameContext) =>
        {
            string filter = parameterText.GetPlaceholderParameters()[0];
            var cities = gameContext.CivInfo?.Cities;
            if (cities == null) return null;
            // Kotlin passes city.matchesFilter its default viewingCiv = city.civ: the "Your" filter needs it
            return cities.Count(it => it.MatchesFilter(filter, it.Civ));
        },
        getErrorSeverity: (parameterText, ruleset) => GetTranslatedErrorSeverity(UniqueParameterType.CityFilter, parameterText, ruleset));

    public static readonly Countables FilteredUnits = new("FilteredUnits", text: "[mapUnitFilter] Units",
        eval: (parameterText, gameContext) =>
        {
            string filter = parameterText.GetPlaceholderParameters()[0];
            var civ = gameContext.CivInfo;
            if (civ == null) return null;
            return civ.GetCivUnits().Count(it => it.MatchesFilter(filter));
        },
        getErrorSeverity: (parameterText, ruleset) => GetTranslatedErrorSeverity(UniqueParameterType.MapUnitFilter, parameterText, ruleset),
        getKnownValuesForAutocomplete: ruleset => MapToSet(ruleset.UnitTypes.Keys.Concat(ruleset.Units.Keys), it => $"[{it}] Units"));

    public static readonly Countables Carried = new("Carried", text: "Carried [mapUnitFilter] units",
        shortDocumentation: "The number of units being carried by this unit",
        documentationStrings: ["Only counts transported units matching the filter. For use with 'when number of' conditionals."],
        example: "Carried [Air] units",
        eval: (parameterText, gameContext) =>
        {
            var relevantUnit = gameContext.RelevantUnit;
            if (relevantUnit == null) return null;
            string filter = parameterText.GetPlaceholderParameters()[0];
            // Count transported units on the same tile matching the filter
            return relevantUnit.GetTile().AirUnits.Count(it => it.IsTransported && it.MatchesFilter(filter));
        },
        getErrorSeverity: (parameterText, ruleset) => GetTranslatedErrorSeverity(UniqueParameterType.MapUnitFilter, parameterText, ruleset),
        getKnownValuesForAutocomplete: ruleset => MapToSet(ruleset.UnitTypes.Keys.Concat(ruleset.Units.Keys), it => $"Carried [{it}] units"));

    public static readonly Countables FilteredBuildings = new("FilteredBuildings", text: "[buildingFilter] Buildings",
        eval: (parameterText, gameContext) =>
        {
            string filter = parameterText.GetPlaceholderParameters()[0];
            var cities = gameContext.CivInfo?.Cities;
            if (cities == null) return null;
            var ruleset = gameContext.GameInfo?.Ruleset;
            if (ruleset == null) return null;
            var matchingBuildings = FilterToMatchingBuildingsCache.GetOrAdd(filter,
                key => new HashSet<string>(ruleset.GetBuildingNamesMatchingFilter(key), StringComparer.Ordinal));
            return SumOf(cities, city =>
            {
                // Pure time optimization, intersecting both ways gives the same result
                var builtBuildings = city.BuiltBuildings;
                if (matchingBuildings.Count <= builtBuildings.Count)
                    return matchingBuildings.Count(it => builtBuildings.Contains(it));
                return builtBuildings.Count(it => matchingBuildings.Contains(it));
            });
        },
        getErrorSeverity: (parameterText, ruleset) => GetTranslatedErrorSeverity(UniqueParameterType.BuildingFilter, parameterText, ruleset),
        getKnownValuesForAutocomplete: _ => EmptySet());

    public static readonly Countables FilteredBuildingsByCivs = new("FilteredBuildingsByCivs", text: "[buildingFilter] Buildings by [civFilter] Civilizations",
        eval: (parameterText, gameContext) =>
        {
            var parameters = parameterText.GetPlaceholderParameters();
            string buildingFilter = parameters[0], civFilter = parameters[1];
            var civilizations = gameContext.GameInfo?.Civilizations;
            if (civilizations == null) return null;
            return SumOf(civilizations.Where(it => it.IsAlive() && it.MatchesFilter(civFilter, gameContext)),
                civ => SumOf(civ.Cities, city => city.CountBuiltBuildingsMatchingFilter(buildingFilter, gameContext)));
        },
        getErrorSeverity: (parameterText, ruleset) =>
        {
            var parameters = parameterText.GetPlaceholderParameters();
            return UniqueParameterType.BuildingFilter.GetErrorSeverity(parameters[0], ruleset)
                ?? UniqueParameterType.CivFilter.GetErrorSeverity(parameters[1], ruleset);
        },
        getKnownValuesForAutocomplete: _ => EmptySet());

    public static readonly Countables FilteredPopulation = new("FilteredPopulation", text: "[populationFilter] in [cityFilter] Cities",
        eval: (parameterText, gameContext) =>
        {
            var parameters = parameterText.GetPlaceholderParameters();
            string populationFilter = parameters[0], cityFilter = parameters[1];

            if (cityFilter == "in this city" && gameContext.City != null)
                return gameContext.City.GetPopulationFilterAmount(populationFilter);
            var cities = gameContext.CivInfo?.Cities;
            if (cities == null) return null;
            return SumOf(cities.Where(it => it.MatchesFilter(cityFilter, gameContext.CivInfo)),
                city => city.GetPopulationFilterAmount(populationFilter));
        },
        getErrorSeverity: (parameterText, ruleset) =>
        {
            var parameters = parameterText.GetPlaceholderParameters();
            return UniqueParameterType.PopulationFilter.GetErrorSeverity(parameters[0], ruleset)
                ?? UniqueParameterType.CityFilter.GetErrorSeverity(parameters[1], ruleset);
        },
        getKnownValuesForAutocomplete: _ => EmptySet());

    public static readonly Countables FilteredCitiesByCivs = new("FilteredCitiesByCivs", text: "[cityFilter] Cities of [civFilter] Civilizations",
        eval: (parameterText, gameContext) =>
        {
            var parameters = parameterText.GetPlaceholderParameters();
            string cityFilter = parameters[0], civFilter = parameters[1];
            var civilizations = gameContext.GameInfo?.Civilizations;
            if (civilizations == null) return null;
            return SumOf(civilizations.Where(it => it.IsAlive() && it.MatchesFilter(civFilter, gameContext)),
                civ => civ.Cities.Count(city => city.MatchesFilter(cityFilter, city.Civ)));
        },
        getErrorSeverity: (parameterText, ruleset) =>
        {
            var parameters = parameterText.GetPlaceholderParameters();
            return UniqueParameterType.CityFilter.GetErrorSeverity(parameters[0], ruleset)
                ?? UniqueParameterType.CivFilter.GetErrorSeverity(parameters[1], ruleset);
        },
        getKnownValuesForAutocomplete: _ => EmptySet());

    public static readonly Countables FilteredPolicies = new("FilteredPolicies", text: "Adopted [policyFilter] Policies",
        eval: (parameterText, gameContext) =>
        {
            string filter = parameterText.GetPlaceholderParameters()[0];
            var civ = gameContext.CivInfo;
            if (civ == null) return null;
            return civ.CountAdoptedPoliciesMatching(filter, gameContext);
        },
        getErrorSeverity: (parameterText, ruleset) => GetTranslatedErrorSeverity(UniqueParameterType.PolicyFilter, parameterText, ruleset),
        getKnownValuesForAutocomplete: ruleset => MapToSet(UniqueParameterType.PolicyFilter.GetKnownValuesForAutocomplete(ruleset),
            it => "Adopted [policyFilter] Policies".FillPlaceholders(it)));

    public static readonly Countables FilteredPoliciesByCivs = new("FilteredPoliciesByCivs", text: "Adopted [policyFilter] Policies by [civFilter] Civilizations",
        eval: (parameterText, gameContext) =>
        {
            var parameters = parameterText.GetPlaceholderParameters();
            string policyFilter = parameters[0], civFilter = parameters[1];
            var civilizations = gameContext.GameInfo?.Civilizations;
            if (civilizations == null) return null;
            return SumOf(civilizations.Where(it => it.IsAlive() && it.MatchesFilter(civFilter, gameContext)),
                civ => civ.CountAdoptedPoliciesMatching(policyFilter, gameContext));
        },
        getErrorSeverity: (parameterText, ruleset) =>
        {
            var parameters = parameterText.GetPlaceholderParameters();
            return UniqueParameterType.PolicyFilter.GetErrorSeverity(parameters[0], ruleset)
                ?? UniqueParameterType.CivFilter.GetErrorSeverity(parameters[1], ruleset);
        },
        getKnownValuesForAutocomplete: _ => EmptySet());

    public static readonly Countables FilteredTechnologies = new("FilteredTechnologies", text: "Researched [techFilter] Technologies",
        documentationStrings:
        [
            "Counts researched matching technologies for the relevant Civilization",
            "Repeatable technologies, like Future Tech, are only counted once",
        ],
        eval: (parameterText, gameContext) =>
        {
            var ruleset = gameContext.GameInfo?.Ruleset;
            if (ruleset == null) return null;
            var civ = gameContext.CivInfo;
            if (civ == null) return null;
            string filter = parameterText.GetPlaceholderParameters()[0];
            return civ.TechsResearched.Count(it => ruleset.TechnologyMatchesFilter(it, filter, gameContext) ?? false);
        },
        getErrorSeverity: (parameterText, ruleset) => GetTranslatedErrorSeverity(UniqueParameterType.TechFilter, parameterText, ruleset),
        getKnownValuesForAutocomplete: ruleset => MapToSet(UniqueParameterType.TechFilter.GetKnownValuesForAutocomplete(ruleset),
            it => "Researched [techFilter] Technologies".FillPlaceholders(it)));

    public static readonly Countables KnownCivs = new("KnownCivs", text: "Known [civFilter] Civilizations",
        shortDocumentation: "The number of other civilizations the relevant Civilization has met",
        documentationStrings: ["Counts only civilizations that are still alive, and never the civilization itself"],
        eval: (parameterText, gameContext) =>
        {
            string filter = parameterText.GetPlaceholderParameters()[0];
            var civInfo = gameContext.CivInfo;
            if (civInfo == null) return null;
            return civInfo.GetKnownCivs().Count(it => it.MatchesFilter(filter, gameContext));
        },
        getErrorSeverity: (parameterText, ruleset) => GetTranslatedErrorSeverity(UniqueParameterType.CivFilter, parameterText, ruleset),
        getKnownValuesForAutocomplete: ruleset => MapToSet(UniqueParameterType.CivFilter.GetKnownValuesForAutocomplete(ruleset),
            it => "Known [civFilter] Civilizations".FillPlaceholders(it)));

    public static readonly Countables RemainingCivs = new("RemainingCivs", text: "Remaining [civFilter] Civilizations",
        eval: (parameterText, gameContext) =>
        {
            string filter = parameterText.GetPlaceholderParameters()[0];
            var civilizations = gameContext.GameInfo?.Civilizations;
            if (civilizations == null) return null;
            return civilizations.Count(it => it.IsAlive() && it.MatchesFilter(filter, gameContext));
        },
        getErrorSeverity: (parameterText, ruleset) => GetTranslatedErrorSeverity(UniqueParameterType.CivFilter, parameterText, ruleset),
        getKnownValuesForAutocomplete: ruleset => MapToSet(UniqueParameterType.CivFilter.GetKnownValuesForAutocomplete(ruleset),
            it => "Remaining [civFilter] Civilizations".FillPlaceholders(it)));

    public static readonly Countables WorkedTilesCity = new("WorkedTilesCity", text: "Worked [tileFilter] Tiles in this city",
        eval: (parameterText, gameContext) =>
        {
            var city = gameContext.City;
            if (city == null) return null;
            string filter = parameterText.GetPlaceholderParameters()[0];
            return city.GetWorkedTiles().Count(it => it.MatchesFilter(filter, city.Civ));
        },
        getErrorSeverity: (parameterText, ruleset) => GetTranslatedErrorSeverity(UniqueParameterType.TileFilter, parameterText, ruleset),
        getKnownValuesForAutocomplete: _ => EmptySet());

    public static readonly Countables WorkedTiles = new("WorkedTiles", text: "Worked [tileFilter] Tiles",
        eval: (parameterText, gameContext) =>
        {
            var cities = gameContext.CivInfo?.Cities;
            if (cities == null) return null;
            string filter = parameterText.GetPlaceholderParameters()[0];
            return SumOf(cities, city => city.GetWorkedTiles().Count(it => it.MatchesFilter(filter, gameContext.CivInfo)));
        },
        getErrorSeverity: (parameterText, ruleset) => GetTranslatedErrorSeverity(UniqueParameterType.TileFilter, parameterText, ruleset),
        getKnownValuesForAutocomplete: _ => EmptySet());

    public static readonly Countables OwnedTiles = new("OwnedTiles", text: "Owned [tileFilter] Tiles",
        eval: (parameterText, gameContext) =>
        {
            string filter = parameterText.GetPlaceholderParameters()[0];
            var cities = gameContext.CivInfo?.Cities;
            if (cities == null) return null;
            return SumOf(cities, city => city.GetTiles().Count(it => it.MatchesFilter(filter, null)));
        },
        getErrorSeverity: (parameterText, ruleset) => GetTranslatedErrorSeverity(UniqueParameterType.TileFilter, parameterText, ruleset),
        getKnownValuesForAutocomplete: _ => EmptySet());

    public static readonly Countables TileFilterTiles = new("TileFilterTiles", text: "[tileFilter] Tiles",
        eval: (parameterText, gameContext) =>
        {
            string filter = parameterText.GetPlaceholderParameters()[0];
            var tileList = gameContext.GameInfo?.TileList;
            if (tileList == null) return null;
            return tileList.Count(it => it.MatchesFilter(filter, gameContext.CivInfo));
        },
        getErrorSeverity: (parameterText, ruleset) => GetTranslatedErrorSeverity(UniqueParameterType.TileFilter, parameterText, ruleset),
        getKnownValuesForAutocomplete: _ => EmptySet(),
        example: "[Desert] Tiles");

    public static readonly Countables TileResources = new("TileResources",
        documentationHeader: "Resource name - From [TileResources.json](Mod-file-structure/3-Map-related-JSON-files.md#tileresourcesjson)",
        documentationStrings:
        [
            "Can be city stats or civilization stats, depending on where the unique is used",
            "For example: If a unique is placed on a building, then the retrieved resources will be of the city. If placed on a policy, they will be of the civilization.",
            "This can make a difference for e.g. local resources, which are counted per city.",
        ],
        matchesWithRuleset: (parameterText, ruleset) => ruleset.TileResources.ContainsKey(parameterText),
        eval: (parameterText, gameContext) =>
        {
            var resource = gameContext.GameInfo?.Ruleset.TileResources[parameterText];
            if (resource == null) return null;
            var city = gameContext.City;
            return city != null ? city.GetAvailableResourceAmount(resource.Name) : gameContext.CivInfo?.GetResourceAmount(resource.Name);
        },
        example: "Iron",
        getKnownValuesForAutocomplete: ruleset => new LinkedHashSet<string>(ruleset.TileResources.Keys));

    public static readonly Countables TileResourcesByCivs = new("TileResourcesByCivs", text: "[resourceFilter] resource of [civFilter] Civilizations",
        eval: (parameterText, gameContext) =>
        {
            var parameters = parameterText.GetPlaceholderParameters();
            string resourceFilter = parameters[0], civFilter = parameters[1];
            var gameInfo = gameContext.GameInfo;
            var civilizations = gameInfo?.Civilizations;
            if (civilizations == null) return null;
            var ruleset = gameInfo.NotNull().Ruleset;
            var relevantCivs = civilizations.Where(it => it.IsAlive() && it.MatchesFilter(civFilter, gameContext)).ToList();
            return SumOf(ruleset.GetTileResourceNamesMatchingFilter(resourceFilter, gameContext),
                resourceName => SumOf(relevantCivs, civ => civ.GetResourceAmount(resourceName)));
        },
        getErrorSeverity: (parameterText, ruleset) =>
        {
            var parameters = parameterText.GetPlaceholderParameters();
            return UniqueParameterType.ResourceFilter.GetErrorSeverity(parameters[0], ruleset)
                ?? UniqueParameterType.CivFilter.GetErrorSeverity(parameters[1], ruleset);
        },
        getKnownValuesForAutocomplete: _ => EmptySet());

    /// <summary>Please leave this one in, it is tested against in CountableTests.testRulesetValidation.</summary>
    public static readonly Countables CityStates = new("CityStates", text: "City-States", shortDocumentation: "counts all undefeated city-states",
        deprecation: new CountableDeprecation("because it was never actually supported", "Remaining [City-State] Civilizations"),
        eval: (_, gameContext) =>
        {
            var civilizations = gameContext.GameInfo?.Civilizations;
            if (civilizations == null) return null;
            return civilizations.Count(it => it.IsAlive() && it.IsCityState);
        });

    public static readonly Countables EraNumber = new("EraNumber", text: "Era number", shortDocumentation: "Number of the era the current player is in",
        documentationStrings: ["Zero-based index of the Era in Eras.json."],
        eval: (_, gameContext) => gameContext.CivInfo?.GetEraNumber());

    public static readonly Countables GameSpeedModifier = new("GameSpeedModifier", text: "Speed modifier for [stat]",
        shortDocumentation: "A game speed modifier for a specific Stat, as percentage",
        documentationStrings:
        [
            "Chooses an appropriate field from the Speeds.json entry the player has chosen.",
            "It is returned multiplied by 100.",
            "Food and Happiness return the generic `modifier` field.",
            "Other fields like `goldGiftModifier` or `barbarianModifier` are not accessible with this Countable.",
        ],
        eval: (parameterText, gameContext) =>
        {
            var stat = StatExtensions.SafeValueOf(parameterText.GetPlaceholderParameters()[0]);
            if (stat == null) return null;
            var speed = gameContext.GameInfo?.Speed;
            if (speed == null) return null;
            float modifier = stat.Value switch
            {
                Stat.Gold => speed.GoldCostModifier,
                Stat.Production => speed.ProductionCostModifier,
                Stat.Food => speed.Modifier,
                Stat.Science => speed.ScienceCostModifier,
                Stat.Culture => speed.CultureCostModifier,
                Stat.Happiness => speed.Modifier,
                Stat.Faith => speed.FaithCostModifier,
                _ => throw new ArgumentOutOfRangeException(nameof(parameterText)),
            };
            return KotlinMath.FloatToInt(modifier * 100f);
        },
        getErrorSeverity: (parameterText, _) =>
            StatExtensions.IsStat(parameterText.GetPlaceholderParameters()[0]) ? null : UniqueParameterErrorSeverity.RulesetInvariant,
        getKnownValuesForAutocomplete: _ => MapToSet(StatExtensions.Entries.Select(it => it.ToString()),
            it => "Speed modifier for []".FillPlaceholders(it)));

    private static readonly Expressions.Expressions ExpressionEngine = new();

    public static readonly Countables Expression = new("Expression",
        noPlaceholders: false, noPlaceholdersIsExplicit: true,
        matchesWithRuleset: (parameterText, ruleset) => ExpressionEngine.Matches(parameterText, ruleset),
        eval: (parameterText, gameContext) => ExpressionEngine.Eval(parameterText, gameContext),
        getErrorSeverity: (parameterText, ruleset) => ExpressionEngine.GetErrorSeverity(parameterText, ruleset),
        getKnownValuesForAutocomplete: _ => EmptySet(),
        example: "[Iron] + 2",
        documentationHeader: "Evaluate expressions!",
        documentationStrings:
        [
            "Expressions support arbitrary math operations, and can include other countables, when surrounded by square brackets.",
            "For example, since `Cities` is a countable, and `[Melee] units` is a countable, " +
                "you can have something like: `([[Melee] units] + 1) / [Cities]` (the whitespace is optional but helps readability)",
            "Since on translation, the brackets are removed, the expression will be displayed as `(Melee units + 1) / Cities`",
            "Supported operations between 2 values are: " + string.Join(", ", BinaryOperators.Entries.Select(it => it.Symbol)),
            "Supported operations on 1 value are: " + string.Join(", ", UnaryOperators.Entries.Select(it => $"{it.Symbol} ({it.Description})")),
            "Supported functions:",
            .. Functions.Entries.Select(it =>
            {
                string functionParameters = string.Join(",", Enumerable.Repeat("expression", it.ArityRange.First));
                if (it.ArityRange.First != it.ArityRange.Last) functionParameters += ",...";
                return $" - `{it.Symbol}({functionParameters})`";
            }),
        ]);

    /// <summary>All entries in declaration order (Kotlin <c>Countables.entries</c>).</summary>
    public static readonly IReadOnlyList<Countables> Entries =
    [
        Integer, Turns, Year, Cities, Units, Stats, StatOrResourcePerTurn, PolicyBranches, FilteredCities, FilteredUnits, Carried,
        FilteredBuildings, FilteredBuildingsByCivs, FilteredPopulation, FilteredCitiesByCivs, FilteredPolicies, FilteredPoliciesByCivs,
        FilteredTechnologies, KnownCivs, RemainingCivs, WorkedTilesCity, WorkedTiles, OwnedTiles, TileFilterTiles, TileResources,
        TileResourcesByCivs, CityStates, EraNumber, GameSpeedModifier, Expression,
    ];
    //endregion

    public static Countables? GetMatching(string parameterText, IRulesetParameterView? ruleset)
    {
        foreach (var entry in Entries)
        {
            bool matches = entry.MatchesWithRuleset
                ? ruleset != null && entry.Matches(parameterText, ruleset)
                : entry.Matches(parameterText);
            if (matches) return entry;
        }
        return null;
    }

    public static int? GetCountableAmount(string parameterText, GameContext gameContext)
    {
        var ruleset = gameContext.GameInfo?.Ruleset;
        var countable = GetMatching(parameterText, ruleset);
        if (countable == null) return null;
        return countable.Eval(parameterText, gameContext);
    }

    public static bool IsKnownValue(string parameterText, IRulesetParameterView ruleset) =>
        GetMatching(parameterText, ruleset) != null;

    // This will "leak memory" in Kotlin if game rulesets are changed over application lifetime; the weak table does not.
    private static readonly ConditionalWeakTable<IRulesetParameterView, IReadOnlySet<string>> AutocompleteCache = new();

    /// <summary>Companion <c>getKnownValuesForAutocomplete(ruleset)</c> (renamed: C# cannot overload a static and an instance method alike).</summary>
    public static IReadOnlySet<string> GetAllKnownValuesForAutocomplete(IRulesetParameterView ruleset) =>
        AutocompleteCache.GetValue(ruleset, key =>
        {
            var acc = new LinkedHashSet<string>();
            foreach (var entry in Entries) acc.AddAll(entry.GetKnownValuesForAutocomplete(key));
            return acc;
        });

    /// <summary>Companion <c>getErrorSeverity(text, ruleset)</c> (renamed, see <see cref="GetAllKnownValuesForAutocomplete"/>).</summary>
    public static UniqueParameterErrorSeverity? GetErrorSeverityOfText(string parameterText, IRulesetParameterView ruleset)
    {
        var countable = GetMatching(parameterText, ruleset);
        if (countable == null) return UniqueParameterErrorSeverity.RulesetSpecific;
        return countable.GetErrorSeverity(parameterText, ruleset);
    }
}

/// <summary>Wires <see cref="Countables"/> into <see cref="UniqueParameterTypeExtensions.CountablesProvider"/> (the validation of <see cref="UniqueParameterType.Countable"/>).</summary>
internal sealed class CountablesParameterProvider : ICountablesParameterProvider
{
    public static readonly CountablesParameterProvider Instance = new();

    public bool IsKnownValue(string parameterText, IRulesetParameterView ruleset) => Countables.IsKnownValue(parameterText, ruleset);

    public IReadOnlySet<string> GetKnownValuesForAutocomplete(IRulesetParameterView ruleset) => Countables.GetAllKnownValuesForAutocomplete(ruleset);

    public UniqueParameterErrorSeverity? GetErrorSeverity(string parameterText, IRulesetParameterView ruleset) =>
        Countables.GetErrorSeverityOfText(parameterText, ruleset);
}
