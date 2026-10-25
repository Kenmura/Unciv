using Unciv.Utils;

namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Declare war on a civilization. Kotlin: <c>DiplomacyScreen.getDeclareWarButton</c> (offered in both tables while not at war and the mod lets
/// relationships change, disabled while a peace treaty runs, i.e. <c>turnsToPeaceTreaty() &gt; 0</c>) then <c>diplomacyManager.declareWar()</c>,
/// a <c>WarType.DirectWar</c>. Wars that come out of a trade (<c>WarDeclaration</c> offers) are declared by the trade itself.
/// </summary>
[CommandType("declare-war")]
public sealed class DeclareWar : GameCommand
{
    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.Any, out var actor, out var other, out var failure)) return failure;
        if (!context.RelationshipsCanChange()) return DiplomacyCommandChecks.RelationshipsFixed();
        if (actor.IsAtWarWith(other))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"You are already at war with {CivId}");
        return actor.GetDiplomacyManager(other).NotNull().TurnsToPeaceTreaty() > 0
            ? CommandValidation.Fail(CommandErrors.NotAllowed, $"A peace treaty with {CivId} is still in force")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Actor.GetDiplomacyManager(context.ValidatedOther(CivId)).NotNull().DeclareWar();
        return [];
    }
}
