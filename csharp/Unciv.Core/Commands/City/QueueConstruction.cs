using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Utils;

namespace Unciv.Commands.City;

/// <summary>
/// Add a construction to the end (or the top) of a city's queue. Kotlin: <c>CityConstructionsTable.addConstructionToQueue</c> and
/// <c>CityScreenConstructionMenu</c> "Add to the top", i.e. <c>cannotAddConstructionToQueue</c> (queue not full, not a puppet, buildable,
/// no perpetual twice) plus <c>CityConstructions.canAddToQueue</c> (no building twice), then <c>CityView.tryAddToQueue[WithTile]</c>
/// and <c>tryReassignPopulation</c> when the new entry became the current construction.
/// A building with <c>CreatesOneImprovement</c> needs <see cref="Tile"/> (the UI makes the player pick it first) and is never
/// offered "to the top". Not copied: <c>tryAddToQueue</c> checks nothing.
/// </summary>
[CommandType("queue-construction")]
public sealed class QueueConstruction : GameCommand
{
    public string CityId { get; init; } = "";
    public string Name { get; init; } = "";
    public bool AddToTop { get; init; }
    public HexCoord? Tile { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        if (!CityCommandChecks.TryConstruction(city, Name, out var construction, out failure)) return failure;
        var constructions = city.CityConstructions;
        if (!constructions.CanAddToQueue(construction)
            || construction is PerpetualConstruction && constructions.IsBeingConstructedOrEnqueued(Name))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{Name} cannot be added to the queue of {city.Name}");

        var improvement = (construction as Building)?.GetImprovementToCreate(city.GetRuleset(), city.Civ);
        if (AddToTop && (construction is PerpetualConstruction || improvement != null))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{Name} cannot be added to the top of the queue");
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
        constructions.AddToQueue(constructions.GetConstruction(Name), AddToTop, tile);
        if (constructions.ConstructionQueue[0] == Name) city.ReassignPopulation();
        return [];
    }
}
