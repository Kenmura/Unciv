using Unciv.Utils;

namespace Unciv.Commands.City;

/// <summary>
/// Rename one of the actor's cities. Kotlin: CityRenamePopup (validate <c>it != ""</c>) calling
/// <c>CityView.tryRenameCity</c> (owner and current player). The name is stored as given, no translation.
/// </summary>
[CommandType("rename-city")]
public sealed class RenameCity : GameCommand
{
    public string CityId { get; init; } = "";
    public string Name { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnCity(actor, CityId).TryGet(out _, out failure)) return failure;
        return Name == ""
            ? CommandValidation.Fail(CommandErrors.NotAllowed, "A city name cannot be empty")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.OwnCity(context.Actor, CityId).Value.NotNull().Name = Name;
        return [];
    }
}
