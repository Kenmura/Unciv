using Unciv.Logic.Battle;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using CityClass = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/MapUnitView.kt (T085). Stripped: the purity annotations.
// Deferred, each with the ticket that ports what it calls:
//   TODO(T139): containsAttackableEnemy, getAttackableEnemies (TargetHelper), tryAirSweep (AirInterception)
//   TODO(T080): canInvokeUnitAction, tryInvokeUnitAction (UnitActions)
//   TODO(T139): getMovementString, getTurnsToBuild, tryStartWorkingOnImprovement (engine members not ported yet)
// The Kotlin file's `/** How many tiles away ... */` doc comment dangling above containsAttackableEnemy belongs to getAirUnitMovementRange.

/// <summary>View of a <see cref="MapUnit"/> from the perspective of the viewer via the <see cref="GameView"/>.</summary>
public class MapUnitView : ForeignMapUnitView
{
    internal MapUnitView(MapUnit unit, CivilizationClass viewer, bool spectatorMode, GameView gameView)
        : base(unit, viewer, spectatorMode, gameView)
    {
    }

    public bool Due => Unit.Due;

    public int Xp => Unit.Promotions.XP;

    public bool IsDestroyed() => Unit.IsDestroyed;

    public int XpForNextPromotion() => Unit.Promotions.XpForNextPromotion();

    public bool CanBePromoted() => Unit.Promotions.CanBePromoted();

    public JavaHashSet<string> GetPromotionNames() => Unit.Promotions.Promotions;

    /// <summary>The unit's promotions as objects, in json order.</summary>
    public IEnumerable<Promotion> GetPromotions() => Unit.Promotions.GetPromotions(sorted: true);

    /// <summary>Upgrades of this unit worth showing (normal ones, not ruins-only), see <see cref="PossibleUpgrade.IsShownToPlayer"/></summary>
    public List<PossibleUpgrade> GetPossibleUpgrades() => Unit.Upgrade.GetUpgradesOfKind(isSpecial: false);

    /// <summary><c>true</c> if a regular upgrade to <paramref name="baseUnit"/> could be performed right now</summary>
    public bool CanUpgradeTo(BaseUnit baseUnit) => GetPossibleUpgrades()
        .Any(it => it.UpgradedUnit == baseUnit && Unit.Upgrade.CanPerformUpgrade(it, isFree: false));

    /// <summary>Performs a regular upgrade to <paramref name="baseUnit"/> if <see cref="CanUpgradeTo"/> allows it</summary>
    /// <returns>whether an upgrade was performed</returns>
    public bool TryUpgradeTo(BaseUnit baseUnit)
    {
        var upgrade = GetPossibleUpgrades()
            .FirstOrDefault(it => it.UpgradedUnit == baseUnit && Unit.Upgrade.CanPerformUpgrade(it, isFree: false));
        if (upgrade == null) return false;
        Unit.Upgrade.PerformUpgrade(upgrade, isFree: false);
        return true;
    }

    public bool HasUniqueToBuildImprovements() => Unit.Cache.HasUniqueToBuildImprovements;

    public bool HasUniqueToCreateWaterImprovements() => Unit.Cache.HasUniqueToCreateWaterImprovements;

    public bool HasStrengthBonusInRadiusUnique() => Unit.Cache.HasStrengthBonusInRadiusUnique;

    public bool HasCitadelPlacementUnique() => Unit.Cache.HasCitadelPlacementUnique;

    public List<AvailablePromotion> GetAvailablePromotions() => Unit.Promotions.GetPromotionTreeCandidates();

    public bool CanAffordPromotions(int count) => Unit.Promotions.CanAffordPromotions(count);

    /// <summary><c>true</c> if this unit stands in a non-puppet city of its own civ, so its promotions can be saved as the city's default.</summary>
    public bool CanSaveDefaultPromotions()
    {
        var city = (CityClass?)Unit.CurrentTile.GetCity();
        if (city == null) return false;
        return city.Civ == Unit.Civ && !city.IsPuppet;
    }

    public override CivView Civ() => GameView.GetCivView(Unit.Civ);

    public MapUnitView? GetOtherEscortUnit()
    {
        var other = Unit.GetOtherEscortUnit();
        return other == null ? null : GameView.GetMapUnitView(other);
    }

    // TODO(T139): getMovementString needs MapUnit.getMovementString (UncivGame settings number format); getTurnsToBuild needs TileImprovement.getTurnsToBuild
    public TileView GetMovementDestination() => GameView.TileMapView.GetTile(Unit.GetMovementDestination());

    public string? Action => Unit.Action;

    public bool IsEscorting() => Unit.IsEscorting();

    public bool IsAutomated() => Unit.IsAutomated();

    public bool CanBuildImprovement(TileImprovement improvement) => Unit.CanBuildImprovement(improvement);

    public Counter<string> GetStockpiledResourceRequirements(TileImprovement improvement) =>
        improvement.GetStockpiledResourceRequirements(Unit.Cache.State);

    /// <summary><c>true</c> if the unit was removed from its tile (captured, killed) since being selected.</summary>
    public bool HasDisappeared() => !Unit.GetTile().GetUnits().Contains(Unit);

    /// <exception cref="UnitMovement.UnreachableDestinationException">if no tile can be reached this turn towards <paramref name="targetTileView"/></exception>
    public TileView GetTileToMoveToThisTurn(TileView targetTileView) =>
        GameView.TileMapView.GetTile(Unit.Movement.GetTileToMoveToThisTurn(targetTileView.Unwrap()));

    /// <summary>Tiles this unit can reach this turn, with the total movement needed to get there.</summary>
    public Dictionary<TileView, float> GetDistanceToTiles()
    {
        // Kotlin: entries.associate { ... } - a LinkedHashMap in the iteration order of the source map
        var result = new Dictionary<TileView, float>();
        foreach (var entry in Unit.Movement.GetDistanceToTiles())
            result[GameView.TileMapView.GetTile(entry.Key)] = entry.Value.TotalMovement;
        return result;
    }

    public List<TileView> GetPathToTile(TileView tileView) =>
        Unit.Movement.GetDistanceToTiles().GetPathToTile(tileView.Unwrap()).Select(it => GameView.TileMapView.GetTile(it)).ToList();

    public List<TileView>? GetRoadPath(TileView tileView) =>
        Unit.Movement.GetRoadPath(tileView.Unwrap())?.Select(it => GameView.TileMapView.GetTile(it)).ToList();

    public bool CanReach(TileView tileView) => Unit.Movement.CanReach(tileView.Unwrap());

    public List<TileView> GetShortestPath(TileView tileView) =>
        Unit.Movement.GetShortestPath(tileView.Unwrap()).Select(it => GameView.TileMapView.GetTile(it)).ToList();

    public bool CanSwapTo(TileView tileView) => Unit.Movement.CanUnitSwapTo(tileView.Unwrap());

    // All "prepare and then choose tile" logic is actually UI stuff, and should be migrated out of logic layer
    public bool CanMoveTo(TileView tileView) => Unit.Movement.CanMoveTo(tileView.Unwrap());

    public int GetDamageFromTerrain(TileView tileView) => Unit.GetDamageFromTerrain(tileView.Unwrap());

    // This reads as "logic leaking through to UI"
    public bool IsUnknownTileWeShouldAssumeToBePassable(TileView tileView) =>
        Unit.Movement.IsUnknownTileWeShouldAssumeToBePassable(tileView.Unwrap());

    // TODO(T139): containsAttackableEnemy needs TargetHelper.containsAttackableEnemy
    public int MaxAttacksPerTurn() => Unit.MaxAttacksPerTurn();

    public bool CannotMove() => Unit.Cache.CannotMove;

    public List<TileView> GetReachableTilesInCurrentTurn() =>
        Unit.Movement.GetReachableTilesInCurrentTurn().Select(it => GameView.TileMapView.GetTile(it)).ToList();

    /// <summary>Defensive bonus of <paramref name="tile"/> as seen by this unit - unit-specific uniques of the improvement are taken into account</summary>
    public float GetDefensiveBonusOn(TileView tile, bool includeImprovementBonus = true) =>
        tile.Unwrap().GetDefensiveBonus(includeImprovementBonus, Unit);

    public int GetVisibilityRange() => Unit.GetVisibilityRange();

    public List<TileView> GetViewableTiles() => Unit.ViewableTiles.Select(it => GameView.TileMapView.GetTile(it)).ToList();

    /// <summary>Health points this unit would receive if healing on <paramref name="tile"/></summary>
    public int HealingAmountForTile(TileView tile, bool noTerrainDamage = false) => Unit.HealingAmountForTile(tile.Unwrap(), noTerrainDamage);

    public List<TileView> GetUnitSwappableTiles() =>
        Unit.Movement.GetUnitSwappableTiles().Select(it => GameView.TileMapView.GetTile(it)).ToList();

    public List<TileView> GetValidRoadConnectionTiles() =>
        Unit.Civ.GameInfo.TileMap.TileList.Where(it => MapPathing.IsValidRoadPathTile(Unit.Civ, it))
            .Select(it => GameView.TileMapView.GetTile(it)).ToList();

    public List<TileView> GetTilesInAttackRange() =>
        Unit.GetTile().GetTilesInDistanceRange(new IntRange(1, Unit.GetRange())).Select(it => GameView.TileMapView.GetTile(it)).ToList();

    // TODO(T139): getAttackableEnemies needs TargetHelper.getAttackableEnemies

    /// <summary><c>null</c> if the unit isn't currently pathing a road; otherwise the tiles still to come on that path.</summary>
    public List<TileView>? GetFutureAutomatedRoadConnectionTiles()
    {
        var path = Unit.AutomatedRoadConnectionPath;
        if (path == null) return null;
        var currTileIndex = path.IndexOf(Unit.CurrentTile.Position);
        if (currTileIndex == -1) return [];
        // Kotlin: filterIndexed { index > currTileIndex }.mapNotNull { getTile(it) } - the lookup is by HexCoord and drops unexplored tiles
        return path.Where((_, index) => index > currTileIndex)
            .Select(it => GameView.TileMapView.GetTile(it))
            .OfType<TileView>()
            .ToList();
    }

    // Actions
    public bool TrySwapMoveToTile(TileView tileView, bool keepEscorting = false)
    {
        Unit.Movement.SwapMoveToTile(tileView.Unwrap(), keepEscorting);
        return true;
    }

    /// <summary>Clears the improvement queue of the tile this unit stands on.</summary>
    public bool TryStopWorkingOnImprovement()
    {
        Unit.GetTile().StopWorkingOnImprovement();
        return true;
    }

    // TODO(T139): tryStartWorkingOnImprovement needs Tile.startWorkingOnImprovement/queueImprovement(TileImprovement, civ, unit)

    public bool TrySetInstanceName(string? name)
    {
        Unit.InstanceName = name;
        return true;
    }

    public bool TryAddPromotion(string promotionName)
    {
        Unit.Promotions.AddPromotion(promotionName);
        return true;
    }

    /// <summary>Remembers this unit's promotions as the default for new units of its type built in the city it stands in.</summary>
    public bool TrySaveDefaultPromotions()
    {
        var city = (CityClass?)Unit.CurrentTile.GetCity();
        if (city == null) return false;
        city.UnitShouldUseSavedPromotion[Unit.BaseUnit.Name] = true;
        city.UnitToPromotions[Unit.BaseUnit.Name] = Unit.Promotions;
        return true;
    }

    public bool TryDisband()
    {
        Unit.Disband();
        return true;
    }

    public bool TryResetAction()
    {
        Unit.Action = null;
        return true;
    }

    public bool TryHeadTowards(TileView tileView)
    {
        Unit.Movement.HeadTowards(tileView.Unwrap());
        return true;
    }

    public bool TrySetMoveToAction(TileView tileView)
    {
        var position = tileView.Position();
        Unit.Action = $"moveTo {position.X},{position.Y}";
        return true;
    }

    /// <summary>Moves the unit to <paramref name="attackableTileView"/>, handles siege setup, and returns <c>true</c> if an attack is still possible.</summary>
    public bool TryMovePreparingAttack(AttackableTileView attackableTileView, bool tryHealPillage = false) =>
        Battle.MovePreparingAttack(new MapUnitCombatant(Unit), attackableTileView.Unwrap(), tryHealPillage);

    /// <summary>Meant to be called only after all prerequisite checks (e.g. <see cref="TryMovePreparingAttack"/>) have been done.</summary>
    public DamageDealt AttackOrNuke(AttackableTileView attackableTileView) =>
        Battle.AttackOrNuke(new MapUnitCombatant(Unit), attackableTileView.Unwrap());

    public bool MayUseNuke(TileView targetTileView) => Nuke.MayUseNuke(new MapUnitCombatant(Unit), targetTileView.Unwrap());

    public bool TryNuke(TileView targetTileView)
    {
        Nuke.NUKE(new MapUnitCombatant(Unit), targetTileView.Unwrap());
        return true;
    }

    // TODO(T139): tryAirSweep needs AirInterception.airSweep
    // TODO(T080): canInvokeUnitAction, tryInvokeUnitAction need UnitActions

    public bool CanPillage(TileView tileView) => UnitActionsPillage.CanPillage(Unit, tileView.Unwrap());

    public bool TryFortifyIfCan()
    {
        Unit.FortifyIfCan();
        return true;
    }

    public bool TryMoveToTile(TileView tileView)
    {
        Unit.Movement.MoveToTile(tileView.Unwrap());
        return true;
    }

    /// <returns>whether <see cref="Due"/> was set</returns>
    public bool SetDue(bool value)
    {
        Unit.Due = value;
        return true;
    }

    public Counter<string> GetAirSweepAttackModifiers() => BattleDamage.GetAirSweepAttackModifiers(new MapUnitCombatant(Unit));
}

// Helpers built on top of the view API, rather than being part of it
public static class MapUnitViewExtensions
{
    /// <summary>
    /// Like <see cref="GameContext.StateBasedRandom"/>, but seeded only from what the unit can be read as through the view:
    /// owner, unit type, tile, remaining movement and the current turn. Deterministic for the same state, so reloading a save gives the same choices.
    /// </summary>
    public static GameRandom StateBasedRandom(this MapUnitView view, string caller, int seed = 31)
    {
        var position = view.GetTile().Position();
        return new GameRandom(Randomness.HashOf(JavaHash.HashCode(caller), seed, JavaHash.HashCode(view.Civ().CivId), JavaHash.HashCode(view.Name),
            position.X, position.Y, JavaHash.HashCode(unchecked(17 * view.CurrentMovement)), view.GameView.GetTurn()));
    }

    // Unit state derived from MapUnitView.Action
    public static bool IsMoving(this MapUnitView view) => view.Action?.StartsWith("moveTo", StringComparison.Ordinal) == true;

    public static bool IsExploring(this MapUnitView view) => view.Action == UnitActionType.Explore.Value();

    public static bool IsAutomatingRoadConnection(this MapUnitView view) => view.Action == Constants.ConnectRoadAction;

    public static bool IsPreparingParadrop(this MapUnitView view) => view.Action == UnitActionType.Paradrop.Value();

    public static bool IsPreparingAirSweep(this MapUnitView view) => view.Action == UnitActionType.AirSweep.Value();

    public static string? GetImprovementInProgress(this MapUnitView view) => view.GetTile().ImprovementInProgress;

    public static bool CanBuildCurrentImprovement(this MapUnitView view)
    {
        var tile = view.GetTile();
        var improvementInProgress = tile.ImprovementInProgress;
        if (improvementInProgress == null) return false;
        return view.CanBuildImprovement(tile.GetRuleset().TileImprovements[improvementInProgress]);
    }

    /// <summary>How many tiles away this unit can relocate to or reach in one move, or <c>null</c> if it isn't an air unit</summary>
    public static int? GetAirUnitMovementRange(this MapUnitView view) => view.IsAirUnit() ? view.GetRange() * 2 : null;

    public static bool IsNuclearWeapon(this MapUnitView view) => view.HasUnique(UniqueType.NuclearWeapon);

    public static int GetNukeBlastRadius(this MapUnitView view) =>
        view.GetMatchingUniques(UniqueType.BlastRadius).FirstOrDefault()?.Params[0].ToInt() ?? 2;
}
