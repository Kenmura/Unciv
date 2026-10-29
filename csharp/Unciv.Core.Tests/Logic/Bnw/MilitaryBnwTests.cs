using Unciv.Core.Tests.Support;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset.Unique;
using BattleObject = Unciv.Logic.Battle.Battle;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Core.Tests.Logic.Bnw;

// B023 (bnw-spec 4.12): the military data effects of Brave New World on the REAL generated ruleset: Sapper, Heavy Charge, the post-combat
// promotions of the Mystic Blade, the era-scaled warmonger penalty, the AI handicap XP and the three military tenets. G&K stays as it was.
[Collection("RulesetCache")]
public sealed class MilitaryBnwTests
{
    private static readonly string[] MysticBladeOutcomes =
        ["Invulnerability", "Sneak Attack", "Heroism", "Ambition", "Restlessness", "Recruitment", "Enemy Blade", "Evil Spirits"];

    private readonly TestGame game;
    private readonly CivilizationObject a, b;

    public MilitaryBnwTests()
    {
        game = new TestGame(BaseRuleset.Civ_V_BNW);
        game.MakeHexagonalMap(6);
        a = game.AddCiv();
        b = game.AddCiv();
        a.Tech.Era = game.Ruleset.Eras["Ancient era"];
        b.Tech.Era = game.Ruleset.Eras["Ancient era"];
    }

    private static DamageDealt Attack(MapUnitClass attacker, MapUnitClass defender) =>
        BattleObject.Attack(new MapUnitCombatant(attacker), new MapUnitCombatant(defender));

    private MapUnitClass Unit(string name, CivilizationObject civ, int x, int y)
    {
        var unit = game.AddUnit(name, civ, game.GetTile(x, y));
        unit.CurrentMovement = unit.GetMaxMovement();
        foreach (var promotion in unit.BaseUnit.Promotions) unit.Promotions.AddPromotion(promotion, true); // what TileMap.PlaceUnitNearTile does
        return unit;
    }

    // ---- the generated data

    [Fact]
    public void TheGeneratedUnitsCarryTheirPromotions()
    {
        var r = game.Ruleset;
        Assert.Contains("Sapper", r.Units["Siege Tower"].Promotions);
        Assert.Contains("Heavy Charge", r.Units["Winged Hussar"].Promotions);
        Assert.Contains("Mystic Blade", r.Units["Kris Swordsman"].Promotions);
        Assert.Equal(MysticBladeOutcomes, r.UnitPromotions["Mystic Blade"].Uniques
            .Where(it => it.StartsWith("Can randomly gain", StringComparison.Ordinal)).Select(it => it.Split('[', ']')[1]));
        Assert.All(MysticBladeOutcomes, name => Assert.True(r.UnitPromotions.ContainsKey(name)));
        Assert.Equal(2, r.ModOptions.Constants.SapperBonusRange);
    }

    [Fact]
    public void TheEraAndHandicapDataReachTheRuleset()
    {
        var r = game.Ruleset;
        Assert.Equal([50, 60, 70, 80, 90, 100, 100, 100, 100], r.Eras.Values.OrderBy(it => it.EraNumber).Select(it => it.WarmongerPercent));
        Assert.Equal([0, 0, 0, 0, 10, 10, 30, 30], r.Difficulties.Values.Select(it => it.AiFreeXp));
        Assert.Equal([0, 0, 0, 0, 0, 25, 50, 100], r.Difficulties.Values.Select(it => it.AiFreeXpPercent));
    }

    // ---- Sapper

    private Counter<string> CityAttack(MapUnitClass attacker)
    {
        var city = game.GetTile(5, 0).GetCity() ?? game.AddCity(b, game.GetTile(5, 0));
        return BattleDamage.GetAttackModifiers(new MapUnitCombatant(attacker), new CityCombatant(city), attacker.GetTile());
    }

    [Theory]
    [InlineData(4, 1, 50)] // the sapper stands next to the attacker
    [InlineData(2, 0, 50)] // exactly SapperBonusRange (2) away from the attacking tile
    [InlineData(1, 0, 0)] // three tiles away
    public void ASapperGivesCityAttackersWithinTwoTilesItsBonus(int sapperX, int sapperY, int expected)
    {
        var attacker = Unit("Swordsman", a, 4, 0);
        Unit("Siege Tower", a, sapperX, sapperY);
        var modifiers = CityAttack(attacker);
        Assert.Equal(expected, modifiers.Get("Sapper"));
    }

    [Fact]
    public void ASapperOfAnotherCivOrAgainstAUnitDoesNothingAndBonusesDoNotStack()
    {
        var attacker = Unit("Swordsman", a, 4, 0);
        Unit("Siege Tower", b, 3, 0);
        Assert.False(CityAttack(attacker).ContainsKey("Sapper"));
        Unit("Siege Tower", a, 3, 1);
        Unit("Siege Tower", a, 2, 1);
        Assert.Equal(50, CityAttack(attacker)["Sapper"]);

        var target = Unit("Warrior", b, 5, 3);
        var unitAttack = BattleDamage.GetAttackModifiers(new MapUnitCombatant(attacker), new MapUnitCombatant(target), attacker.GetTile());
        Assert.False(unitAttack.ContainsKey("Sapper"));
    }

    // ---- Heavy Charge

    private (DamageDealt Dealt, MapUnitClass Hussar, MapUnitClass Defender) Charge(bool charge, bool blocked)
    {
        var hussar = Unit("Winged Hussar", a, 1, 0);
        if (!charge) hussar.Promotions.RemovePromotion("Heavy Charge");
        Assert.Equal(charge, hussar.HasUnique(UniqueType.HeavyCharge));
        var defender = Unit("Musketman", b, 0, 0);
        if (blocked)
            foreach (var tile in game.GetTile(0, 0).Neighbors.Where(it => it != hussar.GetTile())) Unit("Warrior", b, tile.Position.X, tile.Position.Y);
        return (Attack(hussar, defender), hussar, defender);
    }

    [Fact]
    public void HeavyChargePushesASurvivingDefenderOneTileBack()
    {
        var (dealt, hussar, defender) = Charge(true, false);
        Assert.True(defender.Health > 0 && dealt.AttackerDealt > 0);
        Assert.Equal(100 - dealt.AttackerDealt, defender.Health);
        Assert.Equal(2, defender.GetTile().AerialDistanceTo(hussar.GetTile())); // pushed away from the attacker, never into the attacker's reach
        Assert.NotEqual(game.GetTile(0, 0), defender.GetTile());
    }

    [Fact]
    public void HeavyChargeAgainstADefenderWithNowhereToGoDealsHalfAgainAsMuch()
    {
        var (plain, _, plainDefender) = new MilitaryBnwTests().Charge(false, true);
        var (charged, _, defender) = Charge(true, true);
        Assert.Equal(game.GetTile(0, 0), defender.GetTile()); // blocked on every side
        Assert.Equal(plain.AttackerDealt + plain.AttackerDealt / 2, charged.AttackerDealt);
        Assert.Equal(100 - charged.AttackerDealt, defender.Health);
        Assert.Equal(100 - plain.AttackerDealt, plainDefender.Health);
    }

    // ---- post-combat promotions

    private int Outcomes(MapUnitClass unit) => MysticBladeOutcomes.Count(it => unit.Promotions.Promotions.Contains(it));

    [Fact]
    public void AMysticBladeGainsExactlyOneOfTheEightPromotionsAfterItsFirstCombat()
    {
        var kris = Unit("Kris Swordsman", a, 1, 0);
        Assert.Equal(0, Outcomes(kris));
        Attack(kris, Unit("Warrior", b, 0, 0));
        Assert.Equal(1, Outcomes(kris));
        kris.CurrentMovement = kris.GetMaxMovement();
        kris.AttacksThisTurn = 0;
        Attack(kris, Unit("Warrior", b, 2, 0));
        Assert.Equal(1, Outcomes(kris)); // exclusive: no second one
    }

    [Fact]
    public void ADefendingMysticBladeGainsOneToo_AndAPlainSwordsmanNone()
    {
        var kris = Unit("Kris Swordsman", a, 1, 0);
        Attack(Unit("Warrior", b, 0, 0), kris);
        Assert.Equal(1, Outcomes(kris));
        var plain = Unit("Swordsman", a, 3, 0);
        Attack(plain, Unit("Warrior", b, 4, 0));
        Assert.Equal(0, Outcomes(plain));
    }

    [Fact]
    public void ADefeatedMysticBladeGainsNothingAndTheChoiceIsDeterministic()
    {
        var kris = Unit("Kris Swordsman", a, 1, 0);
        kris.Health = 1;
        Attack(Unit("Musketman", b, 0, 0), kris);
        Assert.True(kris.IsDestroyed || Outcomes(kris) <= 1);

        string First()
        {
            var g = new MilitaryBnwTests();
            var k = g.Unit("Kris Swordsman", g.a, 1, 0);
            Attack(k, g.Unit("Warrior", g.b, 0, 0));
            return MysticBladeOutcomes.Single(it => k.Promotions.Promotions.Contains(it));
        }
        Assert.Equal(First(), First());
    }

    // ---- warmonger

    [Fact]
    public void TheWarmongerPenaltyScalesWithTheAggressorsEraAndACityStateVictim()
    {
        var cs = game.AddCiv(cityStateType: "Mercantile");
        Assert.Equal(-2.5f, WarmongerRules.Scale(-5f, a, b)); // Ancient 50%
        a.Tech.Era = game.Ruleset.Eras["Industrial era"];
        Assert.Equal(-4.5f, WarmongerRules.Scale(-5f, a, b));
        Assert.Equal(-2.25f, WarmongerRules.Scale(-5f, a, cs)); // x 50% for a city-state victim
        a.Tech.Era = game.Ruleset.Eras["Modern era"];
        Assert.Equal(-5f, WarmongerRules.Scale(-5f, a, b));
        Assert.Equal(0f, WarmongerRules.Scale(0f, a, cs));
    }

    [Fact]
    public void GnkWarmongerPenaltyIsUnchanged()
    {
        var gnk = new TestGame();
        gnk.MakeHexagonalMap(3);
        var x = gnk.AddCiv();
        var cs = gnk.AddCiv(cityStateType: "Mercantile");
        Assert.Equal(-5f, WarmongerRules.Scale(-5f, x, cs));
    }

    [Fact]
    public void DeclaringWarGivesTheThirdCivsTheEraScaledPenalty()
    {
        var c = game.AddCiv();
        game.AddCity(a, game.GetTile(-3, 0)); // civs without a city or unit count as dead and are not known
        game.AddCity(b, game.GetTile(3, 0));
        game.AddCity(c, game.GetTile(0, 3));
        foreach (var x in new[] { b, c }) a.DiplomacyFunctions.MakeCivilizationsMeet(x);
        c.DiplomacyFunctions.MakeCivilizationsMeet(b);
        foreach (var (p, q) in new[] { (a, b), (b, a), (a, c), (c, a), (b, c), (c, b) })
            p.GetDiplomacyManager(q)!.DiplomaticStatus = DiplomaticStatus.Peace;
        a.GetDiplomacyManager(b)!.DeclareWar();
        Assert.Equal(-2.5f, c.GetDiplomacyManager(a)!.GetModifier(DiplomaticModifiers.WarMongerer));
    }

    // ---- AI handicap

    [Fact]
    public void AnAiMajorCivsNewMilitaryUnitsStartWithTheHandicapXpAndEarnMoreInCombat()
    {
        game.SetDifficulty("Deity");
        var city = game.AddCity(a, game.GetTile(-4, 0));
        var human = game.AddCiv(isPlayer: true);
        var humanCity = game.AddCity(human, game.GetTile(4, 0));
        var warrior = Unit("Warrior", a, -3, 0);
        game.Ruleset.Units["Warrior"].AddConstructionBonuses(warrior, city.CityConstructions);
        Assert.Equal(30, warrior.Promotions.XP);
        var settler = Unit("Settler", a, -3, 1);
        game.Ruleset.Units["Settler"].AddConstructionBonuses(settler, city.CityConstructions);
        Assert.Equal(0, settler.Promotions.XP);
        var own = Unit("Warrior", human, 3, 0);
        game.Ruleset.Units["Warrior"].AddConstructionBonuses(own, humanCity.CityConstructions);
        Assert.Equal(0, own.Promotions.XP);

        BattleObject.AddXp(new MapUnitCombatant(warrior), 10, new MapUnitCombatant(own));
        Assert.Equal(30 + 20, warrior.Promotions.XP); // +100% at Deity
        BattleObject.AddXp(new MapUnitCombatant(own), 10, new MapUnitCombatant(warrior));
        Assert.Equal(10, own.Promotions.XP);
    }

    // ---- the three military tenets

    private void Follow(CivilizationObject civ, string ideology, params string[] tenets)
    {
        civ.Tech.Era = game.Ruleset.Eras["Modern era"];
        civ.Policies.ChooseIdeology(game.Ruleset.PolicyBranches[ideology]);
        civ.Policies.FreeTenets = 100;
        foreach (var tenet in tenets) civ.Policies.Adopt(game.Ruleset.Policies[tenet]);
    }

    [Fact]
    public void PatrioticWarAddsFifteenPercentStrengthInFriendlyLandsOnly()
    {
        var city = game.AddCity(b, game.GetTile(0, 0));
        var home = game.GetTile(1, 0);
        Assert.Equal(b, home.GetOwner());
        var attacker = Unit("Swordsman", a, 2, 0);
        var onHome = Unit("Swordsman", b, 1, 0);
        var abroad = Unit("Swordsman", b, -5, 0);
        float Strength(MapUnitClass defender) => BattleDamage.GetDefendingStrength(new MapUnitCombatant(attacker), new MapUnitCombatant(defender), attacker.GetTile());
        float homeBefore = Strength(onHome), abroadBefore = Strength(abroad);
        Follow(b, "Order", "Patriotic War");
        Assert.Equal(homeBefore + game.Ruleset.Units["Swordsman"].Strength * 0.15f, Strength(onHome), 2); // modifiers add up
        Assert.Equal(abroadBefore, Strength(abroad));
        Assert.NotNull(city);
    }

    [Fact]
    public void EliteForcesRemoveTheWoundedDamagePenalty()
    {
        var attacker = Unit("Swordsman", a, 1, 0);
        var defender = Unit("Swordsman", b, 0, 0);
        attacker.Health = 40;
        int wounded() => BattleDamage.CalculateDamageToDefender(new MapUnitCombatant(attacker), new MapUnitCombatant(defender), null, 0.5f);
        int before = wounded();
        attacker.Health = 100;
        int healthy = wounded();
        attacker.Health = 40;
        Assert.True(before < healthy);
        Follow(a, "Autocracy", "Elite Forces");
        Assert.Equal(healthy, wounded());
    }

    [Fact]
    public void LightningWarfareGivesNewArmoredUnitsTheirPromotion()
    {
        var city = game.AddCity(a, game.GetTile(-4, 0));
        Follow(a, "Autocracy", "Elite Forces", "Lightning Warfare");
        var tank = Unit("Tank", a, -3, 0);
        var sword = Unit("Swordsman", a, -3, 1);
        game.Ruleset.Units["Tank"].AddConstructionBonuses(tank, city.CityConstructions);
        game.Ruleset.Units["Swordsman"].AddConstructionBonuses(sword, city.CityConstructions);
        Assert.Contains("Lightning Warfare", tank.Promotions.Promotions);
        Assert.True(tank.HasUnique(UniqueType.IgnoresZOC));
        Assert.DoesNotContain("Lightning Warfare", sword.Promotions.Promotions);
        Assert.False(sword.HasUnique(UniqueType.IgnoresZOC));
    }
}
