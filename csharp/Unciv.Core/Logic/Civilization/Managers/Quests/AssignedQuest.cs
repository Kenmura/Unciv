using System.Globalization;
using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Models.Ruleset;
using Unciv.Models.Translations;
using Unciv.Utils;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/quests/AssignedQuest.kt (T069). Stripped: the purity annotations and
// onClickAction (it only drives the GUI: GUI.resetToWorldScreen / setCenterPosition, client code).

public class AssignedQuest : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("questName")]
    public string QuestName { get; init; } = "";
    [JsonPropertyName("assigner")]
    public string Assigner { get; init; } = "";
    [JsonPropertyName("assignee")]
    public string Assignee { get; init; } = "";
    [JsonPropertyName("assignedOnTurn")]
    public int AssignedOnTurn { get; init; }
    [JsonPropertyName("data1")]
    public string Data1 { get; init; } = "";
    [JsonPropertyName("data2")]
    public string Data2 { get; init; } = "";

    /// <summary>Used in deserialization</summary>
    public AssignedQuest()
    {
    }

    public AssignedQuest(string questName, string assigner = "", string assignee = "", int assignedOnTurn = 0, string data1 = "", string data2 = "")
    {
        QuestName = questName;
        Assigner = assigner;
        Assignee = assignee;
        AssignedOnTurn = assignedOnTurn;
        Data1 = data1;
        Data2 = data2;
    }

    public AssignedQuest(IGameInfoState gameInfo, Quest quest, Civilization assigner, Civilization assignee, int assignedOnTurn = 0, string data1 = "", string data2 = "")
        : this(quest.Name, assigner.CivId, assignee.CivId, assignedOnTurn, data1, data2)
    {
        this.gameInfo = gameInfo;
        this.quest = quest;
        assignerCiv = assigner;
        assigneeCiv = assignee;
    }

    private Civilization? assignerCiv;
    [JsonIgnore]
    public Civilization AssignerCiv => assignerCiv ?? throw new InvalidOperationException("lateinit property assignerCiv has not been initialized");

    private Civilization? assigneeCiv;
    [JsonIgnore]
    public Civilization AssigneeCiv => assigneeCiv ?? throw new InvalidOperationException("lateinit property assigneeCiv has not been initialized");

    private IGameInfoState? gameInfo;
    private IGameInfoState GameInfo => gameInfo ?? throw new InvalidOperationException("lateinit property gameInfo has not been initialized");

    private Quest? quest;
    [JsonIgnore]
    public Quest Quest => quest ?? throw new InvalidOperationException("lateinit property quest has not been initialized");

    [JsonIgnore]
    public QuestName QuestNameInstance => Quest.QuestNameInstance;

    public AssignedQuest Clone() => new(QuestName, Assigner, Assignee, AssignedOnTurn, Data1, Data2);

    internal void SetTransients(IGameInfoState gameInfo, Quest? quest = null)
    {
        this.gameInfo = gameInfo;
        if (quest != null)
            this.quest = quest;
        else if (this.quest == null)
            this.quest = gameInfo.Ruleset.Quests[QuestName]; // Kotlin `!!`: a missing key throws
        assignerCiv ??= gameInfo.GetCivilization(Assigner);
        assigneeCiv ??= gameInfo.GetCivilization(Assignee);
    }

    public bool IsIndividual() => !IsGlobal();
    public bool IsGlobal() => Quest.IsGlobal();
    public bool DoesExpire() => Quest.Duration > 0;
    public bool IsExpired() => DoesExpire() && GetRemainingTurns() == 0;
    public int GetDuration() => KotlinMath.FloatToInt(GameInfo.Speed.Modifier * Quest.Duration);
    public int GetRemainingTurns() => Math.Max(AssignedOnTurn + GetDuration() - GameInfo.Turns, 0);
    public float GetInfluence() => Quest.Influence;

    public string GetDescription() => Quest.Description.FillPlaceholders(Data1);

    /// <summary>Debug visualization only</summary>
    public override string ToString() => quest == null ? "(uninitialized)"
        : string.Create(CultureInfo.InvariantCulture, $"{QuestName}({Quest.Type}, assigner={Assigner}, assignee={Assignee}, d1={Data1}, d2={Data2}, {UI.Components.Fonts.Turn}{AssignedOnTurn})");
}
