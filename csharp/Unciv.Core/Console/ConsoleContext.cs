using Unciv.Logic;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.DevConsole;

/// <summary>
/// What the Kotlin console read from the WorldScreen: the selected tile, unit and civilization. A headless engine has no screen, so the
/// client sends them along with the line, and they become part of the logged command.
/// </summary>
public sealed record ConsoleSelection(HexCoord? Tile = null, int? UnitId = null, string? Civ = null);

/// <summary>Kotlin: the lookups of <c>DevConsolePopup</c> (<c>getCivByName</c>, <c>getSelectedTile</c>, ...), over a game and a selection.</summary>
public sealed class ConsoleContext(GameInfo game, string actorId, ConsoleSelection selection)
{
    public GameInfo Game { get; } = game;

    public string ActorId { get; } = actorId;

    public ConsoleSelection Selection { get; } = selection;

    public CivilizationObject? GetCivByNameOrNull(CliInput name) => Game.Civilizations.FirstOrDefault(it => name.Equals(it.CivId));

    public CivilizationObject GetCivByName(CliInput name) =>
        GetCivByNameOrNull(name) ?? throw new ConsoleErrorException($"Unknown civ: {name}");

    /// <summary>The named civ, else the selected one (Kotlin <c>screen.selectedCiv</c>), else the actor.</summary>
    public CivilizationObject GetCivByNameOrSelected(CliInput? name)
    {
        if (name != null) return GetCivByName(name);
        return Selection.Civ != null && Game.GetCivilizationOrNull(Selection.Civ) is { } selected ? selected : Game.GetCivilization(ActorId);
    }

    public Tile GetSelectedTile() =>
        (Selection.Tile is { } position ? Game.TileMap.GetOrNull(position.x, position.y) : null) ?? throw new ConsoleErrorException("Select tile");

    /// <summary>The city owning the selected tile.</summary>
    public CityObject GetSelectedCity() =>
        GetSelectedTile().GetCity() ?? throw new ConsoleErrorException("Select tile belonging to city");

    public CityObject GetCity(CliInput cityName) =>
        Game.GetCities().FirstOrDefault(it => cityName.Equals(it.Name)) ?? throw new ConsoleErrorException($"Unknown city: {cityName}");

    public MapUnit GetSelectedUnit()
    {
        var units = GetSelectedTile().GetUnits().ToList();
        if (units.Count == 0) throw new ConsoleErrorException("Select tile with units");
        return units.FirstOrDefault(it => it.Id == Selection.UnitId) ?? units[0];
    }
}
