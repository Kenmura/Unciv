using Unciv.Assets;
using Unciv.Net;

namespace Unciv.Core.Tests.Assets;

/// <summary>C090: what the client keeps of the relayed map pings: kinds, the lifetime, replacement on the same tile, the cap.</summary>
public sealed class MapPingModelTests
{
    private static MapPingNotice Notice(int x, int y, string kind = MapPingKinds.Look, string sender = "Bob") => new() { X = x, Y = y, Kind = kind, Sender = sender };

    [Fact]
    public void Kinds_AreTheProtocolOnes_AndEachHasWordsAndAColour()
    {
        Assert.Equal(MapPingKinds.All, MapPingModel.Kinds);
        Assert.All(MapPingModel.Kinds, kind =>
        {
            Assert.True(MapPingModel.IsKnown(kind));
            Assert.NotEmpty(MapPingModel.TextOf(kind));
        });
        Assert.Equal(MapPingModel.Kinds.Count, MapPingModel.Kinds.Select(MapPingModel.ColourOf).Distinct().Count());
        Assert.False(MapPingModel.IsKnown("Spam"));
        Assert.False(MapPingModel.IsKnown(null));
    }

    [Fact]
    public void Add_UnknownKindIsIgnored_AndANewerPingOnTheSameTileReplacesTheOlder()
    {
        var pings = MapPingModel.Add([], Notice(1, 2));
        Assert.Same(pings, MapPingModel.Add(pings, Notice(3, 4, "Spam")));

        pings = MapPingModel.Add(pings, Notice(5, 6));
        pings = MapPingModel.Add(MapPingModel.Advance(pings, 3f), Notice(1, 2, MapPingKinds.Danger, "Eve"));

        Assert.Equal([(5, 6), (1, 2)], pings.Select(it => (it.X, it.Y)));
        var replaced = pings[^1];
        Assert.Equal(("Eve", MapPingKinds.Danger, MapPingModel.Lifetime), (replaced.Sender, replaced.Kind, replaced.Remaining));
    }

    [Fact]
    public void Add_KeepsTheNewestMaxActive()
    {
        IReadOnlyList<ActivePing> pings = [];
        for (int i = 0; i < MapPingModel.MaxActive + 5; i++) pings = MapPingModel.Add(pings, Notice(i, 0));

        Assert.Equal(MapPingModel.MaxActive, pings.Count);
        Assert.Equal(5, pings[0].X);
        Assert.Equal(MapPingModel.MaxActive + 4, pings[^1].X);
    }

    [Fact]
    public void Advance_DropsPingsThatRanOut_AndProgressGoesFromZeroToOne()
    {
        var pings = MapPingModel.Add([], Notice(1, 1));
        Assert.Equal(0f, MapPingModel.Progress(pings[0]));

        pings = MapPingModel.Advance(pings, MapPingModel.Lifetime / 2f);
        Assert.Equal(0.5f, MapPingModel.Progress(pings[0]), 3);

        Assert.Empty(MapPingModel.Advance(pings, MapPingModel.Lifetime));
        Assert.Equal(1f, MapPingModel.Progress(pings[0] with { Remaining = -2f }));
    }
}
