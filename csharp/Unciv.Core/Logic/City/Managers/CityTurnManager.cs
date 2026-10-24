using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Screens.OverviewScreen;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.City.Managers;

// Ported from core/src/com/unciv/logic/city/managers/CityTurnManager.kt (T064).
//
// Stripped: the timeThis profiling wrappers.

public class CityTurnManager(CityObject city)
{
    private readonly CityObject city = city;

    public void StartTurn()
    {
        city.ClearCaches();

        foreach (var resource in city.GetResourcesGeneratedByCity())
        {
            if (resource.Resource.IsStockpiled && resource.Resource.IsCityWide)
                city.GainStockpiledResource(resource.Resource, resource.Amount);
        }
        foreach (var unique in city.GetTriggeredUniques(UniqueType.TriggerUponTurnStart, includeCivUniques: false).ToList())
        {
            UniqueTriggerActivation.TriggerUnique(unique, city);
        }

        // Construct units at the beginning of the turn,
        // so they won't be generated out in the open and vulnerable to enemy attacks before you can control them
        city.CityConstructions.ConstructIfEnough();

        city.TryUpdateRoadStatus();
        city.AttackedThisTurn = false;

        // The ordering is intentional - you get a turn without WLTKD even if you have the next resource already
        // Also resolve end of resistance before updateCitizens
        if (!city.HasFlag(CityFlags.WeLoveTheKing))
            TryWeLoveTheKing();
        NextTurnFlags();

        if (city.IsPuppet)
        {
            city.SetCityFocus(CityFocus.GoldFocus);
            city.ReassignAllPopulation();
        }
        else if (city.ShouldReassignPopulation || city.Civ.IsAI())
        {
            if (city.Civ.IsAI())
                city.SetCityFocus(ChooseCityFocus());
            city.ReassignPopulation(); // includes cityStats.update
        }
        else
            city.CityStats.Update();

        // Seed resource demand countdown
        if (city.DemandedResource == "" && !city.HasFlag(CityFlags.ResourceDemand))
        {
            SetWltkResourceDemandCooldown(true);
        }
    }

    private CityFocus ChooseCityFocus()
    {
        // Small cities focus on growing, following Vox Populi.
        if (city.Population.Population <= 3) return CityFocus.NoFocus;
        var construction = city.CityConstructions.GetCurrentConstruction();
        // Focus citizens on production while the city builds a world wonder or spaceship part.
        if (construction is Building { IsWonder: true }) return CityFocus.ProductionFocus;
        if (construction is INonPerpetualConstruction nonPerpetual && nonPerpetual.HasUnique(UniqueType.SpaceshipPart))
            return CityFocus.ProductionFocus;
        return CityFocus.NoFocus;
    }

    private void SetWltkResourceDemandCooldown(bool isNewCity)
    {
        var rng = city.State.StateBasedRandom("CityTurnManager.setWltkResourceDemandCooldown");
        // Demand a new resource in ~20 turns on Standard speed
        var duration = 15 + rng.NextInt(10);
        if (isNewCity && city.IsCapital())
            duration += 10;
        city.SetFlag(CityFlags.ResourceDemand, duration, true);
    }

    private void TryWeLoveTheKing()
    {
        if (city.DemandedResource == "") return;
        if (city.GetAvailableResourceAmount(city.DemandedResource) <= 0) return;

        // manually adjust with game speed because of the +1 at the end
        var duration = KotlinMath.RoundToInt(20 * city.Civ.GameInfo.Speed.Modifier) + 1; // +1 because it will be decremented by 1 in the same startTurn()
        city.SetFlag(CityFlags.WeLoveTheKing, duration);
        // Otherwise ResourceDemand can expire mid-celebration, rewrite demandedResource,
        // and the Resources overview mislabels the active WLTKD (celebration is flag-based).
        city.RemoveFlag(CityFlags.ResourceDemand);
        city.Civ.AddNotification(
            $"Because they have [{city.DemandedResource}], the citizens of [{city.Name}] are celebrating We Love The King Day!",
            CityAction.WithLocation(city), NotificationCategory.General, NotificationIcon.City, NotificationIcon.Happiness);
    }

    // cf DiplomacyManager nextTurnFlags
    private void NextTurnFlags()
    {
        foreach (var flag in city.FlagsCountdown.Keys.ToList())
        {
            if (city.FlagsCountdown[flag] > 0)
                city.FlagsCountdown[flag] = city.FlagsCountdown[flag] - 1;

            if (city.FlagsCountdown[flag] == 0)
            {
                city.FlagsCountdown.Remove(flag);

                switch (flag)
                {
                    case nameof(CityFlags.ResourceDemand):
                        // WLTKD end already demands the next resource; demanding while celebrating
                        // overwrites demandedResource and mislabels the active celebration in the UI
                        if (!city.HasFlag(CityFlags.WeLoveTheKing))
                            DemandNewResource();
                        break;
                    case nameof(CityFlags.WeLoveTheKing):
                        city.Civ.AddNotification(
                            $"We Love The King Day in [{city.Name}] has ended.",
                            CityAction.WithLocation(city), NotificationCategory.General, NotificationIcon.City);
                        DemandNewResource();
                        break;
                    case nameof(CityFlags.Resistance):
                        city.ShouldReassignPopulation = true;
                        city.Civ.AddNotification(
                            $"The resistance in [{city.Name}] has ended!",
                            CityAction.WithLocation(city), NotificationCategory.General, "StatIcons/Resistance");
                        break;
                }
            }
        }
    }

    private void DemandNewResource()
    {
        var rng = city.State.StateBasedRandom("CityTurnManager.demandNewResource");
        var candidates = city.GetRuleset().TileResources.Values.Where(it =>
            it.ResourceType == ResourceType.Luxury && // Must be luxury
            !((IHasUniques)it).HasUnique(UniqueType.CityStateOnlyResource) && // Not a city-state only resource eg jewelry
            it.Name != city.DemandedResource && // Not same as last time
            city.TileMap.ResourceObjects.Contains(it) && // Must exist somewhere on the map
            !city.GetCenterTile().GetTilesInDistance(city.GetWorkRange()).Any(nearTile => nearTile.TileResource == it) // Not in this city's radius
        ).ToList();
        var missingResources = candidates.Where(it => !city.Civ.HasResource(it)).ToList();

        if (missingResources.Count == 0) // hooray happpy day forever!
        {
            city.DemandedResource = candidates.RandomOrNull(rng)?.Name ?? "";
            return; // actually triggering "wtlk" is done in tryWeLoveTheKing(), *next turn*
        }

        var chosenResource = missingResources.RandomOrNull(rng);

        city.DemandedResource = chosenResource?.Name ?? ""; // mods may have no resources as candidates even
        SetWltkResourceDemandCooldown(false);

        if (city.DemandedResource != "") // Failed to get a valid resource, try again some time later
            city.Civ.AddNotification($"[{city.Name}] demands [{city.DemandedResource}]!",
                [new LocationAction(city.Location), new OverviewAction(EmpireOverviewCategories.Resources)],
                NotificationCategory.General, NotificationIcon.City, $"ResourceIcons/{city.DemandedResource}");
    }

    public void EndTurn()
    {
        foreach (var unique in city.GetTriggeredUniques(UniqueType.TriggerUponTurnEnd, includeCivUniques: false).ToList())
        {
            UniqueTriggerActivation.TriggerUnique(unique, city);
        }
        var stats = city.CityStats.CurrentCityStats;

        city.CityConstructions.EndTurn(stats);
        city.Expansion.NextTurn(stats.Culture);
        if (city.IsBeingRazed)
        {
            var removedPopulation = 1;
            city.Civ.ForEachMatchingUnique(UniqueType.CitiesAreRazedXTimesFaster, it =>
            {
                removedPopulation += it.Params[0].ToInt() - 1;
            });

            if (city.Population.Population <= removedPopulation)
            {
                city.Espionage.RemoveAllPresentSpies(SpyFleeReason.Other);
                city.Civ.AddNotification(
                    $"[{city.Name}] has been razed to the ground!",
                    city.Location, NotificationCategory.General,
                    "OtherIcons/Fire"
                );
                city.DestroyCity();
            }
            else
            { // if not razed yet:
                city.Population.AddPopulation(-removedPopulation);
                if (city.Population.FoodStored >= city.Population.GetFoodToNextPopulation()) // if surplus in the granary...
                    city.Population.FoodStored =
                        city.Population.GetFoodToNextPopulation() - 1; // ...reduce below the new growth threshold
            }
        }
        else city.Population.NextTurn(city.FoodForNextTurn());

        // This should go after the population change, as that might impact the amount of followers in this city
        if (city.Civ.GameInfo.IsReligionEnabled()) city.Religion.EndTurn();

        if (city.Civ.Cities.Contains(city)) // city was not destroyed
        {
            city.Health = Math.Min(city.Health + 20, city.GetMaxHealth());
            city.Population.UnassignExtraPopulation();
        }

        city.ClearCaches();
    }
}
