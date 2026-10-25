using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Commands.City;

/// <summary>
/// Buy a building or unit. Kotlin: <c>BuyButtonFactory</c> (shown: <c>canBePurchasedWithStat</c>; enabled and confirmed:
/// <c>isConstructionPurchaseAllowed(construction, stat, getStatBuyCost)</c>), then <c>CityConstructions.purchaseConstruction(construction,
/// selectedQueueEntry, stat, tile)</c>. The price is computed here, never sent. <see cref="QueueIndex"/> is the selected queue entry or -1; if
/// given it must hold the construction. <see cref="Tile"/> is the tile of a <c>CreatesOneImprovement</c> building that has no marker yet
/// (the UI always has the player pick one). Puppets are allowed because <c>isConstructionPurchaseAllowed</c> decides that itself.
/// </summary>
[CommandType("purchase-construction")]
public sealed class PurchaseConstruction : GameCommand
{
    public string CityId { get; init; } = "";
    public string Name { get; init; } = "";
    public Stat Stat { get; init; } = Stat.Gold;
    public int QueueIndex { get; init; } = -1;
    public HexCoord? Tile { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: true, out var actor, out var city, out var failure)) return failure;
        if (!CityCommandChecks.TryConstruction(city, Name, out var construction, out failure)) return failure;
        if (construction is not INonPerpetualConstruction purchasable || !StatExtensions.StatsUsableToBuy.Contains(Stat)
            || !purchasable.CanBePurchasedWithStat(city, Stat))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{Name} cannot be bought with {Stat}");
        var constructions = city.CityConstructions;
        var queue = constructions.ConstructionQueue;
        if (QueueIndex < -1 || QueueIndex >= queue.Count || QueueIndex >= 0 && queue[QueueIndex] != Name)
            return CommandValidation.Fail(CommandErrors.StaleReference, $"Queue entry {QueueIndex} is not {Name}");
        if (constructions.IsConstructionPurchaseBlockedByUnit(purchasable))
            return CommandValidation.Fail(CommandErrors.PurchaseBlockedByUnit, "Move the unit out of the city first");

        int? cost = purchasable.GetStatBuyCost(city, Stat);
        if (cost == null) return CommandValidation.Fail(CommandErrors.NotAllowed, $"{Name} has no price in {Stat}");
        if (!constructions.IsConstructionPurchaseAllowed(purchasable, Stat, cost.Value))
            return city.HasStatToBuy(Stat, cost.Value)
                ? CommandValidation.Fail(CommandErrors.NotAllowed, $"{Name} cannot be bought in {city.Name} now")
                : CommandValidation.Fail(CommandErrors.NotEnough(Stat.ToString()), $"{actor.CivId} cannot afford {cost} {Stat}");

        var improvement = (construction as Building)?.GetImprovementToCreate(city.GetRuleset(), city.Civ);
        bool needsTile = improvement != null && constructions.GetTileForImprovement(improvement.Name) == null;
        if (!needsTile)
            return Tile == null ? CommandValidation.Ok : CommandValidation.Fail(CommandErrors.InvalidTarget, $"{Name} needs no tile");
        if (Tile == null)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"{Name} needs a tile for its {improvement.NotNull().Name}");
        if (!context.TileAt(Tile.Value).TryGet(out var tile, out failure)) return failure;
        return constructions.CanPlaceCreateOneImprovementOn(improvement.NotNull(), tile)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, $"{improvement.NotNull().Name} cannot be created on that tile");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.ValidatedCity(CityId);
        var constructions = city.CityConstructions;
        var tile = Tile == null ? null : context.TileAt(Tile.Value).Value;
        // Bought from the queue: pass the marked tile down like BuyButtonFactory, otherwise automation would choose another one.
        if (tile == null && constructions.GetConstruction(Name) is Building building
            && building.GetImprovementToCreate(city.GetRuleset(), city.Civ) is { } improvement)
            tile = constructions.GetTileForImprovement(improvement.Name);
        // The result is true on every path Validate admits (unit placement was checked); the UI only reports a false one.
        city.CityConstructions.PurchaseConstruction(Name, QueueIndex, automatic: false, Stat, tile);
        return [];
    }
}
