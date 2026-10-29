using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Map.MapUnit.Actions;

// Ported from core/src/com/unciv/logic/map/mapunit/actions/UnitActionsReligion.kt (T079). Stripped: the purity annotations.
// Tile.GetCity() is typed by its seam; the owning city is always a City, hence the casts.
public static class UnitActionsReligion
{
    private static bool HasActionModifiers(UniqueObject unique) =>
        unique.Modifiers.Any(it => it.Type?.TargetTypes().Contains(UniqueTarget.UnitActionModifier) == true);

    internal static IEnumerable<UnitAction> GetFoundReligionActions(MapUnitClass unit, Tile tile)
    {
        if (!unit.Civ.ReligionManager.MayFoundReligionAtAll()) return [];

        var unique = UnitActionModifiers.GetUsableUnitActionUniques(unit, UniqueType.MayFoundReligion).FirstOrDefault();
        if (unique == null) return [];

        bool hasActionModifiers = HasActionModifiers(unique);

        return [new UnitAction(
            UnitActionType.FoundReligion,
            hasActionModifiers
                ? UnitActionModifiers.ActionTextWithSideEffects(UnitActionType.FoundReligion.Value(), unique, unit)
                : UnitActionType.FoundReligion.Value(),
            associatedUnique: unique,
            action: unit.Civ.ReligionManager.MayFoundReligionHere(tile)
                && UnitActionModifiers.CanActivateSideEffects(unit, unique)
                ? () =>
                {
                    unit.Civ.ReligionManager.FoundReligion(unit);

                    if (hasActionModifiers) UnitActionModifiers.ActivateSideEffects(unit, unique);
                    else unit.Consume();
                }
                : null)];
    }

    internal static IEnumerable<UnitAction> GetEnhanceReligionActions(MapUnitClass unit, Tile tile)
    {
        if (!unit.Civ.ReligionManager.MayEnhanceReligionAtAll()) return [];

        var unique = UnitActionModifiers.GetUsableUnitActionUniques(unit, UniqueType.MayEnhanceReligion).FirstOrDefault();
        if (unique == null) return [];

        bool hasActionModifiers = HasActionModifiers(unique);

        string baseTitle = $"Enhance [{unit.Civ.ReligionManager.Religion.NotNull().GetReligionDisplayName()}]";
        return [new UnitAction(
            UnitActionType.EnhanceReligion,
            title: hasActionModifiers
                ? UnitActionModifiers.ActionTextWithSideEffects(baseTitle, unique, unit)
                : baseTitle,
            associatedUnique: unique,
            action: unit.Civ.ReligionManager.MayEnhanceReligionHere(tile)
                && UnitActionModifiers.CanActivateSideEffects(unit, unique)
                ? () =>
                {
                    unit.Civ.ReligionManager.UseProphetForEnhancingReligion(unit);
                    if (hasActionModifiers) UnitActionModifiers.ActivateSideEffects(unit, unique);
                    else unit.Consume();
                }
                : null)];
    }

    private static int GetPressureAddedFromSpread(MapUnitClass unit)
    {
        float pressureAdded = unit.BaseUnit.ReligiousStrength;

        unit.ForEachMatchingUnique(UniqueType.SpreadReligionStrength, null, true, unique =>
        {
            pressureAdded *= unique.Params[0].ToPercent();
        });

        if (unit.Civ.GameInfo.LeagueReligionSpreadPercent(unit.Religion) is var leagueSpread and not 0)
            pressureAdded *= 1 + leagueSpread / 100f; // B016: World Religion

        return KotlinMath.FloatToInt(pressureAdded);
    }

    internal static IEnumerable<UnitAction> GetSpreadReligionActions(MapUnitClass unit, Tile tile)
    {
        if (!unit.Civ.ReligionManager.MaySpreadReligionAtAll(unit)) return [];
        var city = (CityObject?)tile.GetCity();
        if (city == null) return [];

        var newStyleUnique = UnitActionModifiers.GetUsableUnitActionUniques(unit, UniqueType.CanSpreadReligion).FirstOrDefault();
        if (newStyleUnique == null) return [];

        string title = UnitActionModifiers.ActionTextWithSideEffects($"Spread [{unit.GetReligionDisplayName().NotNull()}]",
            newStyleUnique, unit);

        return [new UnitAction(
            UnitActionType.SpreadReligion,
            title: title,
            associatedUnique: newStyleUnique,
            action: unit.Civ.ReligionManager.MaySpreadReligionNow(unit)
                && UnitActionModifiers.CanActivateSideEffects(unit, newStyleUnique)
                ? () =>
                {
                    int followersOfOtherReligions = city.Religion.GetFollowersOfOtherReligionsThan(unit.Religion.NotNull());
                    unit.ForEachMatchingUnique(UniqueType.StatsWhenSpreading, null, true, unique =>
                    {
                        unit.Civ.AddStat(StatExtensions.ValueOf(unique.Params[1]), unchecked(followersOfOtherReligions * unique.Params[0].ToInt()));
                    });
                    var previousReligion = city.Religion.GetMajorityReligion();
                    city.Religion.AddPressure(unit.Religion.NotNull(), GetPressureAddedFromSpread(unit));
                    if (unit.HasUnique(UniqueType.RemoveOtherReligions))
                        city.Religion.RemoveAllPressuresExceptFor(unit.Religion.NotNull());

                    var newReligion = city.Religion.GetMajorityReligion();
                    if (previousReligion != newReligion && newReligion != null && city.Civ != unit.Civ)
                    {
                        city.Civ.AddNotification($"[{unit.Civ.CivName}]'s [{unit.Name}] has converted [{city.Name}] to [{newReligion.Name}]!", NotificationCategory.Religion);
                    }

                    UnitActionModifiers.ActivateSideEffects(unit, newStyleUnique);

                    if (city.Civ != unit.Civ // Set flag only if they don't want our religion, which is when they can get their own
                        && (city.Civ.ReligionManager.RemainingFoundableReligions() != 0 || city.Civ.ReligionManager.ReligionState > ReligionState.Pantheon))
                        city.Civ.GetDiplomacyManager(unit.Civ).NotNull().SetFlag(DiplomacyFlags.SpreadReligionInOurCities, 30, true);
                }
                : null)];
    }

    internal static IEnumerable<UnitAction> GetRemoveHeresyActions(MapUnitClass unit, Tile tile)
    {
        if (!unit.Civ.GameInfo.IsReligionEnabled()) return [];
        if (unit.Religion == null || !unit.Civ.GameInfo.Religions.TryGetValue(unit.Religion, out var religion)) return [];
        if (religion.IsPantheon()) return [];

        var city = (CityObject?)tile.GetCity();
        if (city == null) return [];
        if (city.Civ != unit.Civ) return [];
        // Only allow the action if the city actually has any foreign religion
        // This will almost be always due to pressure from cities close-by
        if (city.Religion.GetPressures().All(it => it.Key == unit.Religion)) return [];

        var newStyleUnique = UnitActionModifiers.GetUsableUnitActionUniques(unit, UniqueType.CanRemoveHeresy).FirstOrDefault();
        bool hasNewStyleAbility = newStyleUnique != null;

        if (!hasNewStyleAbility) return [];

        string title =
            UnitActionModifiers.ActionTextWithSideEffects("Remove Heresy", newStyleUnique.NotNull(), unit);

        return [new UnitAction(
            UnitActionType.RemoveHeresy,
            title: title,
            associatedUnique: newStyleUnique,
            action: UnitActionModifiers.CanActivateSideEffects(unit, newStyleUnique.NotNull())
                ? () =>
                {
                    city.Religion.RemoveAllPressuresExceptFor(unit.Religion.NotNull());
                    if (city.Religion.ReligionThisIsTheHolyCityOf != null)
                    {
                        var holyCityReligion = unit.Civ.GameInfo.Religions[city.Religion.ReligionThisIsTheHolyCityOf];
                        if (city.Religion.ReligionThisIsTheHolyCityOf != unit.Religion && !city.Religion.IsBlockedHolyCity)
                        {
                            holyCityReligion.FoundingCiv.AddNotification($"An [{unit.BaseUnit.Name}] has removed your religion [{holyCityReligion.GetReligionDisplayName()}] from its Holy City [{city.Name}]!", NotificationCategory.Religion);
                            city.Religion.IsBlockedHolyCity = true;
                        }
                        else if (city.Religion.ReligionThisIsTheHolyCityOf == unit.Religion && city.Religion.IsBlockedHolyCity)
                        {
                            holyCityReligion.FoundingCiv.AddNotification($"An [{unit.BaseUnit.Name}] has restored [{city.Name}] as the Holy City of your religion [{holyCityReligion.GetReligionDisplayName()}]!", NotificationCategory.Religion);
                            city.Religion.IsBlockedHolyCity = false;
                        }
                    }

                    UnitActionModifiers.ActivateSideEffects(unit, newStyleUnique.NotNull());
                }
                : null)];
    }
}
