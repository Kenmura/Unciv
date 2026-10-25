using Unciv.Commands.Units;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Models;
using Unciv.View;

namespace Unciv.Queries.Units;

// Question and ConfirmText: the confirmation popup texts, when the action asks for one (ExecuteUnitAction then needs Confirmed)
public sealed record UnitActionConfirmationInfo(string Question, string ConfirmText);

// Descriptor: what to send back in ExecuteUnitAction. Title is a translation template (may hold icons), never a key.
// IsEnabled: false when the unit can do the action but not right now (no movement left, ...).
// Priority: the UnitActionPriority modifier of the associated unique, null without one (the default is the client's)
public sealed record UnitActionInfo(UnitActionDescriptor Descriptor, string Title, bool IsEnabled, bool IsCurrentAction,
    UnitActionConfirmationInfo? Confirmation, float? Priority);

public sealed record UnitActionsResult(int UnitId, IReadOnlyList<UnitActionInfo> Actions);

// T189: the serializable form of UnitActions.GetUnitActions. Run builds the action objects but never calls a closure.
[QueryType("units.UnitActions")]
public sealed record UnitActionsQuery(int UnitId) : IQuery<UnitActionsResult>
{
    public string Type => "units.UnitActions";

    public UnitActionsResult Run(GameView view)
    {
        var unit = UnitQueryHelpers.OwnUnit(view, UnitId).Unwrap();
        var actions = UnitActionDescriptor.Describe(unit)
            .Select(it => new UnitActionInfo(it.Descriptor, it.Action.Title, it.Action.Action != null, it.Action.IsCurrentAction,
                it.Action.Confirmation == null ? null : new UnitActionConfirmationInfo(it.Action.Confirmation.Question, it.Action.Confirmation.ConfirmText),
                PriorityOf(unit, it.Action)))
            .ToList();
        return new UnitActionsResult(UnitId, actions);
    }

    private static float? PriorityOf(Logic.Map.MapUnits.MapUnit unit, UnitAction action)
    {
        if (action.AssociatedUnique == null) return null;
        float priority = UnitActionModifiers.GetUseFrequency(unit, action.AssociatedUnique, float.NaN);
        return float.IsNaN(priority) ? null : priority;
    }
}
