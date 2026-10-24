using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Automation.Unit;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Core.Tests.Logic.Automation;

// T155. AirUnitAutomation.kt has no Kotlin test fixture: these are C#-only cases with expected values derived by hand from the Kotlin source.
[Collection("RulesetCache")]
public class AirUnitAutomationTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject civ;
    private readonly CivilizationObject enemy;

    public AirUnitAutomationTests()
    {
        testGame.GameInfo.EngineOptions.VisibleMap = true;
        testGame.MakeHexagonalMap(30, Constants.Grassland);
        civ = testGame.AddCiv();
        enemy = testGame.AddCiv();
        civ.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
    }

    private void War() => civ.GetDiplomacyManager(enemy)!.DeclareWar();

    private MapUnitView ViewOf(MapUnitClass unit) => new GameView(testGame.GameInfo, unit.Civ).GetMapUnitView(unit);

    private CityObject OurCity(int x, int y) => testGame.AddCity(civ, testGame.GetTile(x, y));

    private CityObject EnemyCity(int x, int y) => testGame.AddCity(enemy, testGame.GetTile(x, y));

    // region automateFighter

    [Fact]
    public void WoundedFighterWaitsToHeal()
    {
        OurCity(0, 0);
        EnemyCity(4, 0);
        var fighter = testGame.AddUnit("Fighter", civ, testGame.GetTile(0, 0));
        fighter.Health = 70;
        War();

        AirUnitAutomation.AutomateFighter(ViewOf(fighter), new AutomationCache());

        Assert.Equal(testGame.GetTile(0, 0), fighter.CurrentTile);
        Assert.True(fighter.CurrentMovement > 0);
    }

    [Fact]
    public void FighterSweepsAnEnemyAirUnitInRange()
    {
        OurCity(0, 0);
        EnemyCity(4, 0);
        var fighter = testGame.AddUnit("Fighter", civ, testGame.GetTile(0, 0));
        var enemyFighter = testGame.AddUnit("Fighter", enemy, testGame.GetTile(4, 0));
        War();

        AirUnitAutomation.AutomateFighter(ViewOf(fighter), new AutomationCache());

        Assert.True(fighter.AttacksThisTurn > 0);
        Assert.NotNull(enemyFighter);
    }

    [Fact]
    public void OutnumberedFighterWithoutBonusStaysOnStandby()
    {
        OurCity(0, 0);
        EnemyCity(4, 0);
        var fighter = testGame.AddUnit("Fighter", civ, testGame.GetTile(0, 0));
        // 4 enemy planes -> 2 assumed fighters, we have 1
        for (int i = 0; i < 4; i++)
            testGame.AddUnit("Fighter", enemy, testGame.GetTile(4, 0));
        War();

        AirUnitAutomation.AutomateFighter(ViewOf(fighter), new AutomationCache());

        Assert.Equal(0, fighter.AttacksThisTurn);
        Assert.Equal(testGame.GetTile(0, 0), fighter.CurrentTile);
    }

    [Fact]
    public void IdleFighterWithNoEnemyAirUnitsRelocatesToACityFromWhichItCanAttack()
    {
        OurCity(0, 0);
        var forward = OurCity(7, 0);
        var fighter = testGame.AddUnit("Fighter", civ, testGame.GetTile(0, 0));
        // Only the forward city can reach the target
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(7 + fighter.GetRange(), 0));
        War();

        AirUnitAutomation.AutomateFighter(ViewOf(fighter), new AutomationCache());

        Assert.Equal(forward.GetCenterTile(), fighter.CurrentTile);
    }

    [Fact]
    public void FighterMovesToTheCityWithTheMostEnemyAirUnitsNearby()
    {
        OurCity(0, 0);
        OurCity(7, 0);
        var threatened = OurCity(-7, 0);
        var fighter = testGame.AddUnit("Fighter", civ, testGame.GetTile(0, 0));
        // an enemy plane within 2*range of the threatened city only, out of every city's attack range
        var enemyCity = EnemyCity(-21, 0);
        testGame.AddUnit("Fighter", enemy, enemyCity.GetCenterTile());
        War();

        AirUnitAutomation.AutomateFighter(ViewOf(fighter), new AutomationCache());

        Assert.Equal(threatened.GetCenterTile(), fighter.CurrentTile);
    }

    // region automateBomber

    [Fact]
    public void WoundedBomberWaitsToHeal()
    {
        OurCity(0, 0);
        OurCity(8, 0);
        var bomber = testGame.AddUnit("Bomber", civ, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(8 + bomber.GetRange(), 0));
        War();

        bomber.Health = 74;
        AirUnitAutomation.AutomateBomber(ViewOf(bomber));
        Assert.Equal(testGame.GetTile(0, 0), bomber.CurrentTile);

        bomber.Health = 90; // <= 90 waits too
        AirUnitAutomation.AutomateBomber(ViewOf(bomber));
        Assert.Equal(testGame.GetTile(0, 0), bomber.CurrentTile);

        bomber.Health = 95; // below 100 only waits while at peace
        AirUnitAutomation.AutomateBomber(ViewOf(bomber));
        Assert.NotEqual(testGame.GetTile(0, 0), bomber.CurrentTile);
    }

    [Fact]
    public void BomberAtPeaceWaitsUntilFullyHealed()
    {
        OurCity(0, 0);
        OurCity(8, 0);
        var bomber = testGame.AddUnit("Bomber", civ, testGame.GetTile(0, 0));
        bomber.Health = 95;

        AirUnitAutomation.AutomateBomber(ViewOf(bomber));

        Assert.Equal(testGame.GetTile(0, 0), bomber.CurrentTile);
    }

    [Fact]
    public void BomberRelocatesToACityFromWhichItCanBomb()
    {
        OurCity(0, 0);
        var forward = OurCity(8, 0);
        var bomber = testGame.AddUnit("Bomber", civ, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(8 + bomber.GetRange(), 0));
        War();

        AirUnitAutomation.AutomateBomber(ViewOf(bomber));

        Assert.Equal(forward.GetCenterTile(), bomber.CurrentTile);
    }

    [Fact]
    public void BomberWithNowhereUsefulToGoStays()
    {
        OurCity(0, 0);
        OurCity(8, 0);
        var bomber = testGame.AddUnit("Bomber", civ, testGame.GetTile(0, 0));
        War();

        AirUnitAutomation.AutomateBomber(ViewOf(bomber));

        Assert.Equal(testGame.GetTile(0, 0), bomber.CurrentTile);
    }

    [Fact]
    public void BomberWithOnlyItsOwnCityHasNoPaths()
    {
        OurCity(0, 0);
        var bomber = testGame.AddUnit("Bomber", civ, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(bomber.GetRange(), 0));
        War();

        AirUnitAutomation.AutomateBomber(ViewOf(bomber));

        Assert.Equal(testGame.GetTile(0, 0), bomber.CurrentTile);
    }

    [Fact]
    public void BomberHopsThroughIntermediateCitiesToReachADistantOne()
    {
        OurCity(0, 0);
        var hop = OurCity(9, 0);
        OurCity(18, 0);
        OurCity(27, 0);
        var bomber = testGame.AddUnit("Bomber", civ, testGame.GetTile(0, 0));
        Assert.Equal(10, bomber.GetRange());
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(29, 0)); // only the last city can reach it, 3 hops away
        War();

        AirUnitAutomation.AutomateBomber(ViewOf(bomber));

        Assert.Equal(hop.GetCenterTile(), bomber.CurrentTile);
    }

    // region missiles

    [Fact]
    public void MissileRelocatesToACityWithAnEnemyCityInRange()
    {
        OurCity(0, 0);
        var forward = OurCity(3, 0);
        var missile = testGame.AddUnit("Guided Missile", civ, testGame.GetTile(0, 0));
        EnemyCity(3 + missile.GetRange(), 0);
        War();

        AirUnitAutomation.AutomateMissile(ViewOf(missile));

        Assert.True(missile.CurrentTile == forward.GetCenterTile() || missile.IsDestroyed);
    }

    [Fact]
    public void MissileWithNoEnemyAroundStays()
    {
        OurCity(0, 0);
        OurCity(3, 0);
        var missile = testGame.AddUnit("Guided Missile", civ, testGame.GetTile(0, 0));

        AirUnitAutomation.AutomateMissile(ViewOf(missile));

        Assert.Equal(testGame.GetTile(0, 0), missile.CurrentTile);
    }

    // region automateNukes

    [Fact]
    public void NukesAreHeldBackInPeace()
    {
        OurCity(0, 0);
        EnemyCity(5, 0);
        var nuke = testGame.AddUnit("Atomic Bomb", civ, testGame.GetTile(0, 0));

        AirUnitAutomation.AutomateNukes(ViewOf(nuke));

        Assert.False(nuke.IsDestroyed);
        Assert.Equal(testGame.GetTile(0, 0), nuke.CurrentTile);
    }

    [Fact]
    public void NukeIsLaunchedAtADenseEnemyConcentration()
    {
        OurCity(0, 0);
        var enemyCity = EnemyCity(5, 0);
        var defenders = new List<MapUnitClass> { testGame.AddUnit("Warrior", enemy, enemyCity.GetCenterTile()) };
        foreach (var neighbor in enemyCity.GetCenterTile().Neighbors.Take(4))
            defenders.Add(testGame.AddUnit("Warrior", enemy, neighbor));
        var nuke = testGame.AddUnit("Atomic Bomb", civ, testGame.GetTile(0, 0));
        War();

        AirUnitAutomation.AutomateNukes(ViewOf(nuke));

        Assert.True(nuke.IsDestroyed);
        Assert.Contains(defenders, it => it.IsDestroyed || it.Health < 100);
    }

    [Fact]
    public void NukeIsNotLaunchedWhenItWouldHitAThirdCivWeAreNotAtWarWith()
    {
        OurCity(0, 0);
        var enemyCity = EnemyCity(5, 0);
        testGame.AddUnit("Warrior", enemy, enemyCity.GetCenterTile());
        foreach (var neighbor in enemyCity.GetCenterTile().Neighbors.Take(4))
            testGame.AddUnit("Warrior", enemy, neighbor);
        var bystander = testGame.AddCiv();
        civ.DiplomacyFunctions.MakeCivilizationsMeet(bystander);
        // every candidate target that includes enemy units also reaches this ring, so there is no way to nuke the enemy without hitting the bystander
        foreach (var ringTile in enemyCity.GetCenterTile().GetTilesAtDistance(2))
            testGame.AddUnit("Warrior", bystander, ringTile);
        var nuke = testGame.AddUnit("Atomic Bomb", civ, testGame.GetTile(0, 0));
        War();

        AirUnitAutomation.AutomateNukes(ViewOf(nuke));

        Assert.False(nuke.IsDestroyed);
    }

    [Fact]
    public void NukeIsNotLaunchedWhenNoEnemyIsInTheBlast()
    {
        OurCity(0, 0);
        EnemyCity(12, 0);
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(-8, 0));
        var nuke = testGame.AddUnit("Atomic Bomb", civ, testGame.GetTile(0, 0));
        War();

        AirUnitAutomation.AutomateNukes(ViewOf(nuke));

        Assert.False(nuke.IsDestroyed);
    }

    [Fact]
    public void NukeIsNotLaunchedWhenOurOwnCityIsInTheBlast()
    {
        OurCity(0, 0);
        var ours = OurCity(4, 0);
        var enemyUnit = testGame.AddUnit("Warrior", enemy, ours.GetCenterTile().GetTilesAtDistance(2).First());
        var nuke = testGame.AddUnit("Atomic Bomb", civ, testGame.GetTile(0, 0));
        War();
        Assert.NotNull(enemyUnit);

        AirUnitAutomation.AutomateNukes(ViewOf(nuke));

        Assert.False(nuke.IsDestroyed);
    }
}
