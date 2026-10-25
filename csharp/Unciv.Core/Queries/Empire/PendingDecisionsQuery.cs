using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset;
using Unciv.View;

namespace Unciv.Queries.Empire;

// Choice: the value for RespondToAlert.Choice (an event's choice index as text)
// Label: What the choice says (an event choice's text), else the same as Choice
public sealed record AlertChoice(string Choice, string Label);

// Type, Value: The alert as PopupAlerts holds it (the references the answering commands take)
// AckOnly: The alert can no longer be answered (a civ is gone, an event has no choice left): acknowledge it instead
// EventText: The text of an Event alert
// Choices: The choices RespondToAlert accepts right now, in a fixed order; empty for alerts that are only acknowledged or answered by another command
public sealed record AlertDetail(string Type, string Value, bool AckOnly, string? EventText, IReadOnlyList<AlertChoice> Choices);

// Kind, Subject, ResolvedBy, Blocking, Index: PendingDecisions.Decision, the kind as its name
// Alert: Only for the Alert kind
public sealed record PendingDecisionEntry(string Kind, string Subject, string ResolvedBy, bool Blocking, int Index, AlertDetail? Alert);

public sealed record PendingDecisionsResult(IReadOnlyList<PendingDecisionEntry> Decisions);

// T220: what the viewer still has to decide (PendingDecisions, T184) with the details of every alert, so a client can draw the popup
// and offer only the choices the answering command would accept. It only ever describes the viewer's own civ.
[QueryType("empire.PendingDecisions")]
public sealed record PendingDecisionsQuery : IQuery<PendingDecisionsResult>
{
    public string Type => "empire.PendingDecisions";

    private static string[] CandidateChoices(AlertType type) => type switch
    {
        AlertType.CityConquered => ["annex", "puppet", "raze", "liberate", "destroy"],
        AlertType.CityTraded => ["liberate"],
        AlertType.DiplomaticMarriage => ["annex", "puppet", "destroy"],
        AlertType.RecapturedCivilian => ["return", "keep"],
        AlertType.BulliedProtectedMinor or AlertType.AttackedProtectedMinor or AlertType.AttackedAllyMinor => ["war", "side", "abandon"],
        AlertType.Denounced => ["war"],
        _ => [],
    };

    public PendingDecisionsResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        var entries = PendingDecisions.For(civ).Select(it => new PendingDecisionEntry(it.Kind.ToString(), it.Subject, it.ResolvedBy,
            it.Blocking, it.Index, it.Kind == DecisionKind.Alert ? DetailOf(view.Unwrap(), civ, it.Index) : null)).ToList();
        return new PendingDecisionsResult(entries);
    }

    private static AlertDetail DetailOf(GameInfo game, Civilization civ, int index)
    {
        var alert = civ.PopupAlerts[index];
        bool ackOnly = AlertCommandChecks.IsUnshowable(civ, alert);
        if (alert.Type == AlertType.Event)
        {
            var presentation = EventPresentation.Resolve(civ, alert);
            return new AlertDetail(alert.Type.ToString(), alert.Value, ackOnly, presentation?.Text,
                presentation?.Choices.Select(it => new AlertChoice(it.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), it.Text)).ToList() ?? []);
        }
        var choices = new List<AlertChoice>();
        if (!ackOnly)
        {
            var context = new CommandContext(game, civ.CivId);
            foreach (var choice in CandidateChoices(alert.Type))
            {
                var command = new RespondToAlert { Index = index, AlertType = alert.Type, Value = alert.Value, Choice = choice };
                if (command.ValidateChoice(context, civ, alert).IsValid) choices.Add(new AlertChoice(choice, choice));
            }
        }
        return new AlertDetail(alert.Type.ToString(), alert.Value, ackOnly, null, choices);
    }
}
