using System.Diagnostics.CodeAnalysis;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Commands.Empire;

/// <summary>What the pantheon and belief pickers share (Kotlin <c>PantheonPickerScreen.beliefIsAllowed</c>, <c>ReligiousBeliefsPickerScreen.loadRightTable</c>).</summary>
internal static class ReligionCommandChecks
{
    public static bool BeliefIsAllowed(Belief belief, CivilizationObject civ) =>
        !((IHasUniques)belief).GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState).Any(it => !it.ConditionalsApply(civ.State))
        && !((IHasUniques)belief).HasUnique(UniqueType.Unavailable, civ.State);

    /// <summary>
    /// Resolve belief names against the slots the picker offers, in slot order (the order of <paramref name="slots"/>, each entry repeated by
    /// its count). A slot of type Any takes any belief, other slots exactly their type. No belief may repeat, belong to the civ's current
    /// religion, or be taken by another religion.
    /// </summary>
    public static bool TryBeliefs(CivilizationObject civ, IReadOnlyList<string> names, Counter<BeliefType> slots,
        [NotNullWhen(true)] out List<Belief>? beliefs, out CommandValidation failure)
    {
        beliefs = null;
        var slotTypes = new List<BeliefType>();
        foreach (var entry in slots)
            for (int i = 0; i < entry.Value; i++) slotTypes.Add(entry.Key);
        if (names.Count != slotTypes.Count)
        {
            failure = CommandValidation.Fail(CommandErrors.InvalidTarget, $"Exactly {slotTypes.Count} beliefs must be chosen");
            return false;
        }
        var ruleset = civ.GameInfo.Ruleset;
        var current = civ.ReligionManager.Religion;
        var result = new List<Belief>();
        for (int i = 0; i < names.Count; i++)
        {
            if (!ruleset.Beliefs.TryGetValue(names[i], out var belief))
            {
                failure = CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no belief \"{names[i]}\"");
                return false;
            }
            if (slotTypes[i] != BeliefType.Any && belief.Type != slotTypes[i])
            {
                failure = CommandValidation.Fail(CommandErrors.InvalidTarget, $"{belief.Name} is not a {slotTypes[i]} belief");
                return false;
            }
            var owner = civ.ReligionManager.GetReligionWithBelief(belief);
            if (result.Contains(belief) || (current != null && current.HasBelief(belief.Name))
                || (owner != null && !ReferenceEquals(owner, current)) || !BeliefIsAllowed(belief, civ))
            {
                failure = CommandValidation.Fail(CommandErrors.NotAllowed, $"{belief.Name} is not available");
                return false;
            }
            result.Add(belief);
        }
        beliefs = result;
        failure = CommandValidation.Ok;
        return true;
    }
}
