using Unciv.Commands.Events;
using Unciv.Logic.GreatWorks;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Commands.GreatWorks;

/// <summary>
/// Takes another civ's standing swap offer (bnw-spec/SPEC.md section 4.2.3): the actor gives the work in one of its slots, which must be of the
/// offered work's class, and receives the offered one in that slot, while the other civ's work is replaced by the given one. Not while at war,
/// and only between civs that know each other. The offer is used up.
/// </summary>
[CommandType("take-great-work-swap")]
public sealed class TakeGreatWorkSwap : GameCommand
{
    /// <summary>The civ that made the offer.</summary>
    public string CivId { get; init; } = "";

    /// <summary>The offered work.</summary>
    public string Work { get; init; } = "";

    /// <summary>The slot of the actor holding the work it gives.</summary>
    public string CityId { get; init; } = "";
    public string Building { get; init; } = "";
    public int Slot { get; init; }

    private CommandValidation Resolve(CommandContext context, out CivilizationObject? other, out GreatWorkLocation mine, out GreatWorkLocation theirs)
    {
        other = null;
        mine = default;
        theirs = default;
        if (!context.TryActiveActor(out var actor, out var failure)) return failure;
        if (!context.Civ(CivId).TryGet(out var offerer, out failure)) return failure;
        other = offerer;
        if (ReferenceEquals(actor, offerer)) return CommandValidation.Fail(CommandErrors.InvalidTarget, "You cannot swap with yourself");
        if (!context.Game.Ruleset.GreatWorks.TryGetValue(Work, out var wanted) || GreatWorkRules.FindValidOffer(context.Game, offerer, Work) == null)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"{CivId} does not offer {Work}");
        if (!actor.IsAlive() || !actor.Knows(offerer) || actor.IsAtWarWith(offerer))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"You cannot swap Great Works with {CivId} now");
        if (!context.TrySlot(actor, CityId, Building, Slot, out var slot, out failure)) return failure;
        mine = slot.Value;
        if (!context.TryWorkIn(mine, out var given, out failure)) return failure;
        if (given.GreatWorkClass != wanted.GreatWorkClass)
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{given.Name} is not a work of the same kind as {Work}");
        theirs = GreatWorkRules.FindWork(offerer, Work).NotNull();
        return CommandValidation.Ok;
    }

    protected override CommandValidation ValidateCommand(CommandContext context) => Resolve(context, out _, out _, out _);

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        Resolve(context, out var other, out var mine, out var theirs);
        var partner = other.NotNull();
        string given = mine.City.GetGreatWorkIn(mine.Building.Name, mine.Slot).NotNull();
        mine.City.SetGreatWorkIn(mine.Building.Name, mine.Slot, Work);
        theirs.City.SetGreatWorkIn(theirs.Building.Name, theirs.Slot, given);
        GreatWorkRules.RemoveOffer(context.Game, CivId, Work);
        GreatWorkRules.RemoveOffer(context.Game, context.ActorId, given); // the work the taker gave away is no longer theirs to offer
        mine.City.CityStats.Update();
        theirs.City.CityStats.Update();
        context.Actor.UpdateStatsForNextTurn();
        partner.UpdateStatsForNextTurn();
        return [new GreatWorkSwapped(context.ActorId, CivId, given, Work)];
    }
}
