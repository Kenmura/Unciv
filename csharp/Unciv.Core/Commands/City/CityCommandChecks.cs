using System.Diagnostics.CodeAnalysis;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Commands.City;

/// <summary>
/// The checks every city command shares. Kotlin: <c>CityView.canChangeState()</c> (the city is the viewer's and it is the viewer's turn,
/// answered here by <see cref="CommandContext.RequireActive"/>) and <c>CityScreen.canCityBeChanged()</c> (additionally not a puppet).
/// </summary>
internal static class CityCommandChecks
{
    /// <summary>An own city that a command may change. <paramref name="allowPuppet"/> is true for the few commands the UI offers on puppets.</summary>
    public static bool TryChangeableCity(this CommandContext context, string cityId, bool allowPuppet,
        [NotNullWhen(true)] out CivilizationObject? actor, [NotNullWhen(true)] out CityObject? city, out CommandValidation failure)
    {
        city = null;
        if (!context.RequireActive().TryGet(out actor, out failure)) return false;
        if (!context.OwnCity(actor, cityId).TryGet(out city, out failure)) return false;
        if (city.IsPuppet && !allowPuppet)
        {
            failure = CommandValidation.Fail(CommandErrors.NotAllowed, $"{city.Name} is a puppet city");
            return false;
        }
        failure = CommandValidation.Ok;
        return true;
    }

    /// <summary>The city of an already validated command.</summary>
    public static CityObject ValidatedCity(this CommandContext context, string cityId) =>
        context.OwnCity(context.Actor, cityId).Value.NotNull();

    /// <summary>A building, unit or perpetual construction of the city's ruleset by name (the empty name is not a construction here).</summary>
    public static bool TryConstruction(CityObject city, string name, [NotNullWhen(true)] out IConstruction? construction, out CommandValidation failure)
    {
        var ruleset = city.GetRuleset();
        construction = null;
        if (name.Length != 0)
        {
            if (ruleset.Buildings.TryGetValue(name, out var building)) construction = building;
            else if (ruleset.Units.TryGetValue(name, out var unit)) construction = unit;
            else if (PerpetualConstruction.TryGetPerpetual(name, out var perpetual)) construction = perpetual;
        }
        failure = construction == null
            ? CommandValidation.Fail(CommandErrors.InvalidTarget, $"\"{name}\" is not a building, unit or perpetual construction")
            : CommandValidation.Ok;
        return construction != null;
    }

    /// <summary>The tile of the command, which must additionally be a tile of the city's own territory in work range and not blockaded (CityTileGroup shows blockaded tiles as not workable).</summary>
    public static bool TryWorkableTile(this CommandContext context, CityObject city, HexCoord position,
        [NotNullWhen(true)] out TileClass? tile, out CommandValidation failure)
    {
        if (!context.TileAt(position).TryGet(out tile, out failure)) return false;
        if (!city.GetWorkableTiles().Contains(tile) || tile.IsBlockaded())
        {
            failure = CommandValidation.Fail(CommandErrors.InvalidTarget, $"{city.Name} cannot work the tile at {position.x},{position.y}");
            return false;
        }
        return true;
    }
}
