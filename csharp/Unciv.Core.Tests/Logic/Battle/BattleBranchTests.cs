using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using BattleObject = Unciv.Logic.Battle.Battle;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Core.Tests.Logic.Battle;

// C#-only tests (T083) for the Battle and Nuke branches the Kotlin BattleTest does not reach (the follow-up notes of T081/T082 included:
// the nuke branch of attackOrNuke, interception tails, the Battle seams of BattleUnitCapture and AirInterception).
[Collection("RulesetCache")]
public class BattleBranchTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject attackerCiv;
    private readonly CivilizationObject defenderCiv;

    public BattleBranchTests()
    {
        testGame.MakeHexagonalMap(5);
        attackerCiv = testGame.AddCiv(isPlayer: true);
        defenderCiv = testGame.AddCiv(isPlayer: true);
        attackerCiv.DiplomacyFunctions.MakeCivilizationsMeet(defenderCiv);
        attackerCiv.GetDiplomacyManager(defenderCiv)!.DeclareWar();
    }

    private MapUnitClass Unit(string name, CivilizationObject civ, int x, int y)
    {
        var unit = testGame.AddUnit(name, civ, testGame.GetTile(x, y));
        unit.CurrentMovement = 2f;
        return unit;
    }

    private static void SetGold(CivilizationObject civ, int gold) => civ.AddGold(gold - civ.Gold);

    private static MapUnitCombatant C(MapUnitClass unit) => new(unit);

    // ---- moveAndAttack / movePreparingAttack

    [Fact]
    public void MovePreparingAttackMovesNextToTheTargetAndReportsWhetherMovementIsLeft()
    {
        var attacker = Unit("Warrior", attackerCiv, 3, 0);
        var defender = Unit("Warrior", defenderCiv, 0, 0);
        var tile = new AttackableTile(testGame.GetTile(1, 0), testGame.GetTile(0, 0), 1f, null);

        // two tiles of movement are all a warrior has: after the move nothing is left to attack with
        Assert.False(BattleObject.MovePreparingAttack(C(attacker), tile));
        Assert.Equal(testGame.GetTile(1, 0), attacker.GetTile());
        Assert.Equal(100, defender.Health);
    }

    [Fact]
    public void MoveAndAttackMovesNextToTheTargetAndAttacks()
    {
        var defender = Unit("Warrior", defenderCiv, 0, 0);
        var adjacent = Unit("Warrior", attackerCiv, 2, 0);

        BattleObject.MoveAndAttack(C(adjacent), new AttackableTile(testGame.GetTile(1, 0), testGame.GetTile(0, 0), 1f, null));

        Assert.True(defender.Health < 100);
        Assert.True(adjacent.Health < 100);
    }

    [Fact]
    public void MovePreparingAttackStopsWhenTheTargetBecameOurOwn()
    {
        var attacker = Unit("Warrior", attackerCiv, 2, 0);
        Unit("Warrior", attackerCiv, 0, 0);
        Assert.False(BattleObject.MovePreparingAttack(C(attacker), new AttackableTile(testGame.GetTile(1, 0), testGame.GetTile(0, 0), 1f, null)));
        Assert.False(BattleObject.MovePreparingAttack(C(attacker), new AttackableTile(testGame.GetTile(1, 0), testGame.GetTile(0, 1), 1f, null))); // nothing to attack there
    }

    [Fact]
    public void SiegeUnitsSetUpBeforeTheyAttack()
    {
        var siege = testGame.AddDefaultRangedUnitWithUniques(attackerCiv, testGame.GetTile(3, 0), "Must set up to ranged attack");
        siege.CurrentMovement = 3f;
        var defender = Unit("Warrior", defenderCiv, 0, 0);

        BattleObject.MoveAndAttack(C(siege), new AttackableTile(testGame.GetTile(2, 0), testGame.GetTile(0, 0), 1f, null));

        Assert.Equal(testGame.GetTile(2, 0), siege.GetTile());
        Assert.True(siege.IsSetUpForSiege());
        Assert.True(defender.Health < 100);
    }

    [Fact]
    public void SiegeUnitsOutOfMovementAfterSettingUpDoNotAttack()
    {
        var siege = testGame.AddDefaultRangedUnitWithUniques(attackerCiv, testGame.GetTile(3, 0), "Must set up to ranged attack");
        siege.CurrentMovement = 2f;
        var defender = Unit("Warrior", defenderCiv, 0, 0);

        BattleObject.MoveAndAttack(C(siege), new AttackableTile(testGame.GetTile(2, 0), testGame.GetTile(0, 0), 1f, null));

        Assert.True(siege.IsSetUpForSiege());
        Assert.Equal(100, defender.Health);
    }

    [Fact]
    public void WoundedUnitsPillageOnTheWayToTheTargetToHeal()
    {
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(-3, 0));
        var improvementTile = testGame.GetTile(2, 0);
        improvementTile.Improvement = "Farm";
        city.Expansion.TakeOwnership(improvementTile);
        var attacker = Ranged(attackerCiv, 3, 0);
        attacker.Health = 80;
        attacker.CurrentMovement = 3f;
        Unit("Warrior", defenderCiv, 0, 0);

        BattleObject.MoveAndAttack(C(attacker), new AttackableTile(improvementTile, testGame.GetTile(0, 0), 1f, null));

        Assert.True(improvementTile.ImprovementIsPillaged);
    }

    // ---- attack: attackers other than a land unit

    [Fact]
    public void ACityCanAttackAndForgetsItsBombardNotification()
    {
        var city = testGame.AddCity(attackerCiv, testGame.GetTile(0, 2));
        var defender = Unit("Warrior", defenderCiv, 0, 1);
        attackerCiv.AddNotification($"Your city [{city.Name}] can bombard the enemy!", NotificationCategory.War);
        attackerCiv.AddNotification("something else", NotificationCategory.War);

        var damage = BattleObject.AttackOrNuke(new CityCombatant(city), new AttackableTile(city.GetCenterTile(), defender.GetTile(), 0f, new MapUnitCombatant(defender)));

        Assert.True(damage.AttackerDealt > 0);
        Assert.True(city.AttackedThisTurn);
        Assert.Single(attackerCiv.AttacksSinceTurnStart);
        Assert.Equal(city.Location, attackerCiv.AttacksSinceTurnStart[0].Source);
        Assert.DoesNotContain(attackerCiv.Notifications, it => it.Text.Contains("can bombard", StringComparison.Ordinal));
        Assert.Contains(attackerCiv.Notifications, it => it.Text == "something else");
        Assert.Contains(defenderCiv.Notifications, it => it.Text.StartsWith("Enemy city [", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAirAttackerDestroyedByAnInterceptorStopsTheAttack()
    {
        testGame.AddCity(attackerCiv, testGame.GetTile(0, 3));
        var fighter = Unit("Fighter", attackerCiv, 0, 3);
        fighter.Health = 1;
        testGame.AddCity(defenderCiv, testGame.GetTile(0, 0));
        var interceptor = Unit("Fighter", defenderCiv, 0, 0);
        var target = Unit("Warrior", defenderCiv, 0, 1);

        var damage = Attack(fighter, target);

        Assert.True(C(fighter).IsDefeated());
        Assert.Equal(1, damage.DefenderDealt);
        Assert.Equal(100, target.Health);
        Assert.Equal(1, interceptor.AttacksThisTurn);
    }

    [Fact]
    public void AnAirAttackerThatSurvivesTheInterceptionAttacksAndEarnsAirXp()
    {
        testGame.AddCity(attackerCiv, testGame.GetTile(0, 3));
        var fighter = Unit("Fighter", attackerCiv, 0, 3);
        var target = Unit("Warrior", defenderCiv, 0, 1);

        var damage = Attack(fighter, target);

        Assert.True(damage.AttackerDealt > 0);
        Assert.Equal(4, fighter.Promotions.XP);
        Assert.Equal(2, target.Promotions.XP);
        Assert.Equal(testGame.GetTile(0, 3), fighter.GetTile()); // an air unit never moves into the attacked tile
    }

    private static DamageDealt Attack(MapUnitClass attacker, MapUnitClass defender) =>
        BattleObject.Attack(C(attacker), C(defender));

    private MapUnitClass Ranged(CivilizationObject civ, int x, int y, params string[] uniques)
    {
        var unit = testGame.AddDefaultRangedUnitWithUniques(civ, testGame.GetTile(x, y), uniques);
        unit.CurrentMovement = 2f;
        return unit;
    }

    private MapUnitClass Melee(CivilizationObject civ, int x, int y, params string[] uniques)
    {
        var unit = testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.GetTile(x, y), uniques);
        unit.CurrentMovement = 2f;
        return unit;
    }

    // ---- uniques around an attack

    [Fact]
    public void CombatTriggersFireForBothSidesAndCanTargetTheOtherUnit()
    {
        var attacker = Ranged(attackerCiv, 0, 2, "[This Unit] takes [5] damage <upon entering combat>");
        var defender = Melee(defenderCiv, 0, 1, "[Target Unit] takes [3] damage <upon entering combat>");

        var damage = Attack(attacker, defender);

        Assert.Equal(95 - 3, attacker.Health); // the attacker's own trigger and the defender's trigger aimed at the attacker
        Assert.Equal(100 - damage.AttackerDealt, defender.Health);
    }

    [Fact]
    public void VictoryDefeatAndLosingHealthTriggersFire()
    {
        var attacker = Ranged(attackerCiv, 0, 2, "Gain [30] [Gold] <upon defeating a [Military] unit>");
        var defender = Melee(defenderCiv, 0, 1, "Gain [40] [Gold] <upon being defeated>", "Gain [7] [Gold] <upon losing at least [10] HP in a single attack>");
        defender.Health = 1;
        SetGold(attackerCiv, 0);
        SetGold(defenderCiv, 0);

        Attack(attacker, defender);

        Assert.True(defender.IsDestroyed);
        Assert.Equal(30, attackerCiv.Gold);
        Assert.Equal(40, defenderCiv.Gold); // being defeated; the single HP lost is less than the threshold of the other trigger
    }

    [Fact]
    public void ADefeatedAttackerGivesTheDefenderTheKillRewards()
    {
        var attacker = Melee(attackerCiv, 1, 0, "Gain [40] [Gold] <upon being defeated>");
        attacker.Health = 1;
        var defender = Melee(defenderCiv, 0, 0, "Gain [30] [Gold] <upon defeating a [Military] unit>", "Heals [10] damage if it kills a unit");
        defender.Health = 50;
        SetGold(attackerCiv, 0);
        SetGold(defenderCiv, 0);

        Attack(attacker, defender);

        Assert.True(attacker.IsDestroyed);
        Assert.Equal(40, attackerCiv.Gold);
        Assert.Equal(30, defenderCiv.Gold);
        Assert.Contains(defenderCiv.Notifications, it => it.Text.Contains("was destroyed while attacking", StringComparison.Ordinal));
    }

    [Fact]
    public void LosingEnoughHealthTriggersTheLosingHealthUniqueOnBothSides()
    {
        var attacker = Melee(attackerCiv, 1, 0, "Gain [20] [Gold] <upon losing at least [5] HP in a single attack>");
        var defender = Melee(defenderCiv, 0, 0, "Gain [10] [Gold] <upon losing at least [5] HP in a single attack>");
        SetGold(attackerCiv, 0);
        SetGold(defenderCiv, 0);

        var damage = Attack(attacker, defender);

        Assert.True(damage.AttackerDealt >= 5 && damage.DefenderDealt >= 5);
        Assert.Equal(20, attackerCiv.Gold);
        Assert.Equal(10, defenderCiv.Gold);
    }

    [Fact]
    public void DamageTriggersAimedAtTheTargetOrAtTheDamagingUnit()
    {
        var attacker = Melee(attackerCiv, 1, 0, "[This Unit] takes [4] damage <upon damaging a [Military] unit>");
        var defender = Melee(defenderCiv, 0, 0, "[Target Unit] takes [6] damage <upon damaging a [Target Unit] unit>");

        var damage = Attack(attacker, defender);

        Assert.Equal(100 - damage.DefenderDealt - 4, attacker.Health);
    }

    [Fact]
    public void DamageDealtIsPlunderedAsStatsAndAsSubStats()
    {
        var attacker = Ranged(attackerCiv, 0, 2,
            "Earn [100]% of the damage done to [Military] units as [Gold]",
            "Earn [100]% of the damage done to [Military] units as [Golden Age points]");
        var defender = Melee(defenderCiv, 0, 1);
        SetGold(attackerCiv, 0);
        var before = attackerCiv.GoldenAges.StoredHappiness;

        var damage = Attack(attacker, defender);

        Assert.Equal(damage.AttackerDealt, attackerCiv.Gold);
        Assert.Equal(before + damage.AttackerDealt, attackerCiv.GoldenAges.StoredHappiness);
        Assert.Contains(attackerCiv.Notifications, it => it.Text.Contains("plundered", StringComparison.Ordinal) && it.Text.Contains("[Gold]", StringComparison.Ordinal));
        Assert.Contains(attackerCiv.Notifications, it => it.Text.Contains("[Golden Age points]", StringComparison.Ordinal));
        Assert.Equal(NotificationIcon.Happiness, SubStat.GoldenAgePoints.Icon());
        Assert.Equal(NotificationIcon.Food, SubStat.StoredFood.Icon());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((SubStat)99).Icon());
    }

    [Fact]
    public void KillingUnitsPlunderTheirCostAndSetUpTheCityStateReactions()
    {
        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        attackerCiv.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        var barbarianCiv = testGame.AddBarbarianCiv();
        testGame.AddUnit("Warrior", cityState, testGame.GetTile(-4, 0));
        var attacker = Ranged(attackerCiv, 0, 2, "Earn [50]% of killed [Military] unit's [Cost] as [Gold]");
        var barbarian = testGame.AddUnit("Brute", barbarianCiv, testGame.GetTile(0, 1));
        barbarian.Health = 1;
        SetGold(attackerCiv, 0);

        Attack(attacker, barbarian);

        Assert.True(barbarian.IsDestroyed);
        Assert.Equal(barbarian.BaseUnit.Cost / 2, attackerCiv.Gold);
    }

    // ---- cities

    [Fact]
    public void AttackingACityDestroyedByTheAttackerWhenTheUniqueSaysSo()
    {
        testGame.AddCity(defenderCiv, testGame.GetTile(-4, 0)); // the capital
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(0, 1));
        city.Health = 1;
        var attacker = Ranged(attackerCiv, 0, 3, "Destroys [All] cities instead of capturing");

        BattleObject.Attack(C(attacker), new CityCombatant(city));

        Assert.False(testGame.GetTile(0, 1).IsCityCenter());
        Assert.Contains(attackerCiv.Notifications, it => it.Text == "We have destroyed the city of [" + city.Name + "]!");
    }

    [Fact]
    public void BarbariansRaidACityForGoldInsteadOfCapturingIt()
    {
        var barbarianCiv = testGame.AddBarbarianCiv();
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(0, 1));
        city.Health = 1;
        SetGold(defenderCiv, 500);
        var brute = testGame.AddUnit("Brute", barbarianCiv, testGame.GetTile(0, 2));
        brute.CurrentMovement = 2f;

        BattleObject.Attack(C(brute), new CityCombatant(city));

        Assert.Equal(defenderCiv, city.Civ);
        Assert.Equal(300, defenderCiv.Gold);
        Assert.Equal(2, city.Health);
        Assert.True(brute.IsDestroyed);
        Assert.Contains(defenderCiv.Notifications, it => it.Text == $"Barbarians raided [{city.Name}] and stole [200] Gold from your treasury!");
    }

    private void MakeCurrentPlayerSomeoneElse()
    {
        testGame.GameInfo.CurrentPlayerCiv = testGame.AddCiv();
        testGame.GameInfo.CurrentPlayer = testGame.GameInfo.CurrentPlayerCiv.CivId;
    }

    [Fact]
    public void ConqueringACityWithAHumanClientAsksThePlayerAndKeepsItsUnitsOut()
    {
        MakeCurrentPlayerSomeoneElse();
        testGame.AddCity(defenderCiv, testGame.GetTile(-4, 0));
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(0, 1), initialPopulation: 5);
        city.Health = 1;
        var garrison = Unit("Warrior", defenderCiv, 0, 1);
        var worker = testGame.AddUnit("Worker", defenderCiv, testGame.GetTile(0, 1));
        var air = Unit("Fighter", defenderCiv, 0, 1);
        testGame.GameInfo.SetPlayerSettings(attackerCiv.CivId, new PlayerSettings { HumanConquestPrompt = true });
        var attacker = Melee(attackerCiv, 0, 2);

        BattleObject.Attack(C(attacker), new CityCombatant(city));

        Assert.True(city.HasJustBeenConquered);
        Assert.True(garrison.IsDestroyed);
        Assert.True(air.IsDestroyed);
        Assert.Equal(attackerCiv, worker.Civ); // captured, not destroyed
        Assert.Contains(attackerCiv.PopupAlerts, it => it.Type == AlertType.CityConquered && it.Value == city.Id);
        Assert.Contains(attackerCiv.Notifications, it => it.Text == $"We have conquered the city of [{city.Name}]!");
    }

    [Fact]
    public void ConqueringACityOnTheOneCityChallengeDestroysIt()
    {
        MakeCurrentPlayerSomeoneElse();
        testGame.GameInfo.GameParameters.OneCityChallenge = true;
        testGame.AddCity(defenderCiv, testGame.GetTile(-4, 0));
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(0, 1));
        city.Health = 1;
        var attacker = Melee(attackerCiv, 0, 2);

        BattleObject.Attack(C(attacker), new CityCombatant(city));

        Assert.False(testGame.GetTile(0, 1).IsCityCenter());
    }

    [Fact]
    public void ConqueringTheirOwnFormerCapitalPuppetsAndAnnexesIt()
    {
        MakeCurrentPlayerSomeoneElse();
        var city = testGame.AddCity(attackerCiv, testGame.GetTile(0, 1), initialPopulation: 5);
        city.IsOriginalCapital = true;
        city.Health = 1;
        city.CityConstructions.AddBuilding(testGame.CreateBuilding().Name);
        // the city belongs to the defender now, but was founded by the attacker
        city.MoveToCiv(defenderCiv);
        var attacker = Melee(attackerCiv, 0, 2);

        BattleObject.Attack(C(attacker), new CityCombatant(city));

        Assert.Equal(attackerCiv, city.Civ);
        Assert.False(city.IsPuppet);
    }

    [Fact]
    public void TheAiLiberatesAFormerCityStateCityWhenTheAllianceIsWorthIt()
    {
        MakeCurrentPlayerSomeoneElse();
        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        testGame.AddUnit("Warrior", cityState, testGame.GetTile(-4, 0));
        var city = testGame.AddCity(cityState, testGame.GetTile(0, 1), initialPopulation: 5);
        city.Health = 1;
        city.MoveToCiv(defenderCiv);
        testGame.AddCity(defenderCiv, testGame.GetTile(-3, 0));
        testGame.GameInfo.ClientState.NextTurnAutomation = new AllianceValue(10);
        attackerCiv.PlayerType = PlayerType.AI;
        var attacker = Melee(attackerCiv, 0, 2);

        BattleObject.Attack(C(attacker), new CityCombatant(city));

        Assert.Equal(cityState, city.Civ);
    }

    private sealed class AllianceValue(int value) : Unciv.Logic.Civilization.Managers.INextTurnAutomation
    {
        public void AutomateGoldToSciencePercentage(CivilizationObject civInfo) { }
        public void AutomateCityBombardment(CivilizationObject civInfo) { }
        public void ChooseGreatPerson(CivilizationObject civInfo) { }
        public void AutomateCivMoves(CivilizationObject civInfo) { }
        public int ValueCityStateAlliance(CivilizationObject civInfo, CivilizationObject cityState, bool includeQuests = false) => value;
    }

    [Fact]
    public void TheAiPuppetsSmallCitiesAndRazesThemAsACityState()
    {
        MakeCurrentPlayerSomeoneElse();
        testGame.AddCity(defenderCiv, testGame.GetTile(-4, 0));
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(0, 1), initialPopulation: 2);
        city.Health = 1;
        attackerCiv.PlayerType = PlayerType.AI;
        var attacker = Melee(attackerCiv, 0, 2);

        BattleObject.Attack(C(attacker), new CityCombatant(city));

        Assert.Equal(attackerCiv, city.Civ);
        Assert.True(city.IsBeingRazed);
    }

    [Fact]
    public void CapturingACityPlundersItsProduction()
    {
        MakeCurrentPlayerSomeoneElse();
        testGame.AddCity(defenderCiv, testGame.GetTile(-4, 0));
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(0, 1), initialPopulation: 5);
        city.Health = 1;
        attackerCiv.PlayerType = PlayerType.AI;
        var attacker = Melee(attackerCiv, 0, 2, "Upon capturing a city, receive [1] times its [Gold] production as [Faith] immediately",
            "Upon capturing a city, receive [1] times its [Gold] production as [Nonsense] immediately", "Gain [11] [Gold] <upon conquering a city>");
        SetGold(attackerCiv, 0);
        var faithBefore = attackerCiv.ReligionManager.StoredFaith;
        int expectedFaith = Unciv.Utils.KotlinMath.FloatToInt(city.CityStats.CurrentCityStats[Unciv.Models.StatTypes.Stat.Gold]);

        BattleObject.Attack(C(attacker), new CityCombatant(city));

        Assert.Equal(attackerCiv, city.Civ);
        Assert.Equal(11, attackerCiv.Gold);
        Assert.Equal(faithBefore + expectedFaith, attackerCiv.ReligionManager.StoredFaith);
    }

    [Fact]
    public void DestroyIfDefeatedEndsTheCivilizationAndTheCityStateFunctionsHearAboutIt()
    {
        Unit("Warrior", attackerCiv, 3, 3); // a civ without anything is defeated too and gets no notification
        var vanquished = testGame.AddCiv(cityStateType: "Cultured");
        Assert.True(vanquished.IsDefeated());

        BattleObject.DestroyIfDefeated(vanquished, attackerCiv, new HexCoord(1, 1));

        Assert.Contains(attackerCiv.PopupAlerts, it => it.Type == AlertType.Defeated && it.Value == vanquished.CivId);
        Assert.Contains(attackerCiv.Notifications, it => it.Text == $"The City-State of [{vanquished.CivName}] has been destroyed!");

        var stillAlive = Unit("Warrior", defenderCiv, 0, 0);
        BattleObject.DestroyIfDefeated(defenderCiv, attackerCiv); // not defeated: nothing happens
        Assert.False(stillAlive.IsDestroyed);
        Assert.DoesNotContain(attackerCiv.PopupAlerts, it => it.Type == AlertType.Defeated && it.Value == defenderCiv.CivId);
    }

    [Fact]
    public void CapturingTheLastCivilianOfACivDefeatsIt()
    {
        var lonely = testGame.AddCiv(isPlayer: true);
        lonely.DiplomacyFunctions.MakeCivilizationsMeet(attackerCiv);
        lonely.GetDiplomacyManager(attackerCiv)!.DeclareWar();
        var worker = testGame.AddUnit("Worker", lonely, testGame.GetTile(2, 0));
        var attacker = Melee(attackerCiv, 1, 0);

        Attack(attacker, worker);

        Assert.Contains(attackerCiv.PopupAlerts, it => it.Type == AlertType.Defeated && it.Value == lonely.CivId);
    }

    // ---- withdrawing

    [Theory]
    [InlineData("guarding")]
    [InlineData("cannotMove")]
    public void ADefenderThatCannotWithdrawFightsInstead(string reason)
    {
        var defender = reason == "cannotMove"
            ? Melee(defenderCiv, 0, 0, UniqueType.WithdrawsBeforeMeleeCombat.Text(), UniqueType.CannotMove.Text())
            : Melee(defenderCiv, 0, 0, UniqueType.WithdrawsBeforeMeleeCombat.Text());
        if (reason == "guarding") defender.Action = "Guard";
        var attacker = Melee(attackerCiv, 1, 0);

        var damage = Attack(attacker, defender);

        Assert.True(damage.AttackerDealt > 0, reason);
    }

    // ---- more of attack()

    [Fact]
    public void XpBonusesApplyAndAUnitThatCanNowBePromotedSaysSo()
    {
        var attacker = Melee(attackerCiv, 1, 0, "[3] XP gained from combat", "[50]% XP gained from combat");
        var defender = Melee(defenderCiv, 0, 0);
        attacker.Promotions.XP = 5;

        Attack(attacker, defender);

        Assert.Equal(5 + 12, attacker.Promotions.XP); // (5 + 3) * 1.5
        Assert.Contains(attackerCiv.Notifications, it => it.Text == $"[{attacker.DisplayName()}] can be promoted!");
    }

    [Fact]
    public void AttackingABarbarianCampTellsTheBarbarianManager()
    {
        var barbarianCiv = testGame.AddBarbarianCiv();
        var camp = new BarbarianEncampment(new HexCoord(0, 0)) { Countdown = 99 };
        testGame.GameInfo.Barbarians.Encampments.Add(camp);
        testGame.GetTile(0, 0).Improvement = Constants.BarbarianEncampment;
        var barbarian = testGame.AddUnit("Brute", barbarianCiv, testGame.GetTile(0, 0));
        var attacker = Ranged(attackerCiv, 0, 2);

        Attack(attacker, barbarian);

        Assert.Equal(49, camp.Countdown); // halved by WasAttacked
    }

    [Fact]
    public void ASelfDestructingAttackerIsGoneAndAMovingOneStopsMoving()
    {
        var bomb = Melee(attackerCiv, 1, 0, "Self-destructs when attacking");
        Attack(bomb, Melee(defenderCiv, 0, 0));
        Assert.True(bomb.IsDestroyed);

        var walker = Ranged(attackerCiv, 0, 3);
        walker.Action = "moveTo 1,1";
        Attack(walker, Melee(defenderCiv, 0, 2));
        Assert.Null(walker.Action);
    }

    [Fact]
    public void FortifiedAttackersAndExploringDefendersWakeUp()
    {
        var attacker = Ranged(attackerCiv, 0, 2);
        attacker.Action = "Fortify";
        var defender = Melee(defenderCiv, 0, 1);
        defender.Action = "Explore";

        Attack(attacker, defender);

        Assert.Null(attacker.Action);
        Assert.Null(defender.Action);
    }

    [Fact]
    public void UnitsThatDestroyImprovementsDestroyThemOnTheAttackedTile()
    {
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(-4, 0));
        var tile = testGame.GetTile(0, 0);
        tile.Improvement = "Farm";
        city.Expansion.TakeOwnership(tile);
        var defender = Melee(defenderCiv, 0, 0);
        var attacker = Ranged(attackerCiv, 0, 2, "Destroys tile improvements when attacking");

        Attack(attacker, defender);

        Assert.Null(tile.Improvement);
        Assert.Contains(defenderCiv.Notifications, it => it.Text.StartsWith("An enemy [", StringComparison.Ordinal) && it.Text.EndsWith("has destroyed our tile improvement [Farm]", StringComparison.Ordinal));
    }

    [Fact]
    public void DamageTriggersCanBeAimedAtTheDamagedUnit()
    {
        var attacker = Ranged(attackerCiv, 0, 2, "[Target Unit] takes [6] damage <upon damaging a [Military] unit>");
        var defender = Melee(defenderCiv, 0, 1);

        var damage = Attack(attacker, defender);

        Assert.Equal(100 - damage.AttackerDealt - 6, defender.Health);
    }

    [Fact]
    public void ConqueringACityAsTheCurrentPlayerCompletesTheTutorialTask()
    {
        testGame.AddCity(defenderCiv, testGame.GetTile(-4, 0));
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(0, 1), initialPopulation: 5);
        city.Health = 1;
        attackerCiv.PlayerType = PlayerType.AI;
        testGame.GameInfo.CurrentPlayerCiv = attackerCiv;
        testGame.GameInfo.CurrentPlayer = attackerCiv.CivId;
        var attacker = Melee(attackerCiv, 0, 2);

        BattleObject.Attack(C(attacker), new CityCombatant(city));

        Assert.Equal(attackerCiv, city.Civ);
        Assert.Contains("Conquer a city", testGame.GameInfo.ClientState.TutorialTasksCompleted(attackerCiv.CivId));
    }

    [Fact]
    public void ABarbarianKilledNextToACityStateMakesTheCityStateLikeTheKiller()
    {
        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        var fake = new FakeCityStateFunctions();
        cityState.CityStateFunctions = fake;
        testGame.AddCity(cityState, testGame.GetTile(-4, 0));
        attackerCiv.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        var barbarianCiv = testGame.AddBarbarianCiv();
        var barbarian = testGame.AddUnit("Brute", barbarianCiv, testGame.GetTile(-3, 0));
        var attacker = Ranged(attackerCiv, -3, 2);
        barbarian.Health = 1;
        BattleObject.Attack(C(attacker), C(barbarian));

        Assert.True(barbarian.IsDestroyed);
        Assert.Equal([attackerCiv.CivId], fake.BarbarianKillers);
    }

    [Fact]
    public void ConquestTriggersAreEvaluatedLazilySoEarlierTriggersAffectLaterConditionals()
    {
        testGame.AddCity(defenderCiv, testGame.GetTile(-4, 0));
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(0, 1), initialPopulation: 5);
        city.Health = 1;
        attackerCiv.PlayerType = PlayerType.AI;
        MakeCurrentPlayerSomeoneElse();
        var attacker = Melee(attackerCiv, 0, 2, "Gain [100] [Gold] <upon conquering a city>", "Gain [7] [Gold] <upon conquering a city> <when above [50] [Gold]>");
        SetGold(attackerCiv, 0);

        BattleObject.Attack(C(attacker), new CityCombatant(city));

        Assert.Equal(107, attackerCiv.Gold);
    }

    [Fact]
    public void FlatXpBonusesWrapOnOverflowLikeKotlin()
    {
        var attacker = Melee(attackerCiv, 1, 0, "[2147483647] XP gained from combat", "[2147483647] XP gained from combat");
        var defender = Melee(defenderCiv, 0, 0);

        BattleObject.AddXp(C(attacker), 0, C(defender));

        Assert.Equal(-2, attacker.Promotions.XP);
    }

    [Fact]
    public void TheExtraRangedFakeUnitDelegatesToTheWrappedUnitExceptForRangedAndStrength()
    {
        var attacker = Melee(attackerCiv, 1, 0, "Before engaging in combat performs an extra ranged attack with [50]% of melee combat strength");
        var wrapped = C(attacker);
        var fake = BattleObject.GetExtraRangedAttackFakeUnits(wrapped).Single();
        var other = C(Melee(defenderCiv, 0, 0));

        Assert.True(fake.IsRanged());
        Assert.Equal(wrapped.IsMelee(), fake.IsMelee());
        Assert.Equal(((ICombatant)wrapped).IsAirUnit(), fake.IsAirUnit());
        Assert.Equal(((ICombatant)wrapped).IsWaterUnit(), fake.IsWaterUnit());
        Assert.Equal(((ICombatant)wrapped).IsLandUnit(), fake.IsLandUnit());
        Assert.False(fake.IsCity());
        Assert.Equal(wrapped.IsCivilian(), fake.IsCivilian());
        Assert.Equal(wrapped.GetNotificationDisplay("our "), fake.GetNotificationDisplay("our "));
        Assert.Equal(wrapped.GetName(), fake.GetName());
        Assert.Equal(wrapped.GetHealth(), fake.GetHealth());
        Assert.Equal(wrapped.GetMaxHealth(), fake.GetMaxHealth());
        Assert.Equal(wrapped.GetUnitType(), fake.GetUnitType());
        Assert.Equal(wrapped.GetDefendingStrength(other), fake.GetDefendingStrength(other));
        Assert.Equal(wrapped.GetCivInfo(), fake.GetCivInfo());
        Assert.Equal(wrapped.GetTile(), fake.GetTile());
        Assert.Equal(wrapped.IsVisibleTo(defenderCiv), fake.IsVisibleTo(defenderCiv));
        Assert.Equal(wrapped.CanAttack(), fake.CanAttack());
        Assert.Equal(wrapped.MatchesFilter("Military"), fake.MatchesFilter("Military"));
        Assert.Equal(wrapped.GetAttackSound(), fake.GetAttackSound());
        Assert.Empty(fake.GetTriggeredUniques(UniqueType.TriggerUponCombat, GameContext.EmptyState));
        Assert.Equal(attacker.BaseUnit.Strength / 2, fake.GetAttackingStrength(other)); // 50% of the melee strength, always ranged
        Assert.False(fake.IsDefeated());
        fake.TakeDamage(10);
        Assert.Equal(90, attacker.Health);
        Assert.Contains("Extra ranged attack of", fake.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ADestroyingUnitThatRemovesTheLastCityDefeatsTheCiv()
    {
        var lonely = testGame.AddCiv(isPlayer: true);
        lonely.DiplomacyFunctions.MakeCivilizationsMeet(attackerCiv);
        var city = testGame.AddCity(lonely, testGame.GetTile(0, 1));
        testGame.AddCity(attackerCiv, testGame.GetTile(4, 0));
        city.Health = 1;
        city.IsOriginalCapital = false;
        var attacker = Ranged(attackerCiv, 0, 3, "Destroys [All] cities instead of capturing");

        BattleObject.Attack(C(attacker), new CityCombatant(city));

        Assert.Empty(lonely.Cities);
        Assert.Contains(attackerCiv.PopupAlerts, it => it.Type == AlertType.Defeated && it.Value == lonely.CivId);
    }
}
