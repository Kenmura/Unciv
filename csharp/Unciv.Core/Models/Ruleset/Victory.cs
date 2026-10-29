using System.Text.Json.Serialization;
using Unciv.Logic.Civilization;
using Unciv.Logic.Tourism;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Victory.kt
// Not ported (UI types): Milestone.getVictoryScreenButtons / getMilestoneButton (libGDX TextButtons).
// The members that read live game state (hasBeenCompletedBy, getFocus, the countable getters and the victory screen header text) were
// finished by T057 against the concrete Civilization; GameInfo is still its IGameInfoState seam.

public enum MilestoneType
{
    BuiltBuilding,
    BuildingBuiltGlobally,
    AddedSSPartsInCapital,
    DestroyAllPlayers,
    CaptureAllCapitals,
    CompletePolicyBranches,
    WorldReligion,
    WinDiplomaticVote,
    ScoreAfterTimeOut,
    MoreCountableThanEachPlayer,
    /// <summary>Reach an absolute amount of a countable - unlike <see cref="MoreCountableThanEachPlayer"/> this does not depend on the other players</summary>
    HaveCountable,
    /// <summary>Brave New World (B009): influential over every other living major civilization. Not part of the Kotlin source.</summary>
    InfluentialOverAllCivs,
    /// <summary>Brave New World (B009): win the World Leader vote of the World Congress (same check as <see cref="WinDiplomaticVote"/>; the vote itself is B063). Not part of the Kotlin source.</summary>
    WinWorldLeaderVote,
}

public static class MilestoneTypeExtensions
{
    /// <summary>Kotlin <c>MilestoneType.text</c></summary>
    public static string GetText(this MilestoneType type) => type switch
    {
        MilestoneType.BuiltBuilding => "Build [building]",
        MilestoneType.BuildingBuiltGlobally => "Anyone should build [building]",
        MilestoneType.AddedSSPartsInCapital => "Add all [comment] in capital",
        MilestoneType.DestroyAllPlayers => "Destroy all players",
        MilestoneType.CaptureAllCapitals => "Capture all capitals",
        MilestoneType.CompletePolicyBranches => "Complete [amount] Policy branches",
        MilestoneType.WorldReligion => "Become the world religion",
        MilestoneType.WinDiplomaticVote => "Win diplomatic vote",
        MilestoneType.ScoreAfterTimeOut => "Have highest score after max turns",
        MilestoneType.MoreCountableThanEachPlayer => "Have more [countable] than each player's [countable]",
        MilestoneType.HaveCountable => "Have at least [amount] [countable]",
        MilestoneType.InfluentialOverAllCivs => "Influential over all civilizations",
        MilestoneType.WinWorldLeaderVote => "Win the World Leader vote",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

public class Victory : RulesetObject, ICivilopediaText
{
    public enum CompletionStatus
    {
        Completed,
        Partially,
        Incomplete,
    }

    public enum FocusType
    {
        Production,
        Gold,
        Culture,
        Science,
        Faith,
        Military,
        CityStates,
        Score,
    }

    private readonly KotlinLazy<List<Milestone>> milestoneObjects;
    private readonly KotlinLazy<Counter<string>> requiredSpaceshipPartsAsCounter;

    public Victory()
    {
        milestoneObjects = new KotlinLazy<List<Milestone>>(() => Milestones.Select(it => new Milestone(it, this)).ToList());
        requiredSpaceshipPartsAsCounter = new KotlinLazy<Counter<string>>(() =>
        {
            var parts = new Counter<string>();
            foreach (string spaceshipPart in RequiredSpaceshipParts)
                parts.Add(spaceshipPart, 1);
            return parts;
        });
    }

    public string VictoryScreenHeader { get; set; } = "Do things to win!";
    public bool HiddenInVictoryScreen { get; set; }

    /// <summary>Things to do to win. Needs to be ordered, as the milestones are supposed to be obtained in a specific order</summary>
    public List<string> Milestones { get; set; } = [];

    [JsonIgnore]
    public List<Milestone> MilestoneObjects => milestoneObjects.Value;

    public List<string> RequiredSpaceshipParts { get; set; } = [];

    [JsonIgnore]
    public Counter<string> RequiredSpaceshipPartsAsCounter => requiredSpaceshipPartsAsCounter.Value;

    public string VictoryString { get; set; } = "Your civilization stands above all others! The exploits of your people shall be remembered until the end of civilization itself!";
    public string DefeatString { get; set; } = "You have been defeated. Your civilization has been overwhelmed by its many foes. But your people do not despair, for they know that one day you shall return - and lead them forward to victory!";

    public bool EnablesMaxTurns() => MilestoneObjects.Any(it => it.Type == MilestoneType.ScoreAfterTimeOut);

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Victory;
    public override string MakeLink() => $"Victory/{Name}";

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context)
    {
        var lines = new List<FormattedLine>
        {
            new(VictoryScreenHeader),
            new(extraImage: $"VictoryIllustrations/{Name}/Won", centered: true),
            new(),
        };
        lines.AddRange(MilestoneObjects.Select(it => it.GetFormattedLine()));
        this.UniquesToCivilopediaTextLines(lines, context);
        return lines;
    }
}

public class Milestone
{
    public Milestone(string uniqueDescription, Victory parentVictory)
    {
        UniqueDescription = uniqueDescription;
        ParentVictory = parentVictory;
        string placeholderText = uniqueDescription.GetPlaceholderText();
        foreach (var candidate in Enum.GetValues<MilestoneType>())
        {
            if (placeholderText != candidate.GetText().GetPlaceholderText()) continue;
            Type = candidate;
            break;
        }
        Params = uniqueDescription.GetPlaceholderParameters();
    }

    /// <summary>Kotlin <c>private val parentVictory</c>; internal because the deferred members read it (spaceship parts)</summary>
    internal Victory ParentVictory { get; }

    public string UniqueDescription { get; }
    public MilestoneType? Type { get; }
    public List<string> Params { get; }

    /// <summary><see cref="MilestoneType.HaveCountable"/>: the amount the countable must reach</summary>
    public int GetCountableToDo() => int.TryParse(Params[0], System.Globalization.NumberStyles.AllowLeadingSign,
        System.Globalization.CultureInfo.InvariantCulture, out int amount) ? amount : 0;

    private Counter<string> GetIncompleteSpaceshipParts(Civilization civInfo)
    {
        var incompleteSpaceshipParts = ParentVictory.RequiredSpaceshipPartsAsCounter.Clone();
        incompleteSpaceshipParts.Remove(civInfo.VictoryManager.CurrentsSpaceshipParts);
        return incompleteSpaceshipParts;
    }

    internal static int OriginalMajorCapitalsOwned(Civilization civInfo) => civInfo.Cities
        .Count(it => it.IsOriginalCapital && it.FoundingCivObject != null && it.FoundingCivObject.IsMajorCiv());

    internal static HashSet<Civilization> CivsWithPotentialCapitalsToOwn(IGameInfoState gameInfo)
    {
        // Capitals that still exist, even if the civ is dead
        var civsWithCapitals = gameInfo.GetCities().Where(it => it.IsOriginalCapital)
            .Select(it => it.FoundingCivObject.NotNull())
            .Where(it => it.IsMajorCiv()).ToHashSet();
        // If the civ is alive, they can still create a capital, so we need them as well
        var livingCivs = gameInfo.Civilizations.Where(it => it.IsMajorCiv() && !it.IsDefeated());
        civsWithCapitals.UnionWith(livingCivs);
        return civsWithCapitals;
    }

    /// <summary>
    /// Gets the percentage progress of one civilization's countable against another's countable.
    /// </summary>
    /// <returns>The progress percentage (0-100). When the result is &gt;100, it qualifies as "more than".</returns>
    /// <seealso cref="GetMoreCountableThanOtherCivRelevant"/>
    public float GetMoreCountableThanOtherCivPercent(Civilization civ, Civilization otherCiv)
    {
        int countable1 = Countables.GetCountableAmount(Params[0], new GameContext(civ)) ?? 0;
        int countable2 = Countables.GetCountableAmount(Params[1], new GameContext(otherCiv)) ?? 0;
        if (countable2 <= 0) // Protect against zero division and negative resulting percent
            return countable1 > countable2 ? 100.1f : 0f; // Extra .1 so that it qualifies as >100%
        return countable1 / (float)countable2 * 100f;
    }

    /// <summary>
    /// Determines whether or not the given Civilization is relevent for the More Countable Victory Type.
    /// </summary>
    /// <seealso cref="GetMoreCountableThanOtherCivPercent"/>
    public bool GetMoreCountableThanOtherCivRelevant(Civilization civ, Civilization otherCiv) =>
        !ReferenceEquals(civ, otherCiv) && otherCiv.IsMajorCiv() && otherCiv.IsAlive();

    /// <summary><see cref="MilestoneType.HaveCountable"/>: the amount the civilization has right now</summary>
    public int GetCountableDone(Civilization civInfo) =>
        Countables.GetCountableAmount(Params[1], new GameContext(civInfo)) ?? 0;

    public bool HasBeenCompletedBy(Civilization civInfo)
    {
        switch (Type ?? throw new NullReferenceException("Milestone has no type"))
        {
            case MilestoneType.BuiltBuilding:
                return civInfo.Cities.Any(it => it.CityConstructions.BuiltBuildings.Contains(Params[0]));
            case MilestoneType.AddedSSPartsInCapital:
                return GetIncompleteSpaceshipParts(civInfo).IsEmpty();
            case MilestoneType.DestroyAllPlayers:
                {
                    var aliveMajorCivs = civInfo.GameInfo.GetAliveMajorCivs();
                    return aliveMajorCivs.Count == 1 && ReferenceEquals(aliveMajorCivs[0], civInfo);
                }
            case MilestoneType.CaptureAllCapitals:
                return OriginalMajorCapitalsOwned(civInfo) == CivsWithPotentialCapitalsToOwn(civInfo.GameInfo).Count;
            case MilestoneType.CompletePolicyBranches:
                return civInfo.Policies.CompletedBranches.Count >= ParseInt(Params[0]);
            case MilestoneType.HaveCountable:
                return GetCountableDone(civInfo) >= GetCountableToDo();
            case MilestoneType.MoreCountableThanEachPlayer:
                {
                    var relevantCivs = civInfo.GameInfo.Civilizations.Where(it => GetMoreCountableThanOtherCivRelevant(civInfo, it)).ToList();
                    return relevantCivs.Count != 0 && relevantCivs.All(it => GetMoreCountableThanOtherCivPercent(civInfo, it) > 100f);
                }
            case MilestoneType.BuildingBuiltGlobally:
                return civInfo.GameInfo.GetCities().Any(it => it.CityConstructions.BuiltBuildings.Contains(Params[0]));
            case MilestoneType.WinDiplomaticVote:
            case MilestoneType.WinWorldLeaderVote:
                return civInfo.VictoryManager.HasEverWonDiplomaticVote;
            case MilestoneType.InfluentialOverAllCivs:
                return InfluenceRules.IsInfluentialOverAll(civInfo);
            case MilestoneType.ScoreAfterTimeOut:
                {
                    if (civInfo.GameInfo.Turns < civInfo.GameInfo.GameParameters.MaxTurns) return false;
                    // maxByOrNull: the first civilization with the maximal score
                    Civilization? best = null;
                    double bestScore = 0.0;
                    foreach (var civ in civInfo.GameInfo.GetAliveMajorCivs())
                    {
                        double score = civ.CalculateTotalScore();
                        if (best == null || score > bestScore)
                        {
                            best = civ;
                            bestScore = score;
                        }
                    }
                    return ReferenceEquals(civInfo, best);
                }
            case MilestoneType.WorldReligion:
                return civInfo.GameInfo.IsReligionEnabled()
                    && civInfo.ReligionManager.Religion != null
                    && civInfo.GameInfo.Civilizations
                        .Where(it => it.IsMajorCiv() && it.IsAlive())
                        .All(it => it.ReligionManager.IsMajorityReligionForCiv(civInfo.ReligionManager.Religion));
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static int ParseInt(string text) => int.Parse(text, System.Globalization.NumberStyles.AllowLeadingSign,
        System.Globalization.CultureInfo.InvariantCulture);

    public string GetVictoryScreenButtonHeaderText(bool completed, Civilization civInfo)
    {
        switch (Type ?? throw new NullReferenceException("Milestone has no type"))
        {
            case MilestoneType.BuildingBuiltGlobally:
            case MilestoneType.WinDiplomaticVote:
            case MilestoneType.WinWorldLeaderVote:
            case MilestoneType.ScoreAfterTimeOut:
            case MilestoneType.BuiltBuilding:
                return UniqueDescription;
            case MilestoneType.InfluentialOverAllCivs:
                {
                    int amountToDo = InfluenceRules.VictoryRivals(civInfo).Count;
                    int amountDone = completed ? amountToDo : InfluenceRules.InfluentialCount(civInfo);
                    if (civInfo.ShouldHideCivCount())
                        return $"{{{UniqueDescription}}} ({amountDone.Tr()}/?)";
                    return $"{{{UniqueDescription}}} ({amountDone.Tr()}/{amountToDo.Tr()})";
                }
            case MilestoneType.CompletePolicyBranches:
                {
                    string amountToDo = Params[0].Tr();
                    string amountDone = completed ? amountToDo : civInfo.GetCompletedPolicyBranchesCount().Tr();
                    return $"{{{UniqueDescription}}} ({amountDone}/{amountToDo})";
                }
            case MilestoneType.HaveCountable:
                {
                    int amountToDo = GetCountableToDo();
                    int amountDone = completed ? amountToDo : Math.Min(GetCountableDone(civInfo), amountToDo);
                    return $"{{{UniqueDescription}}} ({amountDone.Tr()}/{amountToDo.Tr()})";
                }
            case MilestoneType.CaptureAllCapitals:
                {
                    int amountToDo = CivsWithPotentialCapitalsToOwn(civInfo.GameInfo).Count;
                    int amountDone = completed ? amountToDo : OriginalMajorCapitalsOwned(civInfo);
                    if (civInfo.ShouldHideCivCount())
                        return $"{{{UniqueDescription}}} ({amountDone.Tr()}/?)";
                    return $"{{{UniqueDescription}}} ({amountDone.Tr()}/{amountToDo.Tr()})";
                }
            case MilestoneType.DestroyAllPlayers:
                {
                    int amountToDo = civInfo.GameInfo.Civilizations.Count(it => it.IsMajorCiv()) - 1; // Don't count yourself
                    int amountDone = completed
                        ? amountToDo
                        : amountToDo - civInfo.GameInfo.GetAliveMajorCivs().Count(it => !ReferenceEquals(it, civInfo));
                    if (civInfo.ShouldHideCivCount())
                        return $"{{{UniqueDescription}}} ({amountDone.Tr()}/?)";
                    return $"{{{UniqueDescription}}} ({amountDone.Tr()}/{amountToDo.Tr()})";
                }
            case MilestoneType.MoreCountableThanEachPlayer:
                {
                    var relevantCivs = civInfo.GameInfo.Civilizations.Where(it => GetMoreCountableThanOtherCivRelevant(civInfo, it)).ToList();
                    int amountToDo = relevantCivs.Count;
                    int amountDone = relevantCivs.Count(it => GetMoreCountableThanOtherCivPercent(civInfo, it) > 100f);
                    if (civInfo.ShouldHideCivCount())
                        return $"{{{UniqueDescription}}} ({amountDone.Tr()}/?)";
                    return $"{{{UniqueDescription}}} ({amountDone.Tr()}/{amountToDo.Tr()})";
                }
            case MilestoneType.AddedSSPartsInCapital:
                {
                    var completeSpaceshipParts = civInfo.VictoryManager.CurrentsSpaceshipParts;
                    var incompleteSpaceshipParts = ParentVictory.RequiredSpaceshipPartsAsCounter.Clone();
                    int amountToDo = incompleteSpaceshipParts.SumValues();
                    incompleteSpaceshipParts.Remove(completeSpaceshipParts);

                    int amountDone = amountToDo - incompleteSpaceshipParts.SumValues();

                    return $"{{{UniqueDescription}}} ({amountDone.Tr()}/{amountToDo.Tr()})";
                }
            case MilestoneType.WorldReligion:
                {
                    int amountToDo = civInfo.GameInfo.Civilizations.Count(it => it.IsMajorCiv() && it.IsAlive()) - 1; // Don't count yourself
                    var religion = civInfo.ReligionManager.Religion;
                    int amountDone;
                    if (completed) amountDone = amountToDo;
                    else if (religion == null) amountDone = 0;
                    else if (religion.IsPantheon()) amountDone = 1;
                    else amountDone = civInfo.GameInfo.Civilizations.Count(it =>
                        it.IsMajorCiv() && it.IsAlive() && it.ReligionManager.IsMajorityReligionForCiv(religion));
                    return $"{{{UniqueDescription}}} ({amountDone.Tr()}/{amountToDo.Tr()})";
                }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    /// <summary>Attempt to interpret the focus a countable-based milestone suggests, from the Countable type</summary>
    private static Victory.FocusType GetFocusFromCountable(string countableText, Ruleset ruleset)
    {
        var countable = Countables.GetMatching(countableText, ruleset);
        if (countable == Countables.Stats)
        {
            return StatExtensions.SafeValueOf(countableText) switch
            {
                Stat.Production => Victory.FocusType.Production,
                Stat.Food => Victory.FocusType.Production,
                Stat.Gold => Victory.FocusType.Gold,
                Stat.Science => Victory.FocusType.Science,
                Stat.Culture => Victory.FocusType.Culture,
                Stat.Happiness => Victory.FocusType.Gold,
                Stat.Faith => Victory.FocusType.Faith,
                _ => Victory.FocusType.Production,
            };
        }
        if (countable == Countables.Cities || countable == Countables.FilteredCities || countable == Countables.FilteredBuildings
            || countable == Countables.OwnedTiles) return Victory.FocusType.Production;
        if (countable == Countables.Units || countable == Countables.FilteredUnits) return Victory.FocusType.Military;
        if (countable == Countables.PolicyBranches || countable == Countables.FilteredPolicies) return Victory.FocusType.Culture;
        if (countable == Countables.TileResources || countable == Countables.TileFilterTiles) return Victory.FocusType.Production;
        return Victory.FocusType.Score;
    }

    public Victory.FocusType GetFocus(Civilization civInfo)
    {
        var ruleset = civInfo.GameInfo.Ruleset;
        switch (Type ?? throw new NullReferenceException("Milestone has no type"))
        {
            case MilestoneType.BuiltBuilding:
            case MilestoneType.BuildingBuiltGlobally:
                {
                    // Kotlin: `if (!civInfo.tech.isResearched(building)) Victory.Focus.Science` is an expression whose value is discarded
                    _ = ruleset.Buildings[Params[0]];
                    return Victory.FocusType.Production;
                }
            case MilestoneType.AddedSSPartsInCapital:
                {
                    // Kotlin: `if (constructions.any { !isResearched(it) }) Victory.Focus.Science` is an expression whose value is discarded
                    foreach (var part in GetIncompleteSpaceshipParts(civInfo).Keys.ToList())
                    {
                        if (ruleset.Buildings.ContainsKey(part)) _ = ruleset.Buildings[part];
                        else _ = ruleset.Units[part];
                    }
                    return Victory.FocusType.Production;
                }
            case MilestoneType.DestroyAllPlayers:
            case MilestoneType.CaptureAllCapitals:
                return Victory.FocusType.Military;
            case MilestoneType.CompletePolicyBranches:
                return Victory.FocusType.Culture;
            case MilestoneType.MoreCountableThanEachPlayer:
                return GetFocusFromCountable(Params[0], ruleset);
            case MilestoneType.HaveCountable:
                return GetFocusFromCountable(Params[1], ruleset);
            case MilestoneType.WinDiplomaticVote:
            case MilestoneType.WinWorldLeaderVote:
                return Victory.FocusType.CityStates;
            case MilestoneType.InfluentialOverAllCivs:
                return Victory.FocusType.Culture;
            case MilestoneType.ScoreAfterTimeOut:
                return Victory.FocusType.Score;
            case MilestoneType.WorldReligion:
                return Victory.FocusType.Faith;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public FormattedLine GetFormattedLine() => Type.NotNull() switch
    {
        // Kotlin note: links should be `Building/params[0]`, but then the Wonder links don't resolve correctly
        MilestoneType.BuiltBuilding => new FormattedLine(UniqueDescription, link: $"Wonder/{Params[0]}"),
        MilestoneType.BuildingBuiltGlobally => new FormattedLine(UniqueDescription, link: $"Wonder/{Params[0]}"),
        MilestoneType.WorldReligion => new FormattedLine(UniqueDescription, link: "Tutorials/Religion"),
        MilestoneType.CompletePolicyBranches => new FormattedLine(UniqueDescription, link: "Policies"),
        _ => new FormattedLine(UniqueDescription, starred: true),
    };
}
