using Unciv.Core.Tests.Support;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Models;

namespace Unciv.Core.Tests.Logic.Map;

// T144: port of tests/src/com/unciv/uniques/TileUniquesTests.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class TileUniquesTests
{
    private readonly TestGame game = new();

    [Fact]
    public void PillageYieldTest()
    {
        game.MakeHexagonalMap(2);
        var civInfo = game.AddCiv();

        var tile = game.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        var cityTile = game.SetTileTerrain(new HexCoord(2, 0), Constants.Grassland);
        var city = game.AddCity(civInfo, cityTile, true);
        city.Population.FoodStored = 0; // just to be sure
        civInfo.AddGold(-civInfo.Gold); // reset gold just to be sure

        var testImprovement = game.CreateTileImprovement("Pillaging this improvement yields [+20 Gold, +11 Food]");
        tile.SetImprovement(testImprovement);
        var unit = game.AddUnit("Warrior", civInfo, tile);
        unit.CurrentMovement = 2f;

        UnitActions.InvokeUnitAction(unit, UnitActionType.Pillage);
        Assert.True(civInfo.Gold == 20, "Pillaging should transfer gold to the civ");
        Assert.True(city.Population.FoodStored == 11, "Pillaging should transfer food to the nearest city");
    }
}
