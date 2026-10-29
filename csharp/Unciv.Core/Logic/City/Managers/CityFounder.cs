using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.City.Managers;

// Ported from core/src/com/unciv/logic/city/managers/CityFounder.kt (T061).
//
// Stripped: the purity annotations.
// - foundCity ends in city.resetSpecialistsControl(), which reads UncivGame.Current.settings (client state): its input is the civ's
//   PlayerSettings.AutoAssignSpecialistsInNewCities (T183; default true, the GameSettings default).
// - Kotlin Sets of Civilizations / names that are iterated are insertion-ordered (LinkedHashSet from Sequence.toSet()): lists here.

public class CityFounder
{
    public CityObject FoundCity(CivilizationObject civInfo, HexCoord cityLocation, MapUnit? unit = null)
    {
        var city = new CityObject();

        city.FoundingCivObject = civInfo;
        city.TurnAcquired = civInfo.GameInfo.Turns;
        city.Location = cityLocation;
        city.SetTransients(civInfo);

        city.Name = GenerateNewCityName(
            civInfo,
            civInfo.GameInfo.Civilizations.Where(civ => civ.IsAlive()).ToList()
        ) ?? NamingConstants.Fallback;

        city.IsOriginalCapital = civInfo.CitiesCreated == 0;
        if (city.IsOriginalCapital)
        {
            civInfo.HasEverOwnedOriginalCapital = true;
            // if you have some culture before the 1st city is founded, you may want to adopt the 1st policy
            civInfo.Policies.ShouldOpenPolicyPicker = true;
        }
        civInfo.CitiesCreated++;

        civInfo.Cities = civInfo.Cities.WithItem(city);

        var startingEra = civInfo.GameInfo.GameParameters.StartingEra;

        city.Expansion.Reset();
        ClaimExtraTiles(city, civInfo);

        city.TryUpdateRoadStatus();

        var tile = city.GetCenterTile();
        foreach (var terrainFeature in tile.TerrainFeatures.Where(it =>
            city.GetRuleset().TileImprovements.ContainsKey($"Remove {it}")).ToList())
            tile.RemoveTerrainFeature(terrainFeature);

        if (civInfo.GameInfo.Ruleset.TileImprovements.ContainsKey(Constants.CityCenter))
            tile.SetImprovement(Constants.CityCenter, civInfo);
        tile.StopWorkingOnImprovement();

        var ruleset = civInfo.GameInfo.Ruleset;
        city.WorkedTiles = JavaHashSet<HexCoord>.HashSetOf(); //reassign 1st working tile

        city.Population.SetPopulation(ruleset.Eras[startingEra].SettlerPopulation);

        if (civInfo.ReligionManager.ReligionState == ReligionState.Pantheon)
        {
            city.Religion.AddPressure(
                civInfo.ReligionManager.Religion.NotNull().Name,
                200 * civInfo.GameInfo.Ruleset.ModOptions.Constants.ReligionMissionaryPressureMultiplier * city.Population.Population
            );
        }

        city.Population.AutoAssignPopulation();

        // Update proximity rankings for all civs
        foreach (var otherCiv in civInfo.GameInfo.GetAliveMajorCivs())
        {
            if (civInfo.GetProximity(otherCiv) != Proximity.Neighbors) // unless already neighbors
                civInfo.Cache.UpdateProximity(otherCiv,
                    otherCiv.Cache.UpdateProximity(civInfo));
        }
        foreach (var otherCiv in civInfo.GameInfo.GetAliveCityStates())
        {
            if (civInfo.GetProximity(otherCiv) != Proximity.Neighbors) // unless already neighbors
                civInfo.Cache.UpdateProximity(otherCiv,
                    otherCiv.Cache.UpdateProximity(civInfo));
        }

        TriggerCitiesSettledNearOtherCiv(city);

        AddStartingBuildings(city, civInfo, startingEra);

        city.ResetDisabledConstructions();
        city.ResetSpecialistsControl(civInfo.GameInfo.GetPlayerSettings(civInfo.CivId).AutoAssignSpecialistsInNewCities);

        GameEventHooks.CityFounded(civInfo, city);
        civInfo.ForEachTriggeredUnique(UniqueType.TriggerUponFoundingCity,
            new GameContext(civInfo, city, unit), _ => true, false,
            unique =>
            {
                UniqueTriggerActivation.TriggerUnique(unique, civInfo, city, unit, triggerNotificationText: "due to founding a city");
            }
        );
        if (unit != null)
            foreach (var unique in unit.GetTriggeredUniques(UniqueType.TriggerUponFoundingCity,
                new GameContext(civInfo, city, unit)))
                UniqueTriggerActivation.TriggerUnique(unique, civInfo, city, unit, triggerNotificationText: "due to founding a city");

        return city;
    }

    /// <summary>
    /// B004 deviation (bnw-spec 3.2 ExtraFoundedCityTerritoryClaimRange, Shoshone): "Founded cities claim [N] extra tiles of radius" is read as
    /// N extra tiles. After the usual ring of radius 1 the nearest unowned tiles (ring 2, then 3, in the map's fixed ring order) are claimed.
    /// </summary>
    private static void ClaimExtraTiles(CityObject city, CivilizationObject civInfo)
    {
        int extra = 0;
        foreach (var unique in civInfo.GetMatchingUniques(UniqueType.FoundedCityClaimRange, city.State))
            extra += int.Parse(unique.Params[0], System.Globalization.CultureInfo.InvariantCulture);
        var center = city.GetCenterTile();
        for (int distance = 2; extra > 0 && distance <= 4; distance++)
        {
            foreach (var tile in center.GetTilesAtDistance(distance))
            {
                if (extra <= 0) break;
                if (tile.GetCity() != null) continue;
                city.Expansion.TakeOwnership(tile);
                extra--;
            }
        }
    }

    private static class NamingConstants
    {
        /// <summary>Prefixes to add when every base name is taken, ordered.</summary>
        internal static readonly IReadOnlyList<string> Prefixes = Array.AsReadOnly(new[] { "New", "Neo", "Nova", "Altera" });
        internal const string Fallback = "City Without A Name";
    }

    /// <summary>
    /// Generates and returns a new city name for the <paramref name="foundingCiv"/>.
    ///
    /// This method attempts to return the first unused city name of the <paramref name="foundingCiv"/>, taking used
    /// city names into consideration (including foreign cities). If that fails, it then checks
    /// whether the civilization has <see cref="UniqueType.BorrowsCityNames"/> and, if true, returns a borrowed
    /// name. Else, it repeatedly attaches one of the given prefixes to the list of names
    /// up to ten times until an unused name is successfully generated. If all else fails, null is returned.
    /// </summary>
    /// <param name="foundingCiv">The civilization that founded this city.</param>
    /// <param name="aliveCivs">Every civilization currently alive.</param>
    /// <returns>A new city name. Null if failed to generate a name.</returns>
    internal static string? GenerateNewCityName(CivilizationObject foundingCiv, List<CivilizationObject> aliveCivs)
    {
        var usedCityNames = aliveCivs.SelectMany(civilization => civilization.Cities.Select(city => city.Name)).ToHashSet();

        // Attempt to return the first missing name from the list of city names
        foreach (var cityName in foundingCiv.Nation.Cities)
        {
            if (!usedCityNames.Contains(cityName)) return cityName;
        }

        // If all names are taken and this nation borrows city names,
        // return a random borrowed city name
        if (foundingCiv.HasUnique(UniqueType.BorrowsCityNames))
        {
            return BorrowCityName(foundingCiv, aliveCivs, usedCityNames);
        }

        // If the nation doesn't have the unique above,
        // return the first missing name with an increasing number of prefixes attached
        // Kotlin note: prefixes could be made moddable per nation, and suffixes supported.
        for (var number = 1; number <= 10; number++)
        {
            foreach (var prefix in NamingConstants.Prefixes)
            {
                var repeatedPredix = string.Concat(Enumerable.Repeat($"{prefix} [", number));
                var suffix = string.Concat(Enumerable.Repeat("]", number));
                var candidate = foundingCiv.Nation.Cities
                    .Select(it => repeatedPredix + it + suffix)
                    .FirstOrDefault(it => !usedCityNames.Contains(it));
                if (candidate != null) return candidate;
            }
        }

        // If all else fails (by using some sort of rule set mod without city names),
        return null;
    }

    /// <summary>Borrows a city name from another major civilization.</summary>
    /// <param name="foundingCiv">The civilization that founded this city.</param>
    /// <param name="aliveCivs">Every civilization currently alive.</param>
    /// <param name="usedCityNames">Every city name that have already been taken.</param>
    /// <returns>A new city name. Null if failed to generate a name.</returns>
    internal static string? BorrowCityName(CivilizationObject foundingCiv, List<CivilizationObject> aliveCivs, HashSet<string> usedCityNames)
    {
        var rng = foundingCiv.State.StateBasedRandom("CityFounder.borrowCityName");
        var aliveMajorNations = aliveCivs.Where(civ => civ.IsMajorCiv()).Select(civ => civ.Nation).ToList();

        /*
        We take the last unused city name for each other major nation in this game,
        skipping nations whose names are exhausted,
        and choose a random one from that pool if it's not empty.
        */
        var otherMajorNations = aliveMajorNations.Where(nation => !ReferenceEquals(nation, foundingCiv.Nation));
        var newCityNames = otherMajorNations
            .Select(nation => nation.Cities.LastOrDefault(city => !usedCityNames.Contains(city)))
            .OfType<string>()
            .Distinct()
            .ToList();
        if (newCityNames.Count != 0) return newCityNames.Random(rng);

        // As per fandom wiki, once the names from the other nations in the game are exhausted,
        // names are taken from the rest of the major nations in the rule set
        var absentMajorNations = foundingCiv.GameInfo.Ruleset.Nations.Values.Where(nation =>
            nation.IsMajorCiv && !aliveMajorNations.Contains(nation));
        newCityNames = absentMajorNations
            .SelectMany(nation => nation.Cities.Where(city => !usedCityNames.Contains(city)))
            .Distinct()
            .ToList();
        if (newCityNames.Count != 0) return newCityNames.Random(rng);

        // If for some reason we have used every single city name in the game,
        // (are we using some sort of rule set mod without city names?)
        return null;
    }

    private static void AddStartingBuildings(CityObject city, CivilizationObject civInfo, string startingEra)
    {
        var ruleset = civInfo.GameInfo.Ruleset;
        if (civInfo.Cities.Count == 1)
        {
            var capitalCityIndicator = civInfo.CapitalCityIndicator(city);
            if (capitalCityIndicator != null)
                city.CityConstructions.AddBuilding(capitalCityIndicator, tryAddFreeBuildings: false);
        }

        // Add buildings and pop we get from starting in this era
        foreach (var buildingName in ruleset.Eras[startingEra].SettlerBuildings)
        {
            if (!ruleset.Buildings.TryGetValue(buildingName, out var building)) continue;
            var uniqueBuilding = civInfo.GetEquivalentBuilding(building);
            if (uniqueBuilding.IsBuildable(city.CityConstructions))
                city.CityConstructions.AddBuilding(uniqueBuilding, tryAddFreeBuildings: false);
        }

        civInfo.CivConstructions.TryAddFreeBuildings();
    }

    /*
     When someone settles a city within 6 tiles of another civ, this makes the AI unhappy and it starts a rolling event.
     The SettledCitiesNearUs flag gets added to the AI so it knows this happened,
        and on its turn it asks the player to stop (with a DemandToStopSettlingCitiesNear alert type)
     If the player says "whatever, I'm not promising to stop", they get a -10 modifier which gradually disappears in 40 turns
     If they DO agree, then if they keep their promise for ~100 turns they get a +10 modifier for keeping the promise,
     But if they don't keep their promise they get a -20 that will only fully disappear in 160 turns.
     There's a lot of triggering going on here.
     */
    private static void TriggerCitiesSettledNearOtherCiv(CityObject city)
    {
        var citiesWithin6Tiles =
            city.Civ.GameInfo.Civilizations
                .Where(it => it.IsMajorCiv() && !ReferenceEquals(it, city.Civ))
                .SelectMany(it => it.Cities)
                .Where(it => it.GetCenterTile().AerialDistanceTo(city.GetCenterTile()) <= 6);
        var civsWithCloseCities =
            citiesWithin6Tiles
                .Select(it => it.Civ)
                .Distinct()
                .Where(it => it.Knows(city.Civ) && it.HasExplored(city.GetCenterTile()));
        foreach (var otherCiv in civsWithCloseCities)
            otherCiv.GetDiplomacyManager(city.Civ).NotNull().SetFlag(DiplomacyFlags.SettledCitiesNearUs, 30, true);
    }
}
