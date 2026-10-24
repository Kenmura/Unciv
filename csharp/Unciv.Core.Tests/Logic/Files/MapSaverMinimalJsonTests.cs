using Unciv.Json;
using Unciv.Logic.Files;

namespace Unciv.Core.Tests.Logic.Files;

// T147: libGDX "minimal JSON" (unquoted keys and string values) as written by old Unciv versions.
public sealed class MapSaverMinimalJsonTests
{
    [Fact]
    public void MapFromSavedStringReadsUnquotedValues()
    {
        var map = MapSaver.MapFromSavedString("{mapParameters:{type:Empty,mapSize:{radius:2,width:5,height:3,name:Custom},baseRuleset:Civ V - Vanilla},tileList:[{baseTerrain:Desert},{position:{x:-1,y:-1},baseTerrain:Lakes,terrainFeatures:[Flood plains,Hill],hasBottomLeftRiver:true}]}");
        Assert.Equal("Civ V - Vanilla", map.MapParameters.BaseRuleset);
        Assert.Equal(5, map.MapParameters.MapSize.Width);
        Assert.Equal(2, map.TileList.Count);
        Assert.Equal("Lakes", map.TileList[1].BaseTerrain);
        Assert.Equal(["Flood plains", "Hill"], map.TileList[1].TerrainFeatures);
        Assert.True(map.TileList[1].HasBottomLeftRiver);
    }

    [Fact]
    public void QuoteBareKeysQuotesValuesButKeepsLiterals()
    {
        Assert.Equal("{ \"a\": \"x y\" , \"b\": 1.5e3, \"c\": true, \"d\": null, \"e\": [\"p\", \"q r\", -2, false] }",
            LenientJson.QuoteBareKeys("{ a: x y , b: 1.5e3, c: true, d: null, e: [p, q r, -2, false] }"));
        Assert.Equal("{\"a\":\"1st\",\"b\":\"a:b\"}", LenientJson.QuoteBareKeys("{a:1st,b:a:b}"));
        Assert.Equal("{\"a\":\"say \\\"hi\\\" \\\\\"}", LenientJson.QuoteBareKeys("{a:say \"hi\" \\}"));
        Assert.Equal("{\"a\":\"x\"\n,\"b\":\"y\"}", LenientJson.QuoteBareKeys("{a:x\n,b:y}"));
    }

    [Fact]
    public void QuoteBareKeysLeavesStrictJsonUntouched()
    {
        const string strict = "{\"a\":[1,2.5,-3,\"x, y\",{\"b\":null}],\"c\":true,\"d\":{}, \"e\":[]}";
        Assert.Equal(strict, LenientJson.QuoteBareKeys(strict));
    }
}
