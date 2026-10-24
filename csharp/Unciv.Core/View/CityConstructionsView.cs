using Unciv.Logic.City;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.StatTypes;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/CityConstructionsView.kt (T085). Stripped: the purity annotations.
// The Kotlin @Deprecated getCityConstructions() escape hatch is not ported (views unwrap() instead).

public class CityConstructionsView(CityConstructions wrapped, GameView gameView, CivilizationClass viewer, bool spectatorMode = false)
    : OwnedView<CityConstructions>(wrapped, viewer, spectatorMode, gameView)
{
    private readonly CityConstructions cityConstructions = wrapped;

    internal override CivilizationClass Owner() => cityConstructions.City.Civ;

    public IReadOnlyList<string> ConstructionQueue => cityConstructions.ConstructionQueue;

    // Data retrieval
    public string CurrentConstructionName() => cityConstructions.CurrentConstructionName();

    public IConstruction GetConstruction(string name) => cityConstructions.GetConstruction(name);

    public bool IsFirstConstructionOfItsKind(int index, string name) => cityConstructions.IsFirstConstructionOfItsKind(index, name);

    public bool IsBuilt(string name) => cityConstructions.IsBuilt(name);

    public int GetWorkDone(string name) => cityConstructions.GetWorkDone(name);

    public int TurnsToConstruction(string name, bool useStoredProduction = true) => cityConstructions.TurnsToConstruction(name, useStoredProduction);

    public bool ShouldBeDisplayed(IConstruction construction) => construction.ShouldBeDisplayed(cityConstructions);

    public IEnumerable<RejectionReason> GetRejectionReasons(INonPerpetualConstruction construction) => construction.GetRejectionReasons(cityConstructions);

    public bool IsBuildable(IConstruction construction) => construction.IsBuildable(cityConstructions);

    public bool CanPlaceCreateOneImprovementOn(TileImprovement improvement, TileView tileView) =>
        cityConstructions.CanPlaceCreateOneImprovementOn(improvement, tileView.Unwrap());

    public TileView? GetTileForImprovement(string improvementName)
    {
        var tile = cityConstructions.GetTileForImprovement(improvementName);
        return tile == null ? null : GameView.TileMapView.GetTile(tile);
    }

    public bool IsQueueFull() => cityConstructions.IsQueueFull();

    public bool CanAddToQueue(IConstruction construction) => cityConstructions.CanAddToQueue(construction);

    public int? GetStatBuyCost(INonPerpetualConstruction construction, Stat stat) => construction.GetStatBuyCost(cityConstructions.City, stat);

    public bool IsConstructionPurchaseAllowed(INonPerpetualConstruction construction, Stat stat, int cost) =>
        cityConstructions.IsConstructionPurchaseAllowed(construction, stat, cost);

    public bool IsConstructionPurchaseBlockedByUnit(INonPerpetualConstruction construction) =>
        cityConstructions.IsConstructionPurchaseBlockedByUnit(construction);

    // Actions
    public bool PurchaseConstruction(INonPerpetualConstruction construction, int queuePosition, Stat stat, TileView? tileView) =>
        cityConstructions.PurchaseConstruction(construction, queuePosition, automatic: false, stat, tileView?.Unwrap());
}

// Helpers built on top of the view API, rather than being part of it
public static class CityConstructionsViewExtensions
{
    public static bool IsBeingConstructedOrEnqueued(this CityConstructionsView view, string name) => view.ConstructionQueue.Contains(name);

    public static IConstruction GetCurrentConstruction(this CityConstructionsView view) => view.GetConstruction(view.CurrentConstructionName());
}
