using Unciv.Logic.Civilization;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Map.MapUnit.Actions;

// Ported from core/src/com/unciv/logic/map/mapunit/actions/UnitActionsPillage.kt (T079). Stripped: the purity annotations.
// Tile.GetOwner() is typed by its seam; the owner is always a Civilization, hence the casts.
public static class UnitActionsPillage
{
    internal static IEnumerable<UnitAction> GetPillageActions(MapUnitClass unit, Tile tile)
    {
        var action = GetPillageAction(unit, tile);
        return action == null ? [] : [action];
    }

    internal static UnitAction? GetPillageAction(MapUnitClass unit, Tile tile)
    {
        string? improvementName = unit.CurrentTile.GetImprovementToPillageName();
        if (unit.IsCivilian() || improvementName == null || ReferenceEquals(tile.GetOwner(), unit.Civ)) return null;

        void PillageNow()
        {
            string pillagedImprovement = unit.CurrentTile.GetImprovementToPillageName().NotNull(); // can this differ from improvementName due to later execution???
            bool pillagingImprovement = unit.CurrentTile.CanPillageTileImprovement();
            string pillageText = $"An enemy [{unit.BaseUnit.Name}] has pillaged our [{pillagedImprovement}]";
            string icon = $"ImprovementIcons/{pillagedImprovement}";
            ((CivilizationObject?)tile.GetOwner())?.AddNotification(
                pillageText,
                tile.Position,
                NotificationCategory.War,
                icon,
                NotificationIcon.War,
                unit.BaseUnit.Name);

            PillageLooting(tile, unit);
            tile.SetPillaged(); // Also triggers reassignPopulation
            if (tile.Resource != null) tile.GetOwner()?.UpdateCivResources(); // this might take away a resource

            bool freePillage = unit.HasUnique(UniqueType.NoMovementToPillage, checkCivInfoUniques: true);
            if (!freePillage) unit.UseMovementPoints(1f);

            if (pillagingImprovement) // only Improvements heal HP
            {
                float healAmount = 25f;
                unit.ForEachMatchingUnique(UniqueType.PercentHealthFromPillaging, null, true, unique =>
                {
                    healAmount *= unique.Params[0].ToPercent();
                });
                unit.HealBy(KotlinMath.FloatToInt(healAmount));
            }

            var improvementToPillage = tile.GetImprovementToPillage();
            if (improvementToPillage != null && ((IHasUniques)improvementToPillage).HasUnique(UniqueType.DestroyedWhenPillaged))
                tile.RemoveImprovement();
        }

        return new UnitAction(
            UnitActionType.Pillage,
            title: $"{UnitActionType.Pillage} [{improvementName}]",
            confirmation: new UnitActionConfirmation(
                $"Are you sure you want to pillage this [{improvementName}]?", "Pillage"),
            action: unit.HasMovement() && CanPillage(unit, tile) ? PillageNow : null);
    }

    private static void PillageLooting(Tile tile, MapUnitClass unit)
    {
        CityObject? closestCity = null;
        int closestDistance = 0;
        foreach (var city in unit.Civ.Cities)
        {
            int distance = city.GetCenterTile().AerialDistanceTo(tile);
            if (closestCity == null || distance < closestDistance)
            {
                closestCity = city;
                closestDistance = distance;
            }
        }

        var improvement = tile.GetImprovementToPillage().NotNull();
        var improvementUniques = (IHasUniques)improvement;

        // Accumulate the loot
        var pillageYield = new Stats();
        var stateForConditionals = unit.Cache.State;
        var random = unit.Civ.GameInfo.SeededRandom(unchecked(unit.Civ.GameInfo.Turns * (long)unit.GetTile().Position.GetHashCode()));
        improvementUniques.ForEachMatchingUnique(UniqueType.PillageYieldRandom, stateForConditionals, unique =>
        {
            foreach (var (stat, value) in unique.Stats)
            {
                var yieldsToAdd = new Stats();
                // Unique text says "approximately [X]", so we add 0..X twice - think an RPG's 2d12
                int first = random.NextInt(KotlinMath.FloatToInt(value + 1));
                yieldsToAdd.Add(stat, first + (float)random.NextInt(KotlinMath.FloatToInt(value + 1)));
                if (unique.IsModifiedByGameSpeed())
                    yieldsToAdd *= unit.Civ.GameInfo.Speed.Modifier;
                if (unique.IsModifiedByGameProgress())
                    yieldsToAdd *= unique.GetGameProgressModifier(unit.Civ);
                pillageYield.Add(yieldsToAdd);
            }
        });
        improvementUniques.ForEachMatchingUnique(UniqueType.PillageYieldFixed, stateForConditionals, unique =>
        {
            var yieldsToAdd = unique.Stats;
            if (unique.IsModifiedByGameSpeed())
                yieldsToAdd *= unit.Civ.GameInfo.Speed.Modifier;
            if (unique.IsModifiedByGameProgress())
                yieldsToAdd *= unique.GetGameProgressModifier(unit.Civ);
            pillageYield.Add(yieldsToAdd);
        });

        // Multiply according to uniques
        unit.ForEachMatchingUnique(UniqueType.PercentYieldFromPillaging, null, true, unique =>
        {
            pillageYield *= unique.Params[0].ToPercent();
        });

        // Please no notification when there's no loot
        if (pillageYield.IsEmpty()) return;

        // Distribute the loot and keep record what went to civ/city for the notification(s)
        var globalPillageYield = new Stats();
        var toCityPillageYield = new Stats();
        foreach (var (stat, value) in pillageYield)
        {
            if (StatExtensions.StatsWithCivWideField.Contains(stat))
            {
                unit.Civ.AddStat(stat, KotlinMath.FloatToInt(value));
                globalPillageYield[stat] += value;
            }
            else if (closestCity != null)
            {
                closestCity.AddStat(stat, KotlinMath.FloatToInt(value));
                toCityPillageYield[stat] += value;
            }
        }

        // Now tell the user about the swag
        void Notify(Stats stats, string suffix)
        {
            if (stats.IsEmpty()) return;
            string text = $"We have looted [{stats.ToStringWithoutIcons()}] from a [{improvement.Name}]" + suffix;
            unit.Civ.AddNotification(text, tile.Position, NotificationCategory.War, $"ImprovementIcons/{improvement.Name}", NotificationIcon.War);
        }

        Notify(toCityPillageYield, $" which has been sent to [{closestCity?.Name ?? "null"}]");
        Notify(globalPillageYield, "");
    }

    // Public - used in UnitAutomation
    public static bool CanPillage(MapUnitClass unit, Tile tile)
    {
        if (unit.IsTransported) return false;
        if (!tile.CanPillageTile()) return false;
        if (unit.HasUnique(UniqueType.CannotPillage, checkCivInfoUniques: true)) return false;
        var tileOwner = (CivilizationObject?)tile.GetOwner();
        // Can't pillage friendly tiles, just like you can't attack them - it's an 'act of war' thing
        return tileOwner == null || unit.Civ.IsAtWarWith(tileOwner);
    }
}
