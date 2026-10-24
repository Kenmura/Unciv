using Unciv.Logic.Map.Tiles;
using Unciv.Models.StatTypes;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Logic.City.Managers;

// T061: the seam of the city managers. CityPopulationManager calls Automation (ported by T087), a static Kotlin object, so the seam is a
// replaceable instance property of CityPopulationManager (no mutable static state) that T087 replaces by direct calls. Until then the default throws, like an unported call should.

/// <summary>The members of Kotlin <c>Automation</c> that CityPopulationManager calls. TODO(T087)</summary>
internal interface ICityWorkAutomation
{
    /// <summary><c>Automation.rankStatsForCityWork(stats, city, areWeRankingSpecialist)</c></summary>
    float RankStatsForCityWork(Stats stats, CityObject city, bool areWeRankingSpecialist);
    /// <summary><c>Automation.rankSpecialist(specialist, city)</c></summary>
    float RankSpecialist(string specialist, CityObject city);
    /// <summary><c>Automation.rankTileForCityWork(tile, city)</c></summary>
    float RankTileForCityWork(Tile tile, CityObject city);
}

internal static class CityManagerSeams
{
    private sealed class NotPorted : ICityWorkAutomation
    {
        // TODO(T087): Automation.rankStatsForCityWork
        public float RankStatsForCityWork(Stats stats, CityObject city, bool areWeRankingSpecialist) =>
            throw new NotSupportedException("Automation is not ported yet (TODO(T087))");
        // TODO(T087): Automation.rankSpecialist
        public float RankSpecialist(string specialist, CityObject city) =>
            throw new NotSupportedException("Automation is not ported yet (TODO(T087))");
        // TODO(T087): Automation.rankTileForCityWork
        public float RankTileForCityWork(Tile tile, CityObject city) =>
            throw new NotSupportedException("Automation is not ported yet (TODO(T087))");
    }

    /// <summary>The default of <see cref="CityPopulationManager.Automation"/>: throws. T087 replaces the property by direct calls of <c>Automation</c>.</summary>
    internal static readonly ICityWorkAutomation Unported = new NotPorted();
}
