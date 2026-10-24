using Unciv.Logic.Battle;
using Unciv.Logic.Map;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unique;

// Ported from core/src/com/unciv/models/ruleset/unique/GameContext.kt.
//
// GameContext refers to Civilization, City, MapUnit, Tile, Region, ICombatant and GameInfo, none of which exist yet. It only ever reads
// a handful of members from them, so those members are the interfaces below. Civilization, City, MapUnit, Tile, Region, the combatants
// and GameInfo implement them when they are ported (T046 onwards); nothing else here changes. Member names mirror the Kotlin members.

/// <summary>What <see cref="GameContext"/> reads from <c>GameInfo</c>.</summary>
public interface IGameContextGameInfo
{
    int Turns { get; }
    /// <summary><c>GameInfo.gameId</c>.</summary>
    string? GameId { get; }
    /// <summary><c>tileMap.mapParameters.seed</c>, or <c>null</c> when there is no tile map.</summary>
    long? MapSeed { get; }
}

/// <summary>What <see cref="GameContext"/> reads from <c>Civilization</c>.</summary>
public interface IGameContextCivilization
{
    /// <summary><c>civ.gameInfo</c>; <c>null</c> while it is not set (map generation).</summary>
    IGameContextGameInfo? GameInfo { get; }
    /// <summary><c>civ.civID</c>.</summary>
    string CivId { get; }
    int GetResourceAmount(string resourceName);
    int GetStatReserve(Stat stat);
}

/// <summary>What <see cref="GameContext"/> reads from <c>City</c>.</summary>
public interface IGameContextCity
{
    /// <summary><c>city.id</c>.</summary>
    string Id { get; }
    IGameContextCivilization Civ { get; }
    IGameContextTile? GetCenterTileOrNull();
    int GetAvailableResourceAmount(string resourceName);
    int GetStatReserve(Stat stat);
}

/// <summary>What <see cref="GameContext"/> reads from <c>MapUnit</c>.</summary>
public interface IGameContextUnit
{
    string Name { get; }
    IGameContextCivilization Civ { get; }
    bool HasTile();
    IGameContextTile GetTile();
    float CurrentMovement { get; }
}

/// <summary>What <see cref="GameContext"/> reads from <c>Tile</c>.</summary>
public interface IGameContextTile
{
    HexCoord Position { get; }
    /// <summary><c>tile.tileMap.gameInfo</c>; <c>null</c> while it is not set (map generation).</summary>
    IGameContextGameInfo? GameInfo { get; }
    IGameContextCity? GetCity();
    IEnumerable<IGameContextTile> Neighbors { get; }
    bool MatchesFilter(string filter);
}

/// <summary>
/// What <see cref="GameContext"/> reads from <c>ICombatant</c>. Kotlin tests <c>is MapUnitCombatant</c> and <c>is CityCombatant</c>:
/// <see cref="Unit"/> is non-null exactly for a MapUnitCombatant, <see cref="City"/> exactly for a CityCombatant.
/// </summary>
public interface IGameContextCombatant
{
    IGameContextCivilization GetCivInfo();
    IGameContextTile GetTile();
    string GetName();
    IGameContextUnit? Unit { get; }
    IGameContextCity? City { get; }
}

/// <summary>What <see cref="GameContext"/> reads from <c>Region</c>.</summary>
public interface IGameContextRegion
{
    /// <summary><c>region.rect.hashCode()</c> (libGDX <c>Rectangle</c>).</summary>
    int RectHashCode { get; }
}

/// <summary>
/// The state conditionals and multipliers of a <see cref="Unique"/> are evaluated against.
/// Kotlin <c>data class</c>: <see cref="Equals(object?)"/> is memberwise, <see cref="GetHashCode"/> is the custom state hash.
/// </summary>
/// <remarks>
/// Deviations: Kotlin's <c>copy()</c> is not ported (add it with the first caller); an explicitly passed <c>gameInfo = null</c>
/// is indistinguishable from an omitted one, so it is derived from the other members (Kotlin would keep <c>null</c>).
/// </remarks>
public sealed class GameContext : IEquatable<GameContext>
{
    public GameContext(
        IGameContextCivilization? civInfo = null,
        IGameContextCity? city = null,
        IGameContextUnit? unit = null,
        IGameContextTile? tile = null,
        IGameContextCombatant? ourCombatant = null,
        IGameContextCombatant? theirCombatant = null,
        IGameContextTile? attackedTile = null,
        CombatAction? combatAction = null,
        IGameContextCivilization? otherCiv = null,
        IGameContextRegion? region = null,
        IGameContextGameInfo? gameInfo = null,
        bool ignoreConditionals = false)
    {
        CivInfo = civInfo;
        City = city;
        Unit = unit;
        Tile = tile;
        OurCombatant = ourCombatant;
        TheirCombatant = theirCombatant;
        AttackedTile = attackedTile;
        CombatAction = combatAction;
        OtherCiv = otherCiv;
        Region = region;
        // tile and region do not deduce gameInfo because that field is not set during MapGeneration,
        // and querying if its initialized is non-trivial
        GameInfo = gameInfo
            ?? civInfo?.GameInfo ?? city?.Civ.GameInfo ?? unit?.Civ.GameInfo
            ?? ourCombatant?.GetCivInfo().GameInfo ?? theirCombatant?.GetCivInfo().GameInfo ?? attackedTile?.GameInfo
            ?? otherCiv?.GameInfo;
        IgnoreConditionals = ignoreConditionals;
    }

    // The secondary constructors of the Kotlin class. C# constructors cannot share a signature, so these are factories.

    public static GameContext ForCity(IGameContextCity city) =>
        new(city.Civ, city, tile: city.GetCenterTileOrNull(), gameInfo: city.Civ.GameInfo);

    public static GameContext ForUnit(IGameContextUnit unit) =>
        new(unit.Civ, unit: unit, tile: unit.HasTile() ? unit.GetTile() : null, gameInfo: unit.Civ.GameInfo);

    public static GameContext ForCiv(IGameContextCivilization civ, IGameContextCivilization? civ2 = null) =>
        new(civ, otherCiv: civ2, gameInfo: civ.GameInfo);

    public static GameContext ForCombatants(IGameContextCombatant ourCombatant, IGameContextCombatant? theirCombatant = null,
        IGameContextTile? attackedTile = null, CombatAction? combatAction = null) =>
        new(
            ourCombatant.GetCivInfo(),
            ourCombatant.City,
            ourCombatant.Unit,
            ourCombatant.GetTile(),
            ourCombatant,
            theirCombatant,
            attackedTile,
            combatAction,
            theirCombatant?.GetCivInfo(),
            gameInfo: ourCombatant.GetCivInfo().GameInfo);

    public IGameContextCivilization? CivInfo { get; }
    public IGameContextCity? City { get; }
    public IGameContextUnit? Unit { get; }
    public IGameContextTile? Tile { get; }

    public IGameContextCombatant? OurCombatant { get; }
    public IGameContextCombatant? TheirCombatant { get; }
    public IGameContextTile? AttackedTile { get; }
    public CombatAction? CombatAction { get; }

    public IGameContextCivilization? OtherCiv { get; }

    public IGameContextRegion? Region { get; }
    public IGameContextGameInfo? GameInfo { get; }

    public bool IgnoreConditionals { get; }

    // Kotlin `by lazy`: pure functions of the immutable constructor state, so they are computed on first use.
    private bool relevantUnitComputed;
    private IGameContextUnit? relevantUnit;
    public IGameContextUnit? RelevantUnit
    {
        get
        {
            if (!relevantUnitComputed)
            {
                relevantUnit = OurCombatant != null && OurCombatant.Unit != null ? OurCombatant.Unit : Unit;
                relevantUnitComputed = true;
            }
            return relevantUnit;
        }
    }

    private bool relevantTileComputed;
    private IGameContextTile? relevantTile;
    public IGameContextTile? RelevantTile
    {
        get
        {
            if (!relevantTileComputed)
            {
                relevantTile = AttackedTile
                    ?? Tile
                    // We need to protect against conditionals checking tiles for units pre-placement - see #10425, #10512
                    ?? (RelevantUnit != null && RelevantUnit.HasTile() ? RelevantUnit.GetTile() : null)
                    ?? City?.GetCenterTileOrNull();
                relevantTileComputed = true;
            }
            return relevantTile;
        }
    }

    private bool relevantCityComputed;
    private IGameContextCity? relevantCity;
    public IGameContextCity? RelevantCity
    {
        get
        {
            if (!relevantCityComputed)
            {
                relevantCity = ComputeRelevantCity();
                relevantCityComputed = true;
            }
            return relevantCity;
        }
    }

    private IGameContextCity? ComputeRelevantCity()
    {
        if (City != null) return City;
        // Edge case: If we attack a city, the "relevant tile" becomes the attacked tile -
        //  but we DO NOT want that city to become the relevant city because then *our* conditionals get checked against
        //  the *other civ's* cities, leading to e.g. resource amounts being defined as the *other civ's* resource amounts
        var relevantTileForCity = Tile ?? (RelevantUnit != null && RelevantUnit.HasTile() ? RelevantUnit.GetTile() : null);
        var cityForRelevantTile = relevantTileForCity?.GetCity();
        if (cityForRelevantTile != null &&
            // ...and we can't use the relevantCiv here either, because that'll cause a loop
            (Equals(cityForRelevantTile.Civ, CivInfo) || Equals(cityForRelevantTile.Civ, RelevantUnit?.Civ)))
            return cityForRelevantTile;
        return null;
    }

    public IGameContextCivilization? RelevantCiv => CivInfo ?? RelevantCity?.Civ ?? RelevantUnit?.Civ;

    public GameRandom StateBasedRandom(string caller, int seed = 31) =>
        Randomness.StateBasedRandom(caller, seed, GetHashCode());

    public int GetResourceAmount(string resourceName)
    {
        if (RelevantCity != null) return RelevantCity.GetAvailableResourceAmount(resourceName);
        if (RelevantCiv != null) return RelevantCiv.GetResourceAmount(resourceName);
        return 0;
    }

    public int GetStatAmount(Stat stat)
    {
        if (RelevantCity != null) return RelevantCity.GetStatReserve(stat);
        if (RelevantCiv != null && StatExtensions.StatsWithCivWideField.Contains(stat)) return RelevantCiv.GetStatReserve(stat);
        return 0;
    }

    /// <summary>Kotlin <c>GameContext.IgnoreConditionals</c>; renamed because the instance property <see cref="IgnoreConditionals"/> has the same name in PascalCase.</summary>
    public static readonly GameContext IgnoreConditionalsState = new(ignoreConditionals: true);
    public static readonly GameContext EmptyState = new();
    /// <summary>
    /// When caching uniques, we need to cache them unmultiplied, and apply multiplication only on retrieval from cache
    /// This state lets the multiplication function know that it's always 1:1
    /// </summary>
    public static readonly GameContext IgnoreMultiplicationForCaching = new(ignoreConditionals: true);

    /// <summary>Used ONLY for stateBasedRandom in <c>Conditionals.conditionalApplies</c> to prevent save scumming on <c>ConditionalChance</c>.</summary>
    public override int GetHashCode()
    {
        static int CivHash(IGameContextCivilization? civ) => civ == null ? 0 : JavaHash.HashCode(civ.CivId);
        static int CityHash(IGameContextCity? city) => city == null ? 0 : JavaHash.HashCode(city.Id);
        static int TileHash(IGameContextTile? tile) => tile == null ? 0 : tile.Position.GetHashCode();
        static int UnitHash(IGameContextUnit? unit) => unit == null ? 0 :
            JavaHash.HashCode(unit.Name) +
            (unit.HasTile() ? 17 * TileHash(unit.GetTile()) : 0) +
            JavaHash.HashCode(17 * unit.CurrentMovement);
        static int CombatantHash(IGameContextCombatant? combatant) => combatant == null ? 0 :
            combatant.Unit != null ? UnitHash(combatant.Unit) // line only serves as `lateinit currentTile not initialized` guard
            : JavaHash.HashCode(combatant.GetName()) + 17 * TileHash(combatant.GetTile());
        static int CombatActionHash(CombatAction? action) => action == null ? 0 : JavaHash.HashCode(action.Value.ToString());
        static int RegionHash(IGameContextRegion? region) => region == null ? 0 : region.RectHashCode;

        int gameIdOrSeedHash = GameInfo?.GameId != null ? JavaHash.HashCode(GameInfo.GameId)
            : GameInfo?.MapSeed != null ? JavaHash.HashCode(GameInfo.MapSeed.Value)
            : 0;

        return Randomness.HashOf(
            GameInfo?.Turns ?? 0,
            gameIdOrSeedHash,
            CivHash(RelevantCiv),
            CityHash(RelevantCity),
            UnitHash(RelevantUnit),
            TileHash(RelevantTile),
            CombatantHash(OurCombatant),
            CombatantHash(TheirCombatant),
            TileHash(AttackedTile),
            CombatActionHash(CombatAction),
            CivHash(OtherCiv),
            RegionHash(Region),
            JavaHash.HashCode(IgnoreConditionals));
    }

    public bool Equals(GameContext? other) =>
        other != null
        && Equals(CivInfo, other.CivInfo) && Equals(City, other.City) && Equals(Unit, other.Unit) && Equals(Tile, other.Tile)
        && Equals(OurCombatant, other.OurCombatant) && Equals(TheirCombatant, other.TheirCombatant)
        && Equals(AttackedTile, other.AttackedTile) && CombatAction == other.CombatAction
        && Equals(OtherCiv, other.OtherCiv) && Equals(Region, other.Region) && Equals(GameInfo, other.GameInfo)
        && IgnoreConditionals == other.IgnoreConditionals;

    public override bool Equals(object? obj) => obj is GameContext other && Equals(other);
}
