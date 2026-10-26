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
public partial interface IGameContextGameInfo
{
    int Turns { get; }
    /// <summary><c>GameInfo.gameId</c>.</summary>
    string? GameId { get; }
    /// <summary><c>tileMap.mapParameters.seed</c>, or <c>null</c> when there is no tile map.</summary>
    long? MapSeed { get; }
    /// <summary>T230: the host secret mixed into state-based random seeds; 0 when none is set. Never serialized, never sent to clients.</summary>
    long RandomSalt => 0;
}

/// <summary>What <see cref="GameContext"/> reads from <c>Civilization</c>.</summary>
public partial interface IGameContextCivilization
{
    /// <summary><c>civ.gameInfo</c>; <c>null</c> while it is not set (map generation).</summary>
    IGameContextGameInfo? GameInfo { get; }
    /// <summary><c>civ.civID</c>.</summary>
    string CivId { get; }
    int GetResourceAmount(string resourceName);
    int GetStatReserve(Stat stat);
}

/// <summary>What <see cref="GameContext"/> reads from <c>City</c>.</summary>
public partial interface IGameContextCity
{
    /// <summary><c>city.id</c>.</summary>
    string Id { get; }
    IGameContextCivilization Civ { get; }
    IGameContextTile? GetCenterTileOrNull();
    int GetAvailableResourceAmount(string resourceName);
    int GetStatReserve(Stat stat);
}

/// <summary>What <see cref="GameContext"/> reads from <c>MapUnit</c>.</summary>
public partial interface IGameContextUnit
{
    string Name { get; }
    IGameContextCivilization Civ { get; }
    bool HasTile();
    IGameContextTile GetTile();
    float CurrentMovement { get; }
}

/// <summary>What <see cref="GameContext"/> reads from <c>Tile</c>.</summary>
public partial interface IGameContextTile
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
public partial interface IGameContextCombatant
{
    IGameContextCivilization GetCivInfo();
    IGameContextTile GetTile();
    string GetName();
    IGameContextUnit? Unit { get; }
    IGameContextCity? City { get; }
}

/// <summary>What <see cref="GameContext"/> reads from <c>Region</c>.</summary>
public partial interface IGameContextRegion
{
    /// <summary><c>region.rect.hashCode()</c> (libGDX <c>Rectangle</c>).</summary>
    int RectHashCode { get; }
}

/// <summary>
/// The state conditionals and multipliers of a <see cref="Unique"/> are evaluated against.
/// Kotlin <c>data class</c>: <see cref="Equals(object?)"/> is memberwise, <see cref="GetHashCode"/> is the custom state hash.
/// </summary>
/// <remarks>
/// Deviations: the public constructor derives <c>gameInfo</c> when it is omitted or <c>null</c> (C# cannot tell a default argument
/// from an explicit <c>null</c>); the factories and <see cref="Copy"/> pass <c>gameInfo</c> through explicitly like Kotlin, so a
/// <c>null</c> there stays <c>null</c>.
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
        : this(civInfo, city, unit, tile, ourCombatant, theirCombatant, attackedTile, combatAction, otherCiv, region,
            gameInfo
                ?? civInfo?.GameInfo ?? city?.Civ.GameInfo ?? unit?.Civ.GameInfo
                ?? ourCombatant?.GetCivInfo().GameInfo ?? theirCombatant?.GetCivInfo().GameInfo ?? attackedTile?.GameInfo
                ?? otherCiv?.GameInfo,
            ignoreConditionals, gameInfoIsFinal: true)
    {
        // tile and region do not deduce gameInfo because that field is not set during MapGeneration,
        // and querying if its initialized is non-trivial
    }

    // The Kotlin primary constructor: every member is taken as given, including a null gameInfo.
    // `gameInfoIsFinal` only separates the signature from the public constructor.
    private GameContext(
        IGameContextCivilization? civInfo,
        IGameContextCity? city,
        IGameContextUnit? unit,
        IGameContextTile? tile,
        IGameContextCombatant? ourCombatant,
        IGameContextCombatant? theirCombatant,
        IGameContextTile? attackedTile,
        CombatAction? combatAction,
        IGameContextCivilization? otherCiv,
        IGameContextRegion? region,
        IGameContextGameInfo? gameInfo,
        bool ignoreConditionals,
        bool gameInfoIsFinal)
    {
        _ = gameInfoIsFinal;
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
        GameInfo = gameInfo;
        IgnoreConditionals = ignoreConditionals;
    }

    // The secondary constructors of the Kotlin class. C# constructors cannot share a signature, so these are factories.

    public static GameContext ForCity(IGameContextCity city) =>
        new(city.Civ, city, null, city.GetCenterTileOrNull(), null, null, null, null, null, null, city.Civ.GameInfo, false, true);

    public static GameContext ForUnit(IGameContextUnit unit) =>
        new(unit.Civ, null, unit, unit.HasTile() ? unit.GetTile() : null, null, null, null, null, null, null, unit.Civ.GameInfo, false, true);

    public static GameContext ForCiv(IGameContextCivilization civ, IGameContextCivilization? civ2 = null) =>
        new(civ, null, null, null, null, null, null, null, civ2, null, civ.GameInfo, false, true);

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
            null,
            ourCombatant.GetCivInfo().GameInfo,
            false,
            true);

    /// <summary>
    /// A value for one <see cref="Copy"/> parameter. A parameter left out (<c>null</c>) keeps the current member; passing
    /// <c>new CopyArg&lt;T&gt;(null)</c> sets it to <c>null</c>, as Kotlin's named <c>copy(tile = null)</c> does.
    /// </summary>
    public readonly record struct CopyArg<T>(T? Value) where T : class
    {
        public static implicit operator CopyArg<T>(T value) => new(value);
    }

    /// <summary>
    /// Kotlin data class <c>copy()</c>. The stored <c>gameInfo</c> is copied as is and never re-derived from the new members,
    /// unless <paramref name="gameInfo"/> is given. A <c>null</c> argument means "keep"; to set a member to <c>null</c> pass
    /// <c>new CopyArg&lt;T&gt;(null)</c> (a variable that merely holds <c>null</c> converts to exactly that). <paramref name="combatAction"/>
    /// can only be replaced, not cleared; no Kotlin caller of <c>copy()</c> clears it.
    /// </summary>
    public GameContext Copy(
        CopyArg<IGameContextCivilization>? civInfo = null,
        CopyArg<IGameContextCity>? city = null,
        CopyArg<IGameContextUnit>? unit = null,
        CopyArg<IGameContextTile>? tile = null,
        CopyArg<IGameContextCombatant>? ourCombatant = null,
        CopyArg<IGameContextCombatant>? theirCombatant = null,
        CopyArg<IGameContextTile>? attackedTile = null,
        CombatAction? combatAction = null,
        CopyArg<IGameContextCivilization>? otherCiv = null,
        CopyArg<IGameContextRegion>? region = null,
        CopyArg<IGameContextGameInfo>? gameInfo = null,
        bool? ignoreConditionals = null) =>
        new(
            civInfo.HasValue ? civInfo.Value.Value : CivInfo,
            city.HasValue ? city.Value.Value : City,
            unit.HasValue ? unit.Value.Value : Unit,
            tile.HasValue ? tile.Value.Value : Tile,
            ourCombatant.HasValue ? ourCombatant.Value.Value : OurCombatant,
            theirCombatant.HasValue ? theirCombatant.Value.Value : TheirCombatant,
            attackedTile.HasValue ? attackedTile.Value.Value : AttackedTile,
            combatAction ?? CombatAction,
            otherCiv.HasValue ? otherCiv.Value.Value : OtherCiv,
            region.HasValue ? region.Value.Value : Region,
            gameInfo.HasValue ? gameInfo.Value.Value : GameInfo,
            ignoreConditionals ?? IgnoreConditionals,
            true);

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

    // Kotlin `by lazy` (synchronized): pure functions of the immutable constructor state, computed on first use. The result is
    // published through one reference to an immutable box, so a reader never sees the "computed" marker before the value.
    private sealed class LazyValue<T>(T value)
    {
        public readonly T Value = value;
    }

    private LazyValue<IGameContextUnit?>? relevantUnit;
    public IGameContextUnit? RelevantUnit =>
        (Volatile.Read(ref relevantUnit) ?? Interlocked.CompareExchange(ref relevantUnit,
            new LazyValue<IGameContextUnit?>(OurCombatant != null && OurCombatant.Unit != null ? OurCombatant.Unit : Unit), null)
            ?? relevantUnit).Value;

    private LazyValue<IGameContextTile?>? relevantTile;
    public IGameContextTile? RelevantTile =>
        (Volatile.Read(ref relevantTile) ?? Interlocked.CompareExchange(ref relevantTile,
            new LazyValue<IGameContextTile?>(AttackedTile
                ?? Tile
                // We need to protect against conditionals checking tiles for units pre-placement - see #10425, #10512
                ?? (RelevantUnit != null && RelevantUnit.HasTile() ? RelevantUnit.GetTile() : null)
                ?? City?.GetCenterTileOrNull()), null)
            ?? relevantTile).Value;

    private LazyValue<IGameContextCity?>? relevantCity;
    public IGameContextCity? RelevantCity =>
        (Volatile.Read(ref relevantCity) ?? Interlocked.CompareExchange(ref relevantCity,
            new LazyValue<IGameContextCity?>(ComputeRelevantCity()), null)
            ?? relevantCity).Value;

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
        Randomness.StateBasedRandom(caller, seed, GetHashCode(), GameInfo?.RandomSalt ?? 0);

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
