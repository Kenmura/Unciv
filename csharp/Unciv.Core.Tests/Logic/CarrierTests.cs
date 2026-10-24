using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;

namespace Unciv.Core.Tests.Logic;

// T103: port of tests/src/com/unciv/uniques/CarrierTests.kt.
/// <summary>
/// We're using G&amp;K UnitTypes here but add extra tags - filter "Fighter" would match UnitType _and_ the BaseUnit of that name,
/// so adding a tag ensures the filter is treated as generic.
/// </summary>
[Collection("RulesetCache")]
public class CarrierTests
{
    private readonly TestGame game = new();
    private readonly Civilization civ;
    private MapUnit carrier;
    private readonly MapUnit bomberInCity;
    private readonly MapUnit fighterInCity;

    public CarrierTests()
    {
        game.MakeHexagonalMap(2);
        var waterTile = game.GetTile(1, 0);
        waterTile.BaseTerrain = Constants.Coast;
        waterTile.SetTerrainTransients();
        civ = game.AddCiv();
        var city = civ.AddCity(HexCoord.Zero);
        var fighterBase = game.CreateBaseUnit("Fighter", "MyFighters", "MyAircraft");
        var bomberBase = game.CreateBaseUnit("Bomber", "MyBombers", "MyAircraft");
        var carrierBase = game.CreateBaseUnit("Aircraft Carrier",
            "Can carry [1] [Aircraft] units",
            "Can carry [1] [MyFighters] units",
            "Can carry [1] [MyBombers] units");
        carrierBase.Strength = 10; // Otherwise it's a civilian, and we don't look for carriers in Tile.civilianUnit
        fighterInCity = game.AddUnit(fighterBase.Name, civ, city.GetCenterTile());
        bomberInCity = game.AddUnit(bomberBase.Name, civ, city.GetCenterTile());
        carrier = game.AddUnit(carrierBase.Name, civ, waterTile);
    }

    [Fact]
    public void TestUniformCarriedUnits()
    {
        game.AddUnit(fighterInCity.BaseUnit.Name, civ, carrier.CurrentTile);
        game.AddUnit(fighterInCity.BaseUnit.Name, civ, carrier.CurrentTile);
        var remainingFighterCapacity = carrier.CheckCarryCapacity(fighterInCity);
        var remainingBomberCapacity = carrier.CheckCarryCapacity(bomberInCity);

        // Should return capacity for a third fighter: 0
        Assert.Equal(0, remainingFighterCapacity);
        // Should return capacity for a bomber: 1 - the MyBombers slot
        Assert.Equal(1, remainingBomberCapacity);
    }

    /// <summary>Note: This only succeeds thanks to the `unitCount`-derived specificity in CheckCarryCapacity.</summary>
    [Fact]
    public void TestMixedCarriedUnits()
    {
        game.AddUnit(fighterInCity.BaseUnit.Name, civ, carrier.CurrentTile);
        game.AddUnit(bomberInCity.BaseUnit.Name, civ, carrier.CurrentTile);
        var remainingFighterCapacity = carrier.CheckCarryCapacity(fighterInCity);
        var remainingBomberCapacity = carrier.CheckCarryCapacity(bomberInCity);

        // Should return capacity for a second fighter: 1 (the "Aircraft" slot)
        Assert.Equal(1, remainingFighterCapacity);
        // Should return capacity for a second bomber: 1 (the "Aircraft" slot)
        Assert.Equal(1, remainingBomberCapacity);
    }

    [Fact]
    public void TestMixedCarriedUnitsWithDifferentSelectivity()
    {
        civ.Units.RemoveUnit(carrier);
        var altCarrierBase = game.CreateBaseUnit("Aircraft Carrier",
            "Can carry [1] [MyAircraft] units", // should more selective than Aircraft
            "Can carry [1] [Fighter] units", // should be less selective than MyFighters
            "Can carry [1] [Bomber] units"); // should be less selective than MyBombers
        carrier = game.AddUnit(altCarrierBase.Name, civ, game.GetTile(1, 0));

        TestMixedCarriedUnits();
    }

    [Fact]
    public void TestOverAllocation()
    {
        // Fill up carrier to the brim
        game.AddUnit(fighterInCity.BaseUnit.Name, civ, carrier.CurrentTile);
        game.AddUnit(fighterInCity.BaseUnit.Name, civ, carrier.CurrentTile);
        game.AddUnit(bomberInCity.BaseUnit.Name, civ, carrier.CurrentTile);

        // We need to cram units into the carrier beyond what is allowed...
        var extraFighter = game.AddUnit(fighterInCity.BaseUnit.Name, civ, game.GetTile(0, 0));
        foreach (var unit in new[] { extraFighter, fighterInCity, bomberInCity })
        {
            unit.RemoveFromTile();
            // unit.PutInTile checks capacity, so avoid
            carrier.GetTile().AirUnits.Add(unit);
            unit.IsTransported = true;
        }
        Assert.Equal(6, carrier.GetTile().AirUnits.Count);

        var remainingFighterCapacity = carrier.CheckCarryCapacity(fighterInCity); // The unit is already on the carrier, but the function doesn't care, only filter matching is relevant
        var remainingBomberCapacity = carrier.CheckCarryCapacity(bomberInCity);

        // Should return we got two fighters too many (the "Aircraft" slot is not relevant)
        Assert.Equal(-2, remainingFighterCapacity);
        // Should return there's a bomber over the allowed limit
        Assert.Equal(-1, remainingBomberCapacity);
    }
}
