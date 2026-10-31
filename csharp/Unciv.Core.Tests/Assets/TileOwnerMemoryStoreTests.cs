using Unciv.Assets;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Assets;

// T378: the remembered tile owners survive a restart, a load and a reconnect, per game and viewer, and never grow beyond what was seen.
public sealed class TileOwnerMemoryStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Directory.CreateTempSubdirectory("unciv-owner-memory-").FullName, "store");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static TileDto Tile(int x, int y, string? owner = null, bool visible = true) => new()
    {
        X = x,
        Y = y,
        Visible = visible,
        BaseTerrain = "Grassland",
        Features = [],
        RiverBottom = false,
        RiverBottomLeft = false,
        RiverBottomRight = false,
        Owner = owner,
    };

    private static TileOwnerMemory Seen(params TileDto[] tiles)
    {
        var memory = new TileOwnerMemory();
        memory.Remember(tiles);
        return memory;
    }

    private static readonly TileDto[] Map = [.. Enumerable.Range(0, 10).SelectMany(x => Enumerable.Range(0, 10).Select(y => Tile(x, y, visible: false)))]; // the viewer's explored map

    private static readonly TileDto[] Lit = [Tile(0, 0, "Japan"), Tile(1, 0, "Japan"), Tile(1, 1), Tile(2, 2, "Greece")];
    private static readonly TileDto[] Fogged = [.. Lit.Select(it => Tile(it.X, it.Y, owner: null, visible: false))];

    [Fact]
    public void SaveAndLoad_GiveAnEqualMemoryIncludingKnownUnowned()
    {
        var store = new TileOwnerMemoryStore(folder);
        var before = Seen(Lit);
        Assert.True(store.Save("g1", "Rome", 12, Map, before));
        var after = new TileOwnerMemory();
        Assert.True(store.Load("g1", "Rome", 12, Map, after));
        Assert.Equal(before.Entries(), after.Entries());
        Assert.Equal(4, after.Count);
        Assert.True(after.TryOwner(Fogged[2], out var unowned));
        Assert.Null(unowned); // seen unowned stays known unowned, not unknown
        Assert.False(after.TryOwner(Tile(9, 9, visible: false), out _));
    }

    [Fact]
    public void ALoadedGameShowsTheSameFoggedBordersAsBeforeTheSave()
    {
        var store = new TileOwnerMemoryStore(folder);
        var before = Seen(Lit);
        var expected = MapOverlays.BorderEdges(Fogged, before);
        Assert.Contains(expected, it => it.Owner == "Japan"); // the borders of fogged territory exist at all
        store.Save("g1", "Rome", 5, Map, before);
        var restarted = new TileOwnerMemory();
        Assert.True(store.Load("g1", "Rome", 5, Map, restarted));
        Assert.Equal(expected, MapOverlays.BorderEdges(Fogged, restarted));
        Assert.Empty(MapOverlays.BorderEdges(Fogged.Where(it => it.X == 0).ToList(), new TileOwnerMemory())); // and without the memory they would not
    }

    [Fact]
    public void EachGameAndViewerHasItsOwnFile()
    {
        var store = new TileOwnerMemoryStore(folder);
        store.Save("g1", "Rome", 5, Map, Seen(Tile(0, 0, "Japan")));
        var other = new TileOwnerMemory();
        Assert.False(store.Load("g2", "Rome", 5, Map, other)); // another game
        Assert.False(store.Load("g1", "Greece", 5, Map, other)); // a hotseat rival must not borrow Rome's view
        Assert.Equal(0, other.Count);
        Assert.NotEqual(store.PathFor("g1", "Rome"), store.PathFor("g1", "Greece"));
        Assert.NotEqual(store.PathFor("a", "b/c"), store.PathFor("a/b", "c")); // the separator cannot make two keys one
    }

    [Theory]
    [InlineData("../../escape")]
    [InlineData("a\0b")]
    public void ANastyIdNeverEscapesTheFolder(string id)
    {
        var store = new TileOwnerMemoryStore(folder);
        Assert.Equal(Path.GetFullPath(folder), Path.GetDirectoryName(Path.GetFullPath(store.PathFor(id, id))));
        Assert.True(store.Save(id, id, 1, Map, Seen(Tile(0, 0, "Japan"))));
    }

    [Fact]
    public void AnEarlierTurnIgnoresMemoryFromItsFuture()
    {
        var store = new TileOwnerMemoryStore(folder);
        store.Save("g1", "Rome", 40, Map, Seen(Tile(0, 0, "Japan")));
        var memory = Seen(Tile(7, 7, "Greece"));
        Assert.False(store.Load("g1", "Rome", 39, Map, memory)); // an older save was loaded: its fog must not show borders drawn later
        Assert.Equal(0, memory.Count); // and nothing stale is left behind
        Assert.True(store.Load("g1", "Rome", 40, Map, memory));
        Assert.True(store.Load("g1", "Rome", 41, Map, memory));
    }

    [Fact]
    public void ADifferentMapUnderTheSameKeyDoesNotInheritTheMemory()
    {
        var store = new TileOwnerMemoryStore(folder);
        store.Save("host:abc", "Rome", 5, Map, Seen(Tile(0, 0, "Japan"), Tile(3, 3, "Greece")));
        // A new game of the same host and civ: one remembered tile has other terrain, or is not on the map at all
        var otherTerrain = Map.Select(it => it.X == 3 && it.Y == 3 ? it with { BaseTerrain = "Desert" } : it).ToList();
        Assert.False(store.Load("host:abc", "Rome", 9, otherTerrain, new TileOwnerMemory()));
        Assert.False(store.Load("host:abc", "Rome", 9, [.. Map.Where(it => it.X != 3)], new TileOwnerMemory()));
        Assert.True(store.Load("host:abc", "Rome", 9, [.. Map.Select(it => it with { Features = ["Forest"] })], new TileOwnerMemory())); // features change in a game; base terrain does not
    }

    [Fact]
    public void ASecondSaveReplacesTheFirstAndLeavesNoTempFile()
    {
        var store = new TileOwnerMemoryStore(folder);
        store.Save("g1", "Rome", 1, Map, Seen(Tile(0, 0, "Japan")));
        store.Save("g1", "Rome", 2, Map, Seen(Tile(0, 0, "Greece"), Tile(1, 0)));
        var memory = new TileOwnerMemory();
        Assert.True(store.Load("g1", "Rome", 2, Map, memory));
        Assert.True(memory.TryOwner(Tile(0, 0, visible: false), out var owner));
        Assert.Equal("Greece", owner);
        Assert.Single(Directory.GetFiles(folder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"version\":1,\"turn\":1,\"tiles\":null}")]
    [InlineData("{\"version\":99,\"turn\":1,\"tiles\":[{\"x\":0,\"y\":0,\"o\":\"Japan\"}]}")]
    [InlineData("{\"version\":1,\"turn\":1,\"tiles\":[{\"x\":\"zero\"}]}")]
    [InlineData("null")]
    public void AnUnusableFileReadsAsNoMemoryAndIsReplacedBySave(string content)
    {
        var store = new TileOwnerMemoryStore(folder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(store.PathFor("g1", "Rome"), content);
        var memory = Seen(Tile(3, 3, "Greece"));
        Assert.False(store.Load("g1", "Rome", 5, Map, memory));
        Assert.Equal(0, memory.Count);
        Assert.True(store.Save("g1", "Rome", 5, Map, Seen(Tile(0, 0, "Japan"))));
        Assert.True(store.Load("g1", "Rome", 5, Map, memory));
    }

    [Fact]
    public void AnOversizedFileIsNotRead()
    {
        var store = new TileOwnerMemoryStore(folder);
        Directory.CreateDirectory(folder);
        using (var stream = File.Create(store.PathFor("g1", "Rome"))) stream.SetLength(17 * 1024 * 1024);
        Assert.False(store.Load("g1", "Rome", 5, Map, new TileOwnerMemory()));
    }

    [Fact]
    public void ASaveThatCannotBeWrittenIsReportedNotThrown()
    {
        Directory.CreateDirectory(folder);
        string blocker = Path.Combine(folder, "file");
        File.WriteAllText(blocker, "x");
        var store = new TileOwnerMemoryStore(Path.Combine(blocker, "below-a-file")); // a folder cannot be created under a file
        Assert.False(store.Save("g1", "Rome", 1, Map, Seen(Tile(0, 0, "Japan"))));
        Assert.False(store.Load("g1", "Rome", 1, Map, new TileOwnerMemory()));
    }

    [Fact]
    public void RememberReportsOnlyRealChangesAndFoggedTilesTeachNothing()
    {
        var memory = new TileOwnerMemory();
        Assert.True(memory.Remember(Lit));
        Assert.False(memory.Remember(Lit)); // nothing new: no rewrite of the file
        Assert.False(memory.Remember(Fogged));
        Assert.True(memory.Remember([Tile(0, 0, "Greece")])); // the border moved in sight
        Assert.True(memory.Remember([Tile(1, 1, "Japan")])); // unowned became owned
        Assert.True(memory.Remember([Tile(5, 5)])); // a new tile seen unowned
    }

    [Fact]
    public void RestoreReplacesWhatWasThereAndAnEmptyOwnerMeansUnowned()
    {
        var memory = Seen(Tile(9, 9, "Greece"));
        memory.Restore([new RememberedOwner(0, 0, "Japan"), new RememberedOwner(1, 0, ""), new RememberedOwner(0, 0, "Egypt")]);
        Assert.Equal([new RememberedOwner(0, 0, "Egypt"), new RememberedOwner(1, 0, null)], memory.Entries());
    }

    [Fact]
    public void EntriesAreInAFixedOrder()
    {
        var a = Seen(Tile(2, 0, "A"), Tile(0, 5, "B"), Tile(0, 1, "C"));
        var b = Seen(Tile(0, 1, "C"), Tile(2, 0, "A"), Tile(0, 5, "B"));
        Assert.Equal(a.Entries(), b.Entries());
        Assert.Equal([(0, 1), (0, 5), (2, 0)], a.Entries().Select(it => (it.X, it.Y)));
        new TileOwnerMemoryStore(folder).Save("g", "v", 1, Map, a);
        new TileOwnerMemoryStore(folder + "2").Save("g", "v", 1, Map, b);
        try { Assert.Equal(File.ReadAllText(new TileOwnerMemoryStore(folder).PathFor("g", "v")), File.ReadAllText(new TileOwnerMemoryStore(folder + "2").PathFor("g", "v"))); }
        finally { Directory.Delete(folder + "2", recursive: true); }
    }

    [Fact]
    public void FoggedDtosNeverEnterTheMemory()
    {
        var memory = Seen(Fogged);
        Assert.Equal(0, memory.Count);
    }
}
