using Unciv.Core.Tests.Support;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Fixtures.MapFixtures;

// T141: port of tests/src/com/unciv/logic/map/UpgradeTests.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class UpgradeFixtureTests
{
    private readonly TestGame testGame = new();

    public UpgradeFixtureTests() => testGame.MakeHexagonalMap(5);

    [Fact]
    public void RuinsUpgradeToSpecialUnit()
    {
        var unitToUpgradeTo = testGame.CreateBaseUnit();
        var testUnit = testGame.CreateBaseUnit(uniques: $"May upgrade to [{unitToUpgradeTo.Name}] through ruins-like effects");
        testUnit.UpgradesTo = "Warrior";

        var civ = testGame.AddCiv();
        var unit1 = testGame.AddUnit(testUnit.Name, civ, testGame.GetTile(HexCoord.Zero));
        var triggerUnique = new UniqueObject("[This Unit] upgrades for free including special upgrades");
        UniqueTriggerActivation.TriggerUnique(triggerUnique, unit1);
        unit1 = testGame.GetTile(HexCoord.Zero).GetFirstUnit()!;

        Assert.True(unit1.BaseUnit == unitToUpgradeTo, "Unit should upgrade to special unit, not warrior");
    }

    [Fact]
    public void RuinsUpgradeToNormalUnitWithoutUnique()
    {
        var testUnit = testGame.CreateBaseUnit();
        testUnit.UpgradesTo = "Warrior";

        var civ = testGame.AddCiv();
        var unit1 = testGame.AddUnit(testUnit.Name, civ, testGame.GetTile(HexCoord.Zero));
        var triggerUnique = new UniqueObject("[This Unit] upgrades for free including special upgrades");
        UniqueTriggerActivation.TriggerUnique(triggerUnique, unit1);
        unit1 = testGame.GetTile(HexCoord.Zero).GetFirstUnit()!;

        Assert.Equal("Warrior", unit1.BaseUnit.Name);
    }

    [Fact]
    public void RegularUpgradeCannotUpgradeToSpecialUnit()
    {
        var unitToUpgradeTo = testGame.CreateBaseUnit();
        var testUnit = testGame.CreateBaseUnit(uniques: $"May upgrade to [{unitToUpgradeTo.Name}] through ruins-like effects");
        testUnit.UpgradesTo = "Warrior";

        var civ = testGame.AddCiv();
        var unit1 = testGame.AddUnit(testUnit.Name, civ, testGame.GetTile(HexCoord.Zero));
        var upgrades = unit1.Upgrade.GetUpgradesOfKind(isSpecial: false)
            .Where(it => unit1.Upgrade.CanPerformUpgrade(it, isFree: true)).ToList();

        Assert.Single(upgrades);
        Assert.DoesNotContain(upgrades, it => it.UpgradedUnit == unitToUpgradeTo);

        var triggerUnique = new UniqueObject("[This Unit] upgrades for free");
        UniqueTriggerActivation.TriggerUnique(triggerUnique, unit1);
        unit1 = testGame.GetTile(HexCoord.Zero).GetFirstUnit()!;

        Assert.True(unit1.BaseUnit.Name == "Warrior");
    }

    [Fact]
    public void CanUpgradeToMultipleWithUnique()
    {
        var unitToUpgradeTo = testGame.CreateBaseUnit();
        var testUnit = testGame.CreateBaseUnit(uniques: [$"Can upgrade to [{unitToUpgradeTo.Name}]", "Can upgrade to [Warrior]"]);

        var civ = testGame.AddCiv();
        var unit1 = testGame.AddUnit(testUnit.Name, civ, testGame.GetTile(HexCoord.Zero));
        var upgrades = unit1.Upgrade.GetUpgradesOfKind(isSpecial: false)
            .Where(it => unit1.Upgrade.CanPerformUpgrade(it, isFree: true)).ToList();

        Assert.Equal(2, upgrades.Count);

        var triggerUnique = new UniqueObject("[This Unit] upgrades for free");
        UniqueTriggerActivation.TriggerUnique(triggerUnique, unit1);
        unit1 = testGame.GetTile(HexCoord.Zero).GetFirstUnit()!;

        Assert.False(unit1.BaseUnit == testUnit);
    }

    [Fact]
    public void CannotUpgradeWithoutGold()
    {
        var unitToUpgradeTo = testGame.CreateBaseUnit();
        var testUnit = testGame.CreateBaseUnit();
        testUnit.UpgradesTo = unitToUpgradeTo.Name;

        var civ = testGame.AddCiv();
        testGame.AddCity(civ, testGame.GetTile(HexCoord.Zero)); // We need to own the tile to be able to upgrade here

        var unit1 = testGame.AddUnit(testUnit.Name, civ, testGame.GetTile(HexCoord.Zero));
        var upgrades = unit1.Upgrade.GetUpgradesOfKind(isSpecial: false);

        Assert.True(upgrades.All(it => !unit1.Upgrade.CanPerformUpgrade(it, isFree: false)), "We should need gold to upgrade here");

        civ.AddGold(unit1.Upgrade.GetCostOfUpgrade(unitToUpgradeTo));

        upgrades = unit1.Upgrade.GetUpgradesOfKind(isSpecial: false);

        Assert.Single(upgrades);
        Assert.True(upgrades.All(it => unit1.Upgrade.CanPerformUpgrade(it, isFree: false)));
    }
}
