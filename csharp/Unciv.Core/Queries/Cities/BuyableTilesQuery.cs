using Unciv.View;

namespace Unciv.Queries.Cities;

// Cost: Gold price (the engine's formula, which grows with the tiles already claimed)
// CanBuy: Whether the city may buy it at all (adjacent to the city's land, not puppet, not razing, not in resistance)
// Affordable: Whether the owner has the gold
public sealed record BuyableTile(int X, int Y, int Cost, bool CanBuy, bool Affordable);

public sealed record BuyableTilesResult(string CityId, int Gold, int CultureStored, int CultureToNextTile, IReadOnlyList<BuyableTile> Tiles);

// T196: unowned tiles within the city's expand range that the viewer has explored, with their purchase price. Wraps
// Expansion.GetChoosableTiles, CanBuyTile and GetGoldCostOfTile (the calls the buy-tile command checks).
[QueryType("cities.BuyableTiles")]
public sealed record BuyableTilesQuery(string CityId) : IQuery<BuyableTilesResult>
{
    public string Type => "cities.BuyableTiles";

    public BuyableTilesResult Run(GameView view)
    {
        var city = Units.UnitQueryHelpers.OwnCity(view, CityId);
        int gold = view.CivView.Gold;
        var tiles = new List<BuyableTile>();
        foreach (var tile in city.Unwrap().Expansion.GetChoosableTiles())
        {
            var tileView = view.TileMapView.GetTile(tile.Position);
            if (tileView == null) continue;
            int cost = city.GetGoldCostOfTile(tileView);
            tiles.Add(new BuyableTile(tile.Position.X, tile.Position.Y, cost, city.CanBuyTile(tileView), gold >= cost));
        }
        return new BuyableTilesResult(CityId, gold, city.GetCultureStored(), city.GetCultureToNextTile(),
            tiles.OrderBy(it => it.X).ThenBy(it => it.Y).ToList());
    }
}
