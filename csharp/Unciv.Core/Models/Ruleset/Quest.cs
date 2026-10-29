using System.Text.Json.Serialization;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Quest.kt
// Not ported yet: the IQuestImplementation that each QuestName delegates to (QuestManager logic, needs Civilization).

public enum QuestName
{
    None,
    Route,
    ClearBarbarianCamp,
    ConstructWonder,
    ConnectResource,
    GreatPerson,
    ConquerCityState,
    FindPlayer,
    FindNaturalWonder,
    GiveGold,
    PledgeToProtect,
    ContestCulture,
    ContestFaith,
    ContestTech,
    Invest,
    BullyCityState,
    DenounceCiv,
    SpreadReligion,
    TradeRoute,
}

public static class QuestNameExtensions
{
    public static string GetValue(this QuestName name) => name switch
    {
        QuestName.None => "",
        QuestName.Route => "Route",
        QuestName.ClearBarbarianCamp => "Clear Barbarian Camp",
        QuestName.ConstructWonder => "Construct Wonder",
        QuestName.ConnectResource => "Connect Resource",
        QuestName.GreatPerson => "Acquire Great Person",
        QuestName.ConquerCityState => "Conquer City State",
        QuestName.FindPlayer => "Find Player",
        QuestName.FindNaturalWonder => "Find Natural Wonder",
        QuestName.GiveGold => "Give Gold",
        QuestName.PledgeToProtect => "Pledge to Protect",
        QuestName.ContestCulture => "Contest Culture",
        QuestName.ContestFaith => "Contest Faith",
        QuestName.ContestTech => "Contest Technologies",
        QuestName.Invest => "Invest",
        QuestName.BullyCityState => "Bully City State",
        QuestName.DenounceCiv => "Denounce Civilization",
        QuestName.SpreadReligion => "Spread Religion",
        QuestName.TradeRoute => "Trade Route",
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>Kotlin <c>QuestName.find(value)</c>: the first entry with that <see cref="GetValue"/>, else <see cref="QuestName.None"/></summary>
    public static QuestName Find(string value)
    {
        foreach (var name in Enum.GetValues<QuestName>())
            if (name.GetValue() == value) return name;
        return QuestName.None;
    }
}

public enum QuestType
{
    Individual,
    Global,
}

/// <summary>
/// Holds all functionality relative to a quest.
/// This is **not** serialized in saves, only in the Ruleset.
/// Saves contain QuestManagers instead, which contain lists of AssignedQuest instances. These are matched to this Quest **by name**.
/// </summary>
public class Quest : INamed
{
    private readonly KotlinLazy<QuestName> questNameInstance;

    public Quest()
    {
        // lazy only ensures evaluation happens after deserialization
        questNameInstance = new KotlinLazy<QuestName>(() => QuestNameExtensions.Find(Name));
    }

    /// <summary>Unique identifier name of the quest, it is also shown. Must match a <see cref="QuestNameExtensions.GetValue"/> for the Quest to have any functionality.</summary>
    public string Name { get; set; } = "";

    [JsonIgnore]
    public QuestName QuestNameInstance => questNameInstance.Value;

    /// <summary>Description of the quest shown to players</summary>
    public string Description { get; set; } = "";

    /// <summary>Either Individual or Global</summary>
    public QuestType Type { get; set; } = QuestType.Individual;

    /// <summary>Influence reward gained on quest completion</summary>
    public float Influence { get; set; } = 40f;

    /// <summary>Maximum number of turns to complete the quest, 0 if there's no turn limit</summary>
    public int Duration { get; set; }

    /// <summary>Minimum number of Civilizations needed to start the quest. It is meaningful only for <see cref="QuestType.Global"/> quests.</summary>
    public int MinimumCivs { get; set; } = 1;

    /// <summary>
    /// Certain city states are more likely to give certain quests.
    /// This is based on both personality and city-state type.
    /// Both are mapped here as 'how much to multiply the weight of this quest for this kind of city-state'
    /// </summary>
    public Dictionary<string, float> WeightForCityStateType { get; set; } = [];

    /// <summary>Checks if <c>this</c> is a Global quest</summary>
    public bool IsGlobal() => Type == QuestType.Global;
    public bool IsIndividual() => !IsGlobal();
}
