using Unciv.Core.Tests.Support;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Battle;

// Port of tests/src/com/unciv/logic/battle/BattleDamageTest.kt (T081), plus the C#-only cases for MapUnitCombatant, CityCombatant and the damage maths.
[Collection("RulesetCache")]
public class BattleDamageTests
{
    private readonly Civilization attackerCiv;
    private readonly Civilization defenderCiv;
    private readonly TileClass defaultAttackerTile;
    private readonly TileClass defaultDefenderTile;
    private readonly MapUnitClass defaultAttackerUnit;
    private readonly MapUnitClass defaultDefenderUnit;
    private readonly TestGame testGame = new();

    public BattleDamageTests()
    {
        testGame.MakeHexagonalMap(4);
        attackerCiv = testGame.AddCiv();
        defenderCiv = testGame.AddCiv();

        defaultAttackerTile = testGame.GetTile(1, 1);
        defaultAttackerUnit = testGame.AddUnit("Warrior", attackerCiv, defaultAttackerTile);
        defaultDefenderTile = testGame.GetTile(0, 1);
        defaultDefenderUnit = testGame.AddUnit("Warrior", defenderCiv, defaultDefenderTile);
    }

    private Counter<string> AttackModifiers(MapUnitClass attacker, TileClass from) =>
        BattleDamage.GetAttackModifiers(new MapUnitCombatant(attacker), new MapUnitCombatant(defaultDefenderUnit), from);

    [Fact]
    public void ShouldRetrieveModifiersFromPolicies()
    {
        var policy = testGame.CreatePolicy("[+25]% Strength <when attacking> <for [Military] units>");
        attackerCiv.Policies.Adopt(policy, true);

        var attackModifiers = AttackModifiers(defaultAttackerUnit, defaultAttackerTile);

        Assert.Equal(1, attackModifiers.Count);
        Assert.Equal(25, attackModifiers.SumValues());
    }

    [Fact]
    public void ShouldKeepPromotionModifiersInRulesetOrder()
    {
        // Promotion names are chosen so HashSet iteration reverses the ruleset insertion order.
        var alphaPromotion = testGame.CreateUnitPromotion("[+10]% Strength <when attacking>");
        testGame.Ruleset.UnitPromotions.Remove(alphaPromotion.Name);
        alphaPromotion.Name = "Alpha";
        testGame.Ruleset.UnitPromotions[alphaPromotion.Name] = alphaPromotion;

        var zetaPromotion = testGame.CreateUnitPromotion("[+20]% Strength <when attacking>");
        testGame.Ruleset.UnitPromotions.Remove(zetaPromotion.Name);
        zetaPromotion.Name = "Zeta";
        testGame.Ruleset.UnitPromotions[zetaPromotion.Name] = zetaPromotion;

        defaultAttackerUnit.Promotions.Promotions.Add(alphaPromotion.Name);
        defaultAttackerUnit.Promotions.Promotions.Add(zetaPromotion.Name);
        defaultAttackerUnit.UpdateUniques();

        var attackModifiers = AttackModifiers(defaultAttackerUnit, defaultAttackerTile);

        Assert.Equal(["Alpha", "Zeta"], attackModifiers.Keys.Select(key =>
            key.Contains("Alpha", StringComparison.Ordinal) ? "Alpha" : key.Contains("Zeta", StringComparison.Ordinal) ? "Zeta" : key).ToList());
    }

    [Fact]
    public void ShouldRetrieveModifiersFromBuldings()
    {
        var building = testGame.CreateBuilding("[+15]% Strength <for [Military] units>");
        var attackerCity = testGame.AddCity(attackerCiv, testGame.GetTile(HexCoord.Zero));
        attackerCity.CityConstructions.AddBuilding(building.Name);

        var attackModifiers = AttackModifiers(defaultAttackerUnit, defaultAttackerTile);

        Assert.Equal(1, attackModifiers.Count);
        Assert.Equal(15, attackModifiers.SumValues());
    }

    [Fact]
    public void ShouldRetrieveModifiersFromNationalAbilities()
    {
        var civ = testGame.AddCiv(["[+10]% Strength <for [All] units> <during a Golden Age>"]); // i.e., Persia national ability
        civ.GoldenAges.EnterGoldenAge(2);
        var attackerTile = testGame.GetTile(HexCoord.Zero);
        var attackerUnit = testGame.AddUnit("Warrior", civ, attackerTile);

        var attackModifiers = AttackModifiers(attackerUnit, attackerTile);

        Assert.Equal(1, attackModifiers.Count);
        Assert.Equal(10, attackModifiers.SumValues());
    }

    [Fact]
    public void ShouldRetrieveModifiersFromLackOfStrategicResource()
    {
        defaultAttackerTile.MilitaryUnit = null; // otherwise we'll also get a flanking bonus
        var attackerTile = testGame.GetTile(HexCoord.Zero);
        var attackerUnit = testGame.AddUnit("Horseman", attackerCiv, attackerTile);

        var attackModifiers = AttackModifiers(attackerUnit, attackerTile);

        Assert.Equal(1, attackModifiers.Count);
        Assert.Equal(BattleConstants.MissingResourcesMalus, attackModifiers.SumValues());
    }

    [Fact]
    public void ShouldRetrieveAttackingFlankBonusModifiers()
    {
        var flankingAttackerTile = testGame.GetTile(HexCoord.Zero);
        testGame.AddUnit("Warrior", attackerCiv, flankingAttackerTile);

        var attackModifiers = AttackModifiers(defaultAttackerUnit, defaultAttackerTile);

        Assert.Equal(1, attackModifiers.Count);
        Assert.Equal((int)BattleConstants.BaseFlankingBonus, attackModifiers.SumValues());
    }

    [Fact]
    public void ShouldRetrieveDefenceFortificationModifiers()
    {
        defaultDefenderUnit.CurrentMovement = 2f; // base warrior max movement points
        defaultDefenderUnit.Fortify();
        new Unciv.Logic.Map.MapUnits.UnitTurnManager(defaultDefenderUnit).EndTurn(); // Kotlin: TurnManager(defenderCiv).endTurn() (T071), of which this is the part that fortifies

        var defenceModifiers = BattleDamage.GetDefenceModifiers(new MapUnitCombatant(defaultAttackerUnit), new MapUnitCombatant(defaultDefenderUnit), defaultAttackerTile);

        Assert.Equal(1, defenceModifiers.Count);
        Assert.Equal(BattleConstants.FortificationBonus, defenceModifiers.SumValues());
    }

    [Fact]
    public void ShouldRetrieveDefenceTerrainModifiers()
    {
        testGame.SetTileFeatures(defaultDefenderTile.Position, Constants.Hill);

        var defenceModifiers = BattleDamage.GetDefenceModifiers(new MapUnitCombatant(defaultAttackerUnit), new MapUnitCombatant(defaultDefenderUnit), defaultAttackerTile);

        Assert.Equal(1, defenceModifiers.Count);
        Assert.Equal(25, defenceModifiers.SumValues());
    }

    [Fact]
    public void ShouldNotRetrieveDefenceTerrainModifiersWhenUnitDoesntGetThem()
    {
        var defenderTile = testGame.GetTile(HexCoord.Zero);
        testGame.SetTileFeatures(defenderTile.Position, Constants.Hill);
        var defenderUnit = testGame.AddDefaultMeleeUnitWithUniques(defenderCiv, defenderTile, "No defensive terrain bonus");

        var defenceModifiers = BattleDamage.GetDefenceModifiers(new MapUnitCombatant(defaultAttackerUnit), new MapUnitCombatant(defenderUnit), defaultAttackerTile);

        Assert.True(defenceModifiers.IsEmpty());
        Assert.Equal(0, defenceModifiers.SumValues());
    }

    // ---- C#-only cases ----

    [Fact]
    public void MapUnitCombatantMirrorsItsUnit()
    {
        var combatant = new MapUnitCombatant(defaultAttackerUnit);
        Assert.Equal(100, combatant.GetMaxHealth());
        Assert.Equal(100, combatant.GetHealth());
        Assert.Equal("Warrior", combatant.GetName());
        Assert.Same(defaultAttackerTile, combatant.GetTile());
        Assert.Same(attackerCiv, combatant.GetCivInfo());
        Assert.Equal("Warrior of " + attackerCiv.CivId, combatant.ToString());
        Assert.Equal(new UncivSound(defaultAttackerUnit.BaseUnit.AttackSound!), combatant.GetAttackSound());
        Assert.True(combatant.IsMelee());
        Assert.False(combatant.IsRanged());
        Assert.False(combatant.IsCivilian());
        Assert.False(((ICombatant)combatant).IsCity());
        Assert.True(((ICombatant)combatant).IsLandUnit());
        Assert.False(((ICombatant)combatant).IsWaterUnit());
        Assert.False(((ICombatant)combatant).IsAirUnit());
        Assert.Equal("x[Warrior]", combatant.GetNotificationDisplay("x"));
        defaultAttackerUnit.InstanceName = "Bob";
        Assert.Equal("[Bob]", combatant.GetNotificationDisplay("x"));
        defaultAttackerUnit.InstanceName = "";
        Assert.Equal("x[Warrior]", combatant.GetNotificationDisplay("x"));

        Assert.Equal(new MapUnitCombatant(defaultAttackerUnit), combatant);
        Assert.Equal(new MapUnitCombatant(defaultAttackerUnit).GetHashCode(), combatant.GetHashCode());
        Assert.NotEqual(new MapUnitCombatant(defaultDefenderUnit), combatant);
        Assert.False(combatant.Equals("Warrior"));

        combatant.TakeDamage(30);
        Assert.Equal(70, combatant.GetHealth());
        Assert.False(combatant.IsDefeated());
        combatant.TakeDamage(70);
        Assert.True(combatant.IsDefeated());
    }

    [Fact]
    public void MapUnitCombatantStrengthAddsStrengthAmountUniques()
    {
        var unit = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(HexCoord.Zero), "Strength [+5] <when attacking>");
        var combatant = new MapUnitCombatant(unit);
        var enemy = new MapUnitCombatant(defaultDefenderUnit);
        Assert.Equal(8, combatant.GetDefendingStrength(enemy)); // base 8, the unique is conditional
        Assert.Equal(8, combatant.GetAttackingStrength(enemy));
    }

    [Fact]
    public void MapUnitCombatantTypesAndCityAreDistinguishedByIsMelee()
    {
        var ranged = testGame.AddDefaultRangedUnitWithUniques(attackerCiv, testGame.GetTile(HexCoord.Zero));
        var rangedCombatant = new MapUnitCombatant(ranged);
        Assert.True(rangedCombatant.IsRanged());
        Assert.False(rangedCombatant.IsMelee());
        // an unembarked ranged unit attacked by a ranged one defends with its ranged strength, otherwise with its (melee) strength
        Assert.Equal(ranged.BaseUnit.RangedStrength, rangedCombatant.GetDefendingStrength(rangedCombatant));
        Assert.Equal(ranged.BaseUnit.Strength, rangedCombatant.GetDefendingStrength(new MapUnitCombatant(defaultDefenderUnit)));
        Assert.Equal(ranged.BaseUnit.RangedStrength, rangedCombatant.GetAttackingStrength());
    }

    private CityObject Capital(Civilization civ, HexCoord at, int pop = 1) => testGame.AddCity(civ, testGame.GetTile(at), initialPopulation: pop);

    [Fact]
    public void CityCombatantBasics()
    {
        var city = Capital(defenderCiv, HexCoord.Zero);
        var combatant = new CityCombatant(city);
        var icombatant = (ICombatant)combatant;
        Assert.Equal(city.Name, combatant.ToString());
        Assert.Equal(city.Name, combatant.GetName());
        Assert.Equal(200, combatant.GetMaxHealth());
        Assert.Equal(city.Health, combatant.GetHealth());
        Assert.Same(defenderCiv, combatant.GetCivInfo());
        Assert.Same(city.GetCenterTile(), combatant.GetTile());
        Assert.True(combatant.IsVisibleTo(attackerCiv));
        Assert.Equal(UncivSound.Bombard, combatant.GetAttackSound());
        Assert.Same(Unciv.Models.Ruleset.Unit.UnitType.City, combatant.GetUnitType());
        Assert.True(icombatant.IsCity());
        Assert.True(icombatant.IsRanged());
        Assert.False(icombatant.IsMelee());
        Assert.False(icombatant.IsAirUnit());
        Assert.False(icombatant.IsWaterUnit());
        Assert.False(icombatant.IsLandUnit());
        Assert.False(icombatant.IsCivilian());
        Assert.Equal("", icombatant.GetNotificationDisplay("x"));
        Assert.Equal(combatant.CanAttack(), city.CanBombard());
    }

    [Fact]
    public void CityStateSeamUsesTheCityDefendingStrength()
    {
        var city = Capital(defenderCiv, HexCoord.Zero);
        Assert.Equal(new CityCombatant(city).GetDefendingStrength(), Unciv.Logic.Civilization.Diplomacy.CityStateSeams.GetCityDefendingStrength(city));
    }

    [Fact]
    public void CityCombatantFiltersAndDamage()
    {
        var city = Capital(defenderCiv, HexCoord.Zero);
        var combatant = new CityCombatant(city);
        Assert.True(combatant.MatchesFilter("City"));
        Assert.True(combatant.MatchesFilter("All"));
        Assert.True(combatant.MatchesFilter("all", false));
        Assert.True(combatant.MatchesFilter("{City} {All}"));
        Assert.False(combatant.MatchesFilter("non-[City]"));
        Assert.False(combatant.MatchesFilter("Land"));
        Assert.True(combatant.MatchesFilter("Capital"));
        // multiFilter = false does not parse non-[..]
        Assert.False(combatant.MatchesFilter("non-[City]", false));

        combatant.TakeDamage(50);
        Assert.Equal(150, combatant.GetHealth());
        Assert.False(combatant.IsDefeated());
        combatant.TakeDamage(1000);
        Assert.Equal(1, city.Health); // min health is 1
        Assert.True(combatant.IsDefeated());
        Assert.Equal(1, combatant.GetDefendingStrength());
        combatant.TakeDamage(int.MinValue); // wraps like Kotlin Int, then is clamped
        Assert.Equal(1, city.Health);
    }

    [Fact]
    public void CityCombatantStrengthFollowsTheModConstantsFormula()
    {
        var city = Capital(defenderCiv, HexCoord.Zero, 5);
        var combatant = new CityCombatant(city);
        var constants = defenderCiv.GameInfo.Ruleset.ModOptions.Constants;
        double expected = constants.CityStrengthBase + 5 * constants.CityStrengthPerPop;
        foreach (var terrain in city.GetCenterTile().AllTerrains)
            ((IHasUniques)terrain).ForEachMatchingUnique(UniqueType.GrantsCityStrength, GameContext.EmptyState, u => expected += int.Parse(u.Params[0], System.Globalization.CultureInfo.InvariantCulture));
        float techsPercentKnown = (float)defenderCiv.Tech.TechsResearched.Count / defenderCiv.GameInfo.Ruleset.Technologies.Count;
        expected += System.Math.Pow(techsPercentKnown * constants.CityStrengthFromTechsMultiplier, constants.CityStrengthFromTechsExponent)
            * constants.CityStrengthFromTechsFullMultiplier;
        expected += city.GetStrength();
        int expectedStrength = Unciv.Utils.KotlinMath.RoundToInt(expected);

        Assert.Equal(expectedStrength, combatant.GetCityStrength());
        Assert.Equal(expectedStrength, combatant.GetDefendingStrength());
        Assert.Equal(Unciv.Utils.KotlinMath.RoundToInt(combatant.GetCityStrength(null, CombatAction.Attack) * 0.75), combatant.GetAttackingStrength());
    }

    [Fact]
    public void CityCombatantStrengthCountsGarrisonAndStrengthUniques()
    {
        var city = Capital(defenderCiv, HexCoord.Zero);
        var combatant = new CityCombatant(city);
        int before = combatant.GetCityStrength();
        var garrison = testGame.AddUnit("Warrior", defenderCiv, city.GetCenterTile());
        Assert.Same(garrison, city.GetCenterTile().MilitaryUnit);
        Assert.True(combatant.GetCityStrength() >= before);

        var building = testGame.CreateBuilding("[+30] Strength");
        city.CityConstructions.AddBuilding(building.Name);
        Assert.True(combatant.GetCityStrength() >= before + 30);
    }

    [Fact]
    public void CityAndUnitCombatantsFeedGameContext()
    {
        var city = Capital(defenderCiv, HexCoord.Zero);
        var cityCombatant = new CityCombatant(city);
        var unitCombatant = new MapUnitCombatant(defaultAttackerUnit);
        var cityContext = GameContext.ForCombatants(cityCombatant, unitCombatant);
        Assert.Same(city, cityContext.City);
        Assert.Null(cityContext.Unit);
        var unitContext = GameContext.ForCombatants(unitCombatant, cityCombatant);
        Assert.Same(defaultAttackerUnit, unitContext.Unit);
        Assert.Null(unitContext.City);
    }

    [Fact]
    public void CityStrengthUniquesAppearAsCityModifiers()
    {
        var city = Capital(defenderCiv, HexCoord.Zero);
        defenderCiv.Policies.Adopt(testGame.CreatePolicy("[+20]% Strength for cities"), true);
        var cityCombatant = new CityCombatant(city);
        var enemy = new MapUnitCombatant(defaultAttackerUnit);
        Assert.Equal(20, BattleDamage.GetDefenceModifiers(enemy, cityCombatant, defaultAttackerTile).SumValues());
        Assert.Equal(20, BattleDamage.GetAttackModifiers(cityCombatant, enemy, city.GetCenterTile()).SumValues());
    }

    [Fact]
    public void BarbarianEnemyAddsDifficultyBonusAndSkipsMissingResource()
    {
        var barbarians = testGame.AddBarbarianCiv();
        var barbarianUnit = testGame.AddUnit("Warrior", barbarians, testGame.GetTile(HexCoord.Zero));
        var attackModifiers = BattleDamage.GetAttackModifiers(new MapUnitCombatant(defaultAttackerUnit), new MapUnitCombatant(barbarianUnit), defaultAttackerTile);
        int expected = Unciv.Utils.KotlinMath.FloatToInt(attackerCiv.GameInfo.GetDifficulty().BarbarianBonus * 100);
        Assert.Equal(expected, attackModifiers["Difficulty"]);
    }

    [Fact]
    public void AirSweepModifiersOnlyApplyToMapUnits()
    {
        Assert.True(BattleDamage.GetAirSweepAttackModifiers(new CityCombatant(Capital(defenderCiv, HexCoord.Zero))).IsEmpty());
        Assert.True(BattleDamage.GetAirSweepAttackModifiers(new MapUnitCombatant(defaultAttackerUnit)).IsEmpty());
    }

    [Fact]
    public void AdjacentEnemyStrengthMalusTakesTheLargestFirstParameterAsString()
    {
        // StrengthForAdjacentEnemies: Kotlin maxByOrNull compares the raw String parameter, so "9" beats "10"
        var free = defaultAttackerTile.Neighbors.Where(it => it != defaultDefenderTile && it.MilitaryUnit == null).Take(2).ToList();
        testGame.AddDefaultMeleeUnitWithUniques(defenderCiv, free[0], "[10]% Strength for enemy [Military] units in adjacent [Land] tiles");
        testGame.AddDefaultMeleeUnitWithUniques(defenderCiv, free[1], "[9]% Strength for enemy [Military] units in adjacent [Land] tiles");
        attackerCiv.GetDiplomacyManager(defenderCiv)!.DeclareWar();

        var modifiers = BattleDamage.GetAttackModifiers(new MapUnitCombatant(defaultAttackerUnit), new MapUnitCombatant(defaultDefenderUnit), defaultAttackerTile);
        Assert.Equal(9, modifiers["Adjacent enemy units"]);
    }

    // ---- damage maths ----

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void EqualStrengthsDealDamageAroundThirty(float randomness)
    {
        var attacker = new MapUnitCombatant(defaultAttackerUnit);
        var defender = new MapUnitCombatant(defaultDefenderUnit);
        // ratio 1 -> ratioModifier 1 -> 24 + 12 * randomness; the defender stands on desert, no modifiers except the flank-free attacker
        int toDefender = BattleDamage.CalculateDamageToDefender(attacker, defender, defaultAttackerTile, randomness);
        int toAttacker = BattleDamage.CalculateDamageToAttacker(attacker, defender, defaultAttackerTile, randomness);
        Assert.Equal(Unciv.Utils.KotlinMath.RoundToInt(24 + 12 * randomness), toDefender);
        Assert.Equal(toDefender, toAttacker);
    }

    [Fact]
    public void StrongerAttackerDealsMoreAndTakesLess()
    {
        var strong = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(HexCoord.Zero), "[+100]% Strength");
        var attacker = new MapUnitCombatant(strong);
        var defender = new MapUnitCombatant(defaultDefenderUnit);
        var from = testGame.GetTile(1, 0);
        int toDefender = BattleDamage.CalculateDamageToDefender(attacker, defender, from, 0.5f);
        int toAttacker = BattleDamage.CalculateDamageToAttacker(attacker, defender, from, 0.5f);
        Assert.True(toDefender > 30);
        Assert.True(toAttacker < 30);
    }

    [Fact]
    public void WoundedAttackerDealsLessDamage()
    {
        var attacker = new MapUnitCombatant(defaultAttackerUnit);
        var defender = new MapUnitCombatant(defaultDefenderUnit);
        int healthy = BattleDamage.CalculateDamageToDefender(attacker, defender, defaultAttackerTile, 0.5f);
        defaultAttackerUnit.Health = 40; // 1 - 60/300 = 0.8
        int wounded = BattleDamage.CalculateDamageToDefender(attacker, defender, defaultAttackerTile, 0.5f);
        Assert.Equal(Unciv.Utils.KotlinMath.RoundToInt(30f * (1 - 60 / 300f)), wounded);
        Assert.True(wounded < healthy);
    }

    [Fact]
    public void CivilianDefendersTakeFixedDamageAndNeverRetaliate()
    {
        var settler = testGame.AddUnit("Settler", defenderCiv, testGame.GetTile(HexCoord.Zero));
        var attacker = new MapUnitCombatant(defaultAttackerUnit);
        var civilian = new MapUnitCombatant(settler);
        Assert.True(civilian.IsCivilian());
        Assert.Equal(BattleConstants.DamageToCivilianUnit, BattleDamage.CalculateDamageToDefender(attacker, civilian, defaultAttackerTile, 0.5f));
        Assert.Equal(0, BattleDamage.CalculateDamageToAttacker(attacker, civilian, defaultAttackerTile, 0.5f));
    }

    [Fact]
    public void RangedAttackerTakesNoDamage()
    {
        var ranged = testGame.AddDefaultRangedUnitWithUniques(attackerCiv, testGame.GetTile(HexCoord.Zero));
        Assert.Equal(0, BattleDamage.CalculateDamageToAttacker(new MapUnitCombatant(ranged), new MapUnitCombatant(defaultDefenderUnit), testGame.GetTile(1, 0), 0.5f));
    }

    [Fact]
    public void ZeroStrengthIsFlooredAtOne()
    {
        var attacker = new MapUnitCombatant(defaultAttackerUnit);
        var defender = new MapUnitCombatant(defaultDefenderUnit);
        Assert.True(BattleDamage.GetAttackingStrength(attacker, defender, defaultAttackerTile) >= 1f);
        Assert.True(BattleDamage.GetDefendingStrength(attacker, defender, defaultAttackerTile) >= 1f);
    }

    [Fact]
    public void RandomnessIsDeterministicInTurnAndPosition()
    {
        var attacker = new MapUnitCombatant(defaultAttackerUnit);
        float first = BattleDamage.GetRandomness(attacker);
        Assert.Equal(first, BattleDamage.GetRandomness(attacker));
        Assert.InRange(first, 0f, 1f);
        Assert.Equal(new Unciv.Utils.GameRandom((long)attackerCiv.GameInfo.Turns * defaultAttackerTile.Position.ToVector2().GetHashCode()).NextFloat(), first);
        // damage with the default randomness equals damage with that explicit randomness
        var defender = new MapUnitCombatant(defaultDefenderUnit);
        Assert.Equal(
            BattleDamage.CalculateDamageToDefender(attacker, defender, defaultAttackerTile, BattleDamage.GetRandomness(defender)),
            BattleDamage.CalculateDamageToDefender(attacker, defender, defaultAttackerTile));
    }

    [Fact]
    public void ExtraRangedAttackIsASeamUntilBattleIsPorted()
    {
        Assert.Throws<NotImplementedException>(() =>
            BattleDamage.GetExtraRangedAttackBonusDamage(new MapUnitCombatant(defaultAttackerUnit), new MapUnitCombatant(defaultDefenderUnit), defaultAttackerTile)); // TODO(T083)
    }

    [Fact]
    public void AttackableTileIsAValueRecord()
    {
        var a = new AttackableTile(defaultAttackerTile, defaultDefenderTile, 1.5f, null);
        Assert.Equal(a, new AttackableTile(defaultAttackerTile, defaultDefenderTile, 1.5f, null));
        Assert.NotEqual(a, a with { MovementLeftAfterMovingToAttackTile = 0f });
        Assert.Equal(a.GetHashCode(), new AttackableTile(defaultAttackerTile, defaultDefenderTile, 1.5f, null).GetHashCode());
        Assert.Contains("MovementLeftAfterMovingToAttackTile = 1.5", a.ToString(), StringComparison.Ordinal);
    }

    // ---- GreatGeneralImplementation.getGreatGeneralBonus ----

    private const string GeneralAura = "[15]% Strength bonus for [Military] units within [2] tiles";

    [Fact]
    public void GreatGeneralBonusIsEmptyWithoutAGeneral()
    {
        Assert.Equal(("", 0), GreatGeneralImplementation.GetGreatGeneralBonus(new MapUnitCombatant(defaultAttackerUnit), new MapUnitCombatant(defaultDefenderUnit), CombatAction.Attack));
    }

    [Fact]
    public void GreatGeneralBonusAppliesWithinTheRadiusOnly()
    {
        var general = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(HexCoord.Zero), GeneralAura);
        var enemy = new MapUnitCombatant(defaultDefenderUnit);
        var near = GreatGeneralImplementation.GetGreatGeneralBonus(new MapUnitCombatant(defaultAttackerUnit), enemy, CombatAction.Attack);
        Assert.Equal((general.Name, 15), near);
        Assert.Equal(15, AttackModifiers(defaultAttackerUnit, defaultAttackerTile)[general.Name]);

        var farTile = testGame.GetTile(-3, 0);
        var farUnit = testGame.AddUnit("Warrior", attackerCiv, farTile);
        Assert.True(farTile.AerialDistanceTo(general.GetTile()) > 2);
        Assert.Equal(("", 0), GreatGeneralImplementation.GetGreatGeneralBonus(new MapUnitCombatant(farUnit), enemy, CombatAction.Attack));
    }

    [Fact]
    public void GreatGeneralBonusTakesTheFirstLargestBonus()
    {
        var small = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(HexCoord.Zero), "[10]% Strength bonus for [Military] units within [2] tiles");
        var large = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(1, 0), "[20]% Strength bonus for [Military] units within [2] tiles");
        var result = GreatGeneralImplementation.GetGreatGeneralBonus(new MapUnitCombatant(defaultAttackerUnit), new MapUnitCombatant(defaultDefenderUnit), CombatAction.Attack);
        Assert.Equal((large.Name, 20), result);
        Assert.NotEqual(small.Name, result.Name);
    }

    [Fact]
    public void GreatGeneralBonusFilterIsMatchedAgainstTheUnit()
    {
        testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(HexCoord.Zero), "[15]% Strength bonus for [Water] units within [2] tiles");
        Assert.Equal(("", 0), GreatGeneralImplementation.GetGreatGeneralBonus(new MapUnitCombatant(defaultAttackerUnit), new MapUnitCombatant(defaultDefenderUnit), CombatAction.Attack));
    }
}
