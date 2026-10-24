using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Map;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Core.Tests.Logic.Automation;

// T090. BattleHelper.kt, HeadTowardsEnemyCityAutomation.kt and UseGoldAutomation.kt have no Kotlin test fixture: these are C#-only cases with
// expected values derived by hand from the Kotlin source.
[Collection("RulesetCache")]
public partial class UnitAutomationTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject civ;
    private readonly CivilizationObject enemy;

    public UnitAutomationTests()
    {
        testGame.GameInfo.EngineOptions.VisibleMap = true;
        testGame.MakeHexagonalMap(12, Constants.Grassland);
        civ = testGame.AddCiv(["Enables embarkation for land units"]);
        enemy = testGame.AddCiv();
        civ.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
    }

    private void War() => civ.GetDiplomacyManager(enemy)!.DeclareWar();

    private MapUnitView ViewOf(MapUnitClass unit) => new GameView(testGame.GameInfo, unit.Civ).GetMapUnitView(unit);

    // region HeadTowardsEnemyCityAutomation

    [Fact]
    public void EnemyCityPriorityIsEmptyWithoutWarOrCapital()
    {
        var unit = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        testGame.AddCity(enemy, testGame.GetTile(5, 0));
        Assert.Empty(HeadTowardsEnemyCityAutomation.GetEnemyCitiesByPriority(ViewOf(unit))); // at peace
        War();
        Assert.Empty(HeadTowardsEnemyCityAutomation.GetEnemyCitiesByPriority(ViewOf(unit))); // we have no capital to measure from
    }

    [Fact]
    public void EnemyCitiesAreSortedByDistanceToTheClosestOneAndFarOnesDropped()
    {
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        var unit = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 1));
        var near = testGame.AddCity(enemy, testGame.GetTile(4, 0));
        var mid = testGame.AddCity(enemy, testGame.GetTile(4, 7)); // 7 from near
        testGame.AddCity(enemy, testGame.GetTile(-8, 4)); // 12 from near: dropped
        War();

        var cities = HeadTowardsEnemyCityAutomation.GetEnemyCitiesByPriority(ViewOf(unit)).ToList();

        Assert.Equal([near, mid], cities);
    }

    [Fact]
    public void RangedUnitsSkipCapturableCities()
    {
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        var archer = testGame.AddUnit("Archer", civ, testGame.GetTile(0, 1));
        var near = testGame.AddCity(enemy, testGame.GetTile(4, 0));
        var mid = testGame.AddCity(enemy, testGame.GetTile(4, 5));
        near.Health = 1;

        War();

        Assert.Equal([mid], HeadTowardsEnemyCityAutomation.GetEnemyCitiesByPriority(ViewOf(archer)).ToList());
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 2));
        Assert.Equal([near, mid], HeadTowardsEnemyCityAutomation.GetEnemyCitiesByPriority(ViewOf(warrior)).ToList());
    }

    [Fact]
    public void TryHeadTowardsEnemyCityNeedsOwnCitiesAndAReachableTarget()
    {
        var unit = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        Assert.False(HeadTowardsEnemyCityAutomation.TryHeadTowardsEnemyCity(ViewOf(unit))); // no cities of ours
        testGame.AddCity(civ, testGame.GetTile(-6, 0));
        Assert.False(HeadTowardsEnemyCityAutomation.TryHeadTowardsEnemyCity(ViewOf(unit))); // no enemy city
    }

    [Fact]
    public void FarAwayMeleeUnitMovesAlongTheShortestPath()
    {
        testGame.AddCity(civ, testGame.GetTile(-6, 0));
        var enemyCity = testGame.AddCity(enemy, testGame.GetTile(8, 0));
        var unit = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        War();
        var start = unit.CurrentTile;

        Assert.True(HeadTowardsEnemyCityAutomation.TryHeadTowardsEnemyCity(ViewOf(unit)));

        Assert.NotEqual(start, unit.CurrentTile);
        Assert.True(unit.CurrentTile.AerialDistanceTo(enemyCity.GetCenterTile()) < start.AerialDistanceTo(enemyCity.GetCenterTile()));
    }

    [Fact]
    public void WeakArmyNearTheCityHeadsForTheLandingGroundsInsteadOfTheCity()
    {
        testGame.AddCity(civ, testGame.GetTile(-6, 0));
        var enemyCity = testGame.AddCity(enemy, testGame.GetTile(5, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(5, 1));
        var unit = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        War();

        Assert.True(HeadTowardsEnemyCityAutomation.TryHeadTowardsEnemyCity(ViewOf(unit)));

        // within [3, 5] tiles of the city, i.e. it stops short of the city
        var distance = unit.CurrentTile.AerialDistanceTo(enemyCity.GetCenterTile());
        Assert.InRange(distance, 3, 5);
    }

    [Fact]
    public void LongRangeUnitHoldsPositionWhenAlreadyInFireRange()
    {
        var enemyCity = testGame.AddCity(enemy, testGame.GetTile(8, 0));
        var spot = testGame.GetTile(5, 0); // 3 tiles away: in the range of Artillery (3), outside the city's bombard range
        var artillery = testGame.AddUnit("Artillery", civ, spot);
        War();
        Assert.True(artillery.GetRange() > enemyCity.GetBombardRange());

        Assert.True(HeadTowardsEnemyCityAutomation.HeadTowardsEnemyCity(ViewOf(artillery), enemyCity.GetCenterTile(), []));

        Assert.Equal(spot, artillery.CurrentTile);
        Assert.Equal(artillery.GetMaxMovement(), artillery.CurrentMovement);
    }

    [Fact]
    public void LongRangeUnitMovesToATileInRangeThatTheCityCannotBombard()
    {
        var enemyCity = testGame.AddCity(enemy, testGame.GetTile(8, 0));
        var artillery = testGame.AddUnit("Artillery", civ, testGame.GetTile(0, 0));
        War();

        // the unit cannot get into fire range within this turn, but it can head that way
        Assert.True(HeadTowardsEnemyCityAutomation.HeadTowardsEnemyCity(ViewOf(artillery), enemyCity.GetCenterTile(), []));

        var distance = artillery.CurrentTile.AerialDistanceTo(enemyCity.GetCenterTile());
        Assert.InRange(distance, 3, 7);
        Assert.NotEqual(testGame.GetTile(0, 0), artillery.CurrentTile);
    }

    [Fact]
    public void LongRangeUnitWithNoCandidateTileReturnsFalse()
    {
        var enemyCity = testGame.AddCity(enemy, testGame.GetTile(8, 0));
        var artillery = testGame.AddUnit("Artillery", civ, testGame.GetTile(0, 0));
        artillery.CurrentMovement = 0f; // nothing reachable this turn except the unit's own tile, which is out of range
        War();

        Assert.False(HeadTowardsEnemyCityAutomation.HeadTowardsEnemyCity(ViewOf(artillery), enemyCity.GetCenterTile(), []));
        Assert.Equal(testGame.GetTile(0, 0), artillery.CurrentTile);
    }

    [Fact]
    public void HeadTowardsEnemyCityWithoutACityOnTheTileThrows()
    {
        var unit = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        Assert.ThrowsAny<Exception>(() => HeadTowardsEnemyCityAutomation.HeadTowardsEnemyCity(ViewOf(unit), testGame.GetTile(3, 0), [testGame.GetTile(1, 0)]));
    }

    [Fact]
    public void ForeignCityNearCapitalIsTheClosestCityAndTheFirstOnTies()
    {
        var capital = testGame.AddCity(civ, testGame.GetTile(0, 0));
        Assert.Null(NextTurnAutomation.GetForeignCityNearCapital(capital, enemy)); // no cities
        Assert.Null(NextTurnAutomation.GetForeignCityNearCapital(null, enemy));
        var first = testGame.AddCity(enemy, testGame.GetTile(3, 0));
        testGame.AddCity(enemy, testGame.GetTile(-3, 0)); // same distance: the first one stays
        testGame.AddCity(enemy, testGame.GetTile(0, 6));

        var result = NextTurnAutomation.GetForeignCityNearCapital(capital, enemy);

        Assert.Equal(new NextTurnAutomation.CityDistance(first, 3), result);
    }

    // endregion

    // region BattleHelper

    [Fact]
    public void NoEnemyNearbyMeansNoAttackAndTheUnitStillHasMovement()
    {
        var unit = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        Assert.False(BattleHelper.TryAttackNearbyEnemy(ViewOf(unit)));
        Assert.False(BattleHelper.TryAttackNearbyEnemy(ViewOf(unit), stayOnTile: true));
        Assert.True(unit.CurrentMovement > 0);
    }

    [Fact]
    public void WeakEnemyMilitaryUnitIsAttacked()
    {
        var attacker = testGame.AddUnit("Swordsman", civ, testGame.GetTile(0, 0));
        var victim = testGame.AddUnit("Scout", enemy, testGame.GetTile(1, 0));
        War();

        BattleHelper.TryAttackNearbyEnemy(ViewOf(attacker));

        Assert.Equal(1, attacker.AttacksThisTurn);
        Assert.True(victim.IsDestroyed || victim.Health < 100);
    }

    [Fact]
    public void MeleeUnitCapturesALoneSettlerAndItBecomesAWorker()
    {
        // a lone settler is worth 50 + 60 (FoundCity) + the movement bonus to a melee unit: attack (capture) it
        var attacker = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        var settler = testGame.AddUnit("Settler", enemy, testGame.GetTile(2, 0));
        War();

        BattleHelper.TryAttackNearbyEnemy(ViewOf(attacker));
        // capturing a Settler turns it into a Worker (BattleUnitCapture); the Warrior walked up to the settler's tile
        Assert.True(settler.IsDestroyed);
        Assert.Equal(["Warrior", "Worker"], civ.Units.GetCivUnits().Select(it => it.Name).ToList());
        Assert.Equal(testGame.GetTile(2, 0), attacker.CurrentTile);
    }

    [Fact]
    public void RangedUnitMovesOntoALoneCivilianInsteadOfShootingIt()
    {
        var archer = testGame.AddUnit("Archer", civ, testGame.GetTile(0, 0));
        var worker = testGame.AddUnit("Worker", enemy, testGame.GetTile(1, 0));
        War();

        BattleHelper.TryAttackNearbyEnemy(ViewOf(archer));

        Assert.Equal(testGame.GetTile(1, 0), archer.CurrentTile); // moved onto the tile (capturing the worker) rather than attacking
        Assert.Equal(civ, worker.Civ);
    }

    [Fact]
    public void DisembarkingNeedsAnEmbarkedMeleeLandUnit()
    {
        var landMelee = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        Assert.False(BattleHelper.TryDisembarkUnitToAttackPosition(landMelee)); // not embarked
        var archer = testGame.AddUnit("Archer", civ, testGame.GetTile(0, 1));
        Assert.False(BattleHelper.TryDisembarkUnitToAttackPosition(archer)); // not melee
        testGame.SetTileTerrain(new HexCoord(0, 4), Constants.Coast);
        var ship = testGame.AddUnit("Trireme", civ, testGame.GetTile(0, 4));
        Assert.False(BattleHelper.TryDisembarkUnitToAttackPosition(ship)); // not a land unit
    }

    [Fact]
    public void EmbarkedMeleeUnitCannotDisembarkToAttackBecauseLandingUsesAllMovement()
    {
        testGame.SetTileTerrain(new HexCoord(0, 0), Constants.Coast);
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        var scout = testGame.AddUnit("Scout", enemy, testGame.GetTile(1, 0));
        War();
        Assert.True(warrior.IsEmbarked());
        Assert.Contains(scout.CurrentTile, civ.ViewableTiles);

        // the only attack position with movement left is the unit's own (water) tile, which the isLand filter rejects (Kotlin behaviour)
        Assert.False(BattleHelper.TryDisembarkUnitToAttackPosition(warrior));
        Assert.Equal(testGame.GetTile(0, 0), warrior.CurrentTile);
    }


    /// <summary>Health at which the city's retaliation still does not kill the unit (passes the first filter) but twice that damage would.</summary>
    private static int ThresholdHealth(MapUnitClass unit, CityObject city)
    {
        var damage = Unciv.Logic.Battle.BattleDamage.CalculateDamageToAttacker(new Unciv.Logic.Battle.MapUnitCombatant(unit), new Unciv.Logic.Battle.CityCombatant(city));
        Assert.True(damage > 1);
        return damage + 1;
    }

    private CityObject EnemyCityNextTo(int x, int y, int health = 200)
    {
        var city = testGame.AddCity(enemy, testGame.GetTile(x, y));
        city.Health = health;
        return city;
    }

    [Fact]
    public void MeleeUnitCapturesACityWithOneHitPointLeft()
    {
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        var city = EnemyCityNextTo(1, 0, health: 1);
        War();

        Assert.True(BattleHelper.TryAttackNearbyEnemy(ViewOf(warrior)));

        Assert.Equal(civ, city.Civ);
    }

    [Fact]
    public void RangedUnitIgnoresACityItCouldOnlyCaptureAndMeleeUnitCapturesIt()
    {
        var archer = testGame.AddUnit("Archer", civ, testGame.GetTile(0, 0));
        EnemyCityNextTo(1, 0, health: 1);
        War();

        // a ranged unit cannot capture a city: value 0 < the threshold, nothing is attacked and the movement is kept
        Assert.False(BattleHelper.TryAttackNearbyEnemy(ViewOf(archer)));
        Assert.True(archer.CurrentMovement > 0);
        Assert.Equal(0, archer.AttacksThisTurn);
    }

    [Fact]
    public void MeleeUnitCapturesWhenTheBlowWouldKillTheCityOtherwiseAttacksByStrength()
    {
        var swordsman = testGame.AddUnit("Swordsman", civ, testGame.GetTile(0, 0));
        var city = EnemyCityNextTo(1, 0, health: 5); // one hit finishes it: value 10000
        War();

        BattleHelper.TryAttackNearbyEnemy(ViewOf(swordsman));

        Assert.Equal(civ, city.Civ);
    }

    [Fact]
    public void CatapultAttacksACityEvenIfItIsStrong()
    {
        var catapult = testGame.AddUnit("Catapult", civ, testGame.GetTile(0, 0));
        var city = EnemyCityNextTo(2, 0);
        War();

        BattleHelper.TryAttackNearbyEnemy(ViewOf(catapult));

        Assert.True(city.Health < 200); // siege units get +100
        Assert.Equal(1, catapult.AttacksThisTurn);
    }

    [Fact]
    public void BadlyWoundedMeleeUnitDoesNotSuicideIntoAStrongCityWithFewFriendsAround()
    {
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        var city = EnemyCityNextTo(1, 0);
        War();
        warrior.Health = ThresholdHealth(warrior, city);

        Assert.False(BattleHelper.TryAttackNearbyEnemy(ViewOf(warrior)));

        Assert.Equal(200, city.Health);
        Assert.Equal(0, warrior.AttacksThisTurn);
    }

    [Fact]
    public void WoundedMeleeUnitStillAttacksTheCityWithManyFriendsNearby()
    {
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        foreach (var (x, y) in new[] { (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1) })
            testGame.AddUnit("Warrior", civ, testGame.GetTile(x, y));
        var city = EnemyCityNextTo(1, 0);
        War();
        warrior.Health = ThresholdHealth(warrior, city);

        BattleHelper.TryAttackNearbyEnemy(ViewOf(warrior));

        Assert.True(warrior.AttacksThisTurn > 0 || warrior.IsDestroyed); // 5 other units nearby: the value is positive despite the risk
    }

    [Fact]
    public void CityValueCountsUnitsAroundTheCity()
    {
        // enemies of the city (at war with its owner) around it raise the value, units of the attacker's enemies lower it:
        // either way the wounded-city catapult still fires; this walks the "units around the city" loop with both kinds of neighbour
        var catapult = testGame.AddUnit("Catapult", civ, testGame.GetTile(0, 0));
        var city = EnemyCityNextTo(2, 0, health: 80);
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(2, 1));
        testGame.AddUnit("Warrior", civ, testGame.GetTile(1, 1));
        War();

        BattleHelper.TryAttackNearbyEnemy(ViewOf(catapult));

        Assert.True(city.Health < 80);
    }

    [Fact]
    public void SelfDestructingUnitAttacksAnythingEvenIfItDies()
    {
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        var missile = testGame.AddUnit("Guided Missile", civ, testGame.GetTile(0, 0));
        var victim = testGame.AddUnit("Warrior", enemy, testGame.GetTile(3, 0));
        War();

        BattleHelper.TryAttackNearbyEnemy(ViewOf(missile));

        Assert.True(victim.IsDestroyed || victim.Health < 100);
    }

    [Fact]
    public void UnitThatCannotAttackNeverAttacks()
    {
        var worker = testGame.AddUnit("Worker", civ, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(1, 0));
        War();
        Assert.False(BattleHelper.TryAttackNearbyEnemy(ViewOf(worker)));
        Assert.Equal(0, worker.AttacksThisTurn);
    }

    [Fact]
    public void RangedUnitDoesNotShootACivilianItCannotReachButCapturableOnes()
    {
        // an archer on a hill is not allowed to leave: the unreachable, capturable worker is "not worth a shot" (value 10 <= 30)
        var archer = testGame.AddUnit("Archer", civ, testGame.GetTile(0, 0));
        archer.CurrentMovement = 0.5f;
        var worker = testGame.AddUnit("Worker", enemy, testGame.GetTile(2, 0));
        War();

        BattleHelper.TryAttackNearbyEnemy(ViewOf(archer));

        Assert.Equal(enemy, worker.Civ);
        Assert.Equal(0, archer.AttacksThisTurn);
    }

    // endregion

    // region UseGoldAutomation + UnitAutomation.tryUpgradeUnit

    [Fact]
    public void TryUpgradeUnitDoesNothingWithoutAnAvailableUpgrade()
    {
        var unit = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        Assert.False(UnitAutomation.TryUpgradeUnit(ViewOf(unit)));
        Assert.Equal("Warrior", unit.Name);
    }

    [Fact]
    public void TryUpgradeUnitUpgradesToTheCheapestAvailableUpgrade()
    {
        civ.Tech.TechsResearched.Add("Machinery");
        civ.Tech.TechsResearched.Add("Construction");
        civ.AddGold(5000);
        testGame.AddCity(civ, testGame.GetTile(5, 5));
        var archer = testGame.AddUnit("Archer", civ, testGame.GetTile(5, 5)); // upgrading needs friendly territory
        civ.Cache.UpdateState();
        var view = ViewOf(archer);
        var expected = view.GetPossibleUpgrades()
            .Where(it => it.Availability == Unciv.Logic.Map.MapUnits.UpgradeAvailability.Available)
            .MinBy(it => it.UpgradedUnit.Cost);
        Assert.NotNull(expected);

        Assert.True(UnitAutomation.TryUpgradeUnit(view));

        Assert.Single(civ.Units.GetCivUnits(), it => it.Name == expected.UpgradedUnit.Name);
    }

    [Fact]
    public void TryUpgradeUnitDoesNotSpendResourcesTheCivDoesNotWillingToSpend()
    {
        // Swordsman needs Iron, which nobody owns: the upgrade is not Available, so nothing happens
        civ.Tech.TechsResearched.Add("Iron Working");
        civ.AddGold(5000);
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        Assert.False(UnitAutomation.TryUpgradeUnit(ViewOf(warrior)));
    }

    [Fact]
    public void UseGoldWithNothingToBuyChangesNothing()
    {
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        civ.AddGold(100);
        UseGoldAutomation.UseGold(civ);
        // turns < scienceCostModifier * 20 in a fresh game: no tile buying, no unit upgrade, no city states
        Assert.Equal(100, civ.Gold);
    }

    [Fact]
    public void UseGoldBuysTheCurrentConstructionWhenRichAndTheItemIsNotStarted()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        city.CityConstructions.AddToQueue("Warrior");
        civ.AddGold(100_000);

        UseGoldAutomation.UseGold(civ);

        Assert.True(civ.Gold < 100_000);
        Assert.Contains(civ.Units.GetCivUnits(), it => it.Name == "Warrior");
    }

    [Fact]
    public void UseGoldDoesNotBuyConstructionsThatAlreadyHaveProductionInvested()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        city.CityConstructions.AddToQueue("Warrior");
        city.CityConstructions.AddProductionPoints(1);
        civ.AddGold(100_000);

        UseGoldAutomation.UseGold(civ);

        Assert.Equal(100_000, civ.Gold);
    }

    [Fact]
    public void UseGoldBuysDesirableCityTilesOnceTheGameIsOldEnough()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        var ring = testGame.GetTile(2, 0);
        testGame.SetTileTerrain(ring.Position, Constants.Plains);
        ring.SetTileResource("Wheat"); // plains 1/1 + wheat +1 food: 3 food+production, a high-yield tile
        civ.AddGold(100_000);
        Assert.True(ring.GetOwner() == null && ring.IsVisible(civ));

        UseGoldAutomation.UseGold(civ); // turn 0 of the game: too early to buy tiles
        Assert.Null(ring.GetOwner());

        testGame.GameInfo.Turns = 1000;
        UseGoldAutomation.UseGold(civ);

        Assert.Equal(civ, ring.GetOwner());
        Assert.Same(city, ring.GetCity() ?? ring.OwningCity);
        Assert.True(civ.Gold < 100_000);
    }

    [Fact]
    public void UseGoldRevertsTilePurchasesItCannotAfford()
    {
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        var ring = testGame.GetTile(2, 0);
        testGame.SetTileTerrain(ring.Position, Constants.Plains);
        ring.SetTileResource("Wheat");
        testGame.GameInfo.Turns = 1000;
        civ.AddGold(1); // positive, but less than the tile costs

        UseGoldAutomation.UseGold(civ);

        Assert.Null(ring.GetOwner());
        Assert.Equal(1, civ.Gold);
    }

    [Fact]
    public void UseGoldDoesNothingForTilesWithoutYieldsOrWhenBroke()
    {
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        testGame.GameInfo.Turns = 1000;
        civ.AddGold(100_000);
        var owned = testGame.TileMap.Values.Count(it => it.GetOwner() != null);

        UseGoldAutomation.UseGold(civ); // plain grassland (2 food) everywhere: nothing is highly desirable

        Assert.Equal(owned, testGame.TileMap.Values.Count(it => it.GetOwner() != null));
        Assert.Equal(100_000, civ.Gold);
    }


    private sealed class FixedAllianceValue(int value) : Unciv.Logic.Civilization.Managers.INextTurnAutomation
    {
        public void AutomateGoldToSciencePercentage(CivilizationObject civInfo) { }
        public void AutomateCityBombardment(CivilizationObject civInfo) { }
        public void ChooseGreatPerson(CivilizationObject civInfo) { }
        public void AutomateCivMoves(CivilizationObject civInfo) { }
        public int ValueCityStateAlliance(CivilizationObject civInfo, CivilizationObject cityState, bool includeQuests = false) => value;
    }

    private CivilizationObject CityStateWithInfluence(float influence, int allianceValue = 10)
    {
        testGame.GameInfo.ClientState.NextTurnAutomation = new FixedAllianceValue(allianceValue);
        var cityState = testGame.AddCiv(cityStateType: "Mercantile");
        testGame.AddCity(cityState, testGame.GetTile(8, 8));
        testGame.AddCity(civ, testGame.GetTile(-8, -8)); // a civ without cities counts as defeated and cannot be an ally
        civ.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        cityState.GetDiplomacyManager(civ)!.SetInfluence(influence);
        cityState.CityStateFunctions.UpdateAllyCivForCityState();
        civ.AddGold(-civ.Gold); // meeting a city-state may pay a bonus: start every test from zero
        return cityState;
    }

    [Fact]
    public void UseGoldGiftsGoldToAValuableCityStateWeAreFriendsWith()
    {
        var cityState = CityStateWithInfluence(30);
        civ.AddGold(600);

        UseGoldAutomation.UseGold(civ);

        Assert.Equal(100, civ.Gold); // 500 gift, no purchases
        Assert.True(cityState.GetDiplomacyManager(civ)!.GetInfluence() > 30);
    }

    [Fact]
    public void UseGoldGiftsAThousandWhenRich()
    {
        var cityState = CityStateWithInfluence(30);
        civ.AddGold(1200);

        UseGoldAutomation.UseGold(civ);

        Assert.Equal(200, civ.Gold);
        Assert.True(cityState.GetDiplomacyManager(civ)!.GetInfluence() > 30);
    }

    [Theory]
    [InlineData(5f, 10)] // too little influence to bother
    [InlineData(90f, 10)] // above 60 + 2 * value: be content
    [InlineData(30f, 0)] // an alliance with no value is not pursued
    public void UseGoldSavesItsGoldWhenTheCityStateIsNotWorthIt(float influence, int allianceValue)
    {
        var cityState = CityStateWithInfluence(influence, allianceValue);
        civ.AddGold(600);

        UseGoldAutomation.UseGold(civ);

        Assert.Equal(600, civ.Gold);
        Assert.Equal(influence, cityState.GetDiplomacyManager(civ)!.GetInfluence());
    }

    [Fact]
    public void UseGoldWithoutAnAutomationHookValuesAlliancesAtZero()
    {
        var cityState = CityStateWithInfluence(30);
        testGame.GameInfo.ClientState.NextTurnAutomation = null;
        civ.AddGold(600);

        UseGoldAutomation.UseGold(civ);

        Assert.Equal(600, civ.Gold);
        Assert.Equal(30f, cityState.GetDiplomacyManager(civ)!.GetInfluence());
    }

    [Fact]
    public void UseGoldDoesNotGiftToACityStateThatIsAlreadyOurAlly()
    {
        var cityState = CityStateWithInfluence(100);
        civ.AddGold(600);
        Assert.Equal(civ, cityState.AllyCiv);

        UseGoldAutomation.UseGold(civ);

        Assert.Equal(600, civ.Gold);
    }

    [Fact]
    public void UseGoldDoesNotGiftWhenGoldIsBelowTheThreshold()
    {
        CityStateWithInfluence(30);
        civ.AddGold(400);
        UseGoldAutomation.UseGold(civ);
        Assert.Equal(400, civ.Gold);
    }

    [Fact]
    public void UseGoldMarriesAnAlliedCityStateWhenTheCivCanBuyCityStates()
    {
        var buyer = testGame.AddCiv(["Can spend Gold to annex or puppet a City-State that has been your Ally for [5] turns"]);
        testGame.AddCity(buyer, testGame.GetTile(-8, -8));
        testGame.GameInfo.ClientState.NextTurnAutomation = new FixedAllianceValue(0);
        var cityState = testGame.AddCiv(cityStateType: "Mercantile");
        var csCity = testGame.AddCity(cityState, testGame.GetTile(8, 8));
        buyer.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        cityState.GetDiplomacyManager(buyer)!.SetInfluence(100);
        cityState.CityStateFunctions.UpdateAllyCivForCityState();
        buyer.AddGold(-buyer.Gold);
        buyer.AddGold(5000);
        Assert.True(buyer.GetHappiness() > 0);
        Assert.Equal(buyer, cityState.AllyCiv);
        Assert.True(buyer.GetMatchingUniques(Unciv.Models.Ruleset.Unique.UniqueType.CityStateCanBeBoughtForGold).Any());
        buyer.GetDiplomacyManager(cityState)!.RemoveFlag(Unciv.Logic.Civilization.Diplomacy.DiplomacyFlags.MarriageCooldown); // set when the city-state became our ally
        Assert.False(cityState.IsDefeated());
        Assert.True(cityState.GetDiplomacyManager(buyer)!.IsRelationshipLevelEQ(Unciv.Logic.Civilization.Diplomacy.RelationshipLevel.Ally));
        Assert.False(buyer.GetDiplomacyManager(cityState)!.HasFlag(Unciv.Logic.Civilization.Diplomacy.DiplomacyFlags.MarriageCooldown));
        Assert.True(buyer.Gold >= ((Unciv.Logic.Civilization.Diplomacy.CityStateFunctions)cityState.CityStateFunctions).GetDiplomaticMarriageCost());
        Assert.True(((Unciv.Logic.Civilization.Diplomacy.CityStateFunctions)cityState.CityStateFunctions).CanBeMarriedBy(buyer));

        UseGoldAutomation.UseGold(buyer);

        Assert.Equal(buyer, csCity.Civ);
        Assert.True(buyer.Gold < 5000);
    }

    // endregion
}
