using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/ReligiousUnitAutomation.kt (T091). Purity annotations dropped (T027 policy).
// - Kotlin Sequences stay lazy (IEnumerable); sortedBy is stable (OrderBy); MinBy/MaxBy return the first extremum like minByOrNull/maxByOrNull.
public static class ReligiousUnitAutomation
{
    public static void AutomateMissionary(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        if (unitView.Religion != unitView.Civ().GetReligion()?.Name || unitView.Religion == null)
        {
            unitView.TryDisband();
            return;
        }

        var ourCitiesWithoutReligion = unit.Civ.Cities
            .Where(it => it.Religion.GetMajorityReligion() != unitView.Civ().GetReligion())
            .ToList();

        bool IsValidSpreadReligionTarget(CityObject city)
        {
            var diplomacyManager = unit.Civ.GetDiplomacyManager(city.Civ);
            if (diplomacyManager?.HasFlag(DiplomacyFlags.AgreedToNotSpreadReligion) == true)
            {
                // See NextTurnAutomation - these are the conditions under which AI agrees to religious demands
                // If they still hold, keep the agreement, otherwise we can renege
                if (diplomacyManager.RelationshipLevel() == RelationshipLevel.Ally) return false;
                if (Automation.ThreatAssessment(unitView.Civ(), new GameView((GameInfo)unit.Civ.GameInfo, unit.Civ).GetForeignCivView(city.Civ)) >= ThreatLevel.High) return false;
            }
            return true;
        }

        // Lowest value will be chosen
        int RankCityForReligionSpread(CityObject city)
        {
            var rank = city.GetCenterTile().AerialDistanceTo(unit.GetTile());

            var diplomacyManager = unit.Civ.GetDiplomacyManager(city.Civ);
            if (diplomacyManager?.HasFlag(DiplomacyFlags.AgreedToNotSpreadReligion) == true)
                rank += 10; // Greatly discourage, but if the other options are too far away we'll take it anyway

            return rank;
        }

        var city = ourCitiesWithoutReligion.Count != 0
            ? ourCitiesWithoutReligion.MinBy(it => it.GetCenterTile().AerialDistanceTo(unit.GetTile()))
            : unit.Civ.GameInfo.GetCities()
                .Where(it => it.Religion.GetMajorityReligion() != unitView.Civ().GetReligion())
                .Where(it => it.Civ.Knows(unit.Civ) && !it.Civ.IsAtWarWith(unit.Civ))
                .Where(it => !it.Religion.IsProtectedByInquisitor(unitView.Religion))
                .Where(IsValidSpreadReligionTarget)
                .MinBy(RankCityForReligionSpread);

        if (city == null) return;
        var destination = city.GetTiles()
            .Where(it => unit.Movement.CanMoveTo(it) || it == unit.GetTile())
            .OrderBy(it => it.AerialDistanceTo(unit.GetTile()))
            .FirstOrDefault(it => unit.Movement.CanReach(it));
        if (destination == null) return;

        unit.Movement.HeadTowards(destination);

        if (city.GetTiles().Contains(unit.GetTile()) && unit.Civ.ReligionManager.MaySpreadReligionNow(unit))
            unitView.TryInvokeUnitAction(UnitActionType.SpreadReligion);
    }

    public static void AutomateInquisitor(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        var civReligion = unitView.Civ().GetReligion();

        if (unitView.Religion != civReligion?.Name || unitView.Religion == null)
        {
            // No need to keep a unit we can't use, as it only blocks religion spreads of religions other that its own
            unitView.TryDisband();
            return;
        }

        var holyCity = unit.Civ.ReligionManager.GetHolyCity();
        var cityToConvert = DetermineBestInquisitorCityToConvert(unitView); // Also returns null if the inquisitor can't convert cities
        var pressureDeficit = cityToConvert == null ? 0 : cityToConvert.Religion.GetPressureDeficit(civReligion?.Name);

        var citiesToProtect = unit.Civ.Cities
            .Where(it => it.Religion.GetMajorityReligion() == civReligion)
            // We only look at cities that are not currently protected or are protected by us
            .Where(it => !it.Religion.IsProtectedByInquisitor() || it.GetCenterTile().Neighbors.Contains(unit.GetTile()));

        // cities with most populations will be prioritized by the AI
        var cityToProtect = citiesToProtect.MaxBy(it => it.Population.Population);

        CityObject? destinationCity;
        if (cityToConvert != null
            && (cityToConvert == holyCity
                || pressureDeficit > Constants.AiPreferInquisitorOverMissionaryPressureDifference * unit.Civ.GameInfo.Ruleset.ModOptions.Constants.ReligionMissionaryPressureMultiplier
                || (cityToConvert.Religion.IsBlockedHolyCity && cityToConvert.Religion.ReligionThisIsTheHolyCityOf == civReligion?.Name))
            && unitView.HasUnique(UniqueType.CanRemoveHeresy))
            destinationCity = cityToConvert;
        else if (cityToProtect != null && unitView.HasUnique(UniqueType.PreventSpreadingReligion))
            destinationCity = holyCity != null && !holyCity.Religion.IsProtectedByInquisitor() ? holyCity : cityToProtect;
        else if (cityToConvert != null)
            destinationCity = cityToConvert;
        else
            destinationCity = null;

        if (destinationCity == null) return;
        var destinationTile = destinationCity.GetCenterTile().Neighbors
            .Where(it => unit.Movement.CanMoveTo(it) || it == unit.GetTile())
            .OrderBy(it => it.AerialDistanceTo(unit.CurrentTile))
            .FirstOrDefault(it => unit.Movement.CanReach(it));
        if (destinationTile == null) return;

        unit.Movement.HeadTowards(destinationTile);

        if (cityToConvert != null && unit.GetTile().GetCity() == destinationCity)
            unitView.TryInvokeUnitAction(UnitActionType.RemoveHeresy);
    }

    private static CityObject? DetermineBestInquisitorCityToConvert(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        if (unitView.Religion != unitView.Civ().GetReligion()?.Name || !unitView.HasUnique(UniqueType.CanRemoveHeresy))
            return null;

        var holyCity = unit.Civ.ReligionManager.GetHolyCity();
        // Our own holy city was taken over!
        if (holyCity != null && holyCity.Civ == unit.Civ
            && holyCity.Religion.GetMajorityReligion() != unitView.Civ().GetReligion().NotNull())
            return holyCity;

        var blockedHolyCity = unit.Civ.Cities.FirstOrDefault(it => it.Religion.IsBlockedHolyCity && it.Religion.ReligionThisIsTheHolyCityOf == unitView.Religion);
        if (blockedHolyCity != null)
            return blockedHolyCity;

        // Find cities
        var relevantCities = unit.Civ.Cities
            .Where(it => it.GetCenterTile().IsExplored(unit.Civ)) // Cities we know about
                                                                  // Someone else is controlling this city
            .Where(it =>
            {
                var majorityReligion = it.Religion.GetMajorityReligion();
                return majorityReligion != null && majorityReligion != unitView.Civ().GetReligion();
            });

        var closeCity = relevantCities
            .Where(it => it.GetCenterTile().AerialDistanceTo(unit.CurrentTile) <= 20)
            // Find the city that we're the closest to converting
            .MinBy(it => it.Religion.GetPressureDeficit(unitView.Civ().GetReligion()?.Name));
        if (closeCity != null) return closeCity;

        return relevantCities.MinBy(it => it.Religion.GetPressureDeficit(unitView.Civ().GetReligion()?.Name));
    }

    public static void FoundReligion(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        var owningCity = (CityObject?)unit.GetTile().OwningCity;
        var cityToFoundReligionAt =
            unit.GetTile().IsCityCenter() && !owningCity.NotNull().IsHolyCity()
                ? owningCity
                : unit.Civ.Cities.FirstOrDefault(it =>
                    !it.IsHolyCity()
                    && unit.Movement.CanMoveTo(it.GetCenterTile())
                    && unit.Movement.CanReach(it.GetCenterTile()));
        if (cityToFoundReligionAt == null) return;
        if (unit.GetTile() != cityToFoundReligionAt.GetCenterTile())
        {
            unit.Movement.HeadTowards(cityToFoundReligionAt.GetCenterTile());
            return;
        }

        unitView.TryInvokeUnitAction(UnitActionType.FoundReligion);
    }

    public static void EnhanceReligion(MapUnitView unitView)
    {
        // Try go to a nearby city
        if (!unitView.GetTile().IsCityCenter())
            UnitAutomation.TryEnterOwnClosestCity(unitView);

        // If we were unable to go there this turn, unable to do anything else
        if (!unitView.GetTile().IsCityCenter())
            return;

        unitView.TryInvokeUnitAction(UnitActionType.EnhanceReligion);
    }
}
