using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using Tile = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/quests/QuestTargetHelpers.kt (T069). Stripped: the purity annotations.
// Kotlin Sequences that are only filtered and then materialised are evaluated eagerly (no side effects, same order).

internal static class QuestTargetHelpers
{
    // by turn so the same civ doesn't give the same quests always, and by civID so on the same turn different civs give different quests
    public static GameRandom GetRandom(this Civilization civ) => civ.State.StateBasedRandom("QuestManager");

    public static GameRandom GetRandom(this Civilization civ, Civilization? challenger) =>
        challenger == null ? civ.GetRandom()
        : civ.GetDiplomacyManager(challenger)?.State.StateBasedRandom("QuestManager") ?? civ.GetRandom();

    /// <summary>
    /// Returns a random Tile containing a Barbarian encampment within 8 tiles of this city-state to be destroyed
    /// </summary>
    public static Tile? GetBarbarianEncampmentForQuest(this Civilization civ, Civilization? challenger = null)
    {
        var encampments = civ.GetCapital().NotNull().GetCenterTile().GetTilesInDistance(8)
            .Where(it => it.IsBarbarianEncampment()).ToList();

        return encampments.RandomOrNull(civ.GetRandom(challenger));
    }

    /// <summary>
    /// Returns a random resource to be connected to the challenger's trade route as a quest.
    /// The resource must be a Luxury or Strategic resource, must not be owned by this city-state and the challenger,
    /// and must be viewable by the challenger; if none exists, it returns null.
    /// </summary>
    public static TileResource? GetResourceForQuest(this Civilization civ, Civilization challenger)
    {
        var ownedByCityStateResources = civ.DetailedCivResources.Select(it => it.Resource).ToList();
        var ownedByMajorResources = challenger.DetailedCivResources.Select(it => it.Resource).ToList();

        var resourcesOnMap = civ.GameInfo.TileMap.Values.Select(it => it.TileResource).OfType<TileResource>().Distinct();
        var viewableResourcesForChallenger = resourcesOnMap.Where(it => challenger.CanSeeResource(it));

        var notOwnedResources = viewableResourcesForChallenger.Where(it =>
            it.ResourceType != ResourceType.Bonus &&
            !ownedByCityStateResources.Contains(it) &&
            !ownedByMajorResources.Contains(it)).ToList();

        return notOwnedResources.RandomOrNull(civ.GetRandom(challenger));
    }

    public static Building? GetWonderToBuildForQuest(this Civilization civ, Civilization challenger)
    {
        static bool IsMoreThanAQuarterDone(CityObject city, string buildingName) =>
            city.CityConstructions.GetWorkDone(buildingName) * 3 > city.CityConstructions.GetRemainingWork(buildingName);
        static bool IsBuiltOrBeingBuilt(CityObject city, string buildingName) =>
            city.CityConstructions.IsBuilt(buildingName) || IsMoreThanAQuarterDone(city, buildingName);

        var gameInfo = civ.GameInfo;
        var wonders = gameInfo.Ruleset.Buildings.Values
            .Where(building =>
                // Buildable wonder
                building.IsWonder
                && challenger.Tech.IsResearched(building)
                // Can't be disabled
                && !building.IsUnavailableBySettings(gameInfo)
                // Can't be a unique wonder
                && building.UniqueTo == null
                // Big loop last: Exists or more than 25% built anywhere
                && !gameInfo.GetCities().Any(it => IsBuiltOrBeingBuilt(it, building.Name)))
            .ToList();

        return wonders.RandomOrNull(civ.GetRandom(challenger));
    }

    /// <summary>
    /// Returns a random Natural Wonder not yet discovered by the challenger, or the city-state dispatching the quest.
    /// </summary>
    public static string? GetNaturalWonderToFindForQuest(this Civilization civ, Civilization challenger) =>
        // Kotlin Iterable.subtract(): a LinkedHashSet in the order of tileMap.naturalWonders
        civ.GameInfo.TileMap.NaturalWonders
            .Where(it => !challenger.NaturalWonders.Contains(it) && !civ.NaturalWonders.Contains(it))
            .ToList()
            .RandomOrNull(civ.GetRandom(challenger));

    /// <summary>
    /// Returns a Great Person BaseUnit that is not owned by both the challenger and this city-state
    /// </summary>
    public static BaseUnit? GetGreatPersonForQuest(this Civilization civ, Civilization challenger)
    {
        var gameInfo = civ.GameInfo;
        var ruleset = gameInfo.Ruleset;

        var existingGreatPeople =
            // concatenate sequences of existing GP for the challenger (a player) and our `civ` (the quest-giving city-state)
            challenger.Units.GetCivGreatPeople().Concat(civ.Units.GetCivGreatPeople())
                .Select(it => it.BaseUnit.GetReplacedUnit(ruleset)).ToHashSet();

        var greatPeople = challenger.GreatPeople.GetGreatPeople()
            .Select(it => it.GetReplacedUnit(ruleset))
            .Distinct()
            // The hidden test is already done by getGreatPeople for the civ-specific units,
            // repeat for the replaced one we'll be asking for
            .Where(it => !(existingGreatPeople.Contains(it) || it.IsUnavailableBySettings(gameInfo)))
            .ToList();

        return greatPeople.RandomOrNull(civ.GetRandom(challenger));
    }

    /// <summary>
    /// Returns a random Civilization (major) that challenger has met, but whose territory he
    /// cannot see; if none exists, it returns null.
    /// </summary>
    public static Civilization? GetCivilizationToFindForQuest(this Civilization civ, Civilization challenger)
    {
        var civilizationsToFind = challenger.GetKnownCivs()
            .Where(it => it.IsAlive() && it.IsMajorCiv() && !challenger.HasMetCivTerritory(it))
            .ToList();

        return civilizationsToFind.RandomOrNull(civ.GetRandom(challenger));
    }

    /// <summary>
    /// Returns a city-state Civilization that this city-state wants to target for hostile quests
    /// </summary>
    public static Civilization? GetCityStateTarget(this Civilization civ, Civilization challenger)
    {
        Proximity? closestProximity = null;
        foreach (var cityState in civ.GameInfo.GetAliveCityStates())
        {
            if (!civ.Proximity.TryGetValue(cityState.CivId, out var proximity) || proximity == Proximity.None) continue;
            if (closestProximity == null || proximity < closestProximity) closestProximity = proximity;
        }

        if (closestProximity == null || closestProximity == Proximity.Distant) // None close enough
            return null;

        var validTargets = civ.GetKnownCivs().Where(it => it.IsCityState && challenger.Knows(it)
            && civ.Proximity.TryGetValue(it.CivId, out var proximity) && proximity == closestProximity);

        return validTargets.ToList().RandomOrNull(civ.GetRandom(challenger));
    }

    /// <summary>
    /// Returns the civ ID of the civ that most recently bullied this city-state.
    /// Note: forgets after 20 turns has passed!
    /// </summary>
    public static string? GetMostRecentBully(this Civilization civ)
    {
        DiplomacyManager? best = null;
        foreach (var manager in civ.Diplomacy.Values.Where(it => it.HasFlag(DiplomacyFlags.Bullied)))
            if (best == null || manager.GetFlag(DiplomacyFlags.Bullied) > best.GetFlag(DiplomacyFlags.Bullied)) best = manager; // maxByOrNull: first maximum wins
        return best?.OtherCivName;
    }
}
