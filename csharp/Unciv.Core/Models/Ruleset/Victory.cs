using System.Text.Json.Serialization;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Victory.kt
// Not ported yet, they need Civilization, GameInfo, Ruleset, Countables.getCountableAmount on live state, VictoryManager and the Civilopedia/UI types
// (T046 and later): Victory.getCivilopediaTextLines, and Milestone's hasBeenCompletedBy, getMoreCountableThanOtherCivPercent/Relevant,
// getCountableDone, getVictoryScreenButtonHeaderText, getVictoryScreenButtons, getFocus and getFormattedLine.

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
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

public class Victory : RulesetObject
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

    private readonly Lazy<List<Milestone>> milestoneObjects;
    private readonly Lazy<Counter<string>> requiredSpaceshipPartsAsCounter;

    public Victory()
    {
        milestoneObjects = new Lazy<List<Milestone>>(() => Milestones.Select(it => new Milestone(it, this)).ToList());
        requiredSpaceshipPartsAsCounter = new Lazy<Counter<string>>(() =>
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
    public string MakeLink() => $"Victory/{Name}";
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
}
