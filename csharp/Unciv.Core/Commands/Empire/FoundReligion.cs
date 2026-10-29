using Unciv.Logic.Civilization.Managers;
using static Unciv.Constants;

namespace Unciv.Commands.Empire;

/// <summary>
/// Found the religion after a great prophet was used. Kotlin: <c>ReligiousBeliefsPickerScreen</c> with <c>pickIconAndName</c>: an icon of
/// <c>ruleset.religions</c> not yet in the game, a display name (the icon name itself, or one passing the rename popup's validation), the
/// beliefs for <c>getBeliefsToChooseAtFounding</c> in slot order; then <c>foundReligion(displayName, religionName)</c> followed by
/// <c>chooseBeliefs(beliefs, usingFreeBeliefs())</c>.
/// </summary>
[CommandType("found-religion")]
public sealed class FoundReligion : GameCommand
{
    // AskTextPopup of the rename button: maxLength 32 and these characters are filtered out of the input.
    private const int MaxNameLength = 32;
    private const string ForbiddenNameChars = "[]{}\"\\<>";

    public string ReligionName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public List<string> Beliefs { get; init; } = [];

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        var manager = actor.ReligionManager;
        var gameInfo = actor.GameInfo;
        if (manager.ReligionState != ReligionState.FoundingReligion || !actor.Cities.Any(it => it.Id == manager.FoundingCityId))
            return CommandValidation.Fail(CommandErrors.NotAllowed, "There is no religion to found");
        if (!gameInfo.Ruleset.Religions.Contains(ReligionName) || gameInfo.Religions.ContainsKey(ReligionName))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"\"{ReligionName}\" is not an available religion icon");
        bool nameOk = DisplayName == ReligionName
            || (DisplayName != "" && DisplayName.Length <= MaxNameLength && DisplayName.IndexOfAny(ForbiddenNameChars.ToCharArray()) < 0
                && DisplayName != NoReligionName
                && !gameInfo.Ruleset.Religions.Contains(DisplayName)
                && gameInfo.Religions.Values.All(it => it.Name != DisplayName && it.GetReligionDisplayName() != DisplayName)); // T242: Kotlin's picker only compares icon names; two humans naming a religion alike in the same round must not both succeed
        if (!nameOk)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"\"{DisplayName}\" is not a valid religion name");
        return ReligionCommandChecks.TryBeliefs(actor, Beliefs, manager.GetBeliefsToChooseAtFounding(), out _, out failure)
            ? CommandValidation.Ok
            : failure;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var manager = context.Actor.ReligionManager;
        var ruleset = context.Game.Ruleset;
        manager.FoundReligion(DisplayName, ReligionName);
        manager.ChooseBeliefs(Beliefs.Select(it => ruleset.Beliefs[it]).ToList(), manager.UsingFreeBeliefs());
        return [];
    }
}
