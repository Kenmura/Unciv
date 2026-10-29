using Unciv.Commands.Events;
using Unciv.Logic.Archaeology;
using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Commands.Archaeology;

/// <summary>
/// The actor decides what a finished archaeological dig becomes (bnw-spec/SPEC.md section 4.4.3): a Landmark of the tile's owner, or the
/// site's artifact in the closest free Art/Artifact slot (a writing site: a Writing slot). The artifact needs a free slot and a work left to find.
/// </summary>
[CommandType("choose-dig-reward")]
public sealed class ChooseDigReward : GameCommand
{
    public HexCoord Site { get; init; }

    /// <summary>True: take the artifact. False: build the Landmark.</summary>
    public bool TakeArtifact { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!ArchaeologyRules.IsEnabled(context.Game.Ruleset))
            return CommandValidation.Fail(CommandErrors.NotAllowed, "This ruleset has no archaeology");
        if (!context.TileAt(Site).TryGet(out var tile, out failure)) return failure;
        if (ArchaeologyRules.FindPending(context.Game, actor, Site.x, Site.y) == null)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, "There is no finished dig of yours there");
        if (!TakeArtifact)
            return ArchaeologyRules.LandmarkImprovement(context.Game.Ruleset) != null
                ? CommandValidation.Ok
                : CommandValidation.Fail(CommandErrors.NotAllowed, "This ruleset has no Landmark");
        return ArchaeologyRules.FindArtifact(actor, tile) != null
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, "No artifact is left to find or no slot is free for it");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        var tile = context.TileAt(Site).Value.NotNull();
        ArchaeologyRules.RemovePending(context.Game, ArchaeologyRules.FindPending(context.Game, actor, Site.x, Site.y).NotNull());
        if (!TakeArtifact)
        {
            ArchaeologyRules.BuildLandmark(actor, tile);
            return [new DigResolved(actor.CivId, Site.x, Site.y, "", false)];
        }
        var (work, location) = ArchaeologyRules.FindArtifact(actor, tile).NotNull();
        ArchaeologyRules.TakeArtifact(actor, tile, work, location);
        return [new DigResolved(actor.CivId, Site.x, Site.y, work.Name, true)];
    }
}
