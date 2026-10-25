using Unciv.Logic;
using Unciv.Logic.Civilization;

namespace Unciv.Commands.Turn;

/// <summary>
/// Dismiss an informational alert. Kotlin: <c>AlertPopup.close</c> removes the alert from <c>civ.popupAlerts</c> when the popup is closed. That list is
/// serialised, so a client that merely looked at an alert must still send this command or its state and the authority's diverge. Valid after a
/// victory and for a defeated civ. Alerts with a decision are answered with their own commands (<see cref="PendingDecisions.ResolverOf"/>),
/// unless Kotlin would not show them any more.
/// </summary>
[CommandType("ack-alert")]
public sealed class AckAlert : GameCommand
{
    public int Index { get; init; }

    public AlertType AlertType { get; init; }

    public string Value { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequirePlayer().TryGet(out var actor, out var failure)) return failure;
        if (!AlertCommandChecks.TryFind(actor, Index, AlertType, Value, out var alert, out failure)) return failure;
        var resolver = PendingDecisions.ResolverOf(AlertType);
        return resolver == "ack-alert" || AlertCommandChecks.IsUnshowable(actor, alert)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"A {AlertType} alert needs an answer: use {resolver}");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Actor.PopupAlerts.RemoveAt(Index);
        return [];
    }
}
