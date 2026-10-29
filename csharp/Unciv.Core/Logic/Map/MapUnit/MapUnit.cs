using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;
using UnitTypeObject = Unciv.Models.Ruleset.Unit.UnitType;

namespace Unciv.Logic.Map.MapUnits;

// Ported from core/src/com/unciv/logic/map/mapunit/MapUnit.kt (T065), against the seams in MapUnitSeams.cs: UnitMovement (T067, concrete)
// and the barbarian manager (T078) are concrete (UnitPromotions/UnitUpgradeManager are concrete since T066). Tile, Civilization and City are
// concrete. Namespace Unciv.Logic.Map.MapUnits (not ...MapUnit: the namespace of the unit actions would clash with the class name).
//
// Stripped / deferred:
// - getMovementString (needs UncivGame.Current.settings number format: UI), Timers.timeThis profiling, the @Readonly/@Cache/@LocalState
//   purity annotations, VisibleForTesting.
// - doAction's UnitAutomation calls (GameView(..).getMapUnitView) are wired (T156).

/// <summary>
/// The immutable properties and mutable game state of an individual unit present on the map.
/// Kotlin <c>MapUnit</c> has no <c>equals</c>/<c>hashCode</c>: this class keeps reference identity.
/// </summary>
[JsonConverter(typeof(MapUnit.MapUnitJsonConverter))]
public partial class MapUnit : IsPartOfGameInfoSerialization, IMapUnitState
{
    private static T Lateinit<T>(T? value, string name) where T : class =>
        value ?? throw new InvalidOperationException($"lateinit property {name} has not been initialized");

    #region Persisted fields

    private string? owner;
    /// <summary>civName owning the unit. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    public string Owner
    {
        get => Lateinit(owner, "owner");
        set => owner = value;
    }

    /// <summary>
    /// civName of original owner - relevant for returning captured workers from barbarians.
    /// Serialization field for <see cref="OriginalOwningCiv"/>.
    /// </summary>
    public string? OriginalOwner { get; set; }

    private string? name;
    /// <summary>Name key of the unit, used for serialization. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    public string Name
    {
        get => Lateinit(name, "name");
        set => name = value;
    }

    /// <summary>Name of this individual unit, usually resulting from promotion</summary>
    public string? InstanceName { get; set; }

    /// <summary>Should not be changed directly - instead use <see cref="UseMovementPoints"/></summary>
    public float CurrentMovement { get; set; }
    public int Health { get; set; } = 100;
    public int Id { get; set; } = Constants.NoId;

    // work, automation, fortifying, ...
    // Connect roads implies automated is true. It is specified by the action type.
    public string? Action { get; set; }
    public bool Automated { get; set; }

    // We can infer who we are escorting based on our tile
    [JsonPropertyName("escorting")]
    [JsonInclude]
    private bool escorting;

    public HexCoord? AutomatedRoadConnectionDestination { get; set; }
    // Temp disable, since this data broke saves
    [JsonIgnore]
    public List<HexCoord>? AutomatedRoadConnectionPath { get; set; }

    public int AttacksThisTurn { get; set; }

    public UnitPromotions Promotions { get; set; } = new();

    /// <summary>Indicates if unit should be located with 'next unit' action</summary>
    public bool Due { get; set; } = true;

    public bool IsTransported { get; set; }
    public int TurnsFortified { get; set; }

    /// <summary>New - track only *how many have been used*, derive max from uniques, left = max - used</summary>
    public JavaHashMap<string, int> AbilityToTimesUsed { get; set; } = new();

    public string? Religion { get; set; }
    public int ReligiousStrengthLost { get; set; }

    /// <summary>BNW (B006): loads of exotic goods this unit has already sold (see <see cref="Unciv.Logic.Trade.Routes.ExoticGoods"/>).</summary>
    public int ExoticGoodsSold { get; set; }

    /// <summary>
    /// FIFO list of this unit's past positions. Should never exceed two items in length. New item added once at end of turn and once at start,
    /// to allow rare between-turn movements like melee withdrawal to be distinguished. Used in movement arrow overlay.
    /// </summary>
    public List<UnitMovementMemory> MovementMemories { get; set; } = [];

    /// <summary>The most recent type of position change this unit has experienced. Used in movement arrow overlay.</summary>
    public UnitMovementMemoryType MostRecentMoveType { get; set; } = UnitMovementMemoryType.UnitMoved;

    /// <summary>
    /// Array list of all the tiles that this unit has attacked since the start of its most recent turn. Used in movement arrow overlay.
    /// </summary>
    public List<HexCoord> AttacksSinceTurnStart { get; set; } = [];

    public class UnitStatus(string name, int turnsLeft) : IsPartOfGameInfoSerialization
    {
        /// <summary>For deserialization.</summary>
        public UnitStatus() : this("", 1)
        {
        }

        public string Name { get; init; } = name;

        /// <summary>Decreases at *start on next turn* so defensive statuses persist on enemy turns</summary>
        public int TurnsLeft { get; set; } = turnsLeft;

        private IReadOnlyList<UniqueObject>? uniques;
        /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
        [JsonIgnore]
        public IReadOnlyList<UniqueObject> Uniques
        {
            get => Lateinit(uniques, "uniques");
            set => uniques = value;
        }

        public void SetTransients(MapUnit unit) =>
            Uniques = unit.Civ.GameInfo.Ruleset.UnitPromotions.TryGetValue(Name, out var promotion) ? promotion.UniqueObjects : [];

        public UnitStatus Clone() => new(Name, TurnsLeft);
    }

    /// <summary>Kotlin <c>HashMap&lt;String, UnitStatus&gt;</c>.</summary>
    public JavaHashMap<string, UnitStatus> StatusMap { get; set; } = new();

    #endregion
    #region Transient fields

    private Civilization.Civilization? civ;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Civilization.Civilization Civ
    {
        get => Lateinit(civ, "civ");
        set => civ = value;
    }

    private BaseUnit? baseUnit;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public BaseUnit BaseUnit
    {
        get => Lateinit(baseUnit, "baseUnit");
        set => baseUnit = value;
    }

    private Tile? currentTile;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Tile CurrentTile
    {
        get => Lateinit(currentTile, "currentTile");
        set => currentTile = value;
    }

    public bool HasTile() => currentTile != null;

    private UniqueMap tempUniquesMap = new();

    /// <summary>Temp map excluding unit uniques</summary>
    private UniqueMap nonUnitUniquesMap = new();

    /// <summary>Transient. Kotlin <c>val movement = UnitMovement(this)</c>.</summary>
    [JsonIgnore]
    public UnitMovement Movement { get; set; }

    [JsonIgnore]
    public UnitUpgradeManager Upgrade { get; set; }

    [JsonIgnore]
    public bool IsDestroyed { get; set; }

    [JsonIgnore]
    public MapUnitCache Cache { get; set; }

    /// <summary>Forgets the original owner (dev console <c>civ remove</c>: that civ is gone).</summary>
    public void ClearOriginalOwner()
    {
        OriginalOwner = null;
        originalOwningCiv = null;
        originalOwningCivResolved = true;
    }

    private Civilization.Civilization? originalOwningCiv;
    private bool originalOwningCivResolved;
    /// <summary>
    /// civ of original owner - relevant for returning captured workers from barbarians.
    /// Transient field for <see cref="OriginalOwner"/>; Kotlin <c>by lazy</c>.
    /// </summary>
    [JsonIgnore]
    public Civilization.Civilization? OriginalOwningCiv
    {
        get
        {
            if (!originalOwningCivResolved)
            {
                originalOwningCiv = OriginalOwner == null ? null : Civ.GameInfo.GetCivilization(OriginalOwner);
                originalOwningCivResolved = true;
            }
            return originalOwningCiv;
        }
    }

    /// <summary>
    /// This is saved per each unit because if we need to recalculate viewable tiles every time a unit moves,
    /// and we need to go over ALL the units, that's a lot of time spent on updating information we should already know!
    /// About 10% of total NextTurn performance time, at the time of this change!
    /// </summary>
    [JsonIgnore]
    public HashSet<Tile> ViewableTiles { get; set; } = [];

    #endregion

    public MapUnit()
    {
        Cache = new MapUnitCache(this);
        Upgrade = new UnitUpgradeManager(this);
        Movement = new UnitMovement(this);
    }

    /// <summary>
    /// Container class to represent a single instant in a <see cref="MapUnit"/>'s recent movement history.
    /// </summary>
    public class UnitMovementMemory(HexCoord position, UnitMovementMemoryType type) : IsPartOfGameInfoSerialization
    {
        /// <summary>Needed because this is part of a save and gets deserialized.</summary>
        public UnitMovementMemory() : this(HexCoord.Zero, UnitMovementMemoryType.UnitMoved)
        {
        }

        /// <summary>Position on the map at this instant, cloned on instantiation.</summary>
        public HexCoord Position { get; init; } = position;

        /// <summary>Category of the last change in position that brought the unit to this position.</summary>
        public UnitMovementMemoryType Type { get; init; } = type;

        public UnitMovementMemory Clone() => new(Position, Type);

        public override string ToString() => $"{nameof(UnitMovementMemory)}({Position}, {Type})";
    }

    #region pure functions

    // debug helper (please update comment if you see some "$unit" using this - MapUnit.label in WorkerAutomation is console log only as well)
    public override string ToString() => $"{Name} - {Owner}";

    /// <summary>
    /// Name which should be displayed in UI.
    /// Note this is translated after being returned from this function, so let's pay
    /// attention to combined names (renamed units, religion).
    /// </summary>
    public string DisplayName()
    {
        var baseName =
            InstanceName == null ? $"[{Name}]"
            : $"{InstanceName} ([{Name}])";

        return Religion == null ? baseName
            : $"{baseName} ([{GetReligionDisplayName()}])";
    }

    public string ShortDisplayName() => InstanceName != null ? $"[{InstanceName}]" : $"[{Name}]";

    public MapUnit Clone()
    {
        var toReturn = new MapUnit
        {
            BaseUnit = BaseUnit,
            Name = Name,
            Civ = Civ,
            Id = Id,
            Owner = Owner,
            OriginalOwner = OriginalOwner,
            InstanceName = InstanceName,
            CurrentMovement = CurrentMovement,
            Health = Health,
            Action = Action,
            Automated = Automated,
            escorting = escorting,
            AutomatedRoadConnectionDestination = AutomatedRoadConnectionDestination,
            AutomatedRoadConnectionPath = AutomatedRoadConnectionPath,
            AttacksThisTurn = AttacksThisTurn,
            TurnsFortified = TurnsFortified,
        };
        toReturn.Promotions = Promotions.Clone(toReturn);
        toReturn.IsTransported = IsTransported;
        toReturn.AbilityToTimesUsed = new JavaHashMap<string, int>(AbilityToTimesUsed);
        toReturn.Religion = Religion;
        toReturn.ReligiousStrengthLost = ReligiousStrengthLost;
        toReturn.ExoticGoodsSold = ExoticGoodsSold;
        toReturn.MovementMemories = CopyMemories(MovementMemories);
        // Kotlin sizes the copy with `(size * 4 + 2) / 3`
        var newStatusMap = new JavaHashMap<string, UnitStatus>((StatusMap.Count * 4 + 2) / 3);
        foreach (var (statusName, status) in StatusMap)
            newStatusMap[statusName] = status.Clone();
        toReturn.StatusMap = newStatusMap;
        toReturn.MostRecentMoveType = MostRecentMoveType;
        toReturn.AttacksSinceTurnStart = [.. AttacksSinceTurnStart];
        return toReturn;
    }

    public UnitTypeObject Type => BaseUnit.Type;

    public Tile GetTile() => CurrentTile;

    public CityObject? GetClosestCity()
    {
        // Kotlin minByOrNull: the first of the minimal elements
        CityObject? best = null;
        var bestDistance = 0;
        foreach (var city in Civ.Cities)
        {
            var distance = city.GetCenterTile().AerialDistanceTo(CurrentTile);
            if (best == null || distance < bestDistance)
            {
                best = city;
                bestDistance = distance;
            }
        }
        return best;
    }

    public bool IsMilitary() => BaseUnit.IsMilitary;
    public bool IsCivilian() => BaseUnit.IsCivilian();

    public bool IsActionUntilHealed() => Action?.EndsWith("until healed", StringComparison.Ordinal) == true;

    public bool IsFortified() => Action?.StartsWith(UnitActionType.Fortify.Value(), StringComparison.Ordinal) == true;
    public bool IsGuarding() => Action == UnitActionType.Guard.Value();
    public bool IsFortifyingUntilHealed() => IsFortified() && IsActionUntilHealed();

    public int GetFortificationTurns()
    {
        if (!(IsFortified() || IsGuarding())) return 0;
        return TurnsFortified;
    }

    public bool IsSleeping() => Action?.StartsWith(UnitActionType.Sleep.Value(), StringComparison.Ordinal) == true;
    public bool IsSleepingUntilHealed() => IsSleeping() && IsActionUntilHealed();

    public bool IsMoving() => Action?.StartsWith("moveTo", StringComparison.Ordinal) == true;

    public Tile GetMovementDestination()
    {
        var destination = Action.NotNull().Replace("moveTo ", "", StringComparison.Ordinal).Split(',').ToList();
        // Kotlin dropLastWhile { it.isEmpty() }
        while (destination.Count > 0 && destination[^1].Length == 0) destination.RemoveAt(destination.Count - 1);
        var destinationVector = HexCoord.Of(KotlinMath.FloatToInt(destination[0].ToFloat()), KotlinMath.FloatToInt(destination[1].ToFloat()));
        return CurrentTile.TileMap[destinationVector];
    }

    public bool IsAutomated() => Automated;

    public bool IsAutomatingRoadConnection() => Action == Constants.ConnectRoadAction;
    public bool IsExploring() => Action == UnitActionType.Explore.Value();
    public bool IsPreparingParadrop() => Action == UnitActionType.Paradrop.Value();
    public bool IsPreparingAirSweep() => Action == UnitActionType.AirSweep.Value();
    public bool IsSetUpForSiege() => Action == UnitActionType.SetUp.Value();

    /// <param name="includeOtherEscortUnit">
    /// determines whether this method will also check if it's other escort unit is idle if it has one.
    /// Leave it as default unless you know what <see cref="IsIdle"/> does.
    /// </param>
    public bool IsIdle(bool includeOtherEscortUnit = true)
    {
        if (!HasMovement()) return false;
        var tile = GetTile();
        if (tile.ImprovementInProgress != null &&
            CanBuildImprovement(tile.GetTileImprovementInProgress().NotNull()) &&
            !tile.IsMarkedForCreatesOneImprovement()
        ) return false;
        if (includeOtherEscortUnit && IsEscorting() && !GetOtherEscortUnit().NotNull().IsIdle(false)) return false;
        return !(IsFortified() || IsExploring() || IsSleeping() || IsAutomated() || IsMoving() || IsGuarding());
    }

    public IEnumerable<UniqueObject> GetUniques() => tempUniquesMap.GetAllUniques();

    /// <summary>Kotlin default <c>gameContext = cache.state</c>: <c>null</c> means the cached state.</summary>
    public IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType, GameContext? gameContext = null, bool checkCivInfoUniques = false)
    {
        var context = gameContext ?? Cache.State;
        // Kotlin sequence { yieldAll(..); if (check) yieldAll(..) }: lazy, in this order
        return GetMatchingUniquesIterator(uniqueType, context, checkCivInfoUniques);
    }

    private IEnumerable<UniqueObject> GetMatchingUniquesIterator(UniqueType uniqueType, GameContext context, bool checkCivInfoUniques)
    {
        foreach (var unique in tempUniquesMap.GetMatchingUniques(uniqueType, context)) yield return unique;
        if (checkCivInfoUniques)
        {
            foreach (var unique in Civ.GetMatchingUniques(uniqueType, context)) yield return unique;
        }
    }

    public void ForEachMatchingUnique(UniqueType uniqueType, Action<UniqueObject> op) =>
        ForEachMatchingUnique(uniqueType, null, false, op);

    public void ForEachMatchingUnique(UniqueType uniqueType, GameContext? gameContext, Action<UniqueObject> op) =>
        ForEachMatchingUnique(uniqueType, gameContext, false, op);

    public void ForEachMatchingUnique(UniqueType uniqueType, GameContext? gameContext, bool checkCivInfoUniques, Action<UniqueObject> op)
    {
        var context = gameContext ?? Cache.State;
        tempUniquesMap.ForEachMatchingUnique(uniqueType, context, op);
        if (checkCivInfoUniques)
            Civ.ForEachMatchingUnique(uniqueType, context, op);
    }

    public bool HasUnique(UniqueType uniqueType, GameContext? gameContext = null, bool checkCivInfoUniques = false) =>
        checkCivInfoUniques
            ? GetMatchingUniques(uniqueType, gameContext, true).Any()
            : tempUniquesMap.GetMatchingUniques(uniqueType, gameContext ?? Cache.State).Any(); // T262: no wrapper iterator for the (usual) unit-only query

    public IEnumerable<UniqueObject> GetTriggeredUniques(UniqueType trigger, GameContext? gameContext = null, Func<UniqueObject, bool>? triggerFilter = null) =>
        tempUniquesMap.GetTriggeredUniques(trigger, gameContext ?? Cache.State, triggerFilter ?? (_ => true));

    /// <summary>
    /// Gets *per turn* resource requirements - does not include immediate costs for stockpiled resources.
    /// GameContext is assumed to regarding this mapUnit
    /// </summary>
    public Counter<string> GetResourceRequirementsPerTurn()
    {
        var resourceRequirements = new Counter<string>();
        if (BaseUnit.RequiredResource != null) resourceRequirements[BaseUnit.RequiredResource] = 1;
        ForEachMatchingUnique(UniqueType.ConsumesResources, Cache.State, unique =>
            resourceRequirements.Add(unique.Params[1], unique.Params[0].ToInt()));
        return resourceRequirements;
    }

    public bool RequiresResource(string resource)
    {
        if (GetResourceRequirementsPerTurn().ContainsKey(resource)) return true;
        foreach (var unique in GetMatchingUniques(UniqueType.CostsResources, Cache.State))
        {
            if (unique.Params[1] == resource) return true;
        }
        return false;
    }

    public bool HasMovement() => CurrentMovement > 0;

    public int GetMaxMovement(bool ignoreOtherUnit = false)
    {
        var movementValue =
            IsEmbarked() ? 2
            : BaseUnit.Movement;

        movementValue = unchecked(movementValue + GetMatchingUniques(UniqueType.Movement, checkCivInfoUniques: true)
            .SumOf(it => it.Params[0].ToInt()));

        if (movementValue < 1) movementValue = 1;

        // Hakkapeliitta movement boost
        // For every double-stacked tile, check if our cohabitant can boost our speed
        // (a test `count() > 1` is no optimization - two iterations of a sequence instead of one)
        if (!ignoreOtherUnit) // if both units can boost the other, we avoid an endless loop
        {
            foreach (var boostingUnit in CurrentTile.GetUnits())
            {
                if (ReferenceEquals(boostingUnit, this)) continue;
                if (!boostingUnit.GetMatchingUniques(UniqueType.TransferMovement)
                        .Any(it => MatchesFilter(it.Params[0])))
                    continue;
                movementValue = Math.Max(movementValue, boostingUnit.GetMaxMovement(true));
            }
        }

        return movementValue;
    }

    public bool HasUnitMovedThisTurn()
    {
        var max = (float)GetMaxMovement();
        return CurrentMovement < max - (MathF.BitIncrement(max) - max); // max.ulp
    }

    /// <summary>Determines this (land or sea) unit's current maximum vision range from unit properties, civ uniques and terrain.</summary>
    /// <returns>Maximum distance of tiles this unit may possibly see</returns>
    public int GetVisibilityRange()
    {
        var visibilityRange = 2;

        var conditionalState = Cache.State;

        var relevantUniques = GetMatchingUniques(UniqueType.Sight, conditionalState, checkCivInfoUniques: true)
            .Concat(GetTile().GetMatchingUniques(UniqueType.Sight, conditionalState));
        visibilityRange = unchecked(visibilityRange + relevantUniques.SumOf(it => it.Params[0].ToInt()));

        if (visibilityRange < 1) visibilityRange = 1;

        return visibilityRange;
    }

    public int MaxAttacksPerTurn() =>
        unchecked(1 + GetMatchingUniques(UniqueType.AdditionalAttacks, checkCivInfoUniques: true).SumOf(it => it.Params[0].ToInt()));

    public bool CanAttack()
    {
        if (!HasMovement()) return false;
        if (IsCivilian()) return false;
        return AttacksThisTurn < MaxAttacksPerTurn();
    }

    public int GetRange()
    {
        if (BaseUnit.IsMelee()) return 1;
        var range = BaseUnit.Range;
        range = unchecked(range + GetMatchingUniques(UniqueType.Range, checkCivInfoUniques: true).SumOf(it => it.Params[0].ToInt()));
        return range;
    }

    /// <summary>How many tiles away this air unit can relocate to or reach in one move</summary>
    public int GetAirUnitMovementRange() => unchecked(GetRange() * 2);

    public bool IsEmbarked()
    {
        if (!BaseUnit.IsLandUnit) return false;
        if (Cache.CanMoveOnWater) return false;
        return CurrentTile.IsWater;
    }

    /// <summary>
    /// Whether this unit currently has an invisibility unique that hides it from <paramref name="to"/> (ignores fog of war,
    /// own-civ exemption, and detection). Callers checking whether a unit can actually be seen should use <see cref="IsVisibleTo(Civilization.Civilization)"/> instead.
    /// </summary>
    public bool HasActiveInvisibilityUnique(Civilization.Civilization to)
    {
        if (HasUnique(UniqueType.Invisible) && !to.IsSpectator())
            return true;
        if (HasUnique(UniqueType.InvisibleToNonAdjacent) && !to.IsSpectator())
            return !GetTile().GetTilesInDistance(1).Any(it =>
                it.GetUnits().Any(unit => ReferenceEquals(unit.Civ, to)));
        return false;
    }

    /// <returns>Whether <paramref name="civ"/> can currently see this unit on the map, accounting for fog of war and invisibility.</returns>
    public bool IsVisibleTo(Civilization.Civilization civ)
    {
        if (ReferenceEquals(civ, Civ)) return true;
        if (!GetTile().IsVisible(civ)) return false;
        if (!HasActiveInvisibilityUnique(civ)) return true;
        // viewableInvisibleUnitsTiles records which unit filters *could* be detected on each tile,
        // independent of what's actually there - so it never goes stale when units move.
        return civ.ViewableInvisibleUnitsTiles.TryGetValue(GetTile(), out var filters) && filters.Any(it => MatchesFilter(it));
    }

    public bool CanFortify(bool ignoreAlreadyFortified = false)
    {
        if (BaseUnit.IsWaterUnit) return false;
        if (IsCivilian()) return false;
        if (BaseUnit.IsAirUnit()) return false;
        if (IsEmbarked()) return false;
        if (HasUnique(UniqueType.NoDefensiveTerrainBonus, checkCivInfoUniques: true)) return false;
        if (ignoreAlreadyFortified) return true;
        if (IsFortified()) return false;
        return true;
    }

    private int AdjacentHealingBonus() =>
        GetMatchingUniques(UniqueType.HealAdjacentUnits).SumOf(it => it.Params[0].ToInt());

    public int GetHealAmountForCurrentTile()
    {
        if (IsEmbarked()) return 0; // embarked units can't heal
        if (Health >= 100) return 0; // No need to heal if at max health
        if (HasUnique(UniqueType.HealOnlyByPillaging, checkCivInfoUniques: true)) return 0;
        return HealingAmountForTile(GetTile(), noTerrainDamage: true);
    }

    public bool CanHealInCurrentTile() => GetHealAmountForCurrentTile() > 0;

    /// <summary>Returns the health points <see cref="MapUnit"/> will receive if healing on <paramref name="tile"/></summary>
    public int HealingAmountForTile(Tile tile, bool noTerrainDamage = false)
    {
        var isFriendlyTerritory = tile.IsFriendlyTerritory(Civ);

        int healing;
        if (tile.IsCityCenter()) healing = 25;
        else if (tile.IsWater && isFriendlyTerritory && (BaseUnit.IsWaterUnit || IsTransported)) healing = 20; // Water unit on friendly water
        else if (tile.IsWater && isFriendlyTerritory && Cache.CanMoveOnWater) healing = 20; // Treated as a water unit on friendly water
        else if (tile.IsWater) healing = 0; // All other water cases
        else if (isFriendlyTerritory) healing = 20; // Allied territory
        else if (tile.GetOwner() == null) healing = 10; // Neutral territory
        else healing = 10; // Enemy territory

        var mayHeal = healing > 0 || (tile.IsWater && HasUnique(UniqueType.HealsOutsideFriendlyTerritory, checkCivInfoUniques: true));
        if (!mayHeal) return healing;

        healing = unchecked(healing + GetMatchingUniques(UniqueType.Heal, checkCivInfoUniques: true).SumOf(it => it.Params[0].ToInt()));

        var healingCity = (CityObject?)tile.GetTilesInDistance(1).FirstOrDefault(it =>
            it.IsCityCenter() && ((CityObject)it.GetCity().NotNull()).GetMatchingUniques(UniqueType.CityHealingUnits).Any())?.GetCity();
        if (healingCity != null)
        {
            healingCity.ForEachMatchingUnique(UniqueType.CityHealingUnits, unique =>
            {
                if (MatchesFilter(unique.Params[0]) && IsAlly(healingCity.Civ)) // only heal our units or allied units
                    healing = unchecked(healing + unique.Params[1].ToInt());
            });
        }

        int? maxAdjacentHealingBonus = null;
        foreach (var unit in CurrentTile.Neighbors.SelectMany(it => it.GetUnits()).Where(it => ReferenceEquals(it.Civ, Civ)))
        {
            var bonus = unit.AdjacentHealingBonus();
            if (maxAdjacentHealingBonus == null || bonus > maxAdjacentHealingBonus) maxAdjacentHealingBonus = bonus;
        }
        if (maxAdjacentHealingBonus != null)
            healing = unchecked(healing + maxAdjacentHealingBonus.Value);

        if (noTerrainDamage) return healing;
        healing = unchecked(healing - GetDamageFromTerrain(tile));

        return healing;
    }

    /// <summary>Only military land units can truly "garrison"</summary>
    public bool CanGarrison() => IsMilitary() && BaseUnit.IsLandUnit;

    public bool IsGreatPerson() => BaseUnit.IsGreatPerson;
    public bool IsGreatPersonOfType(string type) => BaseUnit.IsGreatPersonOfType(type);

    public bool CanIntercept(Tile attackedTile)
    {
        if (!CanIntercept()) return false;
        if (CurrentTile.AerialDistanceTo(attackedTile) > GetInterceptionRange()) return false;
        return true;
    }

    public int GetInterceptionRange()
    {
        var rangeFromUniques = GetMatchingUniques(UniqueType.AirInterceptionRange, checkCivInfoUniques: true)
            .SumOf(it => it.Params[0].ToInt());
        return unchecked(BaseUnit.InterceptRange + rangeFromUniques);
    }

    public bool CanIntercept()
    {
        if (InterceptChance() == 0) return false;
        // Air Units can only Intercept if they didn't move this turn
        if (BaseUnit.IsAirUnit() && !HasMovement()) return false;
        var maxAttacksPerTurn = unchecked(1 +
            GetMatchingUniques(UniqueType.ExtraInterceptionsPerTurn).SumOf(it => it.Params[0].ToInt()));
        return AttacksThisTurn < maxAttacksPerTurn;
    }

    public int InterceptChance() =>
        GetMatchingUniques(UniqueType.ChanceInterceptAirAttacks).SumOf(it => it.Params[0].ToInt());

    public int InterceptDamagePercentBonus() =>
        GetMatchingUniques(UniqueType.DamageWhenIntercepting).SumOf(it => it.Params[0].ToInt());

    public float ReceivedInterceptDamageFactor()
    {
        var damageFactor = 1f;
        ForEachMatchingUnique(UniqueType.DamageFromInterceptionReduced, unique =>
            damageFactor *= 1f - unique.Params[0].ToFloat() / 100f);
        return Math.Max(damageFactor, 0f);
    }

    public int GetDamageFromTerrain(Tile? tile = null)
    {
        tile ??= CurrentTile;
        return tile.AllTerrains.SumOf(it => it.DamagePerTurn);
    }

    public bool IsTransportTypeOf(MapUnit mapUnit)
    {
        // Currently, only missiles and airplanes can be carried
        if (!mapUnit.BaseUnit.IsAirUnit()) return false;
        return GetMatchingUniques(UniqueType.CarryAirUnits).Any(it => mapUnit.MatchesFilter(it.Params[1]));
    }

    /// <summary>
    /// Returns the free capacity of this carrier for <paramref name="unit"/> or units matching the same Unique filters.
    /// Uses an optimizing algorithm that ensures complex overlapping filters are used to the max.
    /// See issue #15087.
    /// </summary>
    public int CheckCarryCapacity(MapUnit unit)
    {
        // Fetch ALL "slots", not only those the new unit could occupy - otherwise we couldn't count optimally
        var slots = new List<CarrierSlotMatcher.SlotRule>();
        void GetSlotsFor(UniqueType uniqueType)
        {
            ForEachMatchingUnique(uniqueType, unique =>
                slots.Add(new CarrierSlotMatcher.SlotRule(unique.Params[1], unique.Params[0].ToInt())));
        }
        GetSlotsFor(UniqueType.CarryAirUnits);
        GetSlotsFor(UniqueType.CarryExtraAirUnits);
        if (slots.Count == 0) return 0;

        return CarrierSlotMatcher.AvailableCapacity(
            slots,
            CurrentTile.AirUnits.Where(it => it.IsTransported),
            unit);
    }

    private bool CannotCarry(MapUnit unit) =>
        unit.GetMatchingUniques(UniqueType.CannotBeCarriedBy).Any(it => MatchesFilter(it.Params[0]));

    public bool CanTransport(MapUnit unit)
    {
        if (Owner != unit.Owner) return false;
        if (!IsTransportTypeOf(unit)) return false;
        if (CannotCarry(unit)) return false;
        if (CheckCarryCapacity(unit) <= 0) return false;
        return true;
    }

    /// <summary>Gets a Nuke's blast radius from the BlastRadius unique, defaulting to 2. No check whether the unit actually is a Nuke.</summary>
    public int GetNukeBlastRadius()
    {
        // Don't check conditionals as these are not supported
        var unique = GetMatchingUniques(UniqueType.BlastRadius).FirstOrDefault();
        return unique != null ? unique.Params[0].ToInt() : 2;
    }

    private bool IsAlly(Civilization.Civilization otherCiv) =>
        ReferenceEquals(otherCiv, Civ)
        || (otherCiv.IsCityState && ReferenceEquals(otherCiv.AllyCiv, Civ))
        || (Civ.IsCityState && ReferenceEquals(Civ.AllyCiv, otherCiv));

    /// <summary>
    /// Implements UniqueParameterType.MapUnitFilter.
    /// Gets passed state explicitly when the unit applying the filter isn't this unit; <c>null</c> is Kotlin's default <c>GameContext.EmptyState</c>.
    /// </summary>
    public bool MatchesFilter(string filter, bool multiFilter = true, GameContext? state = null) =>
        multiFilter ? MultiFilter.Filter(filter, it => MatchesSingleFilter(it, state ?? GameContext.EmptyState))
        : MatchesSingleFilter(filter, state ?? GameContext.EmptyState);

    private bool MatchesSingleFilter(string filter, GameContext state)
    {
        switch (filter)
        {
            case "other": return !ReferenceEquals(state.Unit, this);
            case Constants.Wounded:
            case "wounded units":
                return Health < 100;
            case Constants.Barbarians:
            case "Barbarian":
                return Civ.IsBarbarian;
            case "City-State": return Civ.IsCityState;
            case Constants.Embarked: return IsEmbarked();
            case "Non-City": return true;
            default:
                if (BaseUnit.MatchesFilter(filter, Cache.State, false)) return true;
                if (Civ.MatchesFilter(filter, Cache.State, false)) return true;
                if (nonUnitUniquesMap.HasUnique(filter, Cache.State)) return true;
                if (Promotions.Promotions.Contains(filter)) return true;
                if (HasStatus(filter)) return true;
                return false;
        }
    }

    public bool CanBuildImprovement(TileImprovement improvement, Tile? tile = null)
    {
        tile ??= CurrentTile;
        if (Civ.IsBarbarian) return false;

        // Workers (and similar) should never be able to (instantly) construct things, only build them
        // HOWEVER, they should be able to repair such things if they are pillaged
        if (improvement.TurnsToBuild == -1
            && improvement.Name != Constants.CancelImprovementOrder
            && tile.ImprovementInProgress != improvement.Name
        ) return false;
        var buildImprovementUniques = GetMatchingUniques(UniqueType.BuildImprovements).ToList();
        // B010: only a unit that names the dig itself (the Archaeologist) can dig, not a Worker that builds every [Land] improvement
        if (((IHasUniques)improvement).HasUnique(UniqueType.IsArchaeologicalDig, GameContext.IgnoreConditionalsState)
            && !buildImprovementUniques.Any(it => it.Params[0] == improvement.Name))
            return false;
        if (tile.ImprovementInProgress == Constants.Repair)
        {
            if (tile.IsEnemyTerritory(Civ)) return false;
            return buildImprovementUniques.Count > 0;
        }

        // Validate that the improvement is available for the unit
        var improvementUniques = (IHasUniques)improvement;
        if (improvementUniques.GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState)
                .Any(unique => !unique.ConditionalsApply(Cache.State)) ||
            improvementUniques.GetMatchingUniques(UniqueType.Unavailable, GameContext.IgnoreConditionalsState)
                .Any(unique => unique.ConditionalsApply(Cache.State)))
        {
            return false;
        }

        return buildImprovementUniques.Any(unique =>
            // Engage the MultiFilter on the entire filter, prior to checking the individual filters
            MultiFilter.Filter(unique.Params[0], it =>
                improvement.MatchesFilter(it, Cache.State) || tile.MatchesTerrainFilter(it, Civ)));
    }

    public string? GetReligionDisplayName()
    {
        if (Religion == null) return null;
        return Civ.GameInfo.Religions[Religion].GetReligionDisplayName();
    }

    public int GetForceEvaluation()
    {
        // Kotlin Float.pow(Float) is Math.pow on the widened doubles, narrowed back
        var promotionBonus = (float)Math.Pow(Promotions.NumberOfPromotions + 1f, 0.3f);
        var power = KotlinMath.FloatToInt(BaseUnit.GetForceEvaluation() * promotionBonus);
        power = unchecked(power * Health);
        power /= 100;
        return power;
    }

    public MapUnit? GetOtherEscortUnit()
    {
        if (currentTile == null) return null; // In some cases we might not have the unit placed on the map yet
        if (IsCivilian()) return GetTile().MilitaryUnit;
        if (IsMilitary()) return GetTile().CivilianUnit;
        return null;
    }

    public bool IsEscorting()
    {
        if (escorting)
        {
            if (GetOtherEscortUnit() != null) return true;
            escorting = false;
        }
        return false;
    }

    public bool ThreatensCiv(Civilization.Civilization civInfo)
    {
        if (ReferenceEquals(GetTile().GetOwner(), civInfo))
            return true;
        return GetTile().Neighbors.Any(it => ReferenceEquals(it.GetOwner(), civInfo));
    }

    /// <summary>Deep clone a list of <see cref="UnitMovementMemory"/>s.</summary>
    private static List<UnitMovementMemory> CopyMemories(List<UnitMovementMemory> memories) => memories.Select(it => it.Clone()).ToList();

    #endregion
    #region state-changing functions

    public void SetTransients(Ruleset ruleset)
    {
        Promotions.SetTransients(this);
        BaseUnit = ruleset.Units.TryGetValue(Name, out var unit) ? unit
            : throw new InvalidOperationException($"Unit {Name} is not found!");

        foreach (var status in StatusMap.Values) status.SetTransients(this);
        UpdateUniques();
        if (Action == UnitActionType.Automate.Value())
        {
            Automated = true;
            Action = null;
        }
    }

    public void UpdateUniques()
    {
        var otherUniqueSources = Promotions.GetPromotions(sorted: true).SelectMany(it => ((IHasUniques)it).UniqueObjects)
            .Concat(StatusMap.Values.SelectMany(it => it.Uniques)).ToList();
        var uniqueSources = BaseUnit.RulesetUniqueObjects.Concat(otherUniqueSources);

        tempUniquesMap = new UniqueMap(uniqueSources);
        nonUnitUniquesMap = new UniqueMap(otherUniqueSources);
        Cache.UpdateUniques();
    }

    public void CopyStatisticsTo(MapUnit newUnit)
    {
        newUnit.Health = Health;
        newUnit.InstanceName = InstanceName;
        newUnit.CurrentMovement = CurrentMovement;
        newUnit.AttacksThisTurn = AttacksThisTurn;
        newUnit.IsTransported = IsTransported;
        foreach (var promotion in newUnit.Promotions.Promotions)
        {
            if (!Promotions.Promotions.Contains(promotion))
                Promotions.AddPromotion(promotion, isFree: true);
        }

        newUnit.Promotions = Promotions.Clone(newUnit);
        newUnit.Automated = Automated;
        newUnit.Action = Action; // Needed too for Unit Overview action column

        newUnit.UpdateUniques();
        newUnit.UpdateVisibleTiles();
    }

    /// <summary>Update this unit's cache of viewable tiles and its civ's as well.</summary>
    public void UpdateVisibleTiles(bool updateCivViewableTiles = true, HexCoord? explorerPosition = null)
    {
        var oldViewableTiles = ViewableTiles;

        if (HasUnique(UniqueType.NoSight))
            ViewableTiles = [GetTile()]; // 0 sight distance still means we can see the Tile we're in
        else if (HasUnique(UniqueType.CanSeeOverObstacles))
            ViewableTiles = GetTile().GetTilesInDistance(GetVisibilityRange()).ToHashSet(); // it's that simple
        else
            ViewableTiles = GetTile().GetViewableTilesList(GetVisibilityRange()).ToHashSet();

        // Set equality automatically determines if anything changed
        var shouldUpdateTiles = updateCivViewableTiles && !oldViewableTiles.SetEquals(ViewableTiles)
            // Don't bother updating if all previous and current viewable tiles are within our borders
            && (oldViewableTiles.Any(it => !Civ.Cache.OurTilesAndNeighboringTiles.Contains(it))
                || ViewableTiles.Any(it => !Civ.Cache.OurTilesAndNeighboringTiles.Contains(it)));

        if (!shouldUpdateTiles) return;

        var unfilteredTriggeredUniques = GetTriggeredUniques(UniqueType.TriggerUponDiscoveringTile, GameContext.IgnoreConditionalsState).ToList();
        if (unfilteredTriggeredUniques.Count > 0)
        {
            var newlyExploredTiles = ViewableTiles.Where(it => !it.IsExplored(Civ)).ToList();
            foreach (var tile in newlyExploredTiles)
            {
                var state = Cache.State;
                foreach (var unique in unfilteredTriggeredUniques)
                {
                    if (unique.GetModifiers(UniqueType.TriggerUponDiscoveringTile)
                            .Any(it => tile.MatchesFilter(it.Params[0], Civ))
                        && unique.ConditionalsApply(state)
                    )
                        UniqueTriggerActivation.TriggerUnique(unique, this);
                }
            }
        }

        Civ.Cache.UpdateViewableTiles(explorerPosition);
    }

    /// <summary>Can accept a negative number to gain movement points</summary>
    public void UseMovementPoints(float amount)
    {
        TurnsFortified = 0;
        CurrentMovement -= amount;
        if (CurrentMovement < 0) CurrentMovement = 0f;
        if (amount < 0)
        {
            var maxMovement = (float)GetMaxMovement();
            if (CurrentMovement > maxMovement) CurrentMovement = maxMovement;
        }
    }

    public void Fortify() => Action = "Fortify";

    public void FortifyUntilHealed() => Action = "Fortify until healed";

    public void FortifyIfCan()
    {
        if (CanFortify()) Fortify();
    }

    public void DoAction()
    {
        if (Action == null && !IsAutomated()) return;
        if (!HasMovement()) return; // We've already done stuff this turn, and can't do any more stuff
        if (IsEscorting() && !GetOtherEscortUnit().NotNull().HasMovement()) return;

        var enemyUnitsInWalkingDistance = Movement.GetDistanceToTiles().Keys
            .Where(it => it.MilitaryUnit != null && Civ.IsAtWarWith(it.MilitaryUnit.Civ))
            .ToList();
        if (enemyUnitsInWalkingDistance.Count > 0)
        {
            if (IsMoving()) // stop on enemy in sight
                Action = null;
            if (!(IsExploring() || IsAutomated())) // have fleeing code
                return; // Don't you dare move.
        }

        var tileNow = GetTile();

        if (IsMoving())
        {
            var destinationTile = GetMovementDestination();
            if (!Movement.CanReach(destinationTile)) // That tile that we were moving towards is now unreachable -
            {
                // for instance we headed towards an unknown tile and it's apparently unreachable
                Action = null;
                return;
            }
            var gotTo = Movement.HeadTowards(destinationTile);
            if (ReferenceEquals(gotTo, tileNow)) // We didn't move at all
            {
                // pathway blocked? Are we still at the same spot as start of turn?
                if (MovementMemories[^1].Position == tileNow.Position)
                    Action = null;
                return;
            }
            if (gotTo.Position == destinationTile.Position) Action = null;
            if (HasMovement()) DoAction();
            return;
        }

        if (IsAutomated()) AutomateUnitMoves();
        if (IsExploring()) AutomatedExplore();
    }

    public void HealBy(int amount)
    {
        Health = unchecked(Health + amount *
            (HasUnique(UniqueType.HealingEffectsDoubled, checkCivInfoUniques: true) ? 2 : 1));
        if (Health > 100) Health = 100;
        Cache.UpdateUniques();
    }

    public void TakeDamage(int amount)
    {
        Health = unchecked(Health - amount);
        if (Health > 100) Health = 100; // For cheating modders, e.g. negative tile damage
        if (Health < 0) Health = 0;
        if (Health == 0) Destroy();
        else Cache.UpdateUniques();
    }

    public void Destroy(bool destroyTransportedUnit = true)
    {
        GameEventHooks.UnitDestroyed(this);
        StopEscorting();
        CurrentMovement = 0f;
        Civ.Units.RemoveUnit(this, true);
        if (currentTile != null)
        {
            var currentPosition = GetTile().Position;
            Civ.AttacksSinceTurnStart.AddRange(AttacksSinceTurnStart.Select(it =>
                new Civilization.Civilization.HistoricalAttackMemory(Name, currentPosition, it)));
            RemoveFromTile();
            Civ.Cache.UpdateViewableTiles();
            if (destroyTransportedUnit)
            {
                // all transported units should be destroyed as well
                foreach (var unit in CurrentTile.GetUnits().Where(it => it.IsTransported && IsTransportTypeOf(it))
                             .ToList()) // because we're changing the list
                    unit.Destroy();
            }
        }

        IsDestroyed = true;
    }

    public void Gift(Civilization.Civilization recipient)
    {
        StopEscorting();
        Civ.Units.RemoveUnit(this, true);
        Civ.Cache.UpdateViewableTiles();
        // all transported units should be gift as well
        foreach (var unit in CurrentTile.GetUnits().Where(it => it.IsTransported && IsTransportTypeOf(it)).ToList())
            unit.Gift(recipient);
        AssignOwner(recipient);
        recipient.Cache.UpdateViewableTiles();
    }

    /// <summary>Destroys the unit and gives stats if its a great person</summary>
    public void Consume()
    {
        Civ.ForEachTriggeredUnique(UniqueType.TriggerUponExpendingUnit, Civ.State, it => MatchesFilter(it.Params[0]), unique =>
            UniqueTriggerActivation.TriggerUnique(unique, this,
                triggerNotificationText: $"due to expending our [{Name}]"));
        Destroy();
    }

    public void RemoveFromTile() => CurrentTile.RemoveUnit(this);

    /// <summary>Return null if military on tile, or no civilian</summary>
    // Could be local to moveThroughTile, therefore left in the state-changing region
    private static MapUnit? GetUnguardedCivilian(Tile tile, MapUnit attacker)
    {
        if (tile.MilitaryUnit != null && !ReferenceEquals(tile.MilitaryUnit, attacker)) return null;
        if (tile.CivilianUnit != null) return tile.CivilianUnit;
        return null;
    }

    public void MoveThroughTile(Tile tile)
    {
        // addPromotion requires currentTile to be valid because it accesses ruleset through it.
        // getAncientRuinBonus, if it places a new unit, does too
        CurrentTile = tile;
        // The state also needs to be valid for uniques to see the cached version
        Cache.State = GameContext.ForUnit(this);
        // The improvement may get removed if it has ruins effects or is a barbarian camp, and will still be needed if removed
        var improvement = tile.TileImprovement;

        // To allow triggering on barb camps and ruins, must happen before entering them
        var triggeredUniques = GetTriggeredUniques(UniqueType.TriggerUponEnteringTile, null, it => tile.MatchesFilter(it.Params[0])).ToList();
        foreach (var triggeredUnique in triggeredUniques)
            UniqueTriggerActivation.TriggerUnique(triggeredUnique, this);

        // clearEncampment must run first, because removing the improvement will invalidate quests, and both functions do so.
        if (!Civ.IsBarbarian && tile.IsBarbarianEncampment())
            ClearEncampment(tile);
        if (Civ.IsMajorCiv() && improvement?.IsAncientRuinsEquivalent(Cache.State) == true)
            GetAncientRuinBonus(tile);

        // Check whether any civilians without military units are there.
        // Keep in mind that putInTile(), which calls this method,
        // might have already placed your military unit in this tile.
        var unguardedCivilian = GetUnguardedCivilian(tile, this);
        // Capture Enemy Civilian Unit if you move on top of it
        if (IsMilitary() && unguardedCivilian != null && Civ.IsAtWarWith(unguardedCivilian.Civ))
        {
            Battle.BattleUnitCapture.CaptureCivilianUnit(new Battle.MapUnitCombatant(this), new Battle.MapUnitCombatant(tile.CivilianUnit.NotNull()));
        }

        var promotionUniques = tile.Neighbors
            .SelectMany(it => it.AllTerrains)
            .SelectMany(it => ((IHasUniques)it).GetMatchingUniques(UniqueType.TerrainGrantsPromotion))
            .ToList();
        foreach (var unique in promotionUniques)
        {
            if (!MatchesFilter(unique.Params[2])) continue;
            var promotion = unique.Params[0];
            Promotions.AddPromotion(promotion, true);
        }

        UpdateVisibleTiles(true, CurrentTile.Position);
    }

    public void PutInTile(Tile tile)
    {
        if (!Movement.CanMoveTo(tile))
        {
            var tileNow = HasTile() ? CurrentTile : null;
            throw new InvalidOperationException($"Unit {Name} of {Civ.CivId} at {tileNow} can't be put in tile {tile}, reason: {Movement.GetCannotMoveToReason(tile)?.ToString() ?? "null"}");
        }
        if (BaseUnit.IsAirUnit()) tile.AirUnits.Add(this);
        else if (IsCivilian()) tile.CivilianUnit = this;
        else tile.MilitaryUnit = this;
        // this check is here in order to not load the fresh built unit into carrier right after the build
        if (BaseUnit.IsAirUnit())
        {
            if (!tile.IsCityCenter()) IsTransported = true;
            else
            {
                var currentUntransportedUnits = tile.GetUnits().Count(it => it.Type.IsAirUnit() && !it.IsTransported);
                // Tile units includes us, we were just added
                IsTransported = currentUntransportedUnits > ((CityObject)tile.GetCity().NotNull()).GetMaxAirUnits();
            }
        }
        MoveThroughTile(tile);
        Cache.UpdateUniques();
    }

    public void StartEscorting()
    {
        if (GetOtherEscortUnit() != null)
        {
            escorting = true;
            GetOtherEscortUnit().NotNull().escorting = true;
        }
        else
        {
            escorting = false;
        }
        Movement.ClearPathfindingCache();
    }

    public void StopEscorting()
    {
        var other = GetOtherEscortUnit();
        if (other != null) other.escorting = false;
        escorting = false;
        if (currentTile != null) // Can cause an error if the unit has not been placed on the map yet.
            Movement.ClearPathfindingCache();
    }

    private void ClearEncampment(Tile tile)
    {
        // Notify City-States that this unit cleared a Barbarian Encampment, required for quests
        // Do this before removing the improvement, otherwise removeImprovement would obsolete the quest
        foreach (var cityState in Civ.GameInfo.GetAliveCityStates())
            cityState.QuestManager.BarbarianCampCleared(Civ, tile.Position);

        tile.RemoveImprovement();
        Archaeology.ArchaeologyRules.RecordCampCleared(tile, Civ); // B010

        var goldGained = (float)Civ.GetDifficulty().ClearBarbarianCampReward;

        // German unique
        Civ.ForEachMatchingUnique(UniqueType.GainFromEncampment, unique =>
        {
            goldGained += unique.Params[0].ToInt();
            var recruitedUnit = Civ.GameInfo.Barbarians.SpawnBarbarian(tile, Civ);
            if (recruitedUnit == null) return;
            recruitedUnit.Health = 100;
            recruitedUnit.CurrentMovement = 0f;
            Civ.AddNotification(
                $"An enemy [{recruitedUnit.Name}] has joined us!",
                new MapUnitAction(recruitedUnit.CurrentTile.Position, recruitedUnit.Id),
                NotificationCategory.War,
                recruitedUnit.Name);
        });

        goldGained *= Civ.GameInfo.Speed.GoldCostModifier;

        // Songhai unique
        Civ.ForEachMatchingUnique(UniqueType.GoldFromEncampmentsAndCities, Cache.State, unique =>
            goldGained *= 1 + unique.Params[0].ToFloat() / 100); // Kotlin String.toPercent(), inlined: the extension lives in a UI namespace

        Civ.AddGold(KotlinMath.FloatToInt(goldGained));
        Civ.AddNotification(
            $"We have captured a barbarian encampment and recovered [{KotlinMath.FloatToInt(goldGained)}] gold!",
            tile.Position,
            NotificationCategory.War,
            NotificationIcon.Gold);
    }

    public void Disband()
    {
        // Safeguard against running on already destroyed instances
        if (IsDestroyed) return;

        // evacuation of transported units before disbanding, if possible. toListed because we're modifying the unit list.
        foreach (var unit in CurrentTile.GetUnits()
                     .Where(it => it.IsTransported && IsTransportTypeOf(it))
                     .ToList())
        {
            // if we disbanded a unit carrying other units in a city, the carried units can still stay in the city
            if (CurrentTile.IsCityCenter() && unit.Movement.CanMoveTo(CurrentTile))
            {
                unit.IsTransported = false;
                continue;
            }
            // if no "fuel" to escape, should be disbanded as well
            if (unit.CurrentMovement < Constants.MinimumMovementEpsilon)
                unit.Disband();
            // let's find closest city or another carrier where it can be evacuated
            var tileCanMoveTo = unit.CurrentTile.GetTilesInDistance(unit.GetAirUnitMovementRange())
                .Where(it => !ReferenceEquals(it, CurrentTile)).FirstOrDefault(it => unit.Movement.CanMoveTo(it));

            if (tileCanMoveTo != null)
                unit.Movement.MoveToTile(tileCanMoveTo);
            else
                unit.Disband();
        }

        Destroy();
        if (ReferenceEquals(CurrentTile.GetOwner(), Civ))
            Civ.AddGold(BaseUnit.GetDisbandGold(Civ));
        if (Civ.IsDefeated()) Civ.Destroy();
    }

    private void GetAncientRuinBonus(Tile tile)
    {
        tile.RemoveImprovement();
        Archaeology.ArchaeologyRules.RecordRuinPopped(tile, Civ); // B010
        Civ.RuinsManager.SelectNextRuinsReward(this);
    }

    /// <summary>
    /// Assigns ownership to <paramref name="civInfo"/>, updating its unit manager and our <see cref="Cache"/>.
    /// Used during game load to rebuild the transient <c>UnitManager.unitList</c> from Tile data.
    /// Cannot be used to reassign from one civ to another - doesn't remove from old owner.
    /// </summary>
    public void AssignOwner(Civilization.Civilization civInfo, bool updateCivInfo = true)
    {
        Owner = civInfo.CivId;
        Civ = civInfo;
        civInfo.Units.AddUnit(this, updateCivInfo);
        // commit named "Fixed game load": GameInfo.setTransients code flow and dependency requirements
        // may lead to this being called before our own setTransients
        if (baseUnit != null)
            Cache.UpdateUniques();
    }

    public void CapturedBy(Civilization.Civilization captor)
    {
        GameEventHooks.UnitCaptured(this, captor);
        Civ.Units.RemoveUnit(this, true);
        AssignOwner(captor);
        CurrentMovement = 0f;
        // It's possible that the unit can no longer stand on the tile it was captured on.
        // For example, because it's embarked and the capturing civ cannot embark units yet.
        if (!Movement.CanPassThrough(GetTile()))
        {
            Movement.TeleportToClosestMoveableTile();
        }
    }

    /// <summary>Add the current position and the most recent movement type to <see cref="MovementMemories"/>. Called once at end and once at start of turn, and at unit creation.</summary>
    public void AddMovementMemory()
    {
        MovementMemories.Add(new UnitMovementMemory(GetTile().Position, MostRecentMoveType));
        while (MovementMemories.Count > 2) // O(n) but n == 2.
        {
            // Keep at most one arrow segment- A lot of the time even that won't be rendered because the two positions will be the same.
            // When in the unit's turn- I.E. For a player unit- The last two entries will be from .endTurn() followed by from .startTurn(), so the segment from .movementMemories will have zero length. Instead, what gets seen will be the segment from the end of .movementMemories to the unit's current position.
            // When not in the unit's turn- I.E. For a foreign unit- The segment from the end of .movementMemories to the unit's current position will have zero length, while the last two entries here will be from .startTurn() followed by .endTurn(), so the segment here will be what gets shown.
            // The exception is when a unit changes position when not in its turn, such as by melee withdrawal or foreign territory expulsion. Then the segment here and the segment from the end of here to the current position can both be shown.
            MovementMemories.RemoveAt(0);
        }
    }

    public UnitStatus? GetStatus(string statusName) => StatusMap.TryGetValue(statusName, out var status) ? status : null;
    public bool HasStatus(string statusName) => GetStatus(statusName) != null;

    public void SetStatus(string statusName, int turns)
    {
        var existingStatus = GetStatus(statusName);
        if (existingStatus != null)
        {
            if (turns > existingStatus.TurnsLeft) existingStatus.TurnsLeft = turns;
            return;
        }

        var status = new UnitStatus(statusName, turns);
        status.SetTransients(this);
        StatusMap[status.Name] = status;
        UpdateUniques();

        foreach (var unique in GetTriggeredUniques(UniqueType.TriggerUponStatusGain, null, it => it.Params[0] == statusName).ToList())
            UniqueTriggerActivation.TriggerUnique(unique, this);
    }

    public void RemoveStatus(string statusName)
    {
        if (!StatusMap.Remove(statusName)) return;

        UpdateUniques();

        foreach (var unique in GetTriggeredUniques(UniqueType.TriggerUponStatusLoss, null, it => it.Params[0] == statusName).ToList())
            UniqueTriggerActivation.TriggerUnique(unique, this);
    }

    public bool IsNuclearWeapon() => HasUnique(UniqueType.NuclearWeapon);

    #endregion

    #region Seam implementations (IGameContextUnit / IMapUnitState)

    // The seams cannot name the concrete Civilization/Tile/MapUnit types of Kotlin's defaults, so the members with a different shape
    // are explicit; everything else is implemented by the public member of the same name.

    IGameContextCivilization IGameContextUnit.Civ => Civ;
    IGameContextTile IGameContextUnit.GetTile() => GetTile();
    bool IGameContextUnit.MatchesFilter(string filter) => MatchesFilter(filter);
    bool IGameContextUnit.HasPromotionOrStatus(string name) => Promotions.Promotions.Contains(name) || HasStatus(name);
    bool IGameContextUnit.AbilityToTimesUsedIsEmpty => AbilityToTimesUsed.IsEmpty;
    IGameContextUnit IGameContextUnit.Clone() => Clone();
    void IGameContextUnit.SetCurrentTile(Tile tile) => CurrentTile = tile;
    void IGameContextUnit.AssignOwner(IGameContextCivilization civInfo, bool updateCivInfo) => AssignOwner((Civilization.Civilization)civInfo, updateCivInfo);
    GameContext IGameContextUnit.CacheState => Cache.State;
    bool IGameContextUnit.CanMoveOnWater => Cache.CanMoveOnWater;
    bool IGameContextUnit.CanPassThrough(Tile tile) => Movement.CanPassThrough(tile);
    bool IGameContextUnit.CanMoveTo(Tile tile) => Movement.CanMoveTo(tile);
    void IGameContextUnit.SetOriginalOwner(string civId) => OriginalOwner = civId;
    void IGameContextUnit.SetCacheState(GameContext state) => Cache.State = state;
    IEnumerable<UniqueObject> IGameContextUnit.GetTriggeredUniques(UniqueType trigger, GameContext gameContext, Func<UniqueObject, bool>? triggerFilter) =>
        GetTriggeredUniques(trigger, gameContext, triggerFilter);
    void IGameContextUnit.ClearPathfindingCache() => Movement.ClearPathfindingCache();
    void IGameContextUnit.SetCurrentMovement(float movement) => CurrentMovement = movement;
    int IGameContextUnit.GetMaxMovement() => GetMaxMovement();
    void IGameContextUnit.AddPromotion(string promotionName, bool isFree) => Promotions.AddPromotion(promotionName, isFree);
    bool IGameContextUnit.HasResourceRequirementsPerTurn() => !GetResourceRequirementsPerTurn().IsEmpty();

    void IMapUnitState.TeleportToClosestMoveableTile() => Movement.TeleportToClosestMoveableTile();
    void IMapUnitState.Destroy() => Destroy();
    bool IMapUnitState.HasUnique(UniqueType uniqueType, GameContext? gameContext) => HasUnique(uniqueType, gameContext);
    IEnumerable<UniqueObject> IMapUnitState.GetMatchingUniques(UniqueType uniqueType, GameContext? gameContext) => GetMatchingUniques(uniqueType, gameContext);
    IEnumerable<Tile> IMapUnitState.ViewableTiles => ViewableTiles;

    #endregion
}
