using Unciv.Logic.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Models.Ruleset.Unique;

// core/src/com/unciv/models/ruleset/unique/UniqueTriggerExecutors.kt
/// <summary>
///  Container for the innermost "executors" for triggers, the code actually affecting game state.
///
///  <see cref="UniqueTriggerActivation.GetTriggerFunction"/> wraps them in lambdas, therefore this also serves
///  as documentation what exactly is needed as closure - here the function parameters.
///  They should return the Boolean "success" status the final lambda should return.
/// </summary>
internal static class UniqueTriggerExecutors
{
    public static bool TriggerGlobalAlerts(Civilization civInfo, string alertText, string triggerNotificationText)
    {
        // civInfo is the cause of the alert, the one not getting notified.
        // triggerNotificationText is e.g. "due to adopting [${policy.name}]" or "due to researching [$techName]"
        // alertText is the parameter of the unique.

        // Translate what the original triggerUnique got from the container into something aimed
        // at the other civilizations, and guess an icon:
        string messageSuffix;
        string iconName;
        if (triggerNotificationText.StartsWith("due to adopting ", StringComparison.Ordinal))
        {
            // templates:
            // [civName|An unknown civilization] has adopted the [policyName] policy
            messageSuffix = "has adopted the " + triggerNotificationText["due to adopting ".Length..] + " policy";
            iconName = NotificationIcon.Culture;
        }
        else if (triggerNotificationText.StartsWith("due to researching ", StringComparison.Ordinal))
        {
            // templates:
            // [civName|An unknown civilization] has researched [techName]
            messageSuffix = "has researched " + triggerNotificationText["due to researching ".Length..];
            iconName = NotificationIcon.Science;
        }
        else if (triggerNotificationText.StartsWith("due to ", StringComparison.Ordinal))
        {
            // Kludge! If we get here, the translation template will be missing.
            messageSuffix = triggerNotificationText["due to ".Length..];
            iconName = "";
        }
        else
        {
            // Megakludge! If we get here, the notification will be unreadable. Probably.
            messageSuffix = triggerNotificationText;
            iconName = "";
        }

        foreach (var civ in civInfo.GameInfo.Civilizations)
        {
            if (civ == civInfo || !civ.IsMajorCiv()) continue;
            string defaultNotificationText = civ.GetKnownCivs().Contains(civInfo)
                ? $"[{civInfo.CivName}] {messageSuffix}"
                : $"[An unknown civilization] {messageSuffix}";
            civ.AddNotification(
                $"{{{defaultNotificationText}}}.\n{{{alertText}}}",
                NotificationCategory.General, iconName
            );
        }

        return true;
    }
}
