using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Civilization;
using Unciv.View;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Core.Tests.Logic.Automation;

// T094: port of tests/src/com/unciv/logic/automation/civilization/NextTurnAutomationTest.kt. The Kotlin class is parameterized over the
// pathfinding algorithm; the A* PathingMap is T093, so only the default (non-A*) pathfinding runs here. The Mockito spy of onFirstAutomation is
// GameClientState.BeforeUnitMoves (the first thing automateUnitMoves asks of a unit). Further cases are C#-only, derived by hand from the Kotlin.
[Collection("RulesetCache")]
public class NextTurnAutomationTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject civInfo;

    public NextTurnAutomationTests()
    {
        testGame.MakeHexagonalMap(7);
        civInfo = testGame.AddCiv();
        var capital = testGame.AddCity(civInfo, testGame.TileMap[0, 0]);
        Assert.True(capital.IsCapital());
    }

    private CivView CivViewOf(CivilizationObject civ) => new GameView(testGame.GameInfo, civ).CivView;

    private void OnFirstAutomation(MapUnitClass unit, Action action)
    {
        var done = false;
        testGame.GameInfo.ClientState.BeforeUnitMoves = view =>
        {
            if (done || !ReferenceEquals(view.Unwrap(), unit)) return;
            done = true;
            action();
        };
    }

    [Fact]
    public void AutomateSettlerEscortingReplacesLowHpEscort()
    {
        var settler = testGame.AddUnit("Settler", civInfo, testGame.TileMap[0, 2]);
        var highHpWarrior = testGame.AddUnit("Warrior", civInfo, testGame.TileMap[0, 1]);
        var lowHpWarrior = testGame.AddUnit("Warrior", civInfo, testGame.TileMap[0, 2]);
        lowHpWarrior.TakeDamage(90);
        lowHpWarrior.StartEscorting();
        Assert.True(settler.IsEscorting());

        NextTurnAutomation.AutomateSettlerEscorting(CivViewOf(civInfo));
        Assert.Equal(testGame.TileMap[0, 2], settler.CurrentTile); // settler should not have moved, else test is invalid

        Assert.Equal(testGame.TileMap[0, 2], highHpWarrior.CurrentTile);
        Assert.Equal(testGame.TileMap[0, 1], lowHpWarrior.CurrentTile);
        Assert.Equal(highHpWarrior, settler.GetOtherEscortUnit());
        Assert.Equal(settler, highHpWarrior.GetOtherEscortUnit());
        Assert.Null(lowHpWarrior.GetOtherEscortUnit());
    }

    [Fact]
    public void AutomateSettlerEscortingReplacesLowHpEscortEvenWithNoMovement()
    {
        var settler1 = testGame.AddUnit("Settler", civInfo, testGame.TileMap[0, 2]);
        var settler2 = testGame.AddUnit("Settler", civInfo, testGame.TileMap[0, 1]);
        var highHpWarrior = testGame.AddUnit("Warrior", civInfo, testGame.TileMap[0, 1]);
        var lowHpWarrior = testGame.AddUnit("Warrior", civInfo, testGame.TileMap[0, 2]);
        lowHpWarrior.TakeDamage(90);
        lowHpWarrior.StartEscorting();
        settler2.CurrentMovement = 0f;
        Assert.Equal(lowHpWarrior, settler1.GetOtherEscortUnit());
        Assert.Equal(highHpWarrior, settler2.GetOtherEscortUnit());

        NextTurnAutomation.AutomateSettlerEscorting(CivViewOf(civInfo));
        Assert.Equal(testGame.TileMap[0, 2], settler1.CurrentTile);
        Assert.Equal(testGame.TileMap[0, 1], settler2.CurrentTile);

        Assert.Equal(testGame.TileMap[0, 2], highHpWarrior.CurrentTile);
        Assert.Equal(testGame.TileMap[0, 1], lowHpWarrior.CurrentTile);
        Assert.Equal(highHpWarrior, settler1.GetOtherEscortUnit());
        Assert.Equal(settler1, highHpWarrior.GetOtherEscortUnit());
        Assert.Equal(lowHpWarrior, settler2.GetOtherEscortUnit());
        Assert.Equal(settler2, lowHpWarrior.GetOtherEscortUnit());
    }

    [Fact]
    public void UnitDestroyedDuringAnEarlierPromotionIsNotPromotedOrAutomated()
    {
        // Civilian has no vanilla promotion tree, so Lead is the only promotion these units can buy.
        var actorBase = testGame.CreateBaseUnit("Civilian");
        actorBase.Movement = 2;
        actorBase.Strength = 8;
        var victimBase = testGame.CreateBaseUnit("Civilian");
        victimBase.Movement = 2;
        victimBase.Strength = 8;
        var bystanderBase = testGame.CreateBaseUnit("Civilian");
        bystanderBase.Movement = 2;
        bystanderBase.Strength = 8;
        // Granted, not chosen: empty unitTypes cannot be bought, but can be applied.
        var doom = testGame.CreateUnitPromotion("[This Unit] is destroyed");
        var lead = testGame.CreateUnitPromotion($"[{victimBase.Name}] units gain the [{doom.Name}] promotion");
        lead.UnitTypes = ["Civilian"];

        var actor = testGame.AddUnit(actorBase.Name, civInfo, testGame.GetTile(1, 0));
        var victim = testGame.AddUnit(victimBase.Name, civInfo, testGame.GetTile(0, 1));
        var bystander = testGame.AddUnit(bystanderBase.Name, civInfo, testGame.GetTile(0, 2));
        var victimTile = victim.CurrentTile;
        actor.Health = 10;
        victim.Health = 40;
        bystander.Health = 100;
        actor.Promotions.XP = 10;
        victim.Promotions.XP = 10;
        bystander.Promotions.XP = 10;
        var bystanderStart = bystander.CurrentTile;

        NextTurnAutomation.AutomateCivMoves(civInfo, tradeAndChangeState: false);

        Assert.Contains(lead.Name, actor.Promotions.Promotions); // earlier promotion ran
        Assert.True(victim.IsDestroyed); // victim was destroyed during the pass
        Assert.Contains(doom.Name, victim.Promotions.Promotions); // destruction was the earlier unit's promotion effect
        Assert.DoesNotContain(lead.Name, victim.Promotions.Promotions); // destroyed unit was not promoted on its own turn
        Assert.NotSame(victim, victimTile.MilitaryUnit);
        Assert.Contains(lead.Name, bystander.Promotions.Promotions); // bystander was still promoted
        Assert.False(bystander.IsDestroyed);
        Assert.True(bystander.CurrentTile != bystanderStart || bystander.CurrentMovement < 2f, "bystander was still automated");
    }

    [Fact]
    public void SettlerTransferredToBarbariansDuringThePassIsNotAutomated()
    {
        var barbarians = testGame.AddBarbarianCiv();
        var spaceship = testGame.AddUnit("SS Engine", civInfo, testGame.GetTile(2, 0));
        spaceship.CurrentMovement = 0f;
        var settler = testGame.AddUnit("Settler", civInfo, testGame.GetTile(3, 0));
        var settlerTile = settler.CurrentTile;
        var bystander = testGame.AddUnit("Warrior", civInfo, testGame.GetTile(0, 2));
        var bystanderStart = bystander.CurrentTile;

        OnFirstAutomation(spaceship, () => settler.CapturedBy(barbarians));

        NextTurnAutomation.AutomateCivMoves(civInfo, tradeAndChangeState: false);

        Assert.True(settler.Civ.IsBarbarian); // transfer happened
        Assert.Same(barbarians, settler.Civ);
        Assert.DoesNotContain(civInfo.Units.GetCivUnits(), it => ReferenceEquals(it, settler));
        Assert.Contains(barbarians.Units.GetCivUnits(), it => ReferenceEquals(it, settler));
        Assert.Same(settlerTile, settler.CurrentTile); // settler received no further move
        Assert.Single(civInfo.Cities);
        Assert.True(bystander.CurrentTile != bystanderStart || bystander.CurrentMovement < 2f, "unaffected unit was still automated");
    }

    [Fact]
    public void WarriorGiftedToAnotherCivDuringThePassIsNotAutomated()
    {
        var otherCiv = testGame.AddCiv();
        var actorBase = testGame.CreateBaseUnit("Civilian");
        var actor = testGame.AddUnit(actorBase.Name, civInfo, testGame.GetTile(2, 2));
        actor.CurrentMovement = 0f;
        var victim = testGame.AddUnit("Warrior", civInfo, testGame.GetTile(2, 0));
        var victimTile = victim.CurrentTile;
        var victimMovement = victim.CurrentMovement;
        var bystander = testGame.AddUnit("Warrior", civInfo, testGame.GetTile(0, 2));
        var bystanderStart = bystander.CurrentTile;

        OnFirstAutomation(actor, () => victim.Gift(otherCiv));

        NextTurnAutomation.AutomateCivMoves(civInfo, tradeAndChangeState: false);

        Assert.Same(otherCiv, victim.Civ); // transfer happened
        Assert.Contains(otherCiv.Units.GetCivUnits(), it => ReferenceEquals(it, victim));
        Assert.DoesNotContain(civInfo.Units.GetCivUnits(), it => ReferenceEquals(it, victim));
        Assert.Same(victimTile, victim.CurrentTile); // former owner did not move the gifted unit
        Assert.Equal(victimMovement, victim.CurrentMovement, 0.01f);
        Assert.True(bystander.CurrentTile != bystanderStart || bystander.CurrentMovement < 2f, "unaffected unit was still automated");
    }
}
