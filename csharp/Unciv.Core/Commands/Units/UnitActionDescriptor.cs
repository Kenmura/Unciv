using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Models;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Commands.Units;

/// <summary>
/// The wire form of a <see cref="UnitAction"/> (T189): its closure cannot be serialized, so it is named by action type (not called Type: the command envelope owns that name), the stable
/// <see cref="UnitAction.Key"/> and the position among the actions of that type and key. It is always resolved against the
/// CURRENT action list of the unit, never against a list the client saw earlier.
/// </summary>
public sealed record UnitActionDescriptor(UnitActionType ActionType, string? Key = null, int Index = 0)
{
    /// <summary>The unit's current actions, each with the descriptor that names it.</summary>
    public static IReadOnlyList<(UnitActionDescriptor Descriptor, UnitAction Action)> Describe(MapUnitClass unit)
    {
        var counts = new Dictionary<(UnitActionType, string?), int>();
        var result = new List<(UnitActionDescriptor, UnitAction)>();
        foreach (var action in UnitActions.GetUnitActions(unit).ToList()) // materialized: closures run outside the enumeration
        {
            var slot = (action.Type, action.Key);
            counts.TryGetValue(slot, out int index);
            counts[slot] = index + 1;
            result.Add((new UnitActionDescriptor(action.Type, action.Key, index), action));
        }
        return result;
    }

    /// <summary>The action this descriptor names right now, or <c>null</c> when the unit has no such action any more.</summary>
    public UnitAction? Resolve(MapUnitClass unit)
    {
        if (Index < 0) return null;
        return UnitActions.GetUnitActions(unit, ActionType).ToList()
            .Where(it => it.Key == Key)
            .Skip(Index)
            .FirstOrDefault();
    }
}
