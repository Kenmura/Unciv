using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic;

// T184: what a civ still has to decide this turn (design/COMMANDS-IMPLEMENTATION.md section 5.3). A pure query over game state:
// it reads no UI state, so the free-great-person picker's UI-only "defer" flag does not exist here.
// The order mirrors NextTurnAction (ui/screens/worldscreen/status/NextTurnAction.kt); alerts and trade requests follow, as in WorldScreen.update.
// Not ported: the "checkForDueUnits" UI setting (due units are always listed, never blocking), promotable units (not part of NextTurnAction).

public enum DecisionKind
{
    FreeGreatPerson,
    CityProduction,
    Technology,
    Policy,
    MoveSpies,
    Pantheon,
    FoundReligion,
    EnhanceReligion,
    ReformReligion,
    DiplomaticVote,
    DueUnits,
    MoveAutomatedUnits,
    Alert,
    TradeRequest,
}

/// <summary>One pending decision. <see cref="ResolvedBy"/> is the type string of the command that resolves it.</summary>
/// <param name="Kind">What is pending</param>
/// <param name="Subject">The city name, alert type, requesting civ, "found"/"expand" for a pantheon, or "" when the kind says it all</param>
/// <param name="ResolvedBy">The command type that makes this decision disappear</param>
/// <param name="Blocking">When true, ending the turn must be refused until it is resolved</param>
/// <param name="Index">The position in <c>PopupAlerts</c> or <c>TradeRequests</c> for those kinds, else -1</param>
public sealed record Decision(DecisionKind Kind, string Subject, string ResolvedBy, bool Blocking, int Index = -1);

public static class PendingDecisions
{
    /// <summary>Alerts that ask for a choice; the rest are information to be dismissed.</summary>
    private static bool IsChoiceAlert(AlertType type) => type is
        AlertType.CityConquered or AlertType.DiplomaticMarriage or AlertType.RecapturedCivilian or AlertType.DeclarationOfFriendship
        or AlertType.Event or AlertType.WarDeclaration or AlertType.BorderConflict
        or AlertType.DemandToStopSettlingCitiesNear or AlertType.DemandToStopSpreadingReligion or AlertType.DemandToStopSpyingOnUs or AlertType.DemandToNotAttackUs;

    /// <summary>Alerts that leave game state in limbo until answered.</summary>
    private static bool IsBlockingAlert(AlertType type) => type == AlertType.CityConquered;

    public static IReadOnlyList<Decision> For(CivilizationClass civ)
    {
        var result = new List<Decision>();
        if (civ.IsSpectator()) return result;
        var game = civ.GameInfo;

        if (civ.GreatPeople.FreeGreatPeople > 0)
            result.Add(new Decision(DecisionKind.FreeGreatPerson, "", "choose-free-great-person", true));

        var city = civ.Cities.FirstOrDefault(it => !it.IsPuppet && it.CityConstructions.CurrentConstructionName().Length == 0);
        if (city != null)
            result.Add(new Decision(DecisionKind.CityProduction, city.Name, "queue-construction", true));

        if (civ.ShouldOpenTechPicker())
            result.Add(new Decision(DecisionKind.Technology, "", civ.Tech.FreeTechs != 0 ? "pick-free-technology" : "set-research-queue", true));

        if (civ.Policies.ShouldShowPolicyPicker())
            result.Add(new Decision(DecisionKind.Policy, "", "adopt-policy", true));

        if (game.IsEspionageEnabled() && civ.EspionageManager.ShouldShowMoveSpies())
            result.Add(new Decision(DecisionKind.MoveSpies, "", "move-spy", true));

        var religion = civ.ReligionManager;
        var canFoundOrExpand = religion.CanFoundOrExpandPantheon();
        if (religion.ReligionState != ReligionState.Pantheon && canFoundOrExpand)
            result.Add(new Decision(DecisionKind.Pantheon, "found", "found-pantheon", true));
        if (religion.ReligionState == ReligionState.Pantheon && canFoundOrExpand)
            result.Add(new Decision(DecisionKind.Pantheon, "expand", "found-pantheon", true));
        if (religion.ReligionState == ReligionState.FoundingReligion)
            result.Add(new Decision(DecisionKind.FoundReligion, "", "found-religion", true));
        if (religion.ReligionState == ReligionState.EnhancingReligion)
            result.Add(new Decision(DecisionKind.EnhanceReligion, "", "choose-beliefs", true));
        if (religion.HasFreeBeliefs())
            result.Add(new Decision(DecisionKind.ReformReligion, "", "choose-beliefs", true));

        if (civ.MayVoteForDiplomaticVictory())
            result.Add(new Decision(DecisionKind.DiplomaticVote, "", "vote-diplomatic", true));

        if (civ.Units.GetCivUnits().Any(it => it.Due && it.IsIdle()))
            result.Add(new Decision(DecisionKind.DueUnits, "", "set-unit-due", false));

        if (CanMoveAutomatedUnits(civ))
            result.Add(new Decision(DecisionKind.MoveAutomatedUnits, "", "move-automated-units", true));

        for (var i = 0; i < civ.PopupAlerts.Count; i++)
        {
            var type = civ.PopupAlerts[i].Type;
            var choice = IsChoiceAlert(type);
            result.Add(new Decision(DecisionKind.Alert, type.ToString(), choice ? "answer-alert" : "dismiss-alert", IsBlockingAlert(type), i));
        }

        for (var i = 0; i < civ.TradeRequests.Count; i++)
            result.Add(new Decision(DecisionKind.TradeRequest, civ.TradeRequests[i].RequestingCiv, "respond-trade-request", false, i));

        return result;
    }

    /// <summary>Only the decisions that stop <c>EndTurn</c>.</summary>
    public static IReadOnlyList<Decision> BlockingFor(CivilizationClass civ) => For(civ).Where(it => it.Blocking).ToList();

    // NextTurnAction.canMoveAutomatedUnits
    private static bool CanMoveAutomatedUnits(CivilizationClass civ)
    {
        if (civ.GameInfo.GetPlayerSettings(civ.CivId).AutomatedUnitsMoveOnTurnStart) return false;
        if (civ.HasMovedAutomatedUnits) return false;
        return civ.Units.GetCivUnits().Any(it =>
            it.CurrentMovement > Constants.MinimumMovementEpsilon && (it.IsAutomated() || it.IsExploring() || it.IsMoving()));
    }
}
