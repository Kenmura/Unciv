using Unciv.Models.Ruleset.Unique;

namespace Unciv.Commands.Empire;

/// <summary>
/// Set the share of gold converted to science. Kotlin: the <c>StatsOverviewTab</c> slider (0..1, step 0.1, only with the
/// <c>ConvertGoldToScience</c> mod option) calls <c>trySetGoldPercentConvertedToScience</c> and <c>tryUpdateAllCityStats</c>.
/// The payload is an int percent (a multiple of 10) so that no float travels on the wire; Apply stores <c>percent / 100f</c>.
/// </summary>
[CommandType("set-gold-to-science")]
public sealed class SetGoldToScience : GameCommand
{
    public int Percent { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!((IHasUniques)actor.GameInfo.Ruleset.ModOptions).HasUnique(UniqueType.ConvertGoldToScience))
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Gold cannot be converted to science in this ruleset");
        return Percent is >= 0 and <= 100 && Percent % 10 == 0
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "The percentage must be 0 to 100 in steps of 10");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        actor.Tech.GoldPercentConvertedToScience = Percent / 100f;
        foreach (var city in actor.Cities) city.CityStats.Update();
        return [];
    }
}
