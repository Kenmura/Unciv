using Unciv.Queries.Cities;
using Unciv.Queries.Empire;
using Unciv.Queries.Tiles;
using Unciv.Queries.Units;
using Unciv.Queries.Victory;

namespace Unciv.GodotClient.Play;

/// <summary>
/// The client's questions to the engine (T185/T195): previews only, always as the viewer and never mutating, asked through the <see cref="IGameSession"/>.
/// The client renders the answer and never recomputes reach, cost or path. A refused query is reported as null, so the view just shows nothing.
/// </summary>
public sealed class GameQueries(IGameSession session)
{
    /// <summary>The policy screen's tree: branches, states with reasons, culture and free policies (the viewer's own).</summary>
    public Task<PolicyTreeResult?> PolicyTree() => session.QueryAsync(new PolicyTreeQuery());

    public Task<EmpireTopBarResult?> TopBar() => session.QueryAsync(new EmpireTopBarQuery());

    public Task<PendingDecisionsResult?> Pending() => session.QueryAsync(new PendingDecisionsQuery());

    public Task<ReachableTilesResult?> Reachable(int unitId) => session.QueryAsync(new ReachableTilesQuery(unitId));

    public Task<PathPreviewResult?> Path(int unitId, int x, int y) => session.QueryAsync(new PathPreviewQuery(unitId, x, y));

    /// <summary>The city screen's numbers for one of the viewer's cities (null for a city of another civ: nothing private is asked for those).</summary>
    public Task<CityPanelResult?> CityPanel(string cityId) => session.QueryAsync(new CityPanelQuery(cityId));

    /// <summary>The whole construction list of one of the viewer's cities (each entry says whether it is displayed): turns, cost, rejection reasons and buy prices from the engine.</summary>
    public Task<ConstructionListResult?> ConstructionList(string cityId) => session.QueryAsync(new ConstructionListQuery(cityId, OnlyDisplayed: false));

    public Task<CityTilesResult?> CityTiles(string cityId) => session.QueryAsync(new CityTilesQuery(cityId));

    /// <summary>The diplomacy screen's answers about one civ the viewer has met (null for any other: the engine answers an unmet civ like an unknown one).</summary>
    public Task<DiplomacyPanelResult?> DiplomacyPanel(string civName) => session.QueryAsync(new DiplomacyPanelQuery(civName));

    public Task<DiplomacyActionsResult?> DiplomacyActions(string civName) => session.QueryAsync(new DiplomacyActionsQuery(civName));

    public Task<CityStateQuestsResult?> CityStateQuests(string civName) => session.QueryAsync(new CityStateQuestsQuery(civName));

    /// <summary>The viewer's tech tree and research queue (C019): states, costs, turns and unlock text from the engine.</summary>
    public Task<TechTreeResult?> TechTree() => session.QueryAsync(new TechTreeQuery());

    /// <summary>The victory/defeat screen's three answers (C024): the outcome, the ranking columns and the score history. The last two are null when the game hides them from the viewer.</summary>
    public Task<GameOverResult?> GameOver() => session.QueryAsync(new GameOverQuery());

    public Task<IReadOnlyList<RankingColumn>?> Rankings() => session.QueryAsync(new RankingsQuery());

    public Task<HistoryResult?> History() => session.QueryAsync(new HistoryQuery());

    /// <summary>C023: the overview screen's pages. A query the game refuses (rankings hidden, espionage off) answers null and the page says so.</summary>
    public Task<EmpireOverviewResult?> Overview() => session.QueryAsync(new EmpireOverviewQuery());

    public Task<ReligionResult?> Religion() => session.QueryAsync(new ReligionQuery());

    public Task<SpyDestinationsResult?> Spies() => session.QueryAsync(new SpyDestinationsQuery());

    /// <summary>C022: everything each side could put on the table with a met major civ.</summary>
    public Task<TradeOptionsResult?> TradeOptions(string civName) => session.QueryAsync(new TradeOptionsQuery(civName));

    /// <summary>C022: the AI's likely answer to the staged trade; null when the game has hints off or the trade is not one the engine can judge.</summary>
    public Task<TradeHintResult?> TradeHint(string civName, IReadOnlyList<TradeOfferRequest> ours, IReadOnlyList<TradeOfferRequest> theirs) =>
        session.QueryAsync(new TradeHintQuery(civName, ours, theirs));

    public Task<UnitActionsResult?> Actions(int unitId) => session.QueryAsync(new UnitActionsQuery(unitId));

    public Task<AttackableTargetsResult?> AttackTargets(int unitId) => session.QueryAsync(new AttackableTargetsQuery(unitId));

    public Task<AttackPreviewResult?> AttackPreview(int unitId, int x, int y) => session.QueryAsync(new AttackPreviewQuery(x, y, unitId));

    public Task<TileInfoResult?> TileInfo(int x, int y, int unitId) => session.QueryAsync(new TileInfoQuery(x, y, unitId));

    public Task<PromotionChoicesResult?> Promotions(int unitId) => session.QueryAsync(new PromotionChoicesQuery(unitId));
}
