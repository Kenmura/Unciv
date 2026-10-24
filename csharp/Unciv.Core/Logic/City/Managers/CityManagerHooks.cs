using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Logic.Trade;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.StatTypes;
using AutomationObject = Unciv.Logic.Automation.Automation;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TradeObject = Unciv.Logic.Trade.Trade;

namespace Unciv.Logic.City.Managers;

// T061/T083: the replaceable hooks of the city managers (no mutable static state). They are not seams of unported code: the defaults call the
// ported Automation (T087), Battle (T083) and TradeLogic (T086); the properties stay so that the tests can drive the city logic with controlled
// ranks and record the Battle / TradeLogic calls over a fake game state.

/// <summary>The members of Kotlin <c>Automation</c> that CityPopulationManager calls.</summary>
internal interface ICityWorkAutomation
{
    /// <summary><c>Automation.rankStatsForCityWork(stats, city, areWeRankingSpecialist)</c></summary>
    float RankStatsForCityWork(Stats stats, CityObject city, bool areWeRankingSpecialist);
    /// <summary><c>Automation.rankSpecialist(specialist, city)</c></summary>
    float RankSpecialist(string specialist, CityObject city);
    /// <summary><c>Automation.rankTileForCityWork(tile, city)</c></summary>
    float RankTileForCityWork(Tile tile, CityObject city);
    /// <summary><c>Automation.getTileForConstructionImprovement(city, improvement)</c> (CityConstructions)</summary>
    Tile? GetTileForConstructionImprovement(CityObject city, TileImprovement improvement);
}

/// <summary>
/// The members of Kotlin <c>Battle</c> and <c>TradeLogic</c> that CityConquestFunctions calls. It is a replaceable instance
/// property of <see cref="CityObject.ConquestExternals"/>: the default calls the ported Battle and TradeLogic, tests install stand-ins.
/// </summary>
internal interface ICityConquestExternals
{
    /// <summary><c>Battle.destroyIfDefeated(attackedCiv, attacker, notificationLocation)</c></summary>
    void DestroyIfDefeated(CivilizationObject attackedCiv, CivilizationObject attacker, HexCoord? notificationLocation);
    /// <summary><c>TradeLogic(ourCiv, theirCiv)</c> with <c>currentTrade</c> = <paramref name="trade"/>, then <c>acceptTrade(false)</c></summary>
    void AcceptTrade(CivilizationObject ourCiv, CivilizationObject theirCiv, TradeObject trade);
}

internal static class CityManagerHooks
{
    private sealed class RealAutomation : ICityWorkAutomation
    {
        public float RankStatsForCityWork(Stats stats, CityObject city, bool areWeRankingSpecialist) =>
            AutomationObject.RankStatsForCityWork(stats, city, areWeRankingSpecialist);
        public float RankSpecialist(string specialist, CityObject city) => AutomationObject.RankSpecialist(specialist, city);
        public float RankTileForCityWork(Tile tile, CityObject city) => AutomationObject.RankTileForCityWork(tile, city);
        public Tile? GetTileForConstructionImprovement(CityObject city, TileImprovement improvement) =>
            AutomationObject.GetTileForConstructionImprovement(city, improvement);
    }

    private sealed class RealConquestExternals : ICityConquestExternals
    {
        public void DestroyIfDefeated(CivilizationObject attackedCiv, CivilizationObject attacker, HexCoord? notificationLocation) =>
            Battle.Battle.DestroyIfDefeated(attackedCiv, attacker, notificationLocation);
        // TradeLogic's TradeEvaluation seam (T089) still throws when it is reached.
        public void AcceptTrade(CivilizationObject ourCiv, CivilizationObject theirCiv, TradeObject trade)
        {
            var tradeLogic = new TradeLogic(ourCiv, theirCiv);
            tradeLogic.CurrentTrade.Set(trade);
            tradeLogic.AcceptTrade(false);
        }
    }

    /// <summary>The default of <see cref="CityObject.ConquestExternals"/>: the ported Battle and TradeLogic; tests install stand-ins.</summary>
    internal static readonly ICityConquestExternals RealConquest = new RealConquestExternals();

    /// <summary>The default of <see cref="CityPopulationManager.Automation"/>: the ported <c>Automation</c> (T087).</summary>
    internal static readonly ICityWorkAutomation RealCityWork = new RealAutomation();
}
