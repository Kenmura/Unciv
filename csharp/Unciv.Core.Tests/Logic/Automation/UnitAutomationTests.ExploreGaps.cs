using System.Reflection;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Models;
using Unciv.View;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Automation;

// T161: coverage for TryFogBust, MapUnit.Automation and the UnitActions Explore/Automate delegates.
public partial class UnitAutomationTests
{
    private MapUnitClass SetUpFogBustScout(Func<TileClass, bool> isExplored)
    {
        testGame.GameInfo.EngineOptions.VisibleMap = false;
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        foreach (var tile in testGame.TileMap.Values) tile.SetExplored(civ, isExplored(tile));
        return testGame.AddUnit("Scout", civ, testGame.GetTile(0, 3));
    }

    [Fact]
    public void MapUnitDoActionRunsAutomateUnitMovesWhenAutomated()
    {
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        var settler = testGame.AddUnit("Settler", civ, testGame.GetTile(2, 0));
        warrior.Automated = true;

        warrior.DoAction();

        Assert.True(warrior.CurrentTile.AerialDistanceTo(settler.CurrentTile) < 2);
    }

    [Fact]
    public void ExploreAndAutomateUnitActionsRunTheirAutomation()
    {
        civ.PlayerType = PlayerType.Human;
        foreach (var tile in testGame.TileMap.Values) tile.SetExplored(civ, true);
        var scout = testGame.AddUnit("Scout", civ, testGame.GetTile(0, 0));

        UnitActions.GetUnitActions(scout, UnitActionType.Explore).Single().Action!.Invoke();
        Assert.Null(scout.Action); // all explored: finished immediately

        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(1, 0));
        var settler = testGame.AddUnit("Settler", civ, testGame.GetTile(3, 0));
        UnitActions.GetUnitActions(warrior, UnitActionType.Automate).Single().Action!.Invoke();
        Assert.True(warrior.Automated);
        Assert.True(warrior.CurrentTile.AerialDistanceTo(settler.CurrentTile) < 2);
    }

    // TryFogBust is private and only reachable after a dozen earlier AutomateUnitMoves steps; invoke it directly.
    private static bool TryFogBust(MapUnitView view) =>
        (bool)typeof(UnitAutomation).GetMethod("TryFogBust", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [view])!;

    [Fact]
    public void FogBustHeadsForReachableUnownedExploredTileNextToOurBorder()
    {
        var scout = SetUpFogBustScout(_ => true);
        var start = scout.CurrentTile;

        Assert.True(TryFogBust(ViewOf(scout)));

        Assert.NotEqual(start, scout.CurrentTile);
    }

    [Fact]
    public void FogBustLooksFurtherWhenNothingIsReachableThisTurn()
    {
        SetUpFogBustScout(_ => true).Destroy();
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 8)); // candidate tiles are in sight but more than a turn away
        var start = warrior.CurrentTile;

        Assert.True(TryFogBust(ViewOf(warrior)));

        Assert.NotEqual(start, warrior.CurrentTile);
    }

    [Fact]
    public void FogBustReturnsFalseWhenNoTileQualifies()
    {
        SetUpFogBustScout(t => t.AerialDistanceTo(testGame.GetTile(0, 0)) <= 1).Destroy();
        var scout = testGame.AddUnit("Scout", civ, testGame.GetTile(0, 10)); // sees nothing near our borders

        Assert.False(TryFogBust(ViewOf(scout)));
    }

    [Fact]
    public void FogBustReturnsFalseWhenNotAfraidOfBarbarians()
    {
        var scout = SetUpFogBustScout(_ => true);
        testGame.GameInfo.GameParameters.NoBarbarians = true;
        Assert.False(TryFogBust(ViewOf(scout)));
    }

    private static bool CallPrivate(string name, MapUnitView view) =>
        (bool)typeof(UnitAutomation).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [view, new AutomationCache()])!;

    private MapUnitClass SetUpRetreat(string friendlyType, int friendlyHealth, int enemyY = -2)
    {
        testGame.AddCity(civ, testGame.GetTile(0, 5));
        War();
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(0, enemyY));
        var friend = testGame.AddUnit(friendlyType, civ, testGame.GetTile(0, 3));
        friend.Health = friendlyHealth;
        var hurt = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 1));
        hurt.Health = 30;
        return hurt;
    }

    [Fact]
    public void TryRetreatSwapsPlacesWithHealthyFriendlyUnit()
    {
        var hurt = SetUpRetreat("Warrior", 100);
        var friend = testGame.GetTile(0, 3).MilitaryUnit!;

        Assert.True(CallPrivate("TryRetreat", ViewOf(hurt)));

        Assert.Equal(testGame.GetTile(0, 3), hurt.CurrentTile);
        Assert.NotEqual(testGame.GetTile(0, 3), friend.CurrentTile);
    }

    [Fact]
    public void TryRetreatDoesNotSwapWithFriendlyUnitAtEightyHealthOrLess()
    {
        var hurt = SetUpRetreat("Warrior", 80);

        Assert.True(CallPrivate("TryRetreat", ViewOf(hurt))); // falls through to the next tile, which is empty
        Assert.NotEqual(testGame.GetTile(0, 3), hurt.CurrentTile);
    }

    [Fact]
    public void TryRetreatDoesNotSwapWithRangedUnitCloserThanItsRange()
    {
        var hurt = SetUpRetreat("Archer", 100, enemyY: 0);

        Assert.True(CallPrivate("TryRetreat", ViewOf(hurt)));
        Assert.NotEqual(testGame.GetTile(0, 3), hurt.CurrentTile);
    }

    [Fact]
    public void TryHealUnitAirUnitWithNothingViableToHealOnHeadsForTheNearestCityTile()
    {
        testGame.AddCity(civ, testGame.GetTile(0, 6));
        testGame.AddCity(civ, testGame.GetTile(0, 7));
        War();
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(1, 7)); // every tile in reach is dangerous, so none is viable
        var fighter = testGame.AddUnit("Fighter", civ, testGame.GetTile(0, 6));
        fighter.Health = 1;
        var start = fighter.CurrentTile;

        var result = CallPrivate("TryHealUnit", ViewOf(fighter));

        // The nearest "empty" city tile for an air unit is the one it already sits on (distance 0), so it stays put.
        Assert.Equal(start, fighter.CurrentTile);
        Assert.True(result);
    }
}
