using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Logic.Civilization.Diplomacy;

/// <summary>
/// B034 (bnw-spec 3.2 and 4.13), not in Kotlin: the Feitoria's luxury siphon. An improvement with the unique
/// <c>Gains [n] copies of the luxury resource from the City-State</c> standing on an unpillaged tile in a city-state's territory gives its civ
/// [n] copies of every luxury resource that city-state's cities produce, under the origin "Feitoria" (the improvement's name).
/// [?] Tiles keep no record of who built an improvement, so the civ is the one whose nation the improvement is unique to (Portugal); an improvement
/// without <c>UniqueTo</c> siphons for nobody. The city-state keeps its own copies and its allies still receive theirs.
/// </summary>
public static class FeitoriaRules
{
    private static List<TileImprovement> SiphoningImprovements(Civilization civ) =>
        civ.GameInfo.Ruleset.TileImprovements.Values
            .Where(it => it.UniqueTo == civ.Nation.Name && ((IHasUniques)it).HasUnique(UniqueType.LuxuryCopiesFromCityState))
            .ToList();

    /// <summary>Adds the siphoned luxuries of <paramref name="civ"/> to <paramref name="target"/>.</summary>
    public static void AddSiphonedResources(Civilization civ, ResourceSupplyList target)
    {
        if (civ.IsCityState) return;
        var improvements = SiphoningImprovements(civ);
        if (improvements.Count == 0) return;
        foreach (var cityState in civ.GameInfo.Civilizations)
        {
            if (!cityState.IsCityState || cityState.IsDefeated()) continue;
            List<TileResource>? luxuries = null;
            foreach (var city in cityState.Cities)
                foreach (var tile in city.GetTiles())
                {
                    if (tile.Improvement == null || tile.ImprovementIsPillaged) continue;
                    var improvement = improvements.FirstOrDefault(it => it.Name == tile.Improvement);
                    if (improvement == null) continue;
                    luxuries ??= cityState.CityStateFunctions.GetCityStateResourcesForAlly()
                        .Where(it => it.Resource.ResourceType == ResourceType.Luxury && it.Amount > 0
                            && !((IHasUniques)it.Resource).HasUnique(UniqueType.CannotBeTraded, cityState.State))
                        .Select(it => it.Resource).Distinct().ToList();
                    var copies = ((IHasUniques)improvement).GetMatchingUniques(UniqueType.LuxuryCopiesFromCityState)
                        .Sum(it => int.Parse(it.Params[0], System.Globalization.CultureInfo.InvariantCulture));
                    foreach (var resource in luxuries) target.Add(resource, improvement.Name, copies);
                }
        }
    }

    /// <summary>A tile in a city-state's territory changed: the civs that siphon from city-states recount their resources.</summary>
    public static void TileChanged(Tile tile)
    {
        if (tile.GetOwner() is not Civilization { IsCityState: true } || !tile.TileMap.HasGameInfo()) return;
        foreach (var civ in tile.TileMap.GameInfo.Civilizations.OfType<Civilization>())
            if (!civ.IsCityState && !civ.IsDefeated() && SiphoningImprovements(civ).Count > 0)
                civ.Cache.UpdateCivResources();
    }
}
