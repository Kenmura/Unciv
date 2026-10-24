using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/ForeignMapUnitView.kt (T085). Stripped: the purity annotations.
// The Kotlin @Deprecated getUnit() escape hatch is not ported (views unwrap() instead).

/// <summary>Should contain information that should be knowable to us about foreign units. Superclass of <see cref="MapUnitView"/>.</summary>
public class ForeignMapUnitView(MapUnit unit, CivilizationClass viewer, bool spectatorMode, GameView gameView)
    : OwnedView<MapUnit>(unit, viewer, spectatorMode, gameView)
{
    protected MapUnit Unit { get; } = unit;

    internal override CivilizationClass Owner() => Unit.Civ;

    public string Name => Unit.Name;

    public string CivName => Unit.Civ.CivName;

    public int UnitHealth => Unit.Health;

    public int ReligiousStrengthLost => Unit.ReligiousStrengthLost;

    public string? Religion => Unit.Religion;

    public int Id => Unit.Id;

    public string? InstanceName => Unit.InstanceName;

    public float CurrentMovement => Unit.CurrentMovement;

    public int AttacksThisTurn => Unit.AttacksThisTurn;

    // Navigation
    public virtual ForeignCivView Civ() => GameView.GetForeignCivView(Unit.Civ);

    /// <summary>Get from a foreign view to an inner view, if the unit belongs to the viewer.</summary>
    public MapUnitView? TryGetMapUnitView()
    {
        if (Unit.Civ != Viewer && !Viewer.IsSpectator()) return null;
        return GameView.GetMapUnitView(Unit);
    }

    public TileView GetTile() => GameView.TileMapView.GetTile(Unit.GetTile());

    /// <summary>Wraps the unit as a <see cref="MapUnitCombatantView"/> for battle purposes.</summary>
    public MapUnitCombatantView AsCombatant() => new(this, Unit, Viewer, SpectatorMode, GameView);

    // Data retrieval
    public bool CanAttack() => Unit.CanAttack();

    public bool IsEmbarked() => Unit.IsEmbarked();

    public string DisplayName() => Unit.DisplayName();

    public bool IsTransportTypeOf(ForeignMapUnitView other) => Unit.IsTransportTypeOf(other.Unit);

    public BaseUnit GetBaseUnit() => Unit.BaseUnit;

    public int GetRange() => Unit.GetRange();

    public int GetMaxMovement() => Unit.GetMaxMovement();

    public int GetInterceptionRange() => Unit.GetInterceptionRange();

    public JavaHashMap<string, MapUnit.UnitStatus> GetStatusMap() => Unit.StatusMap;

    public List<MapUnit.UnitMovementMemory> GetMovementMemories() => Unit.MovementMemories;

    public UnitMovementMemoryType GetMostRecentMoveType() => Unit.MostRecentMoveType;

    public bool HasUnique(UniqueType uniqueType) => Unit.HasUnique(uniqueType);

    public IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType) => Unit.GetMatchingUniques(uniqueType);

    public bool HasReachedMaxXPFromBarbarians() =>
        Unit.Promotions.TotalXpProduced() >= Unit.Civ.GameInfo.Ruleset.ModOptions.Constants.MaxXPfromBarbarians;

    // Rendering (icons/flags) - visible regardless of whether the unit is ours
    public bool IsIdle() => Unit.IsIdle();

    public bool IsFortified() => Unit.IsFortified();

    public bool IsGuarding() => Unit.IsGuarding();
}

// Helpers built on top of the view API, rather than being part of it
public static class ForeignMapUnitViewExtensions
{
    public static bool IsAirUnit(this ForeignMapUnitView view) => view.GetBaseUnit().IsAirUnit();

    public static bool IsCivilian(this ForeignMapUnitView view) => view.GetBaseUnit().IsCivilian();

    public static bool IsMilitary(this ForeignMapUnitView view) => view.GetBaseUnit().IsMilitary;

    public static bool HasMovement(this ForeignMapUnitView view) => view.CurrentMovement > 0;
}
