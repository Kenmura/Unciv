using Unciv.Core.Tests.Support;
using Unciv.Logic.Battle;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using BattleObject = Unciv.Logic.Battle.Battle;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Battle;

// C#-only tests (T083) for Nuke.mayUseNuke and the branches of Nuke.NUKE the Kotlin BattleTest does not reach.
[Collection("RulesetCache")]
public class NukeTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject attackerCiv;
    private readonly CivilizationObject defenderCiv;

    public NukeTests()
    {
        testGame.MakeHexagonalMap(6);
        attackerCiv = testGame.AddCiv(isPlayer: true);
        defenderCiv = testGame.AddCiv(isPlayer: true);
        testGame.AddCity(attackerCiv, testGame.GetTile(0, 5)); // the nukes launch from here
    }

    private MapUnitClass Nuke(int strength, string? blastRadius = null, string unitName = "Missile", string? consumes = null)
    {
        var uniques = new List<string> { $"Nuclear weapon of Strength [{strength}]", "Self-destructs when attacking" };
        if (blastRadius != null) uniques.Add($"Blast radius [{blastRadius}]");
        if (consumes != null) uniques.Add($"Consumes [1] [{consumes}]");
        var baseUnit = testGame.CreateBaseUnit(unitName, uniques.ToArray());
        baseUnit.Movement = 1;
        baseUnit.Range = 12;
        baseUnit.Strength = 100;
        baseUnit.RangedStrength = 100;
        var unit = testGame.AddUnit(baseUnit.Name, attackerCiv, testGame.GetTile(0, 5));
        unit.CurrentMovement = 1f;
        return unit;
    }

    private void Meet(CivilizationObject other, bool war)
    {
        attackerCiv.DiplomacyFunctions.MakeCivilizationsMeet(other);
        if (war) attackerCiv.GetDiplomacyManager(other)!.DeclareWar();
    }

    private static bool MayUse(MapUnitClass nuke, TileClass target) => Unciv.Logic.Battle.Nuke.MayUseNuke(new MapUnitCombatant(nuke), target);

    private static void Nuke_(MapUnitClass nuke, TileClass target) => Unciv.Logic.Battle.Nuke.NUKE(new MapUnitCombatant(nuke), target);

    // ---- mayUseNuke

    [Fact]
    public void ANukeMayNotTargetItsOwnTileAnUnexploredTileOrOneOutOfRange()
    {
        var nuke = Nuke(1);
        Assert.False(MayUse(nuke, nuke.GetTile()));

        var unexplored = testGame.GetTile(0, 0);
        unexplored.SetExplored(attackerCiv, false);
        Assert.False(MayUse(nuke, unexplored));

        var farAway = testGame.GetTile(0, -6);
        farAway.SetExplored(attackerCiv, true);
        nuke.BaseUnit.Range = 3;
        Assert.False(MayUse(nuke, farAway));
    }

    [Fact]
    public void ANukeMayOnlyHitCivsItCouldAttack()
    {
        var nuke = Nuke(1, "2");
        var target = testGame.GetTile(0, 0);
        target.SetExplored(attackerCiv, true);
        Assert.True(MayUse(nuke, target)); // nobody there

        testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(0, 0));
        Assert.True(MayUse(nuke, target)); // our own units do not matter

        var barbarians = testGame.AddBarbarianCiv();
        testGame.AddUnit("Brute", barbarians, testGame.GetTile(1, 0));
        Assert.True(MayUse(nuke, target)); // neither do barbarians

        var stranger = testGame.AddCiv(); // our warrior next to it makes the two civs meet, at peace
        testGame.AddUnit("Warrior", stranger, testGame.GetTile(1, -1));
        Assert.True(MayUse(nuke, target)); // a plain peace does not protect

        var treaty = new Unciv.Logic.Trade.Trade();
        treaty.OurOffers.Add(new Unciv.Logic.Trade.TradeOffer(Constants.PeaceTreaty, Unciv.Logic.Trade.TradeOfferType.Treaty, 1, 10));
        attackerCiv.GetDiplomacyManager(stranger)!.Trades.Add(treaty);
        Assert.False(MayUse(nuke, target)); // a peace treaty does

        attackerCiv.GetDiplomacyManager(stranger)!.DeclareWar();
        Assert.True(MayUse(nuke, target));

        var unknown = testGame.AddCiv();
        testGame.AddUnit("Warrior", unknown, testGame.GetTile(-1, 1));
        attackerCiv.Diplomacy.Remove(unknown.CivId);
        Assert.False(MayUse(nuke, target)); // an unknown civ cannot be attacked
    }

    [Fact]
    public void AnOwnedTileOfAnUnknownCivBlocksTheNukeAndDefeatedCivsAreIgnored()
    {
        var nuke = Nuke(1, "1");
        var target = testGame.GetTile(0, 0);
        target.SetExplored(attackerCiv, true);
        var owner = testGame.AddCiv();
        var city = testGame.AddCity(owner, testGame.GetTile(0, -1));
        city.Expansion.TakeOwnership(target);
        Assert.False(MayUse(nuke, target)); // the owner is an unknown civ

        city.Civ.Cities.ToList().ForEach(it => it.DestroyCity(true));
        Assert.True(owner.IsDefeated());
        Assert.True(MayUse(nuke, target));
    }

    // ---- NUKE

    [Fact]
    public void ADeviceWithoutTheNuclearWeaponUniqueDoesNothing()
    {
        var warrior = testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(0, 4));
        var victim = testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(0, 3));

        Nuke_(warrior, testGame.GetTile(0, 3));

        Assert.Equal(100, victim.Health);
        Assert.False(warrior.IsDestroyed);
    }

    [Theory]
    [InlineData(0, 20, 49)]
    [InlineData(1, 30, 100)]
    [InlineData(2, 100, 100)]
    public void UnitsOutsideGroundZeroLoseHealthAccordingToTheNukeStrength(int strength, int minDamage, int maxDamage)
    {
        var nuke = Nuke(strength, "1");
        Meet(defenderCiv, war: true);
        var ground = testGame.GetTile(0, 0);
        var neighbor = testGame.GetTile(1, 0);
        var victim = testGame.AddUnit("Warrior", defenderCiv, neighbor);
        victim.Health = 100;

        AttackNukeAt(nuke, ground);

        int damage = 100 - victim.Health;
        Assert.InRange(damage, minDamage, maxDamage);
        Assert.True(nuke.IsDestroyed);
    }

    private void AttackNukeAt(MapUnitClass nuke, TileClass target) =>
        BattleObject.AttackOrNuke(new MapUnitCombatant(nuke), new AttackableTile(nuke.GetTile(), target, 0f, null));

    [Fact]
    public void CiviliansDieOnlyWhenTheyWouldDropToTheDeathThreshold()
    {
        var nuke = Nuke(2, "1");
        Meet(defenderCiv, war: true);
        var healthy = testGame.AddUnit("Worker", defenderCiv, testGame.GetTile(1, 0));
        testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(-1, 0)); // keeps the civ alive

        AttackNukeAt(nuke, testGame.GetTile(0, 0));

        Assert.True(healthy.IsDestroyed); // strength 2 does 100 damage
    }

    [Fact]
    public void AWeakNukeLeavesAHealthyCivilianAliveAndUnhurt()
    {
        var nuke = Nuke(0, "1"); // 20 to 49 damage: 100 - damage stays above the death threshold of 40, and civilians are never merely wounded
        Meet(defenderCiv, war: true);
        var worker = testGame.AddUnit("Worker", defenderCiv, testGame.GetTile(1, 0));
        var wounded = testGame.AddUnit("Worker", defenderCiv, testGame.GetTile(0, 1));
        wounded.Health = 60;

        AttackNukeAt(nuke, testGame.GetTile(0, 0));

        Assert.False(worker.IsDestroyed);
        Assert.Equal(100, worker.Health);
        Assert.True(wounded.IsDestroyed); // 60 - at least 20 is at most 40
    }

    [Fact]
    public void MissingResourcesHalveTheDamage()
    {
        var nuke = Nuke(2, "0", consumes: "Uranium");
        attackerCiv.ResourceStockpiles["Uranium"] = -2;
        Meet(defenderCiv, war: true);
        var victim = testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(0, 0));

        AttackNukeAt(nuke, testGame.GetTile(0, 0));

        Assert.Equal(50, victim.Health); // 100 * 0.5
    }

    [Fact]
    public void GroundZeroPillagesImprovementsAndRoadsAndAddsFallout()
    {
        var nuke = Nuke(1, "1");
        var ground = testGame.GetTile(0, 0);
        ground.Improvement = "Farm";
        ground.RoadStatus = RoadStatus.Road;
        var removable = testGame.GetTile(0, 1);
        removable.Improvement = "Barbarian encampment";

        AttackNukeAt(nuke, ground);

        Assert.True(ground.ImprovementIsPillaged);
        Assert.True(ground.RoadIsPillaged);
        Assert.Contains("Fallout", ground.TerrainFeatures);
    }

    [Fact]
    public void TerrainFeaturesCanBeDestroyedByNukes()
    {
        var nuke = Nuke(1, "0");
        var ground = testGame.SetTileTerrainAndFeatures(new HexCoord(0, 0), "Grassland", "Forest");

        AttackNukeAt(nuke, ground);

        Assert.DoesNotContain("Forest", ground.TerrainFeatures); // ground zero: any chance above 0 destroys
        Assert.Contains("Fallout", ground.TerrainFeatures);
    }

    [Fact]
    public void WaterTilesGetNoFalloutButAreStillPillaged()
    {
        var nuke = Nuke(1, "0");
        var ocean = testGame.SetTileTerrain(new HexCoord(0, 0), "Ocean");

        AttackNukeAt(nuke, ocean);

        Assert.DoesNotContain("Fallout", ocean.TerrainFeatures);
    }

    [Fact]
    public void ACityIsDamagedAndLosesPopulationAccordingToTheStrength()
    {
        var nuke = Nuke(1, "0");
        Meet(defenderCiv, war: true);
        testGame.AddCity(defenderCiv, testGame.GetTile(-5, 0)); // the capital
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(0, 0), initialPopulation: 10);
        var healthBefore = city.Health;

        AttackNukeAt(nuke, testGame.GetTile(0, 0));

        Assert.Equal(healthBefore / 2, city.Health);
        Assert.InRange(city.Population.Population, 4, 7);
        Assert.Contains(defenderCiv.Notifications, it => it.Text.Contains("has attacked", StringComparison.Ordinal) || it.Text.Contains("has destroyed", StringComparison.Ordinal));
    }

    [Fact]
    public void ATotallyDestroyingNukeRemovesACityAndTheLastCityDefeatsItsCiv()
    {
        var nuke = Nuke(3, "0");
        Meet(defenderCiv, war: true);
        var capital = testGame.AddCity(defenderCiv, testGame.GetTile(-5, 0), initialPopulation: 1);
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(0, 0), initialPopulation: 3);

        AttackNukeAt(nuke, testGame.GetTile(0, 0));

        Assert.False(testGame.GetTile(0, 0).IsCityCenter());
        Assert.True(capital.GetCenterTile().IsCityCenter());
        Assert.DoesNotContain(city, defenderCiv.Cities);
    }

    [Fact]
    public void EveryCivIsToldAboutTheExplosion()
    {
        var nuke = Nuke(1, "1");
        Meet(defenderCiv, war: false);
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(0, -1));
        city.Expansion.TakeOwnership(testGame.GetTile(0, 0));
        var bystander = testGame.AddCiv(isPlayer: true);
        testGame.AddUnit("Warrior", bystander, testGame.GetTile(-5, 0));
        Meet(bystander, war: false);
        var stranger = testGame.AddCiv(isPlayer: true);
        testGame.AddUnit("Warrior", stranger, testGame.GetTile(0, -5));

        AttackNukeAt(nuke, testGame.GetTile(0, 0));

        var name = nuke.Name;
        Assert.Contains(defenderCiv.Notifications, it => it.Text == $"A(n) [{name}] from [{attackerCiv.CivName}] has exploded in our territory!");
        Assert.Contains(bystander.Notifications, it => it.Text == $"A(n) [{name}] has been detonated by [{attackerCiv.CivName}]!");
        Assert.Contains(stranger.Notifications, it => it.Text == $"A(n) [{name}] has been detonated by [an unknown civilization]!");
        Assert.Contains(attackerCiv.Notifications, it => it.Text == $"After being hit by our [{name}], [{defenderCiv.CivName}] has declared war on us!");
    }

    [Fact]
    public void ANukeShotDownByAnInterceptorDoesNotDetonate()
    {
        var nuke = Nuke(1, "1", unitName: "Fighter");
        nuke.Health = 1;
        Meet(defenderCiv, war: false); // the explosion declares war, and the war lets the interceptor shoot
        testGame.AddCity(defenderCiv, testGame.GetTile(0, 0));
        var interceptor = testGame.AddUnit("Fighter", defenderCiv, testGame.GetTile(0, 0));
        var neighborVictim = testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(1, 0));

        AttackNukeAt(nuke, testGame.GetTile(0, 0));

        Assert.True(new MapUnitCombatant(nuke).IsDefeated());
        Assert.Equal(100, neighborVictim.Health);
        Assert.Equal(1, interceptor.AttacksThisTurn);
        Assert.Contains(attackerCiv.Notifications, it => it.Text.StartsWith("After an attempted attack by our [", StringComparison.Ordinal));
    }

    [Fact]
    public void AStrengthTwoNukeKillsAboutSixtyToEightyPercentOfALargeCity()
    {
        var nuke = Nuke(2, "0");
        Meet(defenderCiv, war: true);
        testGame.AddCity(defenderCiv, testGame.GetTile(-5, 0)); // the capital
        var city = testGame.AddCity(defenderCiv, testGame.GetTile(0, 0), initialPopulation: 10);

        AttackNukeAt(nuke, testGame.GetTile(0, 0));

        Assert.True(city.GetCenterTile().IsCityCenter());
        Assert.InRange(city.Population.Population, 2, 4); // loses 60 to 78 percent
    }

    [Fact]
    public void AStrengthThreeNukeCannotDestroyACapitalButReducesItToOnePopulation()
    {
        var nuke = Nuke(3, "0");
        Meet(defenderCiv, war: true);
        var capital = testGame.AddCity(defenderCiv, testGame.GetTile(0, 0), initialPopulation: 8);

        AttackNukeAt(nuke, testGame.GetTile(0, 0));

        Assert.True(capital.GetCenterTile().IsCityCenter());
        Assert.Equal(1, capital.Population.Population);
    }
}
