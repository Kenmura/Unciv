using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Civilization.Managers;

// T056: the managers of this ticket call UniqueTriggerActivation.triggerUnique(unique, civInfo, triggerNotificationText = ...), which
// is ported by T052 (it depends on this ticket). Until then this seam is the single place they call; T052 replaces its body.
internal static class TriggerUniqueSeam
{
    // TODO(T052): UniqueTriggerActivation.triggerUnique(unique, civInfo, triggerNotificationText = triggerNotificationText)
    internal static void TriggerUnique(UniqueObject unique, Civilization civInfo, string? triggerNotificationText = null)
    {
        _ = unique;
        _ = civInfo;
        _ = triggerNotificationText;
    }
}
