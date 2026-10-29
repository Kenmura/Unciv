using Unciv.Logic.GreatWorks;
using Unciv.Utils;

namespace Unciv.Commands.GreatWorks;

/// <summary>
/// Designates the work in a slot as the actor's swap offer for its class, replacing an earlier offer of that class, or (with
/// <see cref="Withdraw"/>) takes the standing offer for that work back (bnw-spec/SPEC.md section 4.2.3). Music cannot be offered.
/// </summary>
[CommandType("offer-great-work-swap")]
public sealed class OfferGreatWorkSwap : GameCommand
{
    public string CityId { get; init; } = "";
    public string Building { get; init; } = "";
    public int Slot { get; init; }
    public bool Withdraw { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryActiveActor(out var actor, out var failure)) return failure;
        if (!context.TrySlot(actor, CityId, Building, Slot, out var location, out failure)) return failure;
        if (!context.TryWorkIn(location.Value, out var work, out failure)) return failure;
        if (!GreatWorkRules.IsSwappable(work.GreatWorkClass))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{work.Name} cannot be offered for a swap");
        bool offered = context.Game.GreatWorkSwapOffers?.Any(it => it.Civ == actor.CivId && it.Work == work.Name) == true;
        if (Withdraw) return offered ? CommandValidation.Ok : CommandValidation.Fail(CommandErrors.InvalidTarget, $"{work.Name} is not on offer");
        return offered ? CommandValidation.Fail(CommandErrors.NotAllowed, $"{work.Name} is already on offer") : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        var game = context.Game;
        var location = context.TrySlot(actor, CityId, Building, Slot, out var found, out _) ? found.Value : default;
        var work = game.Ruleset.GreatWorks[location.City.GetGreatWorkIn(Building, Slot).NotNull()];
        if (Withdraw)
        {
            GreatWorkRules.RemoveOffer(game, actor.CivId, work.Name);
            return [];
        }
        GreatWorkRules.RemoveOffers(game, actor, work.GreatWorkClass);
        (game.GreatWorkSwapOffers ??= []).Add(new GreatWorkSwapOffer { Civ = actor.CivId, Work = work.Name });
        return [];
    }
}
