using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Civilization.Managers;

// T056: the managers call UniqueTriggerActivation.triggerUnique(unique, civInfo, triggerNotificationText = ...) through this single place.
internal static class TriggerUniqueSeam
{
    internal static void TriggerUnique(UniqueObject unique, Civilization civInfo, string? triggerNotificationText = null) =>
        UniqueTriggerActivation.TriggerUnique(unique, civInfo, triggerNotificationText: triggerNotificationText);
}
