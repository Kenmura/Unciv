using Unciv.Commands.Events;
using Unciv.Logic;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.City;
using Unciv.Logic.Civilization;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Commands.Turn;

/// <summary>
/// T226: what a forced end of turn (<see cref="ForceEndTurn"/>, the host's timer) does with the blocking decisions a human left open (design/SERVER.md 4.2).
/// Each one is settled the way the AI would settle it, deterministically (the AI's choices draw only state-based randomness), and reported with a
/// <see cref="DecisionAutoResolved"/> event. A decision that cannot be settled (no city to place a free great person, <c>MoveAutomatedUnits</c>, which only
/// the civ's own client can do) is left as it is, as before. Order: the order of <see cref="PendingDecisions.For"/>, one decision at a time, the list read again
/// after each, so a choice that opens another one (a free tech after a policy) is settled too.
/// </summary>
internal static class DefaultDecisions
{
    private const int MaxSteps = 64;

    // City conquered: the AI puppets a captured city (and annexes it later if it can afford the happiness), so puppet comes first.
    private static readonly IReadOnlyList<string> CityConqueredChoices = new[] { "puppet", "annex", "destroy", "raze" }.AsReadOnly();

    public static IReadOnlyList<GameEvent> Resolve(CivilizationObject civ)
    {
        var events = new List<GameEvent>();
        var skipped = new HashSet<(DecisionKind, string)>();
        for (var step = 0; step < MaxSteps; step++)
        {
            var decision = PendingDecisions.BlockingFor(civ).FirstOrDefault(it => !skipped.Contains((it.Kind, it.Subject)));
            if (decision == null) break;
            var resolution = Settle(civ, decision);
            if (resolution == null) skipped.Add((decision.Kind, decision.Subject));
            else events.Add(new DecisionAutoResolved(civ.CivId, decision.Kind.ToString(), decision.Subject, resolution));
        }
        return events;
    }

    /// <summary>The words describing the choice, or null when the decision is still pending afterwards.</summary>
    private static string? Settle(CivilizationObject civ, Decision decision)
    {
        var before = Count(civ, decision);
        string? resolution = null;
        switch (decision.Kind)
        {
            case DecisionKind.FreeGreatPerson:
                foreach (var option in civ.GreatPeople.GetFreeGreatPersonOptions().ToList())
                {
                    if (civ.GreatPeople.ChooseFreeGreatPerson(option.Name) == null) continue;
                    resolution = option.Name;
                    break;
                }
                break;
            case DecisionKind.CityProduction:
                var city = civ.Cities.First(it => it.Name == decision.Subject);
                CityConstructionsAutomation.ChooseNextConstruction(city.CityConstructions);
                resolution = city.CityConstructions.CurrentConstructionName();
                break;
            case DecisionKind.Technology:
                NextTurnAutomation.ChooseResearchForced(civ);
                resolution = "research";
                break;
            case DecisionKind.Policy:
                NextTurnAutomation.AdoptPolicyForced(civ);
                resolution = "policy";
                break;
            case DecisionKind.MoveSpies:
                civ.EspionageManager.DismissedShouldMoveSpies = true; // the spies stay idle
                resolution = "dismissed";
                break;
            case DecisionKind.Pantheon or DecisionKind.FoundReligion or DecisionKind.EnhanceReligion or DecisionKind.ReformReligion:
                ReligionAutomation.ChooseReligiousBeliefs(civ);
                resolution = "beliefs";
                break;
            case DecisionKind.DiplomaticVote:
                NextTurnAutomation.VoteForced(civ);
                resolution = "vote";
                break;
            case DecisionKind.Alert when decision.Subject == nameof(AlertType.CityConquered):
                resolution = SettleCityConquered(civ, decision.Index);
                break;
        }
        return resolution != null && Count(civ, decision) < before ? resolution : null;
    }

    private static int Count(CivilizationObject civ, Decision decision) =>
        PendingDecisions.BlockingFor(civ).Count(it => it.Kind == decision.Kind && it.Subject == decision.Subject);

    private static string? SettleCityConquered(CivilizationObject civ, int index)
    {
        var game = (GameInfo)civ.GameInfo;
        var alert = civ.PopupAlerts[index];
        var context = new CommandContext(game, civ.CivId);
        foreach (var choice in CityConqueredChoices)
        {
            var command = new RespondToAlert { Index = index, AlertType = alert.Type, Value = alert.Value, Choice = choice };
            if (!command.ValidateChoice(context, civ, alert).IsValid) continue;
            command.Apply(game, civ.CivId);
            return choice;
        }
        return null;
    }
}
