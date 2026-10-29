using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using CityClass = Unciv.Logic.City.City;
using TileImprovementObject = Unciv.Models.Ruleset.Tile.TileImprovement;

namespace Unciv.Logic.Map.Tiles;

// Ported from core/src/com/unciv/logic/map/tile/TileStatFunctions.kt (T054).
// Stripped: timeThis profiling wrappers and the purity annotations. Kotlin <c>Pair&lt;String, Stats&gt;</c> is a value tuple.
// <c>EnumMap</c> is a Dictionary (the map only ever holds the three categories, iteration order never reaches game state).

public static class TileStatsExtensions
{
    public static Stats ToStats(this IEnumerable<(string Cause, Stats Stats)> self)
    {
        var stats = new Stats();
        foreach (var (_, statsToAdd) in self)
            stats.Add(statsToAdd);
        return stats;
    }
}

public class TileStatFunctions(Tile tile)
{
    public Tile Tile { get; } = tile;

    private Terrain? riverTerrainCache;
    private bool riverTerrainLoaded;
    private Terrain? RiverTerrain
    {
        get
        {
            if (!riverTerrainLoaded)
            {
                riverTerrainCache = Tile.Ruleset.Terrains.GetValueOrDefault(Constants.River);
                riverTerrainLoaded = true;
            }
            return riverTerrainCache;
        }
    }

    public Stats GetTileStats(IGameContextCivilization? observingCiv) => GetTileStats(Tile.GetCity(), observingCiv);

    public Stats GetTileStats(CityClass? city, IGameContextCivilization? observingCiv)
    {
        var statsBreakdown = GetTileStatsBreakdown(city, observingCiv);

        var improvement = Tile.GetUnpillagedImprovement();
        var road = Tile.GetUnpillagedRoad();

        var percentageStats = GetTilePercentageStats(observingCiv, city);
        foreach (var (cause, stats) in statsBreakdown)
        {
            TilePercentageCategory tileType;
            if (cause == improvement) tileType = TilePercentageCategory.Improvement;
            else if (cause == road.ToString()) tileType = TilePercentageCategory.Road;
            else tileType = TilePercentageCategory.Terrain;
            foreach (var (stat, value) in percentageStats[tileType])
                stats[stat] *= value.ToPercent();
        }

        return statsBreakdown.ToStats();
    }

    public List<(string Cause, Stats Stats)> GetTileStatsBreakdown(CityClass? city, IGameContextCivilization? observingCiv)
    {
        var gameContext = new GameContext(civInfo: observingCiv, city: city, tile: Tile);
        var listOfStats = GetTerrainStatsBreakdown(gameContext);

        var otherYieldsIgnored = Tile.AllTerrains.Any(it => ((IHasUniques)it).HasUnique(UniqueType.NullifyYields, gameContext));

        var improvement = otherYieldsIgnored ? null // Treat it as if there is no improvement
            : Tile.GetUnpillagedTileImprovement();
        var improvementStats = improvement?.CloneStats() ?? Stats.ZERO; // If improvement==null, will never be added to

        var road = otherYieldsIgnored ? null
            : Tile.GetUnpillagedRoadImprovement();
        var roadStats = road?.CloneStats() ?? Stats.ZERO;

        if (city != null)
        {
            void AddStats(Unique unique)
            {
                var tileType = unique.Params[1];
                if (improvement != null && improvement.MatchesFilter(tileType, gameContext))
                    improvementStats.Add(unique.Stats);
                else if (Tile.MatchesFilter(tileType, observingCiv))
                    listOfStats.Add(($"{{{unique.SourceObjectName ?? "null"}}} ({{{unique.GetDisplayText()}}})", unique.Stats));
                else if (road != null && road.MatchesFilter(tileType, gameContext))
                    roadStats.Add(unique.Stats);
            }
            city.ForEachMatchingUnique(UniqueType.StatsFromTiles, gameContext, unique =>
            {
                if (city.MatchesFilter(unique.Params[2], null)) AddStats(unique);
            });
            city.ForEachMatchingUnique(UniqueType.StatsFromObject, gameContext, AddStats);
            city.ForEachMatchingUnique(UniqueType.StatsFromTilesWithout, gameContext, unique =>
            {
                if (city.MatchesFilter(unique.Params[3], null) && !Tile.MatchesFilter(unique.Params[2]))
                    AddStats(unique);
            });
        }

        if (Tile.IsAdjacentToRiver())
        {
            if (RiverTerrain == null)
                listOfStats.Add(("River", new Stats(gold: 1f))); // Fallback for legacy mods
            else
                // Upstream note: this is one approach to get these stats in - supporting only the Stats UniqueType.
                //     Alternatives: append riverTerrain to allTerrains, or append riverTerrain.uniques to
                //     the Tile's UniqueObjects/UniqueMap (while copying onl<e> base Stats directly here)
                listOfStats.AddRange(GetSingleTerrainStats(RiverTerrain, gameContext));
        }

        var minimumStats = Tile.IsCityCenter() ? Stats.DefaultCityCenterMinimum : Stats.ZERO;
        if (observingCiv != null)
        {
            // resource base
            var resource = Tile.TileResource;
            if (observingCiv.CanSeeResource(resource)) listOfStats.Add((resource.NotNull().Name, resource));

            if (improvement != null)
                improvementStats.Add(GetExtraImprovementStats(improvement, observingCiv, city));

            if (road != null)
                roadStats.Add(GetExtraImprovementStats(road, observingCiv, city));

            if (improvement != null)
            {
                var ensureMinUnique = ((IHasUniques)improvement)
                    .GetMatchingUniques(UniqueType.EnsureMinimumStats, gameContext)
                    .FirstOrDefault();
                if (ensureMinUnique != null) minimumStats = ensureMinUnique.Stats;
            }
        }

        if (road != null) listOfStats.Add((road.Name, roadStats));
        if (improvement != null) listOfStats.Add((improvement.Name, improvementStats));

        var statsFromMinimum = MissingFromMinimum(listOfStats.ToStats(), minimumStats);
        listOfStats.Add(("Minimum", statsFromMinimum));

        if (observingCiv != null && observingCiv.IsGoldenAge() && listOfStats.ToStats().Gold != 0f)
            listOfStats.Add(("Golden Age", new Stats(gold: 1f)));

        // To ensure that the original stats (in uniques, terrains, etc) are not modified in getTileStats, we clone them all
        return listOfStats.Where(it => !it.Stats.IsEmpty()).Select(it => (it.Cause, it.Stats.Clone())).ToList();
    }

    /// <summary>Ensures each stat is >= <paramref name="minimumStats"/>.stat - modifies in place</summary>
    private static Stats MissingFromMinimum(Stats current, Stats minimumStats)
    {
        // Note: Not `for ((stat, value) in other)` - that would skip zero values
        var missingStats = new Stats();
        foreach (var stat in StatExtensions.Entries)
        {
            if (current[stat] < minimumStats[stat])
                missingStats[stat] = minimumStats[stat] - current[stat];
        }
        return missingStats;
    }

    /// <summary>Gets stats of a single Terrain, unifying the Stats class a Terrain inherits and the Stats Unique
    /// @return A Stats reference, must not be mutated</summary>
    private static List<(string Cause, Stats Stats)> GetSingleTerrainStats(Terrain terrain, GameContext gameContext)
    {
        var list = new List<(string Cause, Stats Stats)> { (terrain.Name, terrain) };

        ((IHasUniques)terrain).ForEachMatchingUnique(UniqueType.Stats, gameContext, unique =>
        {
            list.Add((terrain.Name + ": " + unique.GetDisplayText(), unique.Stats));
        });
        return list;
    }

    /// <summary>Gets basic stats to start off <see cref="GetTileStats(CityClass?, IGameContextCivilization?)"/> or the start yield, independently mutable result</summary>
    public List<(string Cause, Stats Stats)> GetTerrainStatsBreakdown(GameContext? gameContext = null)
    {
        gameContext ??= new GameContext();
        var list = new List<(string Cause, Stats Stats)>();

        // allTerrains iterates over base, natural wonder, then features
        foreach (var terrain in Tile.AllTerrains)
        {
            var terrainStats = GetSingleTerrainStats(terrain, gameContext);
            if (((IHasUniques)terrain).HasUnique(UniqueType.NullifyYields, gameContext))
                return terrainStats;
            if (terrain.OverrideStats)
                list = terrainStats;
            else
                list.AddRange(terrainStats);
        }
        return list;
    }

    public enum TilePercentageCategory
    {
        Terrain,
        Improvement,
        Road
    }

    // Only gets the tile percentage bonus, not the improvement percentage bonus
    public Dictionary<TilePercentageCategory, Stats> GetTilePercentageStats(IGameContextCivilization? observingCiv, CityClass? city)
    {
        var terrainStats = new Stats();
        var gameContext = new GameContext(civInfo: observingCiv, city: city, tile: Tile);

        var improvement = Tile.GetUnpillagedTileImprovement();
        var improvementStats = new Stats();

        var road = Tile.GetUnpillagedRoadImprovement();
        var roadStats = new Stats();

        void AddStats(string filter, Stat stat, float amount)
        {
            if (improvement != null && improvement.MatchesFilter(filter, gameContext))
                improvementStats.Add(stat, amount);
            else if (Tile.MatchesFilter(filter, observingCiv))
                terrainStats.Add(stat, amount);
            else if (road != null && road.MatchesFilter(filter, gameContext))
                roadStats.Add(stat, amount);
        }

        if (city != null)
        {
            city.ForEachMatchingUnique(UniqueType.StatPercentFromObject, gameContext, unique =>
                AddStats(unique.Params[2], StatExtensions.ValueOf(unique.Params[1]), ParseFloat(unique.Params[0])));
            city.ForEachMatchingUnique(UniqueType.AllStatsPercentFromObject, gameContext, unique =>
            {
                foreach (var stat in StatExtensions.Entries)
                    AddStats(unique.Params[1], stat, ParseFloat(unique.Params[0]));
            });
        }
        else if (observingCiv != null)
        {
            observingCiv.ForEachMatchingUnique(UniqueType.StatPercentFromObject, gameContext, unique =>
                AddStats(unique.Params[2], StatExtensions.ValueOf(unique.Params[1]), ParseFloat(unique.Params[0])));

            observingCiv.ForEachMatchingUnique(UniqueType.AllStatsPercentFromObject, gameContext, unique =>
            {
                foreach (var stat in StatExtensions.Entries)
                    AddStats(unique.Params[1], stat, ParseFloat(unique.Params[0]));
            });
        }

        return new Dictionary<TilePercentageCategory, Stats>
        {
            [TilePercentageCategory.Terrain] = terrainStats,
            [TilePercentageCategory.Improvement] = improvementStats,
            [TilePercentageCategory.Road] = roadStats,
        };
    }

    public float GetTileStartScore(Stats cityCenterMinStats)
    {
        var sum = 0f;
        Tile.ForEachTileInDistance(2, closeTile =>
        {
            var tileYield = closeTile.Stats.GetTileStartYield(
                closeTile == Tile ? cityCenterMinStats : Stats.ZERO);
            sum += tileYield;
            if (Tile.Neighbors.Contains(closeTile))
                sum += tileYield;
        });

        if (Tile.IsHill())
            sum -= 2f;
        if (Tile.IsAdjacentToRiver())
            sum += 2f;
        if (Tile.Neighbors.Any(it => it.GetBaseTerrain().IsMountain))
            sum += 2f;
        if (Tile.IsAdjacentToCoast())
            sum += 3f;
        if (!Tile.IsAdjacentToCoast() && Tile.Neighbors.Any(it => it.IsAdjacentToCoast()))
            sum -= 7f;

        return sum;
    }

    private float GetTileStartYield(Stats minimumStats)
    {
        var stats = GetTerrainStatsBreakdown().ToStats();
        if (Tile.TileResource != null) stats.Add(Tile.TileResource);
        stats.Add(MissingFromMinimum(stats, minimumStats));
        return stats.Food + stats.Production + stats.Gold;
    }

    /// <summary>Returns the extra stats that we would get if we switched to this improvement
    /// Can be negative if we're switching to a worse improvement</summary>
    /// <param name="improvement">the improvement to switch to</param>
    /// <param name="observingCiv">the observing civ</param>
    /// <param name="city">the city, if any</param>
    /// <param name="currentTileStats">Provide this for performance</param>
    public Stats GetStatDiffForImprovement(
        TileImprovementObject improvement,
        IGameContextCivilization observingCiv,
        CityClass? city,
        Stats? currentTileStats = null)
    {
        var currentStats = currentTileStats ?? GetTileStats(city, observingCiv);

        var tileClone = Tile.Clone(addUnits: false);
        tileClone.SetTerrainTransients();

        tileClone.SetImprovement(improvement.Name);
        var futureStats = tileClone.Stats.GetTileStats(city, observingCiv);

        return futureStats - currentStats;
    }

    // Also multiplies the stats by the percentage bonus for improvements (but not for tiles)
    private Stats GetExtraImprovementStats(
        TileImprovementObject improvement,
        IGameContextCivilization observingCiv,
        CityClass? city)
    {
        var stats = new Stats();

        var resource = Tile.TileResource;
        if (observingCiv.CanSeeResource(resource) && resource.NotNull().IsImprovedBy(improvement.Name)
                && resource.ImprovementStats != null)
            stats.Add(resource.ImprovementStats); // resource-specific improvement

        var conditionalState = new GameContext(civInfo: observingCiv, city: city, tile: Tile);
        ((IHasUniques)improvement).ForEachMatchingUnique(UniqueType.Stats, conditionalState, unique =>
        {
            stats.Add(unique.Stats);
        });

        ((IHasUniques)improvement).ForEachMatchingUnique(UniqueType.ImprovementStatsForAdjacencies, conditionalState, unique =>
        {
            var adjacent = unique.Params[1];
            var numberOfBonuses = Tile.Neighbors.Count(it =>
                it.MatchesFilter(adjacent, observingCiv)
                    || it.GetUnpillagedRoad().ToString() == adjacent);
            stats.Add(unique.Stats * (float)numberOfBonuses);
        });

        ((IHasUniques)improvement).ForEachMatchingUnique(UniqueType.ImprovementStatsOnTile, conditionalState, unique =>
        {
            if (Tile.MatchesFilter(unique.Params[1])
                || (unique.Params[1] == Constants.FreshWater && Tile.IsAdjacentTo(Constants.FreshWater))
                || (unique.Params[1] == "non-fresh water" && !Tile.IsAdjacentTo(Constants.FreshWater)))
                stats.Add(unique.Stats);
        });

        ((IHasUniques)improvement).ForEachMatchingUnique(UniqueType.YieldPerEra, conditionalState, unique =>
        {
            int eras = Archaeology.ArchaeologyRules.ErasSinceDiscovery(Tile); // B010: Landmark
            if (eras > 0) stats.Add(unique.Stats * (float)eras);
        });

        return stats;
    }

    private static float ParseFloat(string text) =>
        float.Parse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
}
