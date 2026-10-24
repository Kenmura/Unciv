using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Map.MapUnits;

// Ported from core/src/com/unciv/logic/map/mapunit/UnitTurnManager.kt (T066). Stripped: Timers.timeThis profiling.

public class UnitTurnManager(MapUnit unit)
{
    public MapUnit Unit { get; } = unit;

    public void EndTurn()
    {
        Unit.Movement.ClearPathfindingCache();

        foreach (var unique in Unit.GetTriggeredUniques(UniqueType.TriggerUponTurnEnd))
            UniqueTriggerActivation.TriggerUnique(unique, Unit);

        if (Unit.HasMovement()
                && Unit.GetTile().ImprovementInProgress != null
                && Unit.CanBuildImprovement(Unit.GetTile().GetTileImprovementInProgress().NotNull()))
        {
            var tile = Unit.GetTile();
            if (tile.DoWorkerTurn(Unit))
            {
                var city = (CityObject?)tile.GetCity();
                if (city != null) city.ShouldReassignPopulation = true;
            }
        }

        if (!Unit.HasUnitMovedThisTurn()
            && (Unit.IsFortified() || (Unit.IsGuarding() && Unit.CanFortify()))
            && Unit.TurnsFortified < 2)
        {
            Unit.TurnsFortified++;
        }
        if (!Unit.IsFortified() && !Unit.IsGuarding())
            Unit.TurnsFortified = 0;

        if ((!Unit.HasUnitMovedThisTurn() && Unit.AttacksThisTurn == 0) || Unit.HasUnique(UniqueType.HealsEvenAfterAction))
            HealUnit();

        if (Unit.IsPreparingParadrop() || Unit.IsPreparingAirSweep())
            Unit.Action = null;

        if (Unit.HasUnique(UniqueType.ReligiousUnit)
                && Unit.GetTile().GetOwner() != null
                && !Unit.GetTile().GetOwner().NotNull().IsCityState
                && !Unit.Civ.DiplomacyFunctions.CanPassThroughTiles((CivilizationObject)Unit.GetTile().GetOwner().NotNull()))
        {
            int? lostReligiousStrength = null;
            foreach (var unique in Unit.GetMatchingUniques(UniqueType.CanEnterForeignTilesButLosesReligiousStrength))
            {
                var value = unique.Params[0].ToInt();
                if (lostReligiousStrength == null || value < lostReligiousStrength) lostReligiousStrength = value;
            }
            if (lostReligiousStrength != null)
                Unit.ReligiousStrengthLost = unchecked(Unit.ReligiousStrengthLost + lostReligiousStrength.Value);
            if (Unit.ReligiousStrengthLost >= Unit.BaseUnit.ReligiousStrength)
            {
                Unit.Civ.AddNotification($"Your [{Unit.Name}] lost its faith after spending too long inside enemy territory!",
                    Unit.GetTile().Position, NotificationCategory.Units, Unit.Name);
                Unit.Destroy();
            }
        }

        DoCitadelDamage();
        DoTerrainDamage();

        Unit.AddMovementMemory();

        foreach (var unique in Unit.GetTriggeredUniques(UniqueType.TriggerUponEndingTurnInTile, null,
                     it => Unit.GetTile().MatchesFilter(it.Params[0], Unit.Civ)))
            UniqueTriggerActivation.TriggerUnique(unique, Unit);
    }

    private void HealUnit()
    {
        var amountToHealBy = Unit.GetHealAmountForCurrentTile();
        if (amountToHealBy == 0) return;

        Unit.HealBy(amountToHealBy);
    }

    private void DoCitadelDamage()
    {
        // Check for Citadel damage - note: 'Damage does not stack with other Citadels'
        // Kotlin maxByOrNull: the first of the maximal elements
        (Tiles.Tile Tile, int Damage)? best = null;
        foreach (var tile in Unit.CurrentTile.Neighbors)
        {
            var owner = tile.GetOwner();
            if (owner == null || tile.GetUnpillagedImprovement() == null || !Unit.Civ.IsAtWarWith((CivilizationObject)owner)) continue;
            var tileDamage = ((IHasUniques)tile.TileImprovement.NotNull()).GetMatchingUniques(UniqueType.DamagesAdjacentEnemyUnits, tile.StateThisTile)
                .SumOf(it => it.Params[0].ToInt());
            if (best == null || best.Value.Damage < tileDamage) best = (tile, tileDamage);
        }
        if (best == null) return;
        var (citadelTile, damage) = best.Value;
        if (damage == 0) return;
        Unit.TakeDamage(damage);
        var improvementName = citadelTile.Improvement.NotNull(); // guarded by `getUnpillagedImprovement() != null` above
        var improvementIcon = $"ImprovementIcons/{improvementName}";
        var locations = LocationAction.Of(citadelTile.Position, Unit.CurrentTile.Position).ToList();
        if (Unit.Health <= 0)
        {
            Unit.Civ.AddNotification(
                $"An enemy [{improvementName}] has destroyed our [{Unit.Name}]",
                locations,
                NotificationCategory.War,
                improvementIcon, NotificationIcon.Death, Unit.Name);
            ((CivilizationObject?)citadelTile.GetOwner())?.AddNotification(
                $"Your [{improvementName}] has destroyed an enemy [{Unit.Name}]",
                locations,
                NotificationCategory.War,
                improvementIcon, NotificationIcon.Death, Unit.Name);
            Unit.Destroy();
        }
        else
        {
            Unit.Civ.AddNotification(
                $"An enemy [{improvementName}] has attacked our [{Unit.Name}]",
                locations,
                NotificationCategory.War,
                improvementIcon, NotificationIcon.War, Unit.Name);
        }
    }

    private void DoTerrainDamage()
    {
        var tileDamage = Unit.GetDamageFromTerrain();
        if (tileDamage == 0) return;

        Unit.TakeDamage(tileDamage);

        if (Unit.IsDestroyed)
        {
            Unit.Civ.AddNotification(
                $"Our [{Unit.Name}] took [{tileDamage}] tile damage and was destroyed",
                Unit.CurrentTile.Position,
                NotificationCategory.Units,
                Unit.Name,
                NotificationIcon.Death);
        }
        else
        {
            Unit.Civ.AddNotification(
                $"Our [{Unit.Name}] took [{tileDamage}] tile damage",
                new MapUnitAction(Unit.CurrentTile.Position, Unit.Id),
                NotificationCategory.Units,
                Unit.Name);
        }
    }

    public void StartTurn()
    {
        Unit.Movement.ClearPathfindingCache();
        Unit.CurrentMovement = Unit.GetMaxMovement();
        Unit.AttacksThisTurn = 0;
        Unit.Due = true;

        foreach (var unique in Unit.GetTriggeredUniques(UniqueType.TriggerUponTurnStart))
            UniqueTriggerActivation.TriggerUnique(unique, Unit);

        // Wake sleeping units if there's an enemy in vision range:
        // Military units always but civilians only if not protected.
        if (Unit.IsSleeping() && (Unit.IsMilitary() || (Unit.CurrentTile.MilitaryUnit == null && !Unit.CurrentTile.IsCityCenter())) &&
                Unit.CurrentTile.GetTilesInDistance(3).Any(it =>
                    it.MilitaryUnit != null && Unit.Civ.ViewableTiles.Contains(it) && it.MilitaryUnit.Civ.IsAtWarWith(Unit.Civ)))
            Unit.Action = null;

        if (Unit.Action != null && Unit.Health > 99 && Unit.IsActionUntilHealed())
        {
            Unit.Action = null; // wake up when healed
            Unit.Civ.AddNotification($"[{Unit.ShortDisplayName()}] has fully healed",
                new MapUnitAction(Unit.CurrentTile.Position, Unit.Id), NotificationCategory.Units, Unit.Name);
        }

        var tileOwner = (CivilizationObject?)Unit.GetTile().GetOwner();
        if (tileOwner != null
                && !Unit.Cache.CanEnterForeignTerrain
                && !Unit.Civ.DiplomacyFunctions.CanPassThroughTiles(tileOwner)
                && !tileOwner.IsCityState
        ) // if an enemy city expanded onto this tile while I was in it
            Unit.Movement.TeleportToClosestMoveableTile();

        Unit.AddMovementMemory();
        Unit.AttacksSinceTurnStart.Clear();

        foreach (var status in Unit.StatusMap.Values.ToList())
        {
            status.TurnsLeft--;
            if (status.TurnsLeft <= 0) Unit.RemoveStatus(status.Name);
        }
        Unit.UpdateUniques();
    }
}
