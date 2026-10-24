using System.Text.Json;
using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.Mathematics;
using Unciv.Models;
using Unciv.Models.StatTypes;

namespace Unciv.Core.Tests.Json;

public class JsonTests
{
    // Port of tests/src/com/unciv/logic/SerializationTests.kt: test Counter(String) serialization roundtrip
    [Fact]
    public void TestCounterStringSerializationRoundtrip()
    {
        var data = new Counter<string>(new Dictionary<string, int>
        {
            ["Foo"] = 1,
            ["Bar"] = 3,
            ["Towel"] = 42
        });

        string json = UncivJson.Serialize(data);
        var deserialized = UncivJson.Deserialize<Counter<string>>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(data.Count, deserialized.Count);
        Assert.Equal(data["Foo"], deserialized["Foo"]);
        Assert.Equal(data["Bar"], deserialized["Bar"]);
        Assert.Equal(data["Towel"], deserialized["Towel"]);
    }

    [Fact]
    public void TestCounterLenientReading()
    {
        // Reading float values and string values
        string json = """{"Foo": 1.9, "Bar": "5", "Nested": {"class": "java.lang.Integer", "value": 7}}""";
        var deserialized = UncivJson.Deserialize<Counter<string>>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized["Foo"]); // libGDX JsonValue.asInt truncates floats
        Assert.Equal(5, deserialized["Bar"]);
        Assert.Equal(7, deserialized["Nested"]);
    }

    private sealed class StringHolder
    {
        public string? A { get; set; }
    }

    // T151: libGDX reads any primitive into a String field (JsonValue.asString)
    [Theory]
    [InlineData("""{"A":0}""", "0")]
    [InlineData("""{"A":1.50}""", "1.50")]
    [InlineData("""{"A":true}""", "true")]
    [InlineData("""{"A":false}""", "false")]
    [InlineData("""{"A":"x"}""", "x")]
    [InlineData("""{"A":null}""", null)]
    public void TestStringFieldReadsPrimitivesLikeGdx(string json, string? expected) =>
        Assert.Equal(expected, UncivJson.Deserialize<StringHolder>(json)!.A);

    [Fact]
    public void TestStringFieldRejectsObjects() =>
        Assert.Throws<System.Text.Json.JsonException>(() => UncivJson.Deserialize<StringHolder>("""{"A":{"b":1}}"""));

    // Port of tests/src/com/unciv/logic/map/HexmathTests.kt: testSerializedHexCoordIsCompact
    [Fact]
    public void TestSerializedHexCoordIsCompact()
    {
        var input = HexCoord.Of(-42, 42);
        string actual = UncivJson.Serialize(input);
        string expected = """{"x":-42,"y":42}""";
        Assert.Equal(expected, actual);

        Assert.Equal("{}", UncivJson.Serialize(HexCoord.Of(0, 0)));
        Assert.Equal("""{"x":5}""", UncivJson.Serialize(HexCoord.Of(5, 0)));
        Assert.Equal("""{"y":-3}""", UncivJson.Serialize(HexCoord.Of(0, -3)));
    }

    // Port of tests/src/com/unciv/logic/map/HexmathTests.kt: testCanDeserializeVerboseVector2Format
    [Fact]
    public void TestCanDeserializeVerboseVector2Format()
    {
        string input = """{"x": {"class": "java.lang.Float", "value": 23}, "y": {"class": "java.lang.Float", "value": -23}}""";
        var actual = UncivJson.Deserialize<HexCoord>(input);
        var expected = HexCoord.Of(23, -23);
        Assert.Equal(expected, actual);
    }

    // Port of tests/src/com/unciv/logic/map/HexmathTests.kt: testHexCoordVector2SerDeser
    [Fact]
    public void TestHexCoordVector2SerDeser()
    {
        for (int x = -2; x <= 2; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                var hexCoord = HexCoord.Of(x, y);
                var vector2 = new Vector2(x, y);

                // hexcoord -> json -> vector2
                string hexCoordSerialized = UncivJson.Serialize(hexCoord);
                var vector2Deserialized = UncivJson.Deserialize<Vector2>(hexCoordSerialized);
                Assert.NotNull(vector2Deserialized);
                Assert.Equal(vector2, vector2Deserialized);

                // vector2 -> json -> hexcoord
                string vector2Serialized = UncivJson.Serialize(vector2);
                var hexCoordDeserialized = UncivJson.Deserialize<HexCoord>(vector2Serialized);
                Assert.Equal(hexCoord, hexCoordDeserialized);
            }
        }
    }

    [Fact]
    public void TestStatsSerializationRoundtrip()
    {
        var stats = new Stats(production: 3f, gold: -1f, food: 2f);
        string json = UncivJson.Serialize(stats);
        var deserialized = UncivJson.Deserialize<Stats>(json);

        Assert.NotNull(deserialized);
        Assert.True(stats.Equals(deserialized));
    }

    [Fact]
    public void TestStatsLenientReading()
    {
        string json = """{"Production": 5, "Food": "2.5", "gold": 3}""";
        var stats = UncivJson.Deserialize<Stats>(json);

        Assert.NotNull(stats);
        Assert.Equal(5f, stats.Production);
        Assert.Equal(2.5f, stats.Food);
        Assert.Equal(3f, stats.Gold);
        Assert.Equal(0f, stats.Science);
    }

    [Fact]
    public void TestStatMapSerializationRoundtrip()
    {
        var statMap = new StatMap();
        statMap.Add("Natural Wonders", new Stats(gold: 1f, happiness: 1f));
        statMap.Add("Buildings", new Stats(production: 2f));

        string json = UncivJson.Serialize(statMap);
        var deserialized = UncivJson.Deserialize<StatMap>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Count);
        Assert.True(statMap["Natural Wonders"].Equals(deserialized["Natural Wonders"]));
        Assert.True(statMap["Buildings"].Equals(deserialized["Buildings"]));
    }

    [Fact]
    public void TestStatEnumSerialization()
    {
        string json = UncivJson.Serialize(Stat.Culture);
        Assert.Equal("\"Culture\"", json);
        var deserialized = UncivJson.Deserialize<Stat>("\"Culture\"");
        Assert.Equal(Stat.Culture, deserialized);
    }

    // Expected values come from libGDX 1.14.2 JsonValue.getInt / readValue(Float) + Float.toInt run on the JVM
    [Theory]
    [InlineData("1.9", 1)]
    [InlineData("-1.9", -1)]
    [InlineData("2.5", 2)]
    [InlineData("1.5", 1)]
    [InlineData("1e2", 100)]
    [InlineData("\"7\"", 7)]
    [InlineData("{\"class\": \"java.lang.Float\", \"value\": 2.9}", 2)]
    public void CounterReadsIntsLikeLibGdx(string value, int expected)
    {
        var deserialized = UncivJson.Deserialize<Counter<string>>("{\"Foo\": " + value + "}");
        Assert.NotNull(deserialized);
        Assert.Equal(expected, deserialized["Foo"]);
    }

    [Fact]
    public void CounterRejectsWrappedValueWithoutValueMember()
    {
        Assert.Throws<JsonException>(() => UncivJson.Deserialize<Counter<string>>("""{"Foo": {"class": "java.lang.Integer"}}"""));
        Assert.Throws<JsonException>(() => UncivJson.Deserialize<Counter<string>>("""{"Foo": null}"""));
        Assert.Throws<JsonException>(() => UncivJson.Deserialize<Counter<string>>("""{"Foo": []}"""));
    }

    [Fact]
    public void CounterReadsBooleansAsLibGdxAsInt()
    {
        var deserialized = UncivJson.Deserialize<Counter<string>>("""{"A": true, "B": false}""");
        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized["A"]);
        Assert.Equal(0, deserialized["B"]);
        Assert.False(deserialized.ContainsKey("B")); // put(0) removes
    }

    [Fact]
    public void CounterWritesAllEntriesIncludingZeroesFromCopyConstructor()
    {
        var withZero = new Counter<string>(new Dictionary<string, int> { ["A"] = 0, ["B"] = 2 });
        Assert.Equal("""{"A":0,"B":2}""", UncivJson.Serialize(withZero));
    }

    [Fact]
    public void CounterWithNonStringKeysIsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => UncivJson.Serialize(new Counter<int>()));
    }

    [Theory]
    [InlineData("""{"x": 1.9, "y": -1.9}""", 1, -1)]
    [InlineData("""{"x": 1.5, "y": 2.5}""", 1, 2)]
    [InlineData("""{"x": "3.7"}""", 3, 0)]
    [InlineData("""{"x": {"class": "java.lang.Float", "value": -0.5}, "y": 7}""", 0, 7)]
    [InlineData("{}", 0, 0)]
    public void HexCoordReadsFloatsAndTruncates(string json, int x, int y)
    {
        Assert.Equal(HexCoord.Of(x, y), UncivJson.Deserialize<HexCoord>(json));
    }

    [Fact]
    public void HexCoordRejectsWrappedValueWithoutValueMember()
    {
        Assert.Throws<JsonException>(() => UncivJson.Deserialize<HexCoord>("""{"x": {"class": "java.lang.Float"}}"""));
    }
}
