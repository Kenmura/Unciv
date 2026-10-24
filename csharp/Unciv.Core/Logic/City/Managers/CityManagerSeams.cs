using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.StatTypes;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TradeObject = Unciv.Logic.Trade.Trade;

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

/// <summary>
/// The members of Kotlin <c>Battle</c> (T083) and <c>TradeLogic</c> (T086) that CityConquestFunctions calls (T064). It is a replaceable instance
/// property of <see cref="CityObject.ConquestExternals"/> (no mutable static state) that T083/T086 replace by direct calls.
/// </summary>
internal interface ICityConquestExternals
{
    /// <summary><c>Battle.destroyIfDefeated(attackedCiv, attacker, notificationLocation)</c></summary>
    void DestroyIfDefeated(CivilizationObject attackedCiv, CivilizationObject attacker, HexCoord? notificationLocation);
    /// <summary><c>TradeLogic(ourCiv, theirCiv)</c> with <c>currentTrade</c> = <paramref name="trade"/>, then <c>acceptTrade(false)</c></summary>
    void AcceptTrade(CivilizationObject ourCiv, CivilizationObject theirCiv, TradeObject trade);
    /// <summary><c>UncivGame.Current.settings.autoAssignSpecialistsInNewCities</c> (client state, read by <c>city.resetSpecialistsControl()</c>); the GameSettings default is true (GameInfo.ClientState.AutoAssignSpecialistsInNewCities; the Battle port T083 wires it)</summary>
    bool AutoAssignSpecialistsInNewCities { get; }
    /// <summary><c>UncivGame.Current.settings.multiplayer.getUserId()</c> (client state, read by <c>city.resetSpecialistsControl()</c>; GameInfo.ClientState.UserId, wired by the Battle port T083)</summary>
    string? LocalUserId { get; }
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

    private sealed class ConquestNotPorted : ICityConquestExternals
    {
        public bool AutoAssignSpecialistsInNewCities => true;
        public string? LocalUserId => null;
        // TODO(T083): Battle.destroyIfDefeated
        public void DestroyIfDefeated(CivilizationObject attackedCiv, CivilizationObject attacker, HexCoord? notificationLocation) =>
            throw new NotSupportedException("Battle is not ported yet (TODO(T083))");
        // TODO(T086): TradeLogic.acceptTrade
        public void AcceptTrade(CivilizationObject ourCiv, CivilizationObject theirCiv, TradeObject trade) =>
            throw new NotSupportedException("TradeLogic is not ported yet (TODO(T086))");
    }

    /// <summary>The default of <see cref="CityObject.ConquestExternals"/>: throws. T083/T086 replace the property by direct calls.</summary>
    internal static readonly ICityConquestExternals UnportedConquest = new ConquestNotPorted();

    /// <summary>The default of <see cref="CityPopulationManager.Automation"/>: throws. T087 replaces the property by direct calls of <c>Automation</c>.</summary>
    internal static readonly ICityWorkAutomation Unported = new NotPorted();
}
