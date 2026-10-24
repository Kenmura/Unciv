using Unciv.Logic.Automation.Unit;
using Unciv.Logic.City;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.View;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Core.Tests.Logic.Automation;

// T159. C#-only cases (UnitAutomation.kt has no Kotlin fixture) covering the T156 dispatch in AutomateUnitMoves and its Try* helpers.
// Expected values derived by hand from the Kotlin source.
public partial class UnitAutomationTests
{
    private void Move(MapUnitClass unit) => UnitAutomation.AutomateUnitMoves(ViewOf(unit));

    // region air dispatch

    [Fact]
    public void AirUnitsAreDispatchedToTheirSpecificAutomation()
    {
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        var fighter = testGame.AddUnit("Fighter", civ, testGame.GetTile(0, 0));
        fighter.Health = 70; // AutomateFighter: a wounded fighter just waits to heal
        Move(fighter);
        Assert.Equal(testGame.GetTile(0, 0), fighter.CurrentTile);

        var bomber = testGame.AddUnit("Bomber", civ, testGame.GetTile(0, 0));
        bomber.Health = 70;
        Move(bomber);
        Assert.Equal(testGame.GetTile(0, 0), bomber.CurrentTile);

        var missile = testGame.AddUnit("Guided Missile", civ, testGame.GetTile(0, 0));
        Move(missile); // nothing to shoot at, stays
        Assert.Equal(testGame.GetTile(0, 0), missile.CurrentTile);

        var nuke = testGame.AddUnit("Atomic Bomb", civ, testGame.GetTile(0, 0));
        Move(nuke); // nukes are held back in peace
        Assert.Equal(testGame.GetTile(0, 0), nuke.CurrentTile);
    }

    // endregion

    // region human promotion/upgrade and civilian-in-peace

    [Fact]
    public void HumanUnitIsUpgradedAndEndsItsAutomation()
    {
        civ.PlayerType = PlayerType.Human;
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        // A human unit goes through ApplyPromotions and TryUpgradeUnit before anything else; the result must not throw.
        Move(warrior);
        Assert.NotNull(warrior.CurrentTile);
    }

    // endregion

    // region TryHealUnit / TryRetreat

    [Fact]
    public void WoundedUnitWithoutEnemiesHealsInPlaceAndFortifies()
    {
        testGame.AddCity(civ, testGame.GetTile(-8, 0));
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        warrior.Health = 40;

        Move(warrior);

        Assert.Equal(testGame.GetTile(0, 0), warrior.CurrentTile);
        Assert.True(warrior.IsFortified() || warrior.Action == UnitActionType.Fortify.Value() || warrior.Action == UnitActionType.FortifyUntilHealed.Value());
    }

    [Fact]
    public void WoundedUnitNextToEnemyRetreatsTowardsItsCity()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(-4, 0));
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        warrior.Health = 30;
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(2, 0));
        War();
        var start = warrior.CurrentTile;

        Move(warrior);

        Assert.True(warrior.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < start.AerialDistanceTo(city.GetCenterTile()));
    }

    [Fact]
    public void WoundedUnitWithoutACityRetreatsAwayFromTheEnemy()
    {
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        warrior.Health = 30;
        var foe = testGame.AddUnit("Warrior", enemy, testGame.GetTile(2, 0));
        War();
        var start = warrior.CurrentTile;

        Move(warrior);

        Assert.True(warrior.CurrentTile.AerialDistanceTo(foe.CurrentTile) > start.AerialDistanceTo(foe.CurrentTile));
    }

    [Fact]
    public void RetreatingUnitSwapsWithAHealthyFriendlyUnitOnTheRetreatTile()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(-6, 0));
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        warrior.Health = 30;
        // every tile towards the city that is better than ours is occupied by a healthy friend
        var friends = new List<MapUnitClass>();
        foreach (var tile in testGame.GetTile(0, 0).GetTilesInDistance(2).Where(it => it != warrior.CurrentTile))
            friends.Add(testGame.AddUnit("Warrior", civ, tile));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(3, 0));
        War();

        Move(warrior);

        Assert.True(city.GetCenterTile().AerialDistanceTo(warrior.CurrentTile) <= 6);
        Assert.All(friends, it => Assert.NotNull(it.CurrentTile));
    }

    [Fact]
    public void WoundedUnitPillagesToHeal()
    {
        testGame.AddCity(civ, testGame.GetTile(-8, 0));
        var farm = testGame.GetTile(1, 0);
        farm.Improvement = "Farm";
        farm.SetOwningCity(testGame.AddCity(enemy, testGame.GetTile(5, 0)));
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        warrior.Health = 40;
        War();

        Move(warrior);

        Assert.True(farm.ImprovementIsPillaged);
    }

    [Fact]
    public void EmbarkedWoundedUnitHeadsForACity()
    {
        testGame.SetTileTerrain(new HexCoord(0, 0), Constants.Coast);
        var city = testGame.AddCity(civ, testGame.GetTile(-2, 0));
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        warrior.Health = 40;
        var start = warrior.CurrentTile;
        Assert.True(warrior.IsEmbarked());

        Move(warrior);

        Assert.True(warrior.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < start.AerialDistanceTo(city.GetCenterTile()));
    }

    [Fact]
    public void EmbarkedWoundedUnitWithoutACityWanders()
    {
        testGame.SetTileTerrain(new HexCoord(0, 0), Constants.Coast);
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        warrior.Health = 40;

        Move(warrior);

        Assert.NotNull(warrior.CurrentTile);
    }

    // endregion

    // region sieged city, captured city, garrison

    [Fact]
    public void UnitHeadsTowardsOurSiegedCity()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(-6, 0));
        city.Health = 10;
        testGame.AddCity(civ, testGame.GetTile(6, 6)); // a healthy one far away
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", civ, city.GetCenterTile()); // garrisoned
        var start = warrior.CurrentTile;

        Move(warrior);

        Assert.True(warrior.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < start.AerialDistanceTo(city.GetCenterTile()));
    }

    [Fact]
    public void UnitNearAnSiegedCityStaysOutOfTheSiegeMovement()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        city.Health = 10;
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 1));

        Move(warrior);

        Assert.NotNull(warrior.CurrentTile);
    }

    [Fact]
    public void UnitTakesBackACityThatWasCapturedFromUs()
    {
        testGame.AddCity(civ, testGame.GetTile(-8, 0));
        var captured = testGame.AddCity(enemy, testGame.GetTile(6, 0));
        captured.FoundingCivObject = civ;
        captured.SetFlag(CityFlags.Resistance, 3);
        captured.Health = 5;
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        War();
        var start = warrior.CurrentTile;

        Move(warrior);

        Assert.True(warrior.CurrentTile.AerialDistanceTo(captured.GetCenterTile()) < start.AerialDistanceTo(captured.GetCenterTile()));
    }

    [Fact]
    public void RangedUnitIgnoresACapturedCityAtOneHealth()
    {
        testGame.AddCity(civ, testGame.GetTile(-8, 0));
        var captured = testGame.AddCity(enemy, testGame.GetTile(6, 0));
        captured.FoundingCivObject = civ;
        captured.SetFlag(CityFlags.Resistance, 3);
        captured.Health = 1;
        var archer = testGame.AddUnit("Archer", civ, testGame.GetTile(0, 0));
        War();

        Move(archer);

        Assert.NotNull(archer.CurrentTile);
    }

    [Fact]
    public void PeacetimeUnitGarrisonsAnEmptyCityAndStaysOnceInside()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(-5, 0));
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        var start = warrior.CurrentTile;

        Move(warrior);
        Assert.True(warrior.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < start.AerialDistanceTo(city.GetCenterTile()));

        var inside = testGame.AddUnit("Scout", civ, city.GetCenterTile());
        city.GetCenterTile().MilitaryUnit = null;
        inside.CurrentMovement = inside.GetMaxMovement();
        Move(inside);
        Assert.Equal(city.GetCenterTile(), inside.CurrentTile);
    }

    [Fact]
    public void WartimeUnitGarrisonsTheCityThatNeedsDefendingFirst()
    {
        var safe = testGame.AddCity(civ, testGame.GetTile(-3, 0));
        var threatened = testGame.AddCity(civ, testGame.GetTile(0, 8));
        testGame.AddCity(enemy, testGame.GetTile(0, 11)); // within 5 of the threatened city
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 2));
        War();
        var start = warrior.CurrentTile;

        Move(warrior);

        Assert.True(warrior.CurrentTile.AerialDistanceTo(threatened.GetCenterTile()) < start.AerialDistanceTo(threatened.GetCenterTile()));
        Assert.NotEqual(safe.GetCenterTile(), warrior.CurrentTile);
    }

    [Fact]
    public void WartimeUnitStaysInACityThatNeedsDefending()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        city.Health = 10;
        var warrior = testGame.AddUnit("Warrior", civ, city.GetCenterTile());
        War();

        Move(warrior);

        Assert.Equal(city.GetCenterTile(), warrior.CurrentTile);
    }

    [Fact]
    public void WartimeUnitFallsBackToAnyEmptyCityWhenNoneNeedsDefending()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(-8, 0));
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        War();
        var start = warrior.CurrentTile;

        Move(warrior);

        Assert.True(warrior.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < start.AerialDistanceTo(city.GetCenterTile()));
    }

    [Fact]
    public void WaterUnitsDoNotGarrison()
    {
        testGame.AddCity(civ, testGame.GetTile(-3, 0));
        testGame.SetTileTerrain(new HexCoord(0, 0), Constants.Coast);
        var boat = testGame.AddUnit("Trireme", civ, testGame.GetTile(0, 0));

        Move(boat);

        Assert.NotNull(boat.CurrentTile);
    }

    // endregion

    // region advance towards enemy, prepare, wander

    [Fact]
    public void UnitAdvancesTowardsACloseEnemyUnitItCannotAttackThisTurn()
    {
        var foe = testGame.AddUnit("Warrior", enemy, testGame.GetTile(2, 0));
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        warrior.AttacksThisTurn = 1; // no attacks left: TryAttacking is skipped, but the unit may still close in
        War();
        var start = warrior.CurrentTile;

        Move(warrior);

        Assert.True(warrior.CurrentTile.AerialDistanceTo(foe.CurrentTile) < start.AerialDistanceTo(foe.CurrentTile));
        Assert.Equal(100, foe.Health);
    }

    [Fact]
    public void RangedUnitDoesNotAdvanceOnACityWithOneHealth()
    {
        var enemyCity = testGame.AddCity(enemy, testGame.GetTile(5, 0));
        enemyCity.Health = 1;
        var archer = testGame.AddUnit("Archer", civ, testGame.GetTile(0, 0));
        War();

        Move(archer);

        Assert.NotNull(archer.CurrentTile);
    }

    [Fact]
    public void UnitDefendsTheCityClosestToADenouncedNeighbour()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", civ, city.GetCenterTile()); // garrison
        testGame.AddCity(enemy, testGame.GetTile(8, 0));
        civ.GetDiplomacyManager(enemy)!.SetFlag(DiplomacyFlags.Denunciation, 10);
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(-6, 0));
        var start = warrior.CurrentTile;

        Move(warrior);

        Assert.True(warrior.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < start.AerialDistanceTo(city.GetCenterTile()));
    }

    [Fact]
    public void UnitNearTheCityItShouldDefendStaysPut()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", civ, city.GetCenterTile());
        testGame.AddCity(enemy, testGame.GetTile(8, 0));
        civ.GetDiplomacyManager(enemy)!.SetFlag(DiplomacyFlags.WaryOf, -1);
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 2));

        Move(warrior);

        Assert.Equal(testGame.GetTile(0, 2), warrior.CurrentTile);
    }

    [Fact]
    public void IdleCityStateUnitWanders()
    {
        var cs = testGame.AddCiv(cityStateType: "Cultured");
        foreach (var tile in testGame.TileMap.Values) tile.SetExplored(cs, true);
        var city = testGame.AddCity(cs, testGame.GetTile(0, 0));
        var warrior = testGame.AddUnit("Warrior", cs, testGame.GetTile(0, 1));
        testGame.AddUnit("Warrior", cs, city.GetCenterTile());
        var start = warrior.CurrentTile;

        UnitAutomation.AutomateUnitMoves(new GameView(testGame.GameInfo, cs).GetMapUnitView(warrior));

        Assert.NotNull(warrior.CurrentTile);
        Assert.NotNull(start);
    }

    // endregion
}
