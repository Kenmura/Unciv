namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Give gold to a city-state for influence. Kotlin: <c>CityStateDiplomacyTable.getGoldGiftTable</c> offers gifts of 250, 500 and 1000 gold,
/// disabled when we are at war with it or cannot pay, then <c>cityStateFunctions.receiveGoldGift</c>.
/// </summary>
[CommandType("gift-gold")]
public sealed class GiftGold : GameCommand
{
    public static readonly IReadOnlyList<int> Amounts = [250, 500, 1000];

    public string CivId { get; init; } = "";

    public int Amount { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.CityState, out var actor, out var other, out var failure)) return failure;
        if (!Amounts.Contains(Amount))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, "A gift is 250, 500 or 1000 gold");
        if (actor.IsAtWarWith(other))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"You are at war with {CivId}");
        return actor.Gold < Amount
            ? CommandValidation.Fail(CommandErrors.NotEnough("gold"), $"A gift of {Amount} gold needs {Amount} gold")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        DiplomacyCommandChecks.CityState(context.ValidatedOther(CivId)).ReceiveGoldGift(context.Actor, Amount);
        return [];
    }
}
