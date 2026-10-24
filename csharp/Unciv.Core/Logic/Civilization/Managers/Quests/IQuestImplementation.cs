using Unciv.Models.Ruleset;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/quests/IQuestImplementation.kt (T069). Stripped: the purity annotations.
// The Kotlin interface default bodies live in QuestImplementation (virtual), the only implementer, so ClearBarbarianCamp can call base.

public interface IQuestImplementation
{
    bool IsValid(Civilization civ, Civilization challenger);
    (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee);
    List<NotificationAction> GetNotificationActions(Civilization civ, AssignedQuest assignedQuest);
    bool IsComplete(Civilization civ, AssignedQuest assignedQuest);
    bool IsObsolete(Civilization civ, AssignedQuest assignedQuest);
}
