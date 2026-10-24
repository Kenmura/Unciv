using System.Text.Json;
using Unciv.Logic.Map.Tiles;

namespace Unciv.Core.Tests.Logic.Map;

/// <summary>
/// T038: TileHistory. T053: Tile itself (TileTests.Tile.cs).
/// </summary>
public partial class TileTests
{
    private sealed class FakeTile(int turns, string? owner, bool center, bool capital) : ITileHistorySource
    {
        public int Turns { get; } = turns;
        public string? OwnerCivID { get; } = owner;
        public bool IsCityCenter { get; } = center;
        public bool IsCityCapital { get; } = capital;
    }

    [Fact]
    public void GetState_UsesFloorEntry()
    {
        var history = new TileHistory();
        Assert.Null(history.GetState(5).OwningCivName);
        history.RecordTakeOwnership(new FakeTile(3, "Spain", true, true));
        history.RecordTakeOwnership(new FakeTile(10, "China", true, false));
        history.RecordRelinquishOwnership(new FakeTile(20, "China", true, false));
        Assert.Null(history.GetState(2).OwningCivName);
        Assert.Equal("Spain", history.GetState(3).OwningCivName);
        Assert.Equal(TileHistory.CityCenterType.Capital, history.GetState(9).CityCenterTypeValue);
        Assert.Equal(TileHistory.CityCenterType.Regular, history.GetState(10).CityCenterTypeValue);
        Assert.Equal("China", history.GetState(19).OwningCivName);
        Assert.Null(history.GetState(1000).OwningCivName);
        Assert.Equal(TileHistory.CityCenterType.None, history.GetState(1000).CityCenterTypeValue);
    }

    [Fact]
    public void StateFromTile_NonCenterIsNone()
    {
        var state = new TileHistory.TileHistoryState(new FakeTile(1, "Rome", false, true));
        Assert.Equal("Rome", state.OwningCivName);
        Assert.Equal(TileHistory.CityCenterType.None, state.CityCenterTypeValue);
    }

    [Fact]
    public void Json_RoundTripAndFormat()
    {
        var history = new TileHistory();
        history.AddTestEntry(12, new TileHistory.TileHistoryState("China", TileHistory.CityCenterType.Regular));
        history.AddTestEntry(0, new TileHistory.TileHistoryState("Spain", TileHistory.CityCenterType.Capital));
        history.AddTestEntry(30, new TileHistory.TileHistoryState());
        string json = JsonSerializer.Serialize(history);
        Assert.Equal("""{"0":["Spain","C"],"12":["China","R"],"30":[null,"N"]}""", json);
        var back = JsonSerializer.Deserialize<TileHistory>(json)!;
        Assert.Equal("Spain", back.GetState(5).OwningCivName);
        Assert.Null(back.GetState(30).OwningCivName);
        Assert.Equal(json, JsonSerializer.Serialize(back));
    }

    [Fact]
    public void Json_UnknownTypeIsNone_EmptyOwnerIsNull_MissingTypeThrows()
    {
        var back = JsonSerializer.Deserialize<TileHistory>("""{"1":["","X"]}""")!;
        Assert.Null(back.GetState(1).OwningCivName);
        Assert.Equal(TileHistory.CityCenterType.None, back.GetState(1).CityCenterTypeValue);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TileHistory>("""{"1":["A"]}"""));
    }

    [Fact]
    public void CloneAndEquals_ShareStatesByReference()
    {
        var history = new TileHistory();
        history.AddTestEntry(1, new TileHistory.TileHistoryState("A"));
        var clone = history.Clone();
        Assert.Equal(history, clone);
        clone.AddTestEntry(2, new TileHistory.TileHistoryState());
        Assert.NotEqual(history, clone);
        // Kotlin TileHistoryState has no equals(): equal content in distinct instances is not equal
        var other = new TileHistory();
        other.AddTestEntry(1, new TileHistory.TileHistoryState("A"));
        Assert.NotEqual(history, other);
    }
}
