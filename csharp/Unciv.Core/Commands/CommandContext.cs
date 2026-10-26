using System.Diagnostics.CodeAnalysis;
using Unciv.Logic;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Commands;

/// <summary>Result of resolving an id: the object, or a failure with a stable code.</summary>
public readonly record struct Resolved<T>(T? Value, CommandValidation Failure) where T : class
{
    public static Resolved<T> Of(T value) => new(value, CommandValidation.Ok);

    public static Resolved<T> Fail(string code, string message) => new(null, CommandValidation.Fail(code, message));

    public bool TryGet([NotNullWhen(true)] out T? value, out CommandValidation failure)
    {
        value = Value;
        failure = Failure;
        return Value != null;
    }
}

/// <summary>
/// What a command sees: the game and the authenticated actor, plus id lookups that fail with stable codes
/// (<see cref="CommandErrors"/>). Lookups never mutate and never draw randomness.
/// </summary>
public sealed class CommandContext(GameInfo game, string actorId)
{
    /// <summary>The id the authority reserves for server commands; a client envelope claiming it is malformed.</summary>
    public const string ServerActor = "server";

    public GameInfo Game { get; } = game;

    public string ActorId { get; } = actorId;

    public bool IsServer => ActorId == ServerActor;

    /// <summary>The actor's civilization. Only valid after validation (the actor was resolved then).</summary>
    public CivilizationObject Actor => Game.GetCivilization(ActorId);

    public Resolved<CivilizationObject> Civ(string civId)
    {
        var civ = Game.GetCivilizationOrNull(civId);
        return civ == null
            ? Resolved<CivilizationObject>.Fail(CommandErrors.UnknownCiv, $"There is no civilization \"{civId}\"")
            : Resolved<CivilizationObject>.Of(civ);
    }

    /// <summary>
    /// May the actor issue ordinary player commands now? The single place that answers "is it your turn", so that
    /// sequential and simultaneous turns (T200/T202) differ in one spot and no command reads CurrentPlayerCiv itself.
    /// Sequential rule: the actor is a human civ, is the current player, and nobody has won yet (or the players chose one more turn).
    /// </summary>
    /// <param name="allowDefeated">True only for ending the turn: when every human is defeated the first one still controls the turn cycle (C024, Kotlin), so a spectating player must be able to go on.</param>
    public Resolved<CivilizationObject> RequireActive(bool allowDefeated = false)
    {
        if (IsServer)
            return Resolved<CivilizationObject>.Fail(CommandErrors.NotHuman, "The server is not a player");
        var civ = Civ(ActorId);
        if (civ.Value == null) return civ;
        if (!civ.Value.IsHuman() || civ.Value.IsSpectator())
            return Resolved<CivilizationObject>.Fail(CommandErrors.NotHuman, $"{ActorId} is not a human player");
        if (Game.VictoryData != null && !Game.OneMoreTurnMode)
            return Resolved<CivilizationObject>.Fail(CommandErrors.GameOver, "The game is over");
        if (civ.Value.IsDefeated() && !allowDefeated)
            return Resolved<CivilizationObject>.Fail(CommandErrors.NotAllowed, $"{ActorId} has been defeated");
        // TODO(T202): simultaneous turns replace this check (phase Playing, civ not ended).
        if (!civ.Value.IsCurrentPlayer())
            return Resolved<CivilizationObject>.Fail(CommandErrors.NotYourTurn, $"It is not {ActorId}'s turn");
        return civ;
    }

    /// <summary>A unit by <c>MapUnit.Id</c>, which must belong to <paramref name="owner"/>.</summary>
    public Resolved<MapUnit> OwnUnit(CivilizationObject owner, int unitId)
    {
        foreach (var civ in Game.Civilizations)
        {
            foreach (var unit in civ.Units.GetCivUnits())
            {
                if (unit.Id != unitId) continue;
                return ReferenceEquals(civ, owner)
                    ? Resolved<MapUnit>.Of(unit)
                    : Resolved<MapUnit>.Fail(CommandErrors.NotYourUnit, $"Unit {unitId} does not belong to {owner.CivId}");
            }
        }
        return Resolved<MapUnit>.Fail(CommandErrors.UnknownUnit, $"There is no unit {unitId}");
    }

    /// <summary>A city by its id, which must belong to <paramref name="owner"/>.</summary>
    public Resolved<CityObject> OwnCity(CivilizationObject owner, string cityId)
    {
        foreach (var civ in Game.Civilizations)
        {
            foreach (var city in civ.Cities)
            {
                if (city.Id != cityId) continue;
                return ReferenceEquals(civ, owner)
                    ? Resolved<CityObject>.Of(city)
                    : Resolved<CityObject>.Fail(CommandErrors.NotYourCity, $"City {cityId} does not belong to {owner.CivId}");
            }
        }
        return Resolved<CityObject>.Fail(CommandErrors.UnknownCity, $"There is no city \"{cityId}\"");
    }

    public Resolved<Tile> TileAt(HexCoord position)
    {
        var tile = Game.TileMap.GetOrNull(position.x, position.y);
        return tile == null
            ? Resolved<Tile>.Fail(CommandErrors.UnknownTile, $"There is no tile at {position.x},{position.y}")
            : Resolved<Tile>.Of(tile);
    }
}
