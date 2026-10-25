using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Tile;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Pay 200 gold to have a city-state's resource tile improved. Kotlin: <c>CityStateDiplomacyTable.getImproveTilesButton</c> (shown when some
/// unimproved resource tile can be improved by an improvement that can be built, disabled below 60 influence) and
/// <c>getImprovementGiftTable</c> (one button per tile and fitting improvement, disabled below 200 gold).
/// </summary>
[CommandType("gift-improvement")]
public sealed class GiftImprovement : GameCommand
{
    public const int Cost = 200;

    public string CivId { get; init; } = "";

    public HexCoord Tile { get; init; }

    public string Improvement { get; init; } = "";

    // Kotlin getImprovableResourceTiles
    private static IEnumerable<Tile> ImprovableResourceTiles(CivilizationObject cityState) =>
        cityState.Cities.SelectMany(it => it.GetTiles()).Where(tile =>
        {
            var resource = tile.TileResource;
            return resource != null && cityState.CanSeeResource(resource)
                && resource.ResourceType != ResourceType.Bonus
                && (tile.Improvement == null || !resource.IsImprovedBy(tile.Improvement));
        });

    private static bool Fits(Tile tile, TileImprovement improvement, CivilizationObject cityState) =>
        tile.TileResource.NotNull().IsImprovedBy(improvement.Name) && tile.ImprovementFunctions.CanBuildImprovement(improvement, cityState.State);

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.CityState, out var actor, out var other, out var failure)) return failure;
        if (other.Cities.Count == 0)
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{CivId} has no cities");
        var improvements = context.Game.Ruleset.TileImprovements;
        var tiles = ImprovableResourceTiles(other).ToList();
        // The button exists only when some improvement that can be built at all would do.
        if (!tiles.Any(tile => improvements.Values.Any(it => it.TurnsToBuild != -1 && Fits(tile, it, other))))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{CivId} has nothing to improve");
        if (other.GetDiplomacyManager(actor).NotNull().GetInfluence() < 60)
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{CivId} needs 60 influence for this");
        if (!improvements.TryGetValue(Improvement, out var improvement))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no improvement \"{Improvement}\"");
        var target = tiles.FirstOrDefault(it => it.Position == Tile);
        if (target == null || !Fits(target, improvement, other))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"{Improvement} cannot be built on that tile");
        return actor.Gold < Cost
            ? CommandValidation.Fail(CommandErrors.NotEnough("gold"), $"This costs {Cost} gold")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var other = context.ValidatedOther(CivId);
        var tile = context.TileAt(Tile).Value.NotNull();
        context.Actor.AddGold(-Cost);
        tile.StopWorkingOnImprovement();
        tile.SetImprovement(context.Game.Ruleset.TileImprovements[Improvement], other);
        other.Cache.UpdateCivResources();
        return [];
    }
}
