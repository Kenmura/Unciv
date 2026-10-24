using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Utils;

namespace Unciv.Core.Tests.Logic.Map;

// T110: the unit fields of the Tile JSON. No Kotlin run was possible in this environment (it needs the Gdx test runner), so the
// expected strings are derived from the libGDX Json rules applied to MapUnit.kt/Tile.kt: declaration order, fields equal to a fresh
// MapUnit() omitted, Float written with Float.toString (so "2.0"), an empty UnitPromotions omitted through UnitPromotions.equals.
public class TileUnitJsonTests
{
    private static string Json(Tile tile) => UncivJson.Serialize(tile);

    private static Tile Parse(string json) => UncivJson.Deserialize<Tile>(json).NotNull();

    private static MapUnit Unit(string name, string owner = "Rome") => new() { Name = name, Owner = owner };

    [Fact]
    public void TileWithoutUnitsWritesNoUnitFields() =>
        Assert.Equal("{\"baseTerrain\":\"Grassland\"}", Json(new Tile { BaseTerrain = "Grassland" }));

    [Fact]
    public void DefaultUnitsWriteOnlyOwnerAndName()
    {
        var tile = new Tile { BaseTerrain = "Grassland", MilitaryUnit = Unit("Warrior"), CivilianUnit = Unit("Settler") };
        tile.AirUnits.Add(Unit("Fighter", "Greece"));
        Assert.Equal(
            "{\"militaryUnit\":{\"owner\":\"Rome\",\"name\":\"Warrior\"},\"civilianUnit\":{\"owner\":\"Rome\",\"name\":\"Settler\"},"
            + "\"airUnits\":[{\"owner\":\"Greece\",\"name\":\"Fighter\"}],\"baseTerrain\":\"Grassland\"}",
            Json(tile));
    }

    private static MapUnit FullUnit()
    {
        var unit = Unit("Archer");
        unit.OriginalOwner = "Barbarians";
        unit.InstanceName = "Robin";
        unit.CurrentMovement = 1.5f;
        unit.Health = 70;
        unit.Id = 12;
        unit.Action = "Fortify";
        unit.Automated = true;
        unit.AutomatedRoadConnectionDestination = HexCoord.Of(3, -2);
        unit.AttacksThisTurn = 1;
        unit.Promotions.XP = 15;
        unit.Promotions.NumberOfPromotions = 1;
        unit.Promotions.Promotions.Add("Shock I");
        unit.Due = false;
        unit.IsTransported = true;
        unit.TurnsFortified = 2;
        unit.AbilityToTimesUsed.Put("Heal", 1);
        unit.Religion = "Buddhism";
        unit.ReligiousStrengthLost = 3;
        unit.MovementMemories.Add(new MapUnit.UnitMovementMemory(HexCoord.Of(1, 0), UnitMovementMemoryType.UnitAttacked));
        unit.MovementMemories.Add(new MapUnit.UnitMovementMemory(HexCoord.Zero, UnitMovementMemoryType.UnitMoved));
        unit.MostRecentMoveType = UnitMovementMemoryType.UnitWithdrew;
        unit.AttacksSinceTurnStart.Add(HexCoord.Of(0, 1));
        unit.StatusMap.Put("Fortified", new MapUnit.UnitStatus("Fortified", 3));
        unit.StatusMap.Put("Plain", new MapUnit.UnitStatus("", 1));
        return unit;
    }

    [Fact]
    public void FullUnitWritesEveryPersistedFieldInKotlinOrder()
    {
        var tile = new Tile { BaseTerrain = "Grassland", MilitaryUnit = FullUnit() };
        var json = Json(tile);
        Assert.Equal(
            "{\"militaryUnit\":{\"owner\":\"Rome\",\"originalOwner\":\"Barbarians\",\"name\":\"Archer\",\"instanceName\":\"Robin\","
            + "\"currentMovement\":1.5,\"health\":70,\"id\":12,\"action\":\"Fortify\",\"automated\":true,"
            + "\"automatedRoadConnectionDestination\":{\"x\":3,\"y\":-2},\"attacksThisTurn\":1,"
            + "\"promotions\":{\"XP\":15,\"promotions\":[\"Shock I\"],\"numberOfPromotions\":1},\"due\":false,\"isTransported\":true,"
            + "\"turnsFortified\":2,\"abilityToTimesUsed\":{\"Heal\":1},\"religion\":\"Buddhism\",\"religiousStrengthLost\":3,"
            + "\"movementMemories\":[{\"position\":{\"x\":1},\"type\":\"UnitAttacked\"},{}],\"mostRecentMoveType\":\"UnitWithdrew\","
            + "\"attacksSinceTurnStart\":[{\"y\":1}],"
            + "\"statusMap\":{" + StatusMapJson(FullUnit()) + "}},\"baseTerrain\":\"Grassland\"}",
            json);
    }

    private static string StatusMapJson(MapUnit unit) =>
        string.Join(",", unit.StatusMap.Select(it => it.Key == "Fortified" ? "\"Fortified\":{\"name\":\"Fortified\",\"turnsLeft\":3}" : "\"Plain\":{}"));

    [Fact]
    public void FullUnitRoundTrips()
    {
        var tile = new Tile { BaseTerrain = "Grassland", MilitaryUnit = FullUnit(), CivilianUnit = Unit("Worker") };
        tile.AirUnits.Add(Unit("Bomber"));
        tile.AirUnits.Add(Unit("Fighter"));
        var json = Json(tile);
        var back = Parse(json);

        Assert.Equal(json, Json(back));
        Assert.Equal(["Bomber", "Fighter"], back.AirUnits.Select(it => it.Name));
        Assert.Equal("Worker", back.CivilianUnit?.Name);
        var unit = back.MilitaryUnit.NotNull();
        Assert.Equal(1.5f, unit.CurrentMovement);
        Assert.Equal(HexCoord.Of(3, -2), unit.AutomatedRoadConnectionDestination);
        Assert.Contains("Shock I", unit.Promotions.Promotions);
        Assert.Equal(15, unit.Promotions.XP);
        Assert.Equal(3, unit.StatusMap["Fortified"].TurnsLeft);
        Assert.Equal(UnitMovementMemoryType.UnitAttacked, unit.MovementMemories[0].Type);
        Assert.Equal(HexCoord.Of(1, 0), unit.MovementMemories[0].Position);
        Assert.Equal(UnitMovementMemoryType.UnitWithdrew, unit.MostRecentMoveType);
        Assert.False(unit.Due);
    }

    [Fact]
    public void DefaultsAreRestoredForOmittedFields()
    {
        var unit = Parse("{\"militaryUnit\":{\"owner\":\"Rome\",\"name\":\"Warrior\"}}").MilitaryUnit.NotNull();
        Assert.Equal(100, unit.Health);
        Assert.True(unit.Due);
        Assert.Equal(Constants.NoId, unit.Id);
        Assert.Empty(unit.MovementMemories);
        Assert.Equal(UnitMovementMemoryType.UnitMoved, unit.MostRecentMoveType);
    }

    [Fact]
    public void UnknownUnitFieldsAreIgnoredAndWrappedListsAccepted()
    {
        var tile = Parse("{\"civilianUnit\":null,\"airUnits\":{\"class\":\"java.util.ArrayList\",\"items\":[{\"owner\":\"A\",\"name\":\"B\",\"future\":[1]}]},"
            + "\"militaryUnit\":{\"owner\":\"A\",\"name\":\"C\",\"movementMemories\":{\"class\":\"java.util.ArrayList\",\"items\":[{\"type\":\"UnitTeleported\"}]},"
            + "\"currentMovement\":\"2\",\"promotions\":{}}}");
        Assert.Null(tile.CivilianUnit);
        Assert.Equal("B", Assert.Single(tile.AirUnits).Name);
        var unit = tile.MilitaryUnit.NotNull();
        Assert.Equal(2f, unit.CurrentMovement);
        Assert.Equal(UnitMovementMemoryType.UnitTeleported, Assert.Single(unit.MovementMemories).Type);
    }

    [Fact]
    public void NullUnitCollectionsReadAsDefaults()
    {
        var unit = Parse("{\"airUnits\":null,\"militaryUnit\":{\"owner\":\"A\",\"name\":\"C\",\"religion\":null,\"promotions\":null,\"abilityToTimesUsed\":null,"
            + "\"movementMemories\":null,\"attacksSinceTurnStart\":null,\"statusMap\":null,\"automatedRoadConnectionDestination\":null}}").MilitaryUnit.NotNull();
        Assert.Empty(unit.StatusMap);
        Assert.Null(unit.Religion);
        Assert.Empty(unit.AttacksSinceTurnStart);
    }

    [Fact]
    public void BadShapesThrowJsonException()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse("{\"militaryUnit\":[]}"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse("{\"airUnits\":3}"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse("{\"militaryUnit\":{\"mostRecentMoveType\":\"Nope\"}}"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse("{\"militaryUnit\":{\"movementMemories\":5}}"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse("{\"militaryUnit\":{\"movementMemories\":[1]}}"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse("{\"militaryUnit\":{\"statusMap\":[]}}"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse("{\"militaryUnit\":{\"statusMap\":{\"a\":1}}}"));
    }

    [Fact]
    public void StatusMapReaderIgnoresUnknownFieldsAndMovementMemoryTooTolerates()
    {
        var unit = Parse("{\"militaryUnit\":{\"statusMap\":{\"a\":{\"x\":1,\"turnsLeft\":2}},\"movementMemories\":[{\"x\":1,\"position\":{\"x\":2}}],\"escorting\":true}}").MilitaryUnit.NotNull();
        Assert.Equal(2, unit.StatusMap["a"].TurnsLeft);
        Assert.Equal("", unit.StatusMap["a"].Name);
        Assert.Equal(HexCoord.Of(2, 0), unit.MovementMemories[0].Position);
        Assert.Contains("\"escorting\":true", UncivJson.Serialize(unit));
    }

    [Theory]
    [InlineData(0f, "0.0")]
    [InlineData(2f, "2.0")]
    [InlineData(-1.5f, "-1.5")]
    [InlineData(0.25f, "0.25")]
    [InlineData(1e7f, "1.0E7")]
    [InlineData(1.25e10f, "1.25E10")]
    [InlineData(-3e-5f, "-3.0E-5")]
    [InlineData(0.001f, "0.001")]
    [InlineData(float.NaN, "NaN")]
    public void FloatsAreWrittenLikeJavaFloatToString(float value, string expected) =>
        Assert.Equal(expected, MapUnit.MapUnitJsonConverter.FloatToJavaString(value));
}
