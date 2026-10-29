using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components;
using Unciv.UI.Components.Extensions;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.Models.Ruleset.Tech;

// core/src/com/unciv/models/ruleset/tech/Era.kt
// matchesSingleFilter reads only the state's gameInfo: Kotlin's
// fallback to the client global UncivGame.Current.gameInfo does not exist here, so without a state it is false like Kotlin without a loaded game.
public class Era : RulesetObject, ICivilopediaText
{
    private static bool IsEraConditional(UniqueType type) =>
        type is UniqueType.ConditionalBeforeEra or UniqueType.ConditionalDuringEra or UniqueType.ConditionalStartingFromEra or UniqueType.ConditionalIfStartingInEra;

    public override string MakeLink() => $"Era/{Name}";

    public FormattedLine? GetCivilopediaTextHeader() => new(Name, header: 2, color: GetHexColor());

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        return FormattedLineListBuilder.BuildCivilopediaText(context, b =>
        {
            b.Add($"Embarked strength: [{KotlinNumberText.Int(EmbarkDefense)}]{Fonts.Strength}");
            b.Add($"Base unit buy cost: [{KotlinNumberText.Int(BaseUnitBuyCost)}]{Fonts.Gold}");
            b.Add($"Research agreement cost: [{KotlinNumberText.Int(ResearchAgreementCost)}]{Fonts.Gold}");
            b.Space();
            b.AddObjects(ruleset.Technologies.Values.Where(it => it.Era() == Name));
            b.AddUniques(this);
            b.AddSeeAlso(GetEraGatedObjects(ruleset));
        });
    }

    public int GetSortGroup(Ruleset ruleset) => EraNumber;

    private IEnumerable<IRulesetObject> GetEraGatedObjects(Ruleset ruleset)
    {
        var policyBranches = ruleset.PolicyBranches.Values.Where(it => it.Era == Name).Cast<IRulesetObject>();
        // This second part is empty in our base rulesets, yes
        var gated = ruleset.AllRulesetObjects()
            .SelectMany(obj => ((IHasUniques)obj).GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState).Select(unique => (Obj: obj, Unique: unique)))
            .Where(it => it.Unique.Modifiers.Any(modifier => modifier.Type is { } type && IsEraConditional(type)))
            .Select(it => it.Obj)
            .Distinct();
        return policyBranches.Concat(gated);
    }

    public List<string> GetStartingUnits(Ruleset ruleset)
    {
        var startingUnits = new List<string>();
        var startingSettlerName =
            ruleset.Units.ContainsKey(StartingSettlerUnit) ? StartingSettlerUnit
            : ruleset.Units.Values.FirstOrDefault(it => it.IsCityFounder())?.Name
                ?? throw new UncivShowableException($"No Settler unit found for era {Name}");
        var startingWorkerName =
            StartingWorkerCount == 0 || ruleset.Units.ContainsKey(StartingWorkerUnit) ? StartingWorkerUnit
            : ruleset.Units.Values.FirstOrDefault(it => ((IHasUniques)it).HasUnique(UniqueType.BuildImprovements))?.Name
                ?? throw new UncivShowableException($"No Worker unit found for era {Name}");
        for (var i = 0; i < StartingSettlerCount; i++) startingUnits.Add(startingSettlerName);
        for (var i = 0; i < StartingWorkerCount; i++) startingUnits.Add(startingWorkerName);
        for (var i = 0; i < StartingMilitaryUnitCount; i++) startingUnits.Add(StartingMilitaryUnit);
        return startingUnits;
    }

    public int EraNumber { get; set; } = -1;
    public int ResearchAgreementCost { get; set; } = 300;
    public int StartingSettlerCount { get; set; } = 1;
    /// <summary>For mods which have differently named settlers</summary>
    public string StartingSettlerUnit { get; set; } = "Settler";
    public int StartingWorkerCount { get; set; }
    public string StartingWorkerUnit { get; set; } = "Worker";
    public int StartingMilitaryUnitCount { get; set; } = 1;
    public string StartingMilitaryUnit { get; set; } = "Warrior";
    public int StartingGold { get; set; }
    public int StartingCulture { get; set; }
    public int SettlerPopulation { get; set; } = 1;
    public List<string> SettlerBuildings { get; set; } = [];
    public List<string> StartingObsoleteWonders { get; set; } = [];
    public int BaseUnitBuyCost { get; set; } = 200;
    public int EmbarkDefense { get; set; } = 3;
    public int StartPercent { get; set; }

    /// <summary>B020 (bnw-spec 4.1.4): the era's share of a domestic food route, hundredths of a food point added to the 300 base.</summary>
    public int TradeRouteFoodBonusTimes100 { get; set; }

    /// <summary>B020 (bnw-spec 4.1.4): the production counterpart of <see cref="TradeRouteFoodBonusTimes100"/>.</summary>
    public int TradeRouteProductionBonusTimes100 { get; set; }

    /// <summary>B023 (bnw-spec 4.12, Eras.WarmongerPercent): the share of a warmongering penalty an aggressor of this era incurs; 100 in G&amp;K.</summary>
    public int WarmongerPercent { get; set; } = 100;

    public string CitySound { get; set; } = "cityClassical";

    public Dictionary<string, List<string>> FriendBonus { get; set; } = [];
    public Dictionary<string, List<string>> AllyBonus { get; set; } = [];

    public List<int>? IconRGB { get; set; }

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Era;

    public EngineColor GetColor()
    {
        if (IconRGB == null) return EngineColor.White.Cpy();
        return ColorExtensions.ColorFromRGB(IconRGB[0], IconRGB[1], IconRGB[2]);
    }

    public string GetHexColor() => "#" + GetColor().ToString()[..6];

    /// <summary>Implements <see cref="UniqueParameterType.EraFilter"/></summary>
    public bool MatchesFilter(string filter, GameContext? state = null, bool multiFilter = true) =>
        multiFilter ? MultiFilter.Filter(filter, it => MatchesSingleFilter(it, state)) : MatchesSingleFilter(filter, state);

    // Kotlin removePrefix(prefix).removeSuffix("]")
    private static string RemoveBrackets(string filter, string prefix)
    {
        var rest = filter[prefix.Length..];
        return rest.EndsWith(']') ? rest[..^1] : rest;
    }

    public bool MatchesSingleFilter(string filter, GameContext? state = null)
    {
        if (filter == "any era" || filter == Name) return true;

        var gameInfo = state?.GameInfo;
        if (gameInfo == null) return false;
        if (filter == "Starting Era") return Name == gameInfo.GameParameters.StartingEra;
        if (filter.StartsWith("pre-[", StringComparison.Ordinal))
        {
            var targetEraName = RemoveBrackets(filter, "pre-[");
            return gameInfo.Ruleset.GetEraNumber(targetEraName) is { } targetNumber && EraNumber < targetNumber;
        }
        if (filter.StartsWith("post-[", StringComparison.Ordinal))
        {
            var targetEraName = RemoveBrackets(filter, "post-[");
            return gameInfo.Ruleset.GetEraNumber(targetEraName) is { } targetNumber && EraNumber > targetNumber;
        }
        return false;
    }
}
