using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/UseGoldAutomation.kt (T090). Purity annotations dropped (T027 policy).
// - NextTurnAutomation.valueCityStateAlliance(civView, cityState, true) (T094) is the INextTurnAutomation hook on the game's client state; with no
//   hook installed the alliance is valued 0 (as in Battle.automateCityConquer), which also skips the gold gifts.
// - getHighlyDesirableTilesToCityMap: the Kotlin TreeMap comparator breaks ties between equal-resource tiles by Tile.hashCode(), which is the JVM
//   identity hash (run-to-run random). That is not reproducible, so ties are broken by the tile position instead (deterministic); the three
//   priority keys are unchanged. The per-tile city sets are LinkedHashSets (insertion order), as in Kotlin.
public static class UseGoldAutomation
{
    /// <summary>allow AI to spend money to purchase city-state friendship, buildings &amp; unit</summary>
    public static void UseGold(CivilizationClass civ)
    {
        foreach (var unit in civ.Units.GetCivUnits())
            UnitAutomation.TryUpgradeUnit(new GameView((GameInfo)unit.Civ.GameInfo, unit.Civ).GetMapUnitView(unit));

        if (civ.IsMajorCiv())
            UseGoldForCityStates(civ);

        var acceptableHurryCostModifier = Math.Max(civ.Gold, 0) / 100;
        // Ideally we buy things with good gold:production conversion from purchasing and hard-build the rest, but some mods have high hurryCostModifier on everything leading to too much gold being saved.
        // Rather than checking the full ruleset for future available constructions and determining if they're worth it, let's gradually accept less efficiency if it turns out we're saving too much

        foreach (var city in civ.Cities.OrderByDescending(it => it.CityStats.CurrentCityStats.Production))
        {
            // Low production cities are disallowed from building military and world wonders, and have low priority for xp buildings
            var construction = city.CityConstructions.GetCurrentConstruction();
            if (construction is not INonPerpetualConstruction nonPerpetual) continue;
            var statBuyCost = nonPerpetual.GetStatBuyCost(city, Stat.Gold);
            if (statBuyCost == null) continue;
            if (!city.CityConstructions.IsConstructionPurchaseAllowed(nonPerpetual, Stat.Gold, statBuyCost.Value)) continue;
            if (civ.Gold < statBuyCost.Value || nonPerpetual.HurryCostModifier > acceptableHurryCostModifier) continue;
            if (city.CityConstructions.GetWorkDone(nonPerpetual.Name) > 0) continue; // Don't buy things we already invested production into (may waste the invested production)
            city.CityConstructions.PurchaseConstruction(nonPerpetual, 0, true);
        }

        MaybeBuyCityTiles(civ);
    }

    private static CityStateFunctions StateFunctions(CivilizationClass cityState) => (CityStateFunctions)cityState.CityStateFunctions;

    private static void UseGoldForCityStates(CivilizationClass civ)
    {
        // RARE EDGE CASE: If you ally with a city-state, you may reveal more map that includes ANOTHER civ!
        // So if we don't lock this list, we may later discover that there are more known civs, concurrent modification exception!
        var gameView = new GameView((GameInfo)civ.GameInfo, civ);
        var knownCityStates = civ.GetKnownCivs()
            .Where(it => it.IsCityState && MotivationToAttackAutomation.HasAtLeastMotivationToAttack(civ, gameView.GetForeignCivView(it), 0f) <= 0)
            .ToList();

        // canBeMarriedBy checks actual cost, but it can't be below 500*speedmodifier, and the later check is expensive
        if (civ.Gold >= 330 && civ.GetHappiness() > 0 && civ.HasUnique(UniqueType.CityStateCanBeBoughtForGold))
        {
            foreach (var cityState in knownCityStates.ToList()) // Materialize sequence as diplomaticMarriage may kill a CS
            {
                if (StateFunctions(cityState).CanBeMarriedBy(civ))
                    StateFunctions(cityState).DiplomaticMarriage(civ);
                if (civ.GetHappiness() <= 0) break; // Stop marrying if happiness is getting too low
            }
        }

        if (civ.Gold < 500 || knownCityStates.Count == 0) return; // skip checks if tryGainInfluence will bail anyway
        var candidates = knownCityStates
            .Where(it => it.AllyCiv != civ)
            .Select(it => (CityState: it, Value: ValueCityStateAlliance(civ, it)))
            .ToList();
        // maxByOrNull: the first maximum
        var best = candidates.Count == 0 ? default : candidates.Aggregate((a, b) => b.Value > a.Value ? b : a);
        if (candidates.Count != 0 && best.Value > 0)
            TryGainInfluence(civ, best.CityState);
    }

    /// <summary>Kotlin <c>NextTurnAutomation.valueCityStateAlliance(GameView(..).civView, cityState, true)</c> (T094): the INextTurnAutomation hook, 0 without one.</summary>
    private static int ValueCityStateAlliance(CivilizationClass civ, CivilizationClass cityState) =>
        civ.GameInfo.ClientState.NextTurnAutomation?.ValueCityStateAlliance(civ, cityState) ?? 0; // TODO(T094): valueCityStateAlliance's third parameter (true)

    private static void MaybeBuyCityTiles(CivilizationClass civInfo)
    {
        if (civInfo.Gold <= 0)
            return;
        // Don't buy tiles in the very early game. It is unlikely that we already have the required
        // tech, the necessary worker and that there is a reasonable threat from another player to
        // grab the tile. We could also check all that, but it would require a lot of cycles each
        // turn and this is probably a good approximation.
        if (civInfo.GameInfo.Turns < KotlinMath.FloatToInt(civInfo.GameInfo.Speed.ScienceCostModifier * 20))
            return;

        var highlyDesirableTiles = GetHighlyDesirableTilesToCityMap(civInfo);

        // Always try to buy highly desirable tiles if it can be afforded.
        foreach (var (highlyDesirableTile, cities) in highlyDesirableTiles)
        {
            var cityWithLeastCostToBuy = cities.MinBy(it => it.GetCenterTile().AerialDistanceTo(highlyDesirableTile)).NotNull();
            var bfs = new BFS(cityWithLeastCostToBuy.GetCenterTile(),
                it => it.GetOwner() == null || it.OwningCity == cityWithLeastCostToBuy);
            bfs.StepUntilDestination(highlyDesirableTile);
            var tilesThatNeedBuying = bfs.GetPathTo(highlyDesirableTile).Where(it => it.GetOwner() == null)
                .ToList();
            tilesThatNeedBuying.Reverse(); // getPathTo is from destination to source

            // We're trying to acquire everything and revert if it fails, because of the difficult
            // way how tile acquisition cost is calculated. Everytime you buy a tile, the next one
            // gets more expensive and by how much depends on other things such as game speed. To
            // not introduce hidden dependencies on that and duplicate that logic here to calculate
            // the price of the whole path, this is probably simpler.
            var ranOutOfMoney = false;
            var goldSpent = 0;
            foreach (var tileThatNeedsBuying in tilesThatNeedBuying)
            {
                var goldCostOfTile = cityWithLeastCostToBuy.Expansion.GetGoldCostOfTile(tileThatNeedsBuying);
                if (civInfo.Gold >= goldCostOfTile)
                {
                    cityWithLeastCostToBuy.Expansion.BuyTile(tileThatNeedsBuying);
                    goldSpent = unchecked(goldSpent + goldCostOfTile);
                }
                else
                {
                    ranOutOfMoney = true;
                    break;
                }
            }
            if (ranOutOfMoney)
            {
                foreach (var tileThatNeedsBuying in tilesThatNeedBuying)
                    cityWithLeastCostToBuy.Expansion.RelinquishOwnership(tileThatNeedsBuying);
                civInfo.AddGold(goldSpent);
            }
        }
    }

    private static SortedDictionary<TileClass, LinkedHashSet<CityObject>> GetHighlyDesirableTilesToCityMap(CivilizationClass civInfo)
    {
        var highlyDesirableTiles = new SortedDictionary<TileClass, LinkedHashSet<CityObject>>(Comparer<TileClass>.Create((a, b) =>
        {
            // compareByDescending { naturalWonder != null }.thenByDescending { luxury }.thenByDescending { strategic }.thenBy { tie breaker }
            var c = (b.NaturalWonder != null).CompareTo(a.NaturalWonder != null);
            if (c != 0) return c;
            c = (b.TileResource?.ResourceType == ResourceType.Luxury).CompareTo(a.TileResource?.ResourceType == ResourceType.Luxury);
            if (c != 0) return c;
            c = (b.TileResource?.ResourceType == ResourceType.Strategic).CompareTo(a.TileResource?.ResourceType == ResourceType.Strategic);
            if (c != 0) return c;
            // This is necessary, so that the map keeps Tiles with the same resource as two separate entries.
            c = a.Position.X.CompareTo(b.Position.X);
            return c != 0 ? c : a.Position.Y.CompareTo(b.Position.Y);
        }));

        foreach (var city in civInfo.Cities.Where(it => !it.IsPuppet && !it.IsBeingRazed))
        {
            city.GetCenterTile().ForEachTileAtDistance(2, it =>
            {
                // Only consider second ring tiles: further tiles may be as good or better, but have much higher gold cost
                if (IsHighlyDesirableTile(it, civInfo, city))
                {
                    if (!highlyDesirableTiles.TryGetValue(it, out var desirableTiles))
                        highlyDesirableTiles[it] = desirableTiles = [];
                    desirableTiles.Add(city);
                }
            });
        }
        return highlyDesirableTiles;
    }

    private static bool IsHighlyDesirableTile(TileClass it, CivilizationClass civInfo, CityObject city)
    {
        if (!it.IsVisible(civInfo)) return false;
        if (it.GetOwner() != null) return false;
        if (!it.Neighbors.Any(neighbor => neighbor.GetCity() == city)) return false;

        bool HasNaturalWonder() => it.NaturalWonder != null;

        bool HasLuxury()
        {
            var resource = it.TileResource;
            return civInfo.CanSeeResource(resource)
                && resource is { ResourceType: ResourceType.Luxury }
                && civInfo.GetResourceAmount(resource) < 2; // At 2 or more, we haven't been able to trade it away for another duplicate...
        }

        bool HasHighYields()
        {
            var tileStats = it.Stats.GetTileStats(civInfo);
            return tileStats.Food + tileStats.Production >= 3;
        }

        return HasNaturalWonder() || HasLuxury() || HasHighYields();
    }

    private static void TryGainInfluence(CivilizationClass civInfo, CivilizationClass cityState)
    {
        if (civInfo.Gold < 500) return; // Save up, giving 500 gold in one go typically grants +5 influence compared to giving 2×250 gold
        var influence = cityState.GetDiplomacyManager(civInfo).NotNull().GetInfluence();
        var stopSpending = influence > 60 + 2 * ValueCityStateAlliance(civInfo, cityState);
        // Don't go into a gold gift race: be content with friendship for cheap, or use the gold on more productive uses,
        // for example upgrading an army to conquer the player who's contesting our city states
        if (influence < 10 || stopSpending) return;
        // Only make an investment if we got our Pledge to Protect influence at the highest level
        if (civInfo.Gold >= 1000) StateFunctions(cityState).ReceiveGoldGift(civInfo, 1000);
        else StateFunctions(cityState).ReceiveGoldGift(civInfo, 500);
    }
}
