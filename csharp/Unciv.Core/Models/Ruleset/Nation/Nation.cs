using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.UI.Components.Extensions;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Nation;

// core/src/com/unciv/models/ruleset/nation/Nation.kt
public class Nation : RulesetObject, ICivilopediaText
{
    public override string MakeLink() => $"Nation/{Name}";

    public int GetSortGroup(Ruleset ruleset) => IsCityState ? 1 : IsBarbarian ? 9 : 0;

    public string? GetSubCategory(Ruleset ruleset) => IsCityState ? "City-States" : IsBarbarian ? "Other" : "Civilizations";

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context) => this.GetNationCivilopediaTextLines(context);

    // region Serialized fields
    private List<int>? outerColor;
    private ImmutableColor outerColorObject = new(EngineColor.White);
    private ImmutableColor innerColorObject = new(EngineColor.Black);
    private readonly KotlinLazy<bool> isCityState;
    private readonly KotlinLazy<bool> isMajorCiv;
    private readonly KotlinLazy<bool> isBarbarian;
    private readonly KotlinLazy<bool> isSpectator;

    public Nation()
    {
        isCityState = new KotlinLazy<bool>(() => CityStateType != null);
        isBarbarian = new KotlinLazy<bool>(() => Name == Constants.Barbarians);
        isSpectator = new KotlinLazy<bool>(() => Name == Constants.Spectator);
        isMajorCiv = new KotlinLazy<bool>(() => !IsBarbarian && !IsCityState && !IsSpectator);
    }

    public string LeaderName { get; set; } = "";
    /// <summary>A Kotlin `val` that json still sets</summary>
    public string Style { get; set; } = "";
    public string? CityStateType { get; set; }
    public string PreferredVictoryType { get; set; } = Constants.NeutralVictoryType;

    // The following all have audio hooks to play corresponding leader voice clips - named <civName>.<fieldName>
    /// <summary>Shown for AlertType.WarDeclaration, when other Civs declare war on a player</summary>
    public string DeclaringWar { get; set; } = "";
    /// <summary>Shown in DiplomacyScreen when a player declares war</summary>
    public string Attacked { get; set; } = "";
    /// <summary>Shown for AlertType.Defeated</summary>
    public string Defeated { get; set; } = "";
    /// <summary>Shown for MajorCivDiplomacyTable.getDenounceButton</summary>
    public string Denounced { get; set; } = "";

    /// <summary>Popup message from the leader that issued a denouncement.</summary>
    public string NeutralDenouncing { get; set; } = "";
    /// <summary>Popup message from the leader that issued a denouncement, when the relationship is poor.</summary>
    public string HateDenouncing { get; set; } = "";
    /// <summary>Shown for Declaration of Friendship</summary>
    public string DeclaringFriendship { get; set; } = "";
    /// <summary>Popup message from the leader that accepted or rejected a demand</summary>
    public string AcceptingDemand { get; set; } = "";
    public string NeutralRejectingDemand { get; set; } = "";
    public string HateRejectingDemand { get; set; } = "";
    /// <summary>Shown for AlertType.FirstContact</summary>
    public string Introduction { get; set; } = "";
    /// <summary>Shown in TradePopup when other Civs initiate trade with a player</summary>
    public string TradeRequest { get; set; } = "";
    /// <summary>Shown in DiplomacyScreen when a player contacts another major civ with RelationshipLevel.Afraid or better</summary>
    public string NeutralHello { get; set; } = "";
    /// <summary>Shown in DiplomacyScreen when a player contacts another major civ with RelationshipLevel.Enemy or worse</summary>
    public string HateHello { get; set; } = "";

    /// <summary>Kotlin `lateinit var`</summary>
    public List<int> OuterColor
    {
        get => outerColor ?? throw new InvalidOperationException("lateinit property outerColor has not been initialized");
        set => outerColor = value;
    }

    public string UniqueName { get; set; } = "";
    public string UniqueText { get; set; } = "";
    public List<int>? InnerColor { get; set; }
    public List<string> StartBias { get; set; } = [];

    /// <summary>BNW <c>Civilization_Start_Place_First_Along_Ocean</c> (B024): in region assignment this civ picks its coastal start before the other coast-biased civs.</summary>
    public bool PlaceFirstAlongOcean { get; set; }
    public string? Personality { get; set; }

    public string StartIntroPart1 { get; set; } = "";
    public string StartIntroPart2 { get; set; } = "";

    public List<string> SpyNames { get; set; } = [];

    public string? FavoredReligion { get; set; }

    public List<string> Cities { get; set; } = [];
    // endregion

    // region Transients
    /// <summary>This is its own transient because we'll need to check this for every tile-to-tile movement which is harsh</summary>
    [JsonIgnore]
    public bool ForestsAndJunglesAreRoads { get; set; }

    /// <summary>Same for Inca unique</summary>
    [JsonIgnore]
    public bool IgnoreHillMovementCost { get; set; }

    [JsonIgnore]
    public bool IsCityState => isCityState.Value;
    [JsonIgnore]
    public bool IsMajorCiv => isMajorCiv.Value;
    [JsonIgnore]
    public bool IsBarbarian => isBarbarian.Value;
    [JsonIgnore]
    public bool IsSpectator => isSpectator.Value;
    // endregion

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Nation;

    // region API
    /// <summary>Retrieves a display name for the nation's leader, considering the provided title (untranslated), e.g. <c>[leaderName] the Great</c></summary>
    public string GetLeaderDisplayName(string title = "")
    {
        if (IsCityState || IsSpectator) return Name;
        if (title.Length == 0) return $"[{LeaderName}] of [{Name}]";
        return $"[{title.FillPlaceholders(LeaderName)}] of [{Name}]";
    }

    public string GetStyleOrCivName() => Style.Length == 0 ? Name : Style;

    public ImmutableColor GetOuterColor() => outerColorObject;
    public ImmutableColor GetInnerColor() => innerColorObject;

    /// <summary>Kotlin <c>getStartBias(ruleset, gameContext)</c>: the nation's own start bias, its StartBias uniques and those of its city state type, in that order.</summary>
    public LinkedHashSet<string> GetStartBias(Ruleset ruleset, GameContext? gameContext = null)
    {
        var context = gameContext ?? GameContext.IgnoreConditionalsState;
        var result = new LinkedHashSet<string>();
        result.AddAll(StartBias);
        foreach (var unique in UniqueMap.GetMatchingUniques(UniqueType.StartBias, context))
            result.Add(unique.Params[0]);
        if (CityStateType == null) return result;
        if (!ruleset.CityStateTypes.TryGetValue(CityStateType, out var type)) return result;
        foreach (var unique in type.UniqueMap.GetMatchingUniques(UniqueType.StartBias, context))
            result.Add(unique.Params[0]);
        return result;
    }

    public void SetTransients()
    {
        static ImmutableColor SafeColorFromRGB(List<int> rgb) => new(rgb.Count >= 3
            ? ColorExtensions.ColorFromRGB(rgb)
            : new EngineColor(0xa0 / 255f, 0x20 / 255f, 0xf0 / 255f, 1f)); // libGDX Color.PURPLE

        outerColorObject = SafeColorFromRGB(OuterColor);

        // ImageGetter.CHARCOAL = Color(0x111111FF)
        innerColorObject = InnerColor == null ? new ImmutableColor(ColorExtensions.ColorFromRGB(0x11, 0x11, 0x11))
                                              : SafeColorFromRGB(InnerColor);

        ForestsAndJunglesAreRoads = UniqueMap.HasUnique(UniqueType.ForestsAndJunglesAreRoads);
        IgnoreHillMovementCost = UniqueMap.HasUnique(UniqueType.IgnoreHillMovementCost);
    }

    public bool MatchesFilter(string filter, GameContext? state = null, bool multiFilter = true)
    {
        var self = (IHasUniques)this;
        bool Single(string it) =>
            MatchesSingleFilter(it) ||
            (state != null && self.HasTagUnique(it, state)) ||
            (state == null && self.HasTagUnique(it));
        return multiFilter ? MultiFilter.Filter(filter, Single) : Single(filter);
    }
    // endregion

    // region Helpers
    private bool MatchesSingleFilter(string filter)
    {
        switch (filter)
        {
            case "All":
            case "all":
                return true;
            case "Major":
                return IsMajorCiv;
            case Constants.CityStates:
            case "City-State":
                return IsCityState;
            default:
                return filter == Name;
        }
    }
    // endregion
}
