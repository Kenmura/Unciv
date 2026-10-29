using System.Diagnostics.CodeAnalysis;
using Unciv.Logic.GreatWorks;
using Unciv.Models.Ruleset;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Commands.GreatWorks;

/// <summary>The checks the Great Work commands share (B007, bnw-spec/SPEC.md section 4.2).</summary>
internal static class GreatWorkCommandChecks
{
    /// <summary>The actor is active and the ruleset has Great Works switched on.</summary>
    public static bool TryActiveActor(this CommandContext context, [NotNullWhen(true)] out CivilizationObject? actor, out CommandValidation failure)
    {
        if (!context.RequireActive().TryGet(out actor, out failure)) return false;
        if (!GreatWorkRules.IsEnabled(context.Game.Ruleset))
        {
            failure = CommandValidation.Fail(CommandErrors.NotAllowed, "This ruleset has no Great Works");
            return false;
        }
        return true;
    }

    /// <summary>A slot of one of the actor's cities: the building is built there and has that slot.</summary>
    public static bool TrySlot(this CommandContext context, CivilizationObject actor, string cityId, string buildingName, int slot,
        [NotNullWhen(true)] out GreatWorkLocation? location, out CommandValidation failure)
    {
        location = null;
        if (!context.OwnCity(actor, cityId).TryGet(out var city, out failure)) return false;
        if (!context.Game.Ruleset.Buildings.TryGetValue(buildingName, out var building) || !city.CityConstructions.IsBuilt(buildingName)
            || building.GreatWorkSlotType == null || slot < 0 || slot >= building.GreatWorkSlots)
        {
            failure = CommandValidation.Fail(CommandErrors.InvalidTarget, $"{city.Name} has no Great Work slot {slot} in \"{buildingName}\"");
            return false;
        }
        location = new GreatWorkLocation(city, building, slot);
        failure = CommandValidation.Ok;
        return true;
    }

    /// <summary>The work in a slot, with its ruleset object; false (with a failure) when the slot is empty.</summary>
    public static bool TryWorkIn(this CommandContext context, GreatWorkLocation location, [NotNullWhen(true)] out GreatWork? work, out CommandValidation failure)
    {
        work = null;
        string? name = location.City.GetGreatWorkIn(location.Building.Name, location.Slot);
        if (name == null || !context.Game.Ruleset.GreatWorks.TryGetValue(name, out work))
        {
            failure = CommandValidation.Fail(CommandErrors.InvalidTarget, $"Slot {location.Slot} of {location.Building.Name} in {location.City.Name} holds no Great Work");
            return false;
        }
        failure = CommandValidation.Ok;
        return true;
    }
}
