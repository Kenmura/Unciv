using Unciv.GodotClient.Saves;
using Unciv.Harness;
using Unciv.Logic.Files;

namespace Unciv.Tests.Godot;

// C016: robustness of the Godot-free save folder (listing, metadata, atomic writes, names, autosave purge).
public sealed class SaveStoreTests : IDisposable
{
    private static readonly Lazy<string> RealSave = new(() => UncivFiles.GameInfoToString(ScenarioGame.Create(solo: true).Game, forceZip: true));

    private readonly string dir = Directory.CreateTempSubdirectory("unciv-saves-").FullName;
    private SaveStore Store => new(Path.Combine(dir, "saves"));

    public void Dispose()
    {
        try { Directory.Delete(dir, true); }
        catch (IOException) { }
    }

    [Fact]
    public void MissingFolderListsEmptyAndReadsFail()
    {
        Assert.Empty(Store.List());
        Assert.Null(Store.Latest());
        Assert.False(Store.TryRead("x", out _).Ok);
        Assert.False(Store.Delete("x").Ok);
    }

    [Fact]
    public void WriteThenListShowsMetadata()
    {
        var store = Store;
        Assert.True(store.Write("Alpha", RealSave.Value).Ok);
        var entry = Assert.Single(store.List());
        Assert.Equal("Alpha", entry.Name);
        Assert.Equal(0, entry.Info?.Turn);
        Assert.Equal(ScenarioGame.HumanA, entry.Info?.Civ);
        Assert.True(store.TryRead("Alpha", out string text).Ok);
        Assert.Equal(RealSave.Value, text);
        Assert.Equal("Alpha", store.Latest()?.Name);
    }

    [Fact]
    public void PlainJsonSaveIsListedToo()
    {
        var store = Store;
        Assert.True(store.Write("Plain", UncivFiles.GameInfoToString(ScenarioGame.Create(solo: true).Game, forceZip: false)).Ok);
        Assert.NotNull(Assert.Single(store.List()).Info);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a save")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"civilizations\":[],\"turns\":-4}")]
    [InlineData("H4sIAAAAAAAA")]
    public void DamagedFilesAreListedWithAProblemAndNeverThrow(string content)
    {
        var store = Store;
        Directory.CreateDirectory(store.Folder);
        File.WriteAllText(Path.Combine(store.Folder, "Bad"), content);
        var entry = Assert.Single(store.List());
        Assert.Null(entry.Info);
        Assert.False(string.IsNullOrEmpty(entry.Problem));
        Assert.Null(store.Latest());
        Assert.True(store.Delete("Bad").Ok);
    }

    [Fact]
    public void TruncatedSaveIsDamagedNotCrashing()
    {
        var store = Store;
        string half = RealSave.Value[..(RealSave.Value.Length / 2)];
        Assert.True(store.Write("Half", half).Ok);
        Assert.Null(Assert.Single(store.List()).Info);
    }

    [Fact]
    public void FailedWriteKeepsTheOldSaveAndLeavesNoTempFile()
    {
        var store = Store;
        Assert.True(store.Write("Keep", RealSave.Value).Ok);
        // a directory squatting on the temp name makes the write fail after the old file exists
        Directory.CreateDirectory(Path.Combine(store.Folder, "Keep.tmp"));
        var result = store.Write("Keep", "replacement");
        Assert.False(result.Ok);
        Assert.True(store.TryRead("Keep", out string text).Ok);
        Assert.Equal(RealSave.Value, text);
    }

    [Fact]
    public void WriteIntoAFileInsteadOfFolderFails()
    {
        File.WriteAllText(Path.Combine(dir, "blocker"), "x");
        var result = new SaveStore(Path.Combine(dir, "blocker", "saves")).Write("A", "text");
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void StrayTempAndHiddenFilesAreNotSaves()
    {
        var store = Store;
        Assert.True(store.Write("A", RealSave.Value).Ok);
        File.WriteAllText(Path.Combine(store.Folder, "A.tmp"), "half");
        File.WriteAllText(Path.Combine(store.Folder, ".hidden"), "x");
        Assert.Equal(["A"], store.List().Select(it => it.Name));
    }

    [Fact]
    public void OverwriteReplacesAndEmptyTextIsRefused()
    {
        var store = Store;
        Assert.True(store.Write("A", "one").Ok);
        Assert.True(store.Write("A", "two").Ok);
        store.TryRead("A", out string text);
        Assert.Equal("two", text);
        Assert.False(store.Write("A", "").Ok);
        store.TryRead("A", out text);
        Assert.Equal("two", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" lead")]
    [InlineData("trail ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData("dot.")]
    [InlineData("a:b")]
    [InlineData("x.tmp")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("tab\there")]
    public void BadNamesAreRefusedEverywhere(string? name)
    {
        Assert.NotNull(SaveStore.ValidateName(name));
        var store = Store;
        Assert.False(store.Write(name!, "text").Ok);
        Assert.False(store.TryRead(name!, out _).Ok);
        Assert.False(store.Delete(name!).Ok);
        Assert.False(Directory.Exists(store.Folder) && Directory.EnumerateFileSystemEntries(store.Folder).Any());
    }

    [Fact]
    public void LongNamesAreRefusedAndOrdinaryOnesAccepted()
    {
        Assert.NotNull(SaveStore.ValidateName(new string('a', SaveStore.MaxNameLength + 1)));
        Assert.Null(SaveStore.ValidateName(new string('a', SaveStore.MaxNameLength)));
        Assert.Null(SaveStore.ValidateName("Mein Spiel 2 (Rom)"));
        Assert.Null(SaveStore.ValidateName("Spielstand-ü"));
    }

    [Fact]
    public void ListIsNewestFirstWithNameTiebreak()
    {
        var store = Store;
        foreach (string name in new[] { "B", "A", "C" }) Assert.True(store.Write(name, RealSave.Value).Ok);
        var same = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(store.Folder, "A"), same);
        File.SetLastWriteTimeUtc(Path.Combine(store.Folder, "B"), same);
        File.SetLastWriteTimeUtc(Path.Combine(store.Folder, "C"), same.AddDays(1));
        Assert.Equal(["C", "A", "B"], store.List().Select(it => it.Name));
    }

    [Fact]
    public void MetadataCacheNoticesAChangedFile()
    {
        var store = Store;
        Assert.True(store.Write("A", RealSave.Value).Ok);
        Assert.NotNull(store.List()[0].Info);
        Assert.True(store.Write("A", "garbage").Ok);
        Assert.Null(store.List()[0].Info);
    }

    [Fact]
    public void AutosaveWritesLatestAndNumberedAndPurgesOldNumberedOnly()
    {
        var store = Store;
        Assert.True(store.Write("Autosave-notes", RealSave.Value).Ok); // a player-named save that merely starts with Autosave
        for (int turn = 1; turn <= 5; turn++) Assert.True(store.Autosave(RealSave.Value, turn, keep: 2).Ok);
        var names = store.List().Select(it => it.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["Autosave", "Autosave-4", "Autosave-5", "Autosave-notes"], names);
    }

    [Fact]
    public void AutosaveKeepIsAtLeastOne()
    {
        var store = Store;
        Assert.True(store.Autosave(RealSave.Value, 1, keep: 0).Ok);
        Assert.True(store.Autosave(RealSave.Value, 2, keep: -3).Ok);
        Assert.Equal(["Autosave", "Autosave-2"], store.List().Select(it => it.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(0, 1, false)]
    [InlineData(5, 0, false)]
    [InlineData(5, -1, false)]
    [InlineData(4, 2, true)]
    [InlineData(3, 2, false)]
    public void AutosaveDueFollowsTheInterval(int turn, int every, bool due) => Assert.Equal(due, SaveStore.AutosaveDue(turn, every));

    [Fact]
    public void ReadOfADirectoryNamedLikeASaveFailsCleanly()
    {
        var store = Store;
        Directory.CreateDirectory(Path.Combine(store.Folder, "Dir"));
        Assert.False(store.TryRead("Dir", out _).Ok);
    }
}
