using System.Globalization;
using Unciv.Commands.Diplomacy;
using Unciv.Logic;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Commands.Turn;

/// <summary>
/// Answer an alert that has a decision. Kotlin: the buttons of <c>AlertPopup</c> (<c>addCityConquered</c>, <c>addCityTraded</c>, <c>addDiplomaticMarriage</c>,
/// <c>addRecapturedCivilian</c>, <c>addBulliedOrAttackedProtectedOrAlliedMinor</c>, <c>addDenouncement</c>, <c>addEvent</c>); the alert is removed first, as
/// <c>AlertPopup.close</c> does. Choices (<see cref="Choice"/>):
/// CityConquered: annex, puppet, raze, liberate, destroy (a one city challenge). CityTraded: liberate. DiplomaticMarriage: annex, puppet, destroy.
/// RecapturedCivilian: return, keep. BulliedProtectedMinor / AttackedProtectedMinor / AttackedAllyMinor: war, side, abandon. Denounced: war.
/// Event: the index of the choice. Not here: demands (<see cref="RespondToDemand"/>), friendship offers (<see cref="RespondToFriendship"/>); a button that only
/// closes the popup is <see cref="AckAlert"/>.
/// </summary>
[CommandType("respond-to-alert")]
public sealed class RespondToAlert : GameCommand
{
    public int Index { get; init; }

    public AlertType AlertType { get; init; }

    public string Value { get; init; } = "";

    public string Choice { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequirePlayer().TryGet(out var actor, out var failure)) return failure;
        // T202 review: an answer can annex, raze or declare war, so it must not land between the steps of a simultaneous round's resolution.
        if (context.Game.TurnState is { Mode: TurnMode.Simultaneous, Phase: SimultaneousPhase.Resolving })
            return CommandValidation.Fail(CommandErrors.TurnResolving, "The turn is being resolved");
        if (!AlertCommandChecks.TryFind(actor, Index, AlertType, Value, out var alert, out failure)) return failure;
        return ValidateChoice(context, actor, alert);
    }

    /// <summary>The part of validation that depends on <see cref="Choice"/> (T220: the pending-decisions query asks it for every candidate choice).</summary>
    internal CommandValidation ValidateChoice(CommandContext context, CivilizationObject actor, PopupAlert alert)
    {
        if (AlertType == AlertType.Event) return ValidateEvent(actor, alert);
        if (AlertType is AlertType.CityConquered or AlertType.CityTraded or AlertType.DiplomaticMarriage) return ValidateCity(context, actor);
        return AlertType switch
        {
            AlertType.RecapturedCivilian => ValidateRecaptured(actor),
            AlertType.BulliedProtectedMinor or AlertType.AttackedProtectedMinor or AlertType.AttackedAllyMinor => ValidateMinor(context, actor),
            AlertType.Denounced => ValidateDenounced(context, actor),
            _ => CommandValidation.Fail(CommandErrors.NotAllowed, $"A {AlertType} alert is answered with {PendingDecisions.ResolverOf(AlertType)}"),
        };
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        var alert = actor.PopupAlerts[Index];
        actor.PopupAlerts.RemoveAt(Index);
        switch (AlertType)
        {
            case AlertType.Event:
                EventPresentation.Resolve(actor, alert).NotNull().Choose(actor, int.Parse(Choice, CultureInfo.InvariantCulture));
                break;
            case AlertType.CityConquered or AlertType.CityTraded or AlertType.DiplomaticMarriage:
                ApplyCity(context, actor);
                break;
            case AlertType.RecapturedCivilian:
                ApplyRecaptured(actor);
                break;
            case AlertType.BulliedProtectedMinor or AlertType.AttackedProtectedMinor or AlertType.AttackedAllyMinor:
                ApplyMinor(context, actor);
                break;
            case AlertType.Denounced:
                actor.GetDiplomacyManager(context.ValidatedOther(Value)).NotNull().DeclareWar();
                break;
        }
        return [];
    }

    private CommandValidation BadChoice() => CommandValidation.Fail(CommandErrors.InvalidTarget, $"\"{Choice}\" is not a choice of a {AlertType} alert");

    private CommandValidation ValidateEvent(CivilizationObject actor, PopupAlert alert)
    {
        var presentation = EventPresentation.Resolve(actor, alert);
        if (presentation == null)
            return CommandValidation.Fail(CommandErrors.NotAllowed, "This event has no choices any more: acknowledge it");
        return int.TryParse(Choice, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < presentation.Choices.Count
            ? CommandValidation.Ok
            : BadChoice();
    }

    // The city of a CityConquered / CityTraded / DiplomaticMarriage alert (the alert value is the city id) is the actor's.
    private CommandValidation ValidateCity(CommandContext context, CivilizationObject actor)
    {
        if (!context.OwnCity(actor, Value).TryGet(out var city, out var failure)) return failure;
        var founder = city.FoundingCivObject;
        var mayAnnex = !actor.HasUnique(UniqueType.MayNotAnnexCities);
        var challenge = actor.IsOneCityChallenger();
        var canLiberate = AlertType switch
        {
            AlertType.CityConquered => founder != null && city.Civ != founder && actor != founder,
            AlertType.CityTraded => founder != null && !actor.IsAtWarWith(founder),
            _ => false,
        };
        var ok = (AlertType, Choice) switch
        {
            (AlertType.CityConquered or AlertType.CityTraded, "liberate") => canLiberate,
            (AlertType.CityConquered, "destroy") => challenge,
            (AlertType.CityConquered, "annex") => !challenge && mayAnnex,
            (AlertType.CityConquered, "puppet") => !challenge,
            (AlertType.CityConquered, "raze") => !challenge && city.CanBeDestroyed(justCaptured: true),
            (AlertType.DiplomaticMarriage, "destroy") => challenge,
            (AlertType.DiplomaticMarriage, "annex") => !challenge && mayAnnex,
            (AlertType.DiplomaticMarriage, "puppet") => !challenge,
            _ => false,
        };
        return ok ? CommandValidation.Ok : BadChoice();
    }

    private void ApplyCity(CommandContext context, CivilizationObject actor)
    {
        var city = context.OwnCity(actor, Value).Value.NotNull();
        var mayAnnex = !actor.HasUnique(UniqueType.MayNotAnnexCities);
        switch (AlertType, Choice)
        {
            case (_, "liberate"):
                city.LiberateCity(actor);
                break;
            case (AlertType.CityConquered, "destroy"):
                city.PuppetCity(actor);
                city.DestroyCity();
                break;
            case (AlertType.CityConquered, "annex"):
                city.PuppetCity(actor);
                city.AnnexCity();
                break;
            case (AlertType.CityConquered, "puppet"):
                city.PuppetCity(actor);
                break;
            case (AlertType.CityConquered, "raze"):
                city.PuppetCity(actor);
                if (mayAnnex) city.AnnexCity();
                city.IsBeingRazed = true;
                break;
            case (_, "destroy"):
                city.DestroyCity(overrideSafeties: true);
                break;
            case (_, "annex"):
                city.AnnexCity();
                break;
            case (_, "puppet"):
                city.IsPuppet = true;
                city.CityStats.Update();
                break;
        }
    }

    private CommandValidation ValidateRecaptured(CivilizationObject actor)
    {
        var unit = actor.GameInfo.TileMap[AlertCommandChecks.HexCoordOf(Value)].CivilianUnit;
        if (unit == null || unit.Civ != actor || unit.OriginalOwningCiv == null || unit.OriginalOwningCiv.IsDefeated())
            return CommandValidation.Fail(CommandErrors.NotAllowed, "The unit is gone: acknowledge the alert");
        return Choice is "return" or "keep" ? CommandValidation.Ok : BadChoice();
    }

    private void ApplyRecaptured(CivilizationObject captor)
    {
        var tile = captor.GameInfo.TileMap[AlertCommandChecks.HexCoordOf(Value)];
        var capturedUnit = tile.CivilianUnit.NotNull();
        if (Choice == "keep")
        {
            BattleUnitCapture.CaptureOrConvertToWorker(capturedUnit, captor);
            return;
        }
        var originalOwner = capturedUnit.OriginalOwningCiv.NotNull();
        // Return it to original owner
        var unitName = capturedUnit.BaseUnit.Name;
        capturedUnit.Destroy();
        var closestCity = originalOwner.Cities.MinBy(it => it.GetCenterTile().AerialDistanceTo(tile));
        // Attempt to place the unit near their nearest city
        if (closestCity != null) originalOwner.Units.PlaceUnitNearTile(closestCity.Location, unitName);

        if (originalOwner.IsCityState) originalOwner.GetDiplomacyManagerOrMeet(captor).AddInfluence(45f);
        else if (originalOwner.IsMajorCiv())
            // No extra bonus from doing it several times
            originalOwner.GetDiplomacyManagerOrMeet(captor).SetModifier(DiplomaticModifiers.ReturnedCapturedUnits, 20f);

        var actions = new List<NotificationAction> { new LocationAction(tile.Position) };
        if (closestCity != null) actions.Add(new LocationAction(closestCity.Location));
        actions.Add(new DiplomacyAction(captor.CivId));
        actions.Add(new CivilopediaAction("Tutorial/Barbarians"));
        originalOwner.AddNotification($"Your captured [{unitName}] has been returned by [{captor.CivId}]", actions,
            NotificationCategory.Diplomacy, NotificationIcon.Trade, unitName, captor.CivId);
    }

    // The value is "bullyOrAttacker@cityState".
    private CommandValidation ValidateMinor(CommandContext context, CivilizationObject actor)
    {
        var parts = Value.Split('@');
        if (parts.Length != 2) return CommandValidation.Fail(CommandErrors.StaleReference, "Malformed alert");
        if (!context.Civ(parts[0]).TryGet(out var bully, out var failure)) return failure;
        if (!context.Civ(parts[1]).TryGet(out _, out failure)) return failure;
        if (bully.IsDefeated()) return CommandValidation.Fail(CommandErrors.NotAllowed, "The civ is gone: acknowledge the alert");
        return Choice switch
        {
            "war" => actor.IsAtWarWith(bully) ? CommandValidation.Fail(CommandErrors.NotAllowed, "You are already at war") : CommandValidation.Ok,
            "side" or "abandon" => CommandValidation.Ok,
            _ => BadChoice(),
        };
    }

    private void ApplyMinor(CommandContext context, CivilizationObject player)
    {
        var parts = Value.Split('@');
        var bully = context.ValidatedOther(parts[0]);
        var cityState = context.ValidatedOther(parts[1]);
        switch (Choice)
        {
            case "war":
                player.GetDiplomacyManager(bully).NotNull().SideWithCityState();
                var warReason = AlertType == AlertType.AttackedAllyMinor ? WarType.AlliedCityStateWar : WarType.ProtectedCityStateWar;
                player.GetDiplomacyManager(bully).NotNull().DeclareWar(new DeclareWarReason(warReason, cityState));
                cityState.GetDiplomacyManager(player).NotNull().Influence += 20f; // You went to war for us!!
                break;
            case "side":
                player.GetDiplomacyManager(bully).NotNull().SideWithCityState();
                break;
            case "abandon":
                player.AddNotification($"You have broken your Pledge to Protect [{cityState.CivId}]!",
                    DiplomacyCommandChecks.CityState(cityState).GetNotificationActions(), NotificationCategory.Diplomacy, cityState.CivId);
                DiplomacyCommandChecks.CityState(cityState).RemoveProtectorCiv(player, forced: true);
                break;
        }
    }

    private CommandValidation ValidateDenounced(CommandContext context, CivilizationObject actor)
    {
        if (!context.Civ(Value).TryGet(out var denouncer, out var failure)) return failure;
        if (denouncer.IsDefeated()) return CommandValidation.Fail(CommandErrors.NotAllowed, "The civ is gone: acknowledge the alert");
        if (Choice != "war") return BadChoice();
        return actor.GetDiplomacyManager(denouncer)?.CanDeclareWar() == true
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"You cannot declare war on {Value} now");
    }
}
