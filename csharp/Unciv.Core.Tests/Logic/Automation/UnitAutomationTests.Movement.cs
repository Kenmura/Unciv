using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Civilization;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.View;
using UnitAutomationSeamsClass = Unciv.Logic.Map.MapUnit.Actions.UnitAutomation;

namespace Unciv.Core.Tests.Logic.Automation;

public partial class UnitAutomationTests
{
    [Fact]
    public void BarbariansAreNotAllowedInAutomateUnitMoves()
    {
        var barb = testGame.AddBarbarianCiv();
        var barbUnit = testGame.AddUnit("Warrior", barb, testGame.GetTile(5, 5));
        var view = new GameView(testGame.GameInfo, barb).GetMapUnitView(barbUnit);

        var ex = Assert.Throws<InvalidOperationException>(() => UnitAutomation.AutomateUnitMoves(view));
        Assert.Equal("Barbarians is not allowed here.", ex.Message);
    }

    [Fact]
    public void AutomateUnitMovesHandlesCivilianWithoutCrashing()
    {
        var worker = testGame.AddUnit("Worker", civ, testGame.GetTile(0, 0));
        UnitAutomation.AutomateUnitMoves(ViewOf(worker));
        // Seam doesn't move unit, returns cleanly
        Assert.Equal(testGame.GetTile(0, 0), worker.CurrentTile);
    }

    [Fact]
    public void AutomateUnitMovesTreatsMilitaryWithCivilianUniqueAsCivilianInPeace()
    {
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        warrior.Promotions.AddPromotion(testGame.CreateUnitPromotion(UniqueType.FoundCity.Text()).Name);

        Assert.False(civ.IsAtWar());
        Assert.True(warrior.HasUnique(UniqueType.FoundCity));

        UnitAutomation.AutomateUnitMoves(ViewOf(warrior));
        // In peace, delegates to civilian seam and returns without moving
        Assert.Equal(testGame.GetTile(0, 0), warrior.CurrentTile);
    }

    [Fact]
    public void WanderMovesUnitToOneAdjacentReachableTile()
    {
        var cs = testGame.AddCiv(cityStateType: "Cultured");
        var warrior = testGame.AddUnit("Warrior", cs, testGame.GetTile(0, 0));
        var csView = new GameView(testGame.GameInfo, cs).GetMapUnitView(warrior);
        var startTile = warrior.CurrentTile;

        UnitAutomation.Wander(csView);

        Assert.NotEqual(startTile, warrior.CurrentTile);
        Assert.Equal(1, startTile.AerialDistanceTo(warrior.CurrentTile));
    }

    [Fact]
    public void WanderRespectsStayInTerritoryWhenPossible()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 1));
        var startTile = warrior.CurrentTile;

        UnitAutomation.Wander(ViewOf(warrior), stayInTerritory: true);

        Assert.NotEqual(startTile, warrior.CurrentTile);
        Assert.Equal(civ, warrior.CurrentTile.GetOwner());
    }

    [Fact]
    public void WanderRespectsTilesToAvoid()
    {
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        var startTile = warrior.CurrentTile;
        var view = ViewOf(warrior);
        var neighbors = view.GetTile().GetExploredTilesAtDistance(1).ToList();
        Assert.True(neighbors.Count > 1);

        // Avoid all neighbors except the first one
        var allowed = neighbors[0];
        var avoid = neighbors.Skip(1).ToHashSet();

        UnitAutomation.Wander(view, tilesToAvoid: avoid);

        Assert.Equal(allowed.Position(), warrior.CurrentTile.Position);
    }

    [Fact]
    public void TryBombardEnemyFailsWhenCityCannotBombard()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        Assert.False(UnitAutomation.TryBombardEnemy(city)); // No enemies in range
    }

    [Fact]
    public void TryBombardEnemyAttacksHighestDamageMilitaryTargetAndIgnoresCivilians()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        War();

        var weakDefender = testGame.AddUnit("Scout", enemy, testGame.GetTile(0, 2));
        var civilianTarget = testGame.AddUnit("Worker", enemy, testGame.GetTile(0, 1));

        Assert.True(city.CanBombard());
        Assert.True(UnitAutomation.TryBombardEnemy(city));

        // Military unit was attacked (took damage), civilian was not targeted
        Assert.True(weakDefender.Health < 100);
        Assert.Equal(100, civilianTarget.Health);
    }

    [Fact]
    public void TryPillageImprovementMovesAndPillages()
    {
        testGame.AddCity(enemy, testGame.GetTile(5, 5));
        var farmTile = testGame.GetTile(1, 0);
        farmTile.Improvement = "Farm";
        testGame.AddCity(civ, testGame.GetTile(-5, -5));
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        War();

        Assert.True(UnitAutomation.TryPillageImprovement(ViewOf(warrior)));

        Assert.Equal(farmTile, warrior.CurrentTile);
        Assert.True(farmTile.ImprovementIsPillaged);
    }

    [Fact]
    public void TryEnterOwnClosestCityHeadsTowardsCity()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(4, 0));
        var start = warrior.CurrentTile;

        Assert.True(UnitAutomation.TryEnterOwnClosestCity(ViewOf(warrior)));

        Assert.NotEqual(start, warrior.CurrentTile);
        Assert.True(warrior.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < start.AerialDistanceTo(city.GetCenterTile()));
    }

    [Fact]
    public void AutomatedExploreFinishesAndSendsNotificationWhenAllExplored()
    {
        civ.PlayerType = PlayerType.Human;
        foreach (var tile in testGame.TileMap.Values) tile.SetExplored(civ, true);
        var scout = testGame.AddUnit("Scout", civ, testGame.GetTile(0, 0));
        scout.Action = UnitActionType.Explore.Value();

        UnitAutomation.AutomatedExplore(ViewOf(scout));

        Assert.Null(scout.Action);
        Assert.Contains(civ.Notifications, n => n.Text.Contains("finished exploring", StringComparison.Ordinal));
    }

    [Fact]
    public void MapUnitDoActionRunsAutomationWhenAutomatedOrExploring()
    {
        civ.PlayerType = PlayerType.Human;
        foreach (var tile in testGame.TileMap.Values) tile.SetExplored(civ, true);
        var scout = testGame.AddUnit("Scout", civ, testGame.GetTile(0, 0));
        scout.Action = UnitActionType.Explore.Value();

        scout.DoAction();

        // AutomatedExplore executed
        Assert.Null(scout.Action);
        Assert.Contains(civ.Notifications, n => n.Text.Contains("finished exploring", StringComparison.Ordinal));
    }

    [Fact]
    public void UnitActionsSeamsForwardToRealUnitAutomation()
    {
        civ.PlayerType = PlayerType.Human;
        foreach (var tile in testGame.TileMap.Values) tile.SetExplored(civ, true);
        var scout = testGame.AddUnit("Scout", civ, testGame.GetTile(0, 0));
        scout.Action = UnitActionType.Explore.Value();

        UnitAutomationSeamsClass.AutomatedExplore(ViewOf(scout));

        Assert.Null(scout.Action);
        Assert.Contains(civ.Notifications, n => n.Text.Contains("finished exploring", StringComparison.Ordinal));

        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(1, 0));
        UnitAutomationSeamsClass.AutomateUnitMoves(ViewOf(warrior));
    }

    [Fact]
    public void TryAccompanySettlerHeadsTowardsSettler()
    {
        var settler = testGame.AddUnit("Settler", civ, testGame.GetTile(2, 0));
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        var start = warrior.CurrentTile;

        UnitAutomation.AutomateUnitMoves(ViewOf(warrior));

        Assert.NotEqual(start, warrior.CurrentTile);
        Assert.True(warrior.CurrentTile.AerialDistanceTo(settler.CurrentTile) < start.AerialDistanceTo(settler.CurrentTile));
    }

    [Fact]
    public void TryHeadTowardsEncampmentMovesUnitTowardsEncampment()
    {
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        var encTile = testGame.GetTile(4, 0);
        encTile.Improvement = Constants.BarbarianEncampment;
        encTile.SetExplored(civ, true);
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 1));
        var start = warrior.CurrentTile;

        UnitAutomation.AutomateUnitMoves(ViewOf(warrior));

        Assert.NotEqual(start, warrior.CurrentTile);
        Assert.True(warrior.CurrentTile.AerialDistanceTo(encTile) < start.AerialDistanceTo(encTile));
    }
}
