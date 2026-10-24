using Unciv.Core.Tests.Support;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using BattleObject = Unciv.Logic.Battle.Battle;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Core.Tests.Logic.Battle;

// Port of tests/src/com/unciv/logic/battle/BattleTest.kt (T083), plus the C#-only cases for the Battle and Nuke branches the Kotlin tests miss.
// The Kotlin test is parameterized over the pathfinding algorithm; the A* branches of UnitMovement are TODO(T093), so only the default runs.
[Collection("RulesetCache")]
public class BattleTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject attackerCiv;
    private readonly CivilizationObject defenderCiv;
    private readonly MapUnitClass defaultAttackerUnit;
    private readonly MapUnitClass defaultDefenderUnit;

    public BattleTests()
    {
        testGame.MakeHexagonalMap(4);
        attackerCiv = testGame.AddCiv();
        defenderCiv = testGame.AddCiv();

        defaultAttackerUnit = testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(1, 0));
        defaultAttackerUnit.CurrentMovement = 2f;
        defaultDefenderUnit = testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(HexCoord.Zero));
        defaultDefenderUnit.CurrentMovement = 2f;
    }

    private static DamageDealt Attack(MapUnitClass attacker, MapUnitClass defender) =>
        BattleObject.Attack(new MapUnitCombatant(attacker), new MapUnitCombatant(defender));

    private DamageDealt AttackNuke(MapUnitClass nuke, Unciv.Logic.Map.Tiles.Tile target) =>
        BattleObject.AttackOrNuke(new MapUnitCombatant(nuke), new AttackableTile(nuke.GetTile(), target, 0f, null));

    [Fact]
    public void DefenderShouldWithdrawFromMeleeAttackIfHasTheUniqueToDoSo()
    {
        var defenderUnit = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(0, 1), UniqueType.WithdrawsBeforeMeleeCombat.Text());
        defenderUnit.CurrentMovement = 2f;

        var damageDealt = Attack(defaultAttackerUnit, defenderUnit);

        Assert.Equal(0, damageDealt.AttackerDealt);
        Assert.Equal(0, damageDealt.DefenderDealt);
        Assert.NotEqual(testGame.GetTile(0, 1), defenderUnit.GetTile()); // defender moves away
        Assert.Equal(testGame.GetTile(1, 0), defaultAttackerUnit.GetTile()); // attacker didn't move
        Assert.Equal(0f, defaultAttackerUnit.CurrentMovement); // warriors cannot move anymore after attacking
    }

    [Fact]
    public void BothDefenderAndAttackerShouldDoDamageWhenAreMelee()
    {
        var damageDealt = Attack(defaultAttackerUnit, defaultDefenderUnit);

        Assert.Equal(31, damageDealt.AttackerDealt);
        Assert.Equal(31, damageDealt.DefenderDealt);
        Assert.Equal(69, defaultAttackerUnit.Health);
        Assert.Equal(69, defaultDefenderUnit.Health);
    }

    [Fact]
    public void OnlyAttackerShouldDoDamageWhenHesRanged()
    {
        var attackerUnit = testGame.AddUnit("Archer", attackerCiv, testGame.GetTile(0, 1));

        var damageDealt = Attack(attackerUnit, defaultDefenderUnit);

        Assert.Equal(28, damageDealt.AttackerDealt);
        Assert.Equal(0, damageDealt.DefenderDealt);
    }

    [Fact]
    public void ShouldMoveToEnemyPositionWhenMeleeKilling()
    {
        defaultDefenderUnit.Health = 1;

        Attack(defaultAttackerUnit, defaultDefenderUnit);

        Assert.Equal(testGame.GetTile(0, 0), defaultAttackerUnit.GetTile());
        Assert.True(defaultDefenderUnit.IsDestroyed);
    }

    [Fact]
    public void ShouldStayInOriginalPositionWhenRangedKilling()
    {
        var attackerUnit = testGame.AddUnit("Archer", attackerCiv, testGame.GetTile(0, 1));
        attackerUnit.CurrentMovement = 2f;
        defaultDefenderUnit.Health = 1;

        Attack(attackerUnit, defaultDefenderUnit);

        Assert.Equal(testGame.GetTile(0, 1), attackerUnit.GetTile());
        Assert.True(defaultDefenderUnit.IsDestroyed);
    }

    [Fact]
    public void BothAttackerAndDefenderShouldEarnXp()
    {
        Attack(defaultAttackerUnit, defaultDefenderUnit);

        Assert.Equal(5, defaultAttackerUnit.Promotions.XP);
        Assert.Equal(4, defaultDefenderUnit.Promotions.XP);
    }

    [Fact]
    public void ShouldEarnXpWhenFightingBarbarianAndBelowXpCap()
    {
        var barbarianCiv = testGame.AddBarbarianCiv();
        var barbarianUnit = testGame.AddUnit("Brute", barbarianCiv, testGame.GetTile(0, 1));

        Attack(barbarianUnit, defaultDefenderUnit);

        Assert.Equal(5, barbarianUnit.Promotions.XP);
        Assert.Equal(4, defaultDefenderUnit.Promotions.XP);
    }

    [Fact]
    public void ShouldNotEarnXpWhenFightingBarbarianIfExceedingXpCap()
    {
        var barbarianCiv = testGame.AddBarbarianCiv();
        var barbarianUnit = testGame.AddUnit("Brute", barbarianCiv, testGame.GetTile(0, 1));
        defaultDefenderUnit.Promotions.XP = 35;

        Attack(barbarianUnit, defaultDefenderUnit);

        Assert.Equal(5, barbarianUnit.Promotions.XP);
        Assert.Equal(35, defaultDefenderUnit.Promotions.XP);
    }

    [Fact]
    public void AttackerShouldExpendAllMovementPointsToAttackWithoutUniques()
    {
        Attack(defaultAttackerUnit, defaultDefenderUnit);

        Assert.Equal(0f, defaultAttackerUnit.CurrentMovement);
    }

    [Fact]
    public void AttackerShouldStillHaveMovementPointsLeftWithAdditionalAttackPerTurnUnique()
    {
        var attackerUnit = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(0, 1), "[1] additional attacks per turn");
        attackerUnit.CurrentMovement = 2f;

        Attack(attackerUnit, defaultDefenderUnit);

        Assert.Equal(1f, attackerUnit.CurrentMovement);
    }

    [Fact]
    public void AttackerShouldStillHaveMovementPointsLeftWithCanMoveAfterAttackingUnique()
    {
        var attackerUnit = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(0, 1), "Can move after attacking");
        attackerUnit.CurrentMovement = 2f;

        Attack(attackerUnit, defaultDefenderUnit);

        Assert.Equal(1f, attackerUnit.CurrentMovement);
    }

    [Fact]
    public void ShouldCaptureCivilian()
    {
        var defenderUnit = testGame.AddUnit("Worker", defenderCiv, testGame.GetTile(2, 0));

        var attack = Attack(defaultAttackerUnit, defenderUnit);

        Assert.Equal(0, attack.AttackerDealt);
        Assert.Equal(0, attack.DefenderDealt);
        Assert.Equal(new HexCoord(2, 0), defaultAttackerUnit.GetTile().Position);
        Assert.Equal(attackerCiv, defaultAttackerUnit.GetTile().CivilianUnit!.Civ); // captured unit
    }

    [Fact]
    public void ShouldTransformSettlerIntoWorkerUponCapture()
    {
        var defenderUnit = testGame.AddUnit("Settler", defenderCiv, testGame.GetTile(2, 0));

        var attack = Attack(defaultAttackerUnit, defenderUnit);

        Assert.Equal(0, attack.AttackerDealt);
        Assert.Equal(0, attack.DefenderDealt);
        Assert.Equal(testGame.GetTile(2, 0), defaultAttackerUnit.GetTile());
        Assert.Equal(attackerCiv, defaultAttackerUnit.GetTile().CivilianUnit!.Civ); // captured unit
        Assert.Equal("Worker", defaultAttackerUnit.GetTile().CivilianUnit!.BaseUnit.Name);
    }

    [Fact]
    public void ShouldEarnGreatGeneralFromCombat()
    {
        Attack(defaultAttackerUnit, defaultDefenderUnit);

        Assert.Equal(5, attackerCiv.GreatPeople.GreatGeneralPointsCounter["Great General"]);
        Assert.Equal(4, defenderCiv.GreatPeople.GreatGeneralPointsCounter["Great General"]);
    }

    [Fact]
    public void ShouldNotEarnGreatGeneralFromCombatAgainstBarbarians()
    {
        var barbarianCiv = testGame.AddBarbarianCiv();
        var barbarianUnit = testGame.AddUnit("Brute", barbarianCiv, testGame.GetTile(0, 1));

        Attack(defaultAttackerUnit, barbarianUnit);

        Assert.Equal(0, attackerCiv.GreatPeople.GreatGeneralPointsCounter["Great General"]);
        Assert.Equal(0, barbarianCiv.GreatPeople.GreatGeneralPointsCounter["Great General"]);
    }

    [Fact]
    public void ShouldEarnMoreGreatGeneralPointsFromUniques()
    {
        var attackerUnit = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(0, 1), "[Great General] is earned [100]% faster");

        Attack(attackerUnit, defaultDefenderUnit);

        Assert.Equal(10, attackerCiv.GreatPeople.GreatGeneralPointsCounter["Great General"]);
    }

    [Fact]
    public void ShouldConquerCityWhenDefeatedAndMeleeAttacked()
    {
        var defenderCity = testGame.AddCity(defenderCiv, testGame.GetTile(0, 1), initialPopulation: 1);
        defenderCity.Health = 1;

        testGame.GameInfo.CurrentPlayerCiv = testGame.AddCiv(); // otherwise test crashes when puppetying city
        testGame.GameInfo.CurrentPlayer = testGame.GameInfo.CurrentPlayerCiv.CivId;

        BattleObject.Attack(new MapUnitCombatant(defaultAttackerUnit), new CityCombatant(defenderCity));

        Assert.Equal(attackerCiv, defenderCity.Civ);
        Assert.Equal(defenderCiv.CivId, defenderCity.PreviousOwner);
        Assert.Equal(defenderCiv, defenderCity.FoundingCivObject);
    }

    [Fact]
    public void ShouldNotConquerCityWhenDefeatedAndRangedAttacked()
    {
        var attackerUnit = testGame.AddUnit("Archer", attackerCiv, testGame.GetTile(0, 2));
        var defenderCity = testGame.AddCity(defenderCiv, testGame.GetTile(0, 1), initialPopulation: 1);
        defenderCity.Health = 1;

        BattleObject.Attack(new MapUnitCombatant(attackerUnit), new CityCombatant(defenderCity));

        Assert.Equal(defenderCiv, defenderCity.Civ);
        Assert.Equal(1, defenderCity.Health);
    }

    [Fact]
    public void ShouldNotConquerCityWhenUnitHasUnableToCaptureCitiesUnique()
    {
        var attackerUnit = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(0, 2), "Unable to capture cities");
        var defenderCity = testGame.AddCity(defenderCiv, testGame.GetTile(0, 1), initialPopulation: 1);
        defenderCity.Health = 1;

        BattleObject.Attack(new MapUnitCombatant(attackerUnit), new CityCombatant(defenderCity));

        Assert.Equal(defenderCiv, defenderCity.Civ);
        Assert.Equal(1, defenderCity.Health);
    }

    [Fact]
    public void ShouldNotGainXpWhenAttackingDefeatedCity()
    {
        var attackerUnit = testGame.AddUnit("Archer", attackerCiv, testGame.GetTile(0, 2));
        var defenderCity = testGame.AddCity(defenderCiv, testGame.GetTile(0, 1), initialPopulation: 1);
        defenderCity.Health = 1;

        BattleObject.Attack(new MapUnitCombatant(attackerUnit), new CityCombatant(defenderCity));

        Assert.Equal(0, attackerUnit.Promotions.XP);
    }

    [Fact]
    public void ShouldEarnPrizesWhenDefeatingUnits()
    {
        var attackerPolicy = testGame.CreatePolicy(
            "Earn [100]% of killed [Military] unit's [Strength] as [Culture]",
            "Earn [100]% of killed [Military] unit's [Strength] as [Gold]");
        attackerCiv.Policies.Adopt(attackerPolicy, true);

        defaultDefenderUnit.Health = 1;

        Attack(defaultAttackerUnit, defaultDefenderUnit);

        Assert.Equal(8, attackerCiv.Gold);
        Assert.Equal(8, attackerCiv.Policies.StoredCulture);
    }

    [Fact]
    public void ShouldHealWhenDefeatingUnitsIfHasUnique()
    {
        var attackerUnit = testGame.AddDefaultRangedUnitWithUniques(attackerCiv, testGame.GetTile(0, 1), "Heals [10] damage if it kills a unit");
        attackerUnit.Health = 90;
        attackerUnit.CurrentMovement = 2f;

        defaultDefenderUnit.Health = 1;

        Attack(attackerUnit, defaultDefenderUnit);

        Assert.Equal(100, attackerUnit.Health);
    }

    [Fact]
    public void ShouldDeclareWarWhenNukingNeutralCivs()
    {
        var thirdCiv = testGame.AddCiv();
        testGame.AddUnit("Warrior", thirdCiv, testGame.GetTile(0, 2));

        testGame.AddCity(attackerCiv, testGame.GetTile(0, 1));
        var attackerUnit = testGame.AddUnit("Atomic Bomb", attackerCiv, testGame.GetTile(0, 1));

        attackerCiv.DiplomacyFunctions.MakeCivilizationsMeet(defenderCiv);
        attackerCiv.DiplomacyFunctions.MakeCivilizationsMeet(thirdCiv);
        Assert.Equal(DiplomaticStatus.Peace, attackerCiv.GetDiplomacyManager(defenderCiv)!.DiplomaticStatus);
        Assert.Equal(DiplomaticStatus.Peace, attackerCiv.GetDiplomacyManager(thirdCiv)!.DiplomaticStatus);

        AttackNuke(attackerUnit, defaultDefenderUnit.CurrentTile);

        Assert.Equal(DiplomaticStatus.War, attackerCiv.GetDiplomacyManager(defenderCiv)!.DiplomaticStatus);
        Assert.Equal(DiplomaticStatus.War, attackerCiv.GetDiplomacyManager(thirdCiv)!.DiplomaticStatus);
    }

    [Fact]
    public void ShouldGiveDiplomacyPenaltyForUsingANuke()
    {
        var thirdCiv = testGame.AddCiv();
        testGame.AddUnit("Warrior", thirdCiv, testGame.GetTile(0, -3)); // need unit or civ is considered defeated

        testGame.AddCity(attackerCiv, testGame.GetTile(0, 1));
        var attackerUnit = testGame.AddUnit("Atomic Bomb", attackerCiv, testGame.GetTile(0, 1));

        attackerCiv.DiplomacyFunctions.MakeCivilizationsMeet(defenderCiv);
        attackerCiv.DiplomacyFunctions.MakeCivilizationsMeet(thirdCiv);

        AttackNuke(attackerUnit, defaultDefenderUnit.CurrentTile);

        Assert.Equal(-75f, defenderCiv.GetDiplomacyManager(attackerCiv)!.OpinionOfOtherCiv()); // 50 for nuke, 25 for war declaration
        Assert.Equal(-55f, thirdCiv.GetDiplomacyManager(attackerCiv)!.OpinionOfOtherCiv()); // 50 for nuke, 5 for warmongering
    }

    [Fact]
    public void ShouldAlwaysDestroyUnitDirectlyHitByNuke()
    {
        var megaWarrior = testGame.CreateBaseUnit("Sword");
        megaWarrior.Strength = 1_000_000;
        var defenderUnit = testGame.AddUnit(megaWarrior.Name, defenderCiv, testGame.GetTile(0, 1));

        testGame.AddCity(attackerCiv, testGame.GetTile(0, 1));
        var attackerUnit = testGame.AddUnit("Atomic Bomb", attackerCiv, testGame.GetTile(0, 1));
        attackerCiv.ResourceStockpiles["Uranium"] = 1;

        AttackNuke(attackerUnit, defenderUnit.CurrentTile);

        Assert.True(defenderUnit.IsDestroyed);
    }

    [Fact]
    public void ShouldDamageAllUnitsInBlastRadius()
    {
        var thirdCiv = testGame.AddCiv();
        var thirdCivUnit = testGame.AddUnit("Warrior", thirdCiv, testGame.GetTile(0, 2));
        var defenderUnit = testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(0, 1));

        testGame.AddCity(attackerCiv, testGame.GetTile(0, 1));
        var attackerUnit = testGame.AddUnit("Atomic Bomb", attackerCiv, testGame.GetTile(0, 1));

        AttackNuke(attackerUnit, defaultDefenderUnit.CurrentTile);

        Assert.True(defenderUnit.Health < 100);
        Assert.True(thirdCivUnit.Health < 100);
        Assert.True(defaultAttackerUnit.Health < 100); // even attacker's own units
        Assert.True(defaultDefenderUnit.Health < 100);
    }

    [Fact]
    public void ShouldKillPeopleInCity()
    {
        var defenderCity = testGame.AddCity(defenderCiv, testGame.GetTile(2, 0), initialPopulation: 10);

        testGame.AddCity(attackerCiv, testGame.GetTile(0, 1));
        var attackerUnit = testGame.AddUnit("Atomic Bomb", attackerCiv, testGame.GetTile(0, 1));

        AttackNuke(attackerUnit, defenderCity.GetCenterTile());

        Assert.InRange(defenderCity.Population.Population, 3, 7); // there is some randomness in population killed
    }

    [Fact]
    public void ShouldKillFewerPeopleInCityWithBombShelter()
    {
        var defenderCity = testGame.AddCity(defenderCiv, testGame.GetTile(2, 0), initialPopulation: 10);
        var building = testGame.CreateBuilding("Population loss from nuclear attacks [-100]% [in this city]");
        defenderCity.CityConstructions.AddBuilding(building.Name);

        testGame.AddCity(attackerCiv, testGame.GetTile(0, 1));
        var attackerUnit = testGame.AddUnit("Atomic Bomb", attackerCiv, testGame.GetTile(0, 1));

        AttackNuke(attackerUnit, defenderCity.GetCenterTile());

        Assert.Equal(10, defenderCity.Population.Population);
    }

    [Fact]
    public void ShouldNotDestroyCityWithNuclearMissileWhenCapital()
    {
        var defenderCity = testGame.AddCity(defenderCiv, testGame.GetTile(2, 0), initialPopulation: 1);

        testGame.AddCity(attackerCiv, testGame.GetTile(0, 1));
        var attackerUnit = testGame.AddUnit("Nuclear Missile", attackerCiv, testGame.GetTile(0, 1));

        AttackNuke(attackerUnit, defenderCity.GetCenterTile());

        Assert.True(testGame.GetTile(2, 0).IsCityCenter());
    }

    [Fact]
    public void ShouldDestroyCityWithNuclearMissileWhenNotCapitalAndBelowPopulationThreshold()
    {
        testGame.AddCity(defenderCiv, testGame.GetTile(2, 0), initialPopulation: 1); // capital
        var nonCapitalDefenderCity = testGame.AddCity(defenderCiv, testGame.GetTile(3, 0), initialPopulation: 1);

        testGame.AddCity(attackerCiv, testGame.GetTile(0, 1));
        var attackerUnit = testGame.AddUnit("Nuclear Missile", attackerCiv, testGame.GetTile(0, 1));

        AttackNuke(attackerUnit, nonCapitalDefenderCity.GetCenterTile());

        Assert.False(testGame.GetTile(3, 0).IsCityCenter());
        Assert.Contains("Fallout", testGame.GetTile(3, 0).TerrainFeatures);
    }

    [Fact]
    public void ShouldNotDestroyCityWithNuclearMissileWhenNotCapitalAndAbovePopulationThreshold()
    {
        testGame.AddCity(defenderCiv, testGame.GetTile(2, 0), initialPopulation: 1); // capital
        var nonCapitalDefenderCity = testGame.AddCity(defenderCiv, testGame.GetTile(3, 0), initialPopulation: 7);

        testGame.AddCity(attackerCiv, testGame.GetTile(0, 1));
        var attackerUnit = testGame.AddUnit("Nuclear Missile", attackerCiv, testGame.GetTile(0, 1));

        AttackNuke(attackerUnit, nonCapitalDefenderCity.GetCenterTile());

        Assert.True(testGame.GetTile(3, 0).IsCityCenter());
    }

    [Fact]
    public void ShouldConsumeNukeOnUsage()
    {
        testGame.AddCity(attackerCiv, testGame.GetTile(0, 1));
        var attackerUnit = testGame.AddUnit("Atomic Bomb", attackerCiv, testGame.GetTile(0, 1));

        AttackNuke(attackerUnit, defaultDefenderUnit.GetTile());

        Assert.True(attackerUnit.IsDestroyed);
    }

    [Fact]
    public void ShouldTriggerDamageTriggersWhenRangedAttacking()
    {
        var unitType = testGame.CreateBaseUnit("Archery", "[This Unit] takes [5] damage <upon damaging a [Test] unit>");
        unitType.RangedStrength = 10;
        var attackerUnit = testGame.AddUnit(unitType.Name, attackerCiv, testGame.GetTile(0, 1));
        attackerUnit.CurrentMovement = 2f;
        defaultDefenderUnit.Health = 1;

        defaultDefenderUnit.SetStatus("Test", 1);
        Attack(attackerUnit, defaultDefenderUnit);

        Assert.Equal(95, attackerUnit.Health);
        Assert.True(defaultDefenderUnit.IsDestroyed);
    }

    [Fact]
    public void PerformExtraRangedAttackIfUniqueIsPresent()
    {
        var attackerUnit = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(0, 1),
            "Before engaging in combat performs an extra ranged attack with [50]% of melee combat strength");

        var damageDealt = Attack(attackerUnit, defaultDefenderUnit);

        Assert.Equal(50, damageDealt.AttackerDealt); // Attacker deals damage from both the ranged attack and melee attack
        Assert.Equal(27, damageDealt.DefenderDealt);
        Assert.Equal(73, attackerUnit.Health);
        Assert.Equal(50, defaultDefenderUnit.Health);
    }

    [Fact]
    public void ExtraRangedAttackVsBarbarians()
    {
        var attackerUnit = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(0, 1),
            "Before engaging in combat performs an extra ranged attack with [50]% of melee combat strength");
        attackerUnit.Promotions.XP = 28;
        var barbarianCiv = testGame.AddBarbarianCiv();
        var barbarianUnit = testGame.AddUnit("Brute", barbarianCiv, testGame.GetTile(0, 2));

        Attack(attackerUnit, barbarianUnit);

        Assert.Equal(33, attackerUnit.Promotions.XP); // Attacker should get no XP from ranged attack, full xp from melee attack
    }
}
