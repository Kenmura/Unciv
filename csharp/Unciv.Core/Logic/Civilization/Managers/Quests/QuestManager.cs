using System.Globalization;
using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Models.Translations;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Civilization.Managers;

// Namespace note (T069): the Kotlin package is ...managers.quests, but the files live in Unciv.Logic.Civilization.Managers so that
// Civilization.cs does not gain a 25th namespace dependency (CouplingRatchetTests).

// Ported from core/src/com/unciv/logic/civilization/managers/quests/QuestManager.kt (T069). Stripped: the purity annotations.
// - Kotlin HashMap fields are JavaHashMap (java.util iteration order). Loops that write to the map they iterate (decrementQuestCountdowns,
//   tryStartNewIndividualQuests) iterate a snapshot: only values of existing keys change, so the order and the values read are the same.
// - JustMet, BarbarianCampCleared, HandleIndividualQuests, GetInvestmentMultiplier, ReceivedGoldGift, CityStateBullied, CityStateConquered, WasAttackedBy and WantsDead are virtual only so that other managers' tests can observe the calls.
// - `assignedQuests.removeAll { ... }` with side effects in the predicate (handleIndividualQuests) is List.RemoveAll, which runs
//   the predicate once per element in order, as Kotlin's does.

public class QuestManager : IsPartOfGameInfoSerialization
{
    public const int UNSET = -1;

    public const int GLOBAL_QUEST_FIRST_POSSIBLE_TURN = 30;
    public const int INDIVIDUAL_QUEST_FIRST_POSSIBLE_TURN = 30;

    public const int GLOBAL_QUEST_FIRST_POSSIBLE_TURN_RAND = 20;
    public const int INDIVIDUAL_QUEST_FIRST_POSSIBLE_TURN_RAND = 20;

    public const int GLOBAL_QUEST_MIN_TURNS_BETWEEN = 40;
    public const int INDIVIDUAL_QUEST_MIN_TURNS_BETWEEN = 20;

    public const int GLOBAL_QUEST_RAND_TURNS_BETWEEN = 25;
    public const int INDIVIDUAL_QUEST_RAND_TURNS_BETWEEN = 25;

    public const int GLOBAL_QUEST_MAX_ACTIVE = 1;
    public const int INDIVIDUAL_QUEST_MAX_ACTIVE = 2;

    #region Serialized fields
    /// <summary>List of active quests, both global and individual ones</summary>
    [JsonPropertyName("assignedQuests")]
    [JsonInclude]
    private List<AssignedQuest> assignedQuests = [];

    /// <summary>Number of turns left before starting new global quest</summary>
    [JsonPropertyName("globalQuestCountdown")]
    [JsonInclude]
    private int globalQuestCountdown = UNSET;

    /// <summary>
    /// Number of turns left before this city state can start a new individual quest.
    /// Key is major civ name, value is turns to quest
    /// </summary>
    [JsonPropertyName("individualQuestCountdown")]
    [JsonInclude]
    private JavaHashMap<string, int> individualQuestCountdown = new();

    /// <summary>Target number of units to kill for this war, for war with major pseudo-quest</summary>
    [JsonPropertyName("unitsToKillForCiv")]
    [JsonInclude]
    private JavaHashMap<string, int> unitsToKillForCiv = new();

    /// <summary>For this attacker, number of units killed by each civ</summary>
    [JsonPropertyName("unitsKilledFromCiv")]
    [JsonInclude]
    private JavaHashMap<string, JavaHashMap<string, int>> unitsKilledFromCiv = new();
    #endregion

    /// <summary>T219: rebuild the hash tables like a load does, see <see cref="JavaHashMap{TKey,TValue}.Canonicalize"/>.</summary>
    internal void Canonicalize()
    {
        individualQuestCountdown.Canonicalize();
        unitsToKillForCiv.Canonicalize();
        foreach (var killed in unitsKilledFromCiv.Values) killed.Canonicalize();
        unitsKilledFromCiv.Canonicalize();
    }

    #region Transients, clone, etc
    private Civilization? civ;
    /// <summary>Civilization object holding and dispatching quests. Kotlin <c>@Transient lateinit</c>.</summary>
    private Civilization Civ => civ ?? throw new InvalidOperationException("lateinit property civ has not been initialized");

    /// <summary>Readability helper to access the Ruleset through <see cref="Civ"/></summary>
    private Ruleset Ruleset => Civ.GameInfo.Ruleset;

    public QuestManager Clone()
    {
        var toReturn = new QuestManager
        {
            globalQuestCountdown = globalQuestCountdown,
        };
        toReturn.individualQuestCountdown.PutAll(individualQuestCountdown);
        toReturn.assignedQuests.AddRange(assignedQuests.Select(it => it.Clone()));
        toReturn.unitsToKillForCiv.PutAll(unitsToKillForCiv);
        foreach (var (attacker, unitsKilled) in unitsKilledFromCiv)
            toReturn.unitsKilledFromCiv[attacker] = new JavaHashMap<string, int>(unitsKilled);
        return toReturn;
    }

    public void SetTransients(Civilization civ)
    {
        this.civ = civ;
        foreach (var quest in assignedQuests)
            quest.SetTransients(civ.GameInfo);
    }

    /// <summary>Debug visualization only</summary>
    public override string ToString() => civ == null ? "(uninitialized)"
        : $"{civ.CivId}:[{string.Join(", ", assignedQuests)}]";
    #endregion

    #region Public
    /// <summary>Returns true if <see cref="Civ"/> have active quests for <paramref name="challenger"/></summary>
    public bool HaveQuestsFor(Civilization challenger) => GetAssignedQuestsFor(challenger).Any();

    /// <summary>Access all assigned Quests for <paramref name="civName"/></summary>
    public IEnumerable<AssignedQuest> GetAssignedQuestsFor(string civName) =>
        assignedQuests.Where(it => it.Assignee == civName);

    /// <summary>Access all assigned Quests for <paramref name="civ"/></summary>
    public IEnumerable<AssignedQuest> GetAssignedQuestsFor(Civilization civ) =>
        assignedQuests.Where(it => it.AssigneeCiv == civ);

    /// <summary>Returns true if <see cref="Civ"/> has asked anyone to conquer <paramref name="target"/></summary>
    public virtual bool WantsDead(string target) => GetAssignedQuestsOfName(QuestName.ConquerCityState).Any(it => it.Data1 == target);

    /// <summary>Returns the influence multiplier for <paramref name="donor"/> from an Investment quest that <see cref="Civ"/> might have (assumes only one)</summary>
    public virtual float GetInvestmentMultiplier(Civilization donor)
    {
        var investmentQuest = GetAssignedQuestsOfName(QuestName.Invest).FirstOrDefault(it => it.AssigneeCiv == donor);
        if (investmentQuest == null) return 1f;
        return investmentQuest.Data1.ToPercent();
    }

    public void EndTurn()
    {
        if (Civ.IsDefeated())
        {
            assignedQuests.Clear();
            individualQuestCountdown.Clear();
            globalQuestCountdown = UNSET;
            return;
        }

        if (Civ.Cities.Count == 0) return; // don't assign quests until we have a city

        SeedGlobalQuestCountdown();
        SeedIndividualQuestsCountdowns();

        DecrementQuestCountdowns();

        HandleGlobalQuests();
        HandleIndividualQuests();

        TryStartNewGlobalQuest();
        TryStartNewIndividualQuests();

        TryBarbarianInvasion();
        TryEndWarWithMajorQuests();
    }

    public virtual void HandleIndividualQuests()
    {
        assignedQuests.RemoveAll(it => it.IsIndividual() && HandleIndividualQuest(it));
    }

    /// <summary>Remove all quests for <paramref name="civ"/> without side effects, for console <c>civ remove</c></summary>
    public void RemoveQuestsFor(Civilization civ)
    {
        RemoveAll(GetAssignedQuestsFor(civ).ToList());
        individualQuestCountdown.Remove(civ.CivId);
        unitsToKillForCiv.Remove(civ.CivId);
        unitsKilledFromCiv.Remove(civ.CivId);
        foreach (var killed in unitsKilledFromCiv.Values) killed.Remove(civ.CivId);
    }

    public void HandleObsoleteGlobalQuests()
    {
        for (var i = 0; i < assignedQuests.Count;)
        {
            var assignedQuest = assignedQuests[i];
            if (!IsObsolete(assignedQuest))
            {
                i++;
                continue;
            }
            NotifyExpired(assignedQuest);
            assignedQuests.RemoveAt(i);
        }
    }
    #endregion

    #region Internal Implementation
    /// <summary>Kotlin <c>assignedQuests.removeAll(List)</c>: removes by equality (reference identity for AssignedQuest)</summary>
    private void RemoveAll(IReadOnlyCollection<AssignedQuest> quests)
    {
        assignedQuests.RemoveAll(it => quests.Contains(it));
    }

    private void AddQuestNotification(Civilization target, string text, IEnumerable<NotificationAction> actions) =>
        target.AddNotification(text, actions, NotificationCategory.Diplomacy, Civ.CivName, NotificationIcon.Quest);

    private void AddDiplomacyNotification(Civilization target, string text) =>
        AddQuestNotification(target, text, [new DiplomacyAction(Civ.CivId)]);

    private void AddQuestNotification(Civilization target, string text) =>
        AddQuestNotification(target, text, Civ.GetCapital().NotNull().Location);

    private void AddQuestNotification(Civilization target, string text, HexCoord? location) =>
        // Note: that LocationAction pseudo-constructor is able to filter out null location(s), no need for `if`
        AddQuestNotification(target, text, LocationAction.Of(location));

    /// <summary>Access all assigned Quests of "type" <paramref name="questName"/></summary>
    private IEnumerable<AssignedQuest> GetAssignedQuestsOfName(QuestName questName) =>
        assignedQuests.Where(it => it.QuestNameInstance == questName);

    private void DecrementQuestCountdowns()
    {
        if (globalQuestCountdown > 0)
            globalQuestCountdown -= 1;

        foreach (var entry in individualQuestCountdown.ToList())
            if (entry.Value > 0)
                individualQuestCountdown[entry.Key] = entry.Value - 1;
    }

    private void SeedGlobalQuestCountdown()
    {
        if (Civ.GameInfo.Turns < GLOBAL_QUEST_FIRST_POSSIBLE_TURN)
            return;

        if (globalQuestCountdown != UNSET)
            return;

        var rng = Civ.State.StateBasedRandom("QuestManager.seedGlobalQuestCooldown");
        var countdown =
            Civ.GameInfo.Turns == GLOBAL_QUEST_FIRST_POSSIBLE_TURN
                ? rng.NextInt(GLOBAL_QUEST_FIRST_POSSIBLE_TURN_RAND)
                : GLOBAL_QUEST_MIN_TURNS_BETWEEN + rng.NextInt(GLOBAL_QUEST_RAND_TURNS_BETWEEN);

        globalQuestCountdown = KotlinMath.FloatToInt(countdown * Civ.GameInfo.Speed.Modifier);
    }

    private void SeedIndividualQuestsCountdowns()
    {
        if (Civ.GameInfo.Turns < INDIVIDUAL_QUEST_FIRST_POSSIBLE_TURN)
            return;

        var majorCivs = Civ.GameInfo.GetAliveMajorCivs();
        foreach (var majorCiv in majorCivs)
            if (!individualQuestCountdown.ContainsKey(majorCiv.CivId) || individualQuestCountdown[majorCiv.CivId] == UNSET)
                SeedIndividualQuestsCountdown(majorCiv);
    }

    private void SeedIndividualQuestsCountdown(Civilization challenger)
    {
        var rng = Civ.State.Copy(otherCiv: challenger).StateBasedRandom("QuestManager.seedIndividualQuestCooldown");
        var countdown =
            Civ.GameInfo.Turns == INDIVIDUAL_QUEST_FIRST_POSSIBLE_TURN
                ? rng.NextInt(INDIVIDUAL_QUEST_FIRST_POSSIBLE_TURN_RAND)
                : INDIVIDUAL_QUEST_MIN_TURNS_BETWEEN + rng.NextInt(INDIVIDUAL_QUEST_RAND_TURNS_BETWEEN);

        individualQuestCountdown[challenger.CivId] = KotlinMath.FloatToInt(countdown * Civ.GameInfo.Speed.Modifier);
    }

    // Readability helper - No lazy sequence: call frequency * data size is small
    private List<Quest> GetQuests(Func<Quest, bool> predicate) => Ruleset.Quests.Values.Where(predicate).ToList();

    private void TryStartNewGlobalQuest()
    {
        if (globalQuestCountdown != 0)
            return;
        if (assignedQuests.Count(it => it.IsGlobal()) >= GLOBAL_QUEST_MAX_ACTIVE)
            return;

        var majorCivs = Civ.GetKnownCivs().Where(it => it.IsMajorCiv() && !it.IsAtWarWith(Civ)).ToList();
        bool IsAssignable(Quest quest) => majorCivs.Count(it => IsQuestValid(quest, it)) >= quest.MinimumCivs;
        var assignableQuests = GetQuests(it => it.IsGlobal() && IsAssignable(it));

        if (assignableQuests.Count != 0)
        {
            var quest = assignableQuests.RandomWeighted(Civ.GetRandom(), it => GetQuestWeight(it.Name));
            var assignees = Civ.GameInfo.GetAliveMajorCivs().Where(it => !it.IsAtWarWith(Civ) && IsQuestValid(quest, it)).ToList();

            AssignNewQuest(quest, assignees);
            globalQuestCountdown = UNSET;
        }
    }

    private void TryStartNewIndividualQuests()
    {
        foreach (var (challengerName, countdown) in individualQuestCountdown.ToList())
        {
            var challenger = Civ.GameInfo.GetCivilization(challengerName);

            if (countdown != 0)
                continue;

            if (GetAssignedQuestsFor(challenger).Count(it => it.IsIndividual()) >= INDIVIDUAL_QUEST_MAX_ACTIVE)
                continue;

            var assignableQuests = GetQuests(it => it.IsIndividual() && IsQuestValid(it, challenger));

            if (assignableQuests.Count != 0)
            {
                var quest = assignableQuests.RandomWeighted(Civ.GetRandom(challenger), it => GetQuestWeight(it.Name));
                var assignees = new List<Civilization> { challenger };

                AssignNewQuest(quest, assignees);
            }
        }
    }

    private void TryBarbarianInvasion()
    {
        if ((Civ.GetTurnsTillCallForBarbHelp() == null || Civ.GetTurnsTillCallForBarbHelp() == 0)
            && Civ.CityStateFunctions.GetNumThreateningBarbarians() >= 2)
        {
            Civ.ForEachKnownCiv(it =>
            {
                if (!it.IsMajorCiv()
                    || !it.IsAlive()
                    || it.IsAtWarWith(Civ)
                    || it.GetProximity(Civ) > Proximity.Far
                ) return;
                it.AddNotification(
                    $"[{Civ.CivName}] is being invaded by Barbarians! Destroy Barbarians near their territory to earn Influence.",
                    Civ.GetCapital().NotNull().Location,
                    NotificationCategory.Diplomacy, Civ.CivName,
                    NotificationIcon.War);
            });
            Civ.AddFlag(CivFlags.TurnsTillCallForBarbHelp.ToString(), 30);
        }
    }

    private void HandleGlobalQuests()
    {
        // Remove any participants that are no longer valid because of being dead or at war with the CS
        assignedQuests.RemoveAll(it => it.IsGlobal() && !CanAssignAQuestTo(it.AssigneeCiv));
        var globalQuestsExpired = assignedQuests.Where(it => it.IsGlobal() && it.IsExpired()).Select(it => it.QuestNameInstance).Distinct().ToList();
        foreach (var globalQuestName in globalQuestsExpired)
            HandleGlobalQuest(globalQuestName);
    }

    private void HandleGlobalQuest(QuestName questName)
    {
        var winnersAndLosers = new WinnersAndLosers(this, questName);
        winnersAndLosers.Winners.ForEach(GiveReward);
        winnersAndLosers.Losers.ForEach(it => NotifyExpired(it, winnersAndLosers.Winners));

        assignedQuests.RemoveAll(it => it.QuestNameInstance == questName); // removing winners then losers would leave those with score 0
    }

    /// <summary>
    /// If quest is complete, it gives the influence reward to the player.
    /// Returns true if the quest can be removed (is either complete, obsolete or expired)
    /// </summary>
    private bool HandleIndividualQuest(AssignedQuest assignedQuest)
    {
        var assignee = assignedQuest.AssigneeCiv;

        // One of the civs is defeated, or they started a war: remove quest
        if (!CanAssignAQuestTo(assignee))
            return true;

        if (IsComplete(assignedQuest))
        {
            GiveReward(assignedQuest);
            return true;
        }

        if (IsObsolete(assignedQuest))
        {
            NotifyExpired(assignedQuest);
            return true;
        }

        if (assignedQuest.IsExpired())
        {
            NotifyExpired(assignedQuest);
            return true;
        }

        return false;
    }

    private void AssignNewQuest(Quest quest, IEnumerable<Civilization> assignees)
    {
        var turn = Civ.GameInfo.Turns;

        foreach (var assignee in assignees)
        {
            var (data1, data2) = quest.QuestNameInstance.GetAssignedQuestData(Civ, quest, assignee);
            var newQuest = new AssignedQuest(
                gameInfo: Civ.GameInfo,
                quest: quest,
                assigner: Civ,
                assignee: assignee,
                assignedOnTurn: turn,
                data1: data1,
                data2: data2);
            var notificationActions = quest.QuestNameInstance.GetNotificationActions(Civ, newQuest);

            assignedQuests.Add(newQuest);
            if (quest.IsIndividual())
                individualQuestCountdown[assignee.CivId] = UNSET;

            AddQuestNotification(assignee, $"[{Civ.CivName}] assigned you a new quest: [{quest.Name}].", notificationActions);
        }
    }

    /// <summary>Returns true if <see cref="Civ"/> can assign a quest to <paramref name="challenger"/></summary>
    private bool CanAssignAQuestTo(Civilization challenger)
    {
        return !challenger.IsDefeated() && challenger.IsMajorCiv() &&
            Civ.Knows(challenger) && !Civ.IsAtWarWith(challenger);
    }

    /// <summary>Returns true if the <paramref name="quest"/> can be assigned to <paramref name="challenger"/></summary>
    private bool IsQuestValid(Quest quest, Civilization challenger)
    {
        if (!CanAssignAQuestTo(challenger)) return false;
        if (GetAssignedQuestsOfName(quest.QuestNameInstance).Any(it => it.AssigneeCiv == challenger)) return false;
        if (quest.IsIndividual() && Civ.GetDiplomacyManager(challenger).NotNull().HasFlag(DiplomacyFlags.Bullied)) return false;
        return quest.QuestNameInstance.IsValid(Civ, challenger);
    }

    /// <summary>Returns true if the <paramref name="assignedQuest"/> is successfully completed</summary>
    private bool IsComplete(AssignedQuest assignedQuest) =>
        assignedQuest.QuestNameInstance.IsComplete(Civ, assignedQuest);

    /// <summary>Returns true if the <paramref name="assignedQuest"/> request cannot be fulfilled anymore</summary>
    private bool IsObsolete(AssignedQuest assignedQuest) =>
        assignedQuest.QuestNameInstance.IsObsolete(Civ, assignedQuest);

    /// <summary>Increments the assignee influence on <see cref="Civ"/> and adds a Notification</summary>
    private void GiveReward(AssignedQuest assignedQuest)
    {
        var assignee = assignedQuest.AssigneeCiv;
        var rewardInfluence = CityStateFunctions.ScaleInfluenceGain(assignee, assignedQuest.GetInfluence()); // B034

        Civ.GetDiplomacyManager(assignee).NotNull().AddInfluence(rewardInfluence);
        if (rewardInfluence > 0)
            AddQuestNotification(assignee, $"[{Civ.CivName}] rewarded you with [{KotlinMath.FloatToInt(rewardInfluence).ToString(CultureInfo.InvariantCulture)}] influence for completing the [{assignedQuest.QuestName}] quest.");

        // We may have received bonuses from city-state friend-ness or ally-ness
        foreach (var city in Civ.Cities)
            city.CityStats.Update();
    }

    /// <summary>
    /// Notifies the assignee of <paramref name="assignedQuest"/> that the quest is now obsolete or expired.
    /// Optionally displays the <paramref name="winners"/> of global quests.
    /// </summary>
    private void NotifyExpired(AssignedQuest assignedQuest, List<AssignedQuest>? winners = null)
    {
        var assignee = assignedQuest.AssigneeCiv;
        if (winners == null || winners.Count == 0)
        {
            AddQuestNotification(assignee, $"[{Civ.CivName}] no longer needs your help with the [{assignedQuest.QuestName}] quest.");
        }
        else
        {
            AddQuestNotification(assignee, $"The [{assignedQuest.QuestName}] quest for [{Civ.CivName}] has ended. It was won by [{string.Join(", ", winners.Select(it => "{" + it.AssigneeCiv.CivName + "}"))}].");
        }
    }

    /// <summary>Returns the score for the <paramref name="assignedQuest"/></summary>
    private static int GetScoreForQuest(AssignedQuest assignedQuest)
    {
        var assignee = assignedQuest.AssigneeCiv;

        return assignedQuest.QuestNameInstance switch
        {
            //quest total = civ total - the value at the time the quest started (which was stored in assignedQuest.data1)
            QuestName.ContestCulture => assignee.TotalCultureForContests - QuestImplementation.ParseInt(assignedQuest.Data1),
            QuestName.ContestFaith => assignee.TotalFaithForContests - QuestImplementation.ParseInt(assignedQuest.Data1),
            QuestName.ContestTech => assignee.Tech.GetNumberOfTechsResearched() - QuestImplementation.ParseInt(assignedQuest.Data1),
            _ => 0,
        };
    }

    /// <summary>
    /// Evaluate a contest-type quest:
    ///  - Determines winner(s) (as AssignedQuest instances, which name their assignee): Those whose score is the maximum score, possibly tied.
    ///    and losers: all other assignedQuests matching parameter <c>questName</c>.
    ///  - Called by the UI via <see cref="GetScoreStringForGlobalQuest"/> before a Contest is resolved to display who currently leads,
    ///    and by <see cref="HandleGlobalQuest"/> to distribute rewards and notifications.
    /// </summary>
    private sealed class WinnersAndLosers
    {
        public List<AssignedQuest> Winners { get; } = [];
        public List<AssignedQuest> Losers { get; } = [];
        public int MaxScore { get; private set; } = -1;

        public WinnersAndLosers(QuestManager manager, QuestName questName)
        {
            if (!manager.Ruleset.Quests[questName.GetValue()].IsGlobal())
                throw new ArgumentException("Failed requirement.");

            foreach (var quest in manager.GetAssignedQuestsOfName(questName))
            {
                var qScore = GetScoreForQuest(quest);
                if (qScore <= 0) continue; // no civ is a winner if their score is 0
                if (qScore < MaxScore)
                    Losers.Add(quest);
                else if (qScore == MaxScore)
                    Winners.Add(quest);
                else
                { // qScore > maxScore
                    Losers.AddRange(Winners);
                    Winners.Clear();
                    Winners.Add(quest);
                    MaxScore = qScore;
                }
            }
        }
    }

    /// <summary>
    /// Returns a string to show "competition" status:
    ///  - Show leading civ(s) (more than one only if tied for first place) with best score.
    ///  - The assignee civ of the given <paramref name="inquiringAssignedQuest"/> is shown for comparison if it is not among the leaders.
    ///
    /// Assumes the result will be passed to Tr - but parts are pretranslated to avoid nested brackets.
    /// Tied leaders are separated by ", " - translators cannot influence this, sorry.
    /// </summary>
    public string GetScoreStringForGlobalQuest(AssignedQuest inquiringAssignedQuest)
    {
        if (inquiringAssignedQuest.AssignerCiv != Civ) throw new ArgumentException("Failed requirement.");
        if (!inquiringAssignedQuest.IsGlobal()) throw new ArgumentException("Failed requirement.");

        string scoreDescriptor;
        switch (inquiringAssignedQuest.QuestNameInstance)
        {
            case QuestName.ContestCulture: scoreDescriptor = "Culture"; break;
            case QuestName.ContestFaith: scoreDescriptor = "Faith"; break;
            case QuestName.ContestTech: scoreDescriptor = "Technologies"; break;
            default: return ""; //This handles global quests which aren't a competition, like invest
        }

        // Get list of leaders with leading score (the losers aren't used here)
        var evaluation = new WinnersAndLosers(this, inquiringAssignedQuest.QuestNameInstance);
        if (evaluation.Winners.Count == 0) //Only show leaders if there are some
            return "";

        var listOfLeadersAsTranslatedString = string.Join(", ", evaluation.Winners.Select(it => it.AssigneeCiv.CivName.Tr()));
        string GetScoreString(string name, int score) => $"[{name}] with [{score.ToString(CultureInfo.InvariantCulture)}] [{scoreDescriptor}]".Tr();
        var leadersString = GetScoreString(listOfLeadersAsTranslatedString, evaluation.MaxScore);

        if (evaluation.Winners.Contains(inquiringAssignedQuest))
            return $"Current leader(s): [{leadersString}]";

        var yourScoreString = GetScoreString(inquiringAssignedQuest.Assignee, GetScoreForQuest(inquiringAssignedQuest));
        return $"Current leader(s): [{leadersString}], you: [{yourScoreString}]";
    }

    /// <summary>
    /// Gets notified a barbarian camp in <paramref name="location"/> has been cleared by <paramref name="civInfo"/>.
    /// Since <see cref="QuestName.ClearBarbarianCamp"/> is a global quest, it could have been assigned to
    /// multiple civilizations, so after this notification all matching quests are removed.
    /// </summary>
    public virtual void BarbarianCampCleared(Civilization civInfo, HexCoord location)
    {
        var matchingQuests = GetAssignedQuestsOfName(QuestName.ClearBarbarianCamp)
            .Where(it => QuestImplementation.ParseInt(it.Data1) == location.x && QuestImplementation.ParseInt(it.Data2) == location.y)
            .ToList();

        var winningQuest = matchingQuests.FirstOrDefault(it => it.AssigneeCiv == civInfo);
        if (winningQuest != null)
            GiveReward(winningQuest);

        RemoveAll(matchingQuests);
    }

    /// <summary>Gets notified the city state <paramref name="cityState"/> was just conquered by <paramref name="attacker"/>.</summary>
    public virtual void CityStateConquered(Civilization cityState, Civilization attacker)
    {
        var matchingQuests = GetAssignedQuestsOfName(QuestName.ConquerCityState)
            .Where(it => it.Data1 == cityState.CivId && it.AssigneeCiv == attacker)
            .ToList();

        foreach (var quest in matchingQuests)
            GiveReward(quest);

        RemoveAll(matchingQuests);
    }

    /// <summary>Gets notified the city state <paramref name="cityState"/> was just bullied by <paramref name="bully"/>.</summary>
    public virtual void CityStateBullied(Civilization cityState, Civilization bully)
    {
        var matchingQuests = GetAssignedQuestsOfName(QuestName.BullyCityState)
            .Where(it => it.Data1 == cityState.CivId && it.AssigneeCiv == bully)
            .ToList();

        foreach (var quest in matchingQuests)
            GiveReward(quest);

        RemoveAll(matchingQuests);

        // What idiots haha oh wait that's us
        if (Civ != cityState) return;

        // Revoke most quest types from the bully
        var revokedQuests = GetAssignedQuestsFor(bully)
            .Where(it => it.IsIndividual() || it.QuestNameInstance == QuestName.Invest)
            .ToList();
        RemoveAll(revokedQuests);
        if (revokedQuests.Count == 0) return;
        AddDiplomacyNotification(bully, $"[{Civ.CivName}] cancelled the quests they had given you because you demanded tribute from them.");
    }

    /// <summary>Gets notified when we are attacked, for war with major pseudo-quest</summary>
    public virtual void WasAttackedBy(Civilization attacker)
    {
        // Set target number units to kill
        var totalMilitaryUnits = attacker.Units.GetCivUnits().Count(it => !it.IsCivilian());
        var unitsToKill = Math.Max(totalMilitaryUnits / 4, 3);
        unitsToKillForCiv[attacker.CivId] = unitsToKill;

        // Ask for assistance
        var location = Civ.GetCapital(firstCityIfNoCapital: true)?.Location;
        Civ.ForEachKnownCiv(thirdCiv =>
        {
            if (!thirdCiv.IsMajorCiv() || thirdCiv.IsDefeated() || thirdCiv.IsAtWarWith(Civ))
                return;
            NotifyAskForAssistance(thirdCiv, attacker.CivId, unitsToKill, location);
        });
    }

    private void NotifyAskForAssistance(Civilization assignee, string attackerName, int unitsToKill, HexCoord? location)
    {
        if (attackerName == assignee.CivId) return; // No "Hey Bob help us against Bob"
        var message = $"[{Civ.CivName}] is being attacked by [{attackerName}]!" +
            // Space relevant in template!
            $" Kill [{unitsToKill.ToString(CultureInfo.InvariantCulture)}] of the attacker's military units and they will be immensely grateful.";
        AddQuestNotification(assignee, message, location);
    }

    /// <summary>Gets notified when <paramref name="killed"/>'s military unit was killed by <paramref name="killer"/>, for war with major pseudo-quest</summary>
    public void MilitaryUnitKilledBy(Civilization killer, Civilization killed)
    {
        if (!IsWarWithMajorActive(killed)) return;

        // No credit if we're at war or haven't met
        if (!Civ.Knows(killer) || Civ.IsAtWarWith(killer)) return;

        // Make the map if we haven't already
        if (!unitsKilledFromCiv.TryGetValue(killed.CivId, out var unitsKilledFromCivEntry))
        {
            unitsKilledFromCivEntry = new JavaHashMap<string, int>();
            unitsKilledFromCiv[killed.CivId] = unitsKilledFromCivEntry;
        }

        // Update kill count
        var updatedKillCount = 1 + (unitsKilledFromCivEntry.TryGetValue(killer.CivId, out var previous) ? previous : 0);
        unitsKilledFromCivEntry[killer.CivId] = updatedKillCount;

        // Quest complete?
        if (updatedKillCount >= unitsToKillForCiv[killed.CivId])
        {
            AddDiplomacyNotification(killer, $"[{Civ.CivName}] is deeply grateful for your assistance in the war against [{killed.CivName}]!");
            Civ.GetDiplomacyManager(killer).NotNull().AddInfluence(100f); // yikes
            EndWarWithMajorQuest(killed);
        }
    }

    /// <summary>Called when a major civ meets the city-state for the first time. Mainly for war with major pseudo-quest.</summary>
    public virtual void JustMet(Civilization otherCiv)
    {
        if (unitsToKillForCiv.IsEmpty) return;
        var location = Civ.GetCapital(firstCityIfNoCapital: true)?.Location;
        foreach (var (attackerName, unitsToKill) in unitsToKillForCiv)
            NotifyAskForAssistance(otherCiv, attackerName, unitsToKill, location);
    }

    /// <summary>Ends War with Major pseudo-quests that aren't relevant any longer</summary>
    private void TryEndWarWithMajorQuests()
    {
        foreach (var attacker in unitsToKillForCiv.Keys.Select(it => Civ.GameInfo.GetCivilization(it)).ToList())
        {
            if (Civ.IsDefeated()
                || attacker.IsDefeated()
                || !Civ.IsAtWarWith(attacker))
            {
                EndWarWithMajorQuest(attacker);
            }
        }
    }

    private void EndWarWithMajorQuest(Civilization attacker)
    {
        Civ.ForEachKnownCiv(thirdCiv =>
        {
            if (thirdCiv.IsDefeated() || thirdCiv == attacker || thirdCiv.IsAtWarWith(Civ))
                return;
            if (UnitsKilledSoFar(attacker, thirdCiv) >= UnitsToKill(attacker)) // Don't show the notification to the one who won the quest
                return;
            AddDiplomacyNotification(thirdCiv, $"[{Civ.CivName}] no longer needs your assistance against [{attacker.CivName}].");
        });
        unitsToKillForCiv.Remove(attacker.CivId);
        unitsKilledFromCiv.Remove(attacker.CivId);
    }

    public bool IsWarWithMajorActive(Civilization target) => unitsToKillForCiv.ContainsKey(target.CivId);

    public int UnitsToKill(Civilization target) => unitsToKillForCiv.TryGetValue(target.CivId, out var units) ? units : 0;

    public int UnitsKilledSoFar(Civilization target, Civilization viewingCiv)
    {
        if (!unitsKilledFromCiv.TryGetValue(target.CivId, out var killMap)) return 0;
        return killMap.TryGetValue(viewingCiv.CivId, out var killed) ? killed : 0;
    }

    /// <summary>Gets notified when given gold by <paramref name="donorCiv"/>.</summary>
    public virtual void ReceivedGoldGift(Civilization donorCiv)
    {
        var matchingQuests = GetAssignedQuestsOfName(QuestName.GiveGold)
            .Where(it => it.AssigneeCiv == donorCiv)
            .ToList();

        foreach (var quest in matchingQuests)
            GiveReward(quest);

        RemoveAll(matchingQuests);
    }

    /// <summary>Returns the weight of the <paramref name="questName"/>, depends on city state trait and personality</summary>
    private float GetQuestWeight(string questName)
    {
        var weight = 1f;
        if (!Ruleset.Quests.TryGetValue(questName, out var quest)) return 0f;

        if (quest.WeightForCityStateType.TryGetValue(Civ.CityStatePersonality.ToString(), out var personalityWeight))
            weight *= personalityWeight;

        if (quest.WeightForCityStateType.TryGetValue(Civ.CityStateType.Name, out var traitWeight))
            weight *= traitWeight;
        return weight;
    }
    #endregion
}
