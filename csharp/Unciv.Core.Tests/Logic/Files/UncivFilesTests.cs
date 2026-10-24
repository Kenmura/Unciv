using System.Text;
using System.Text.Json;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using GeneratorClass = Unciv.Logic.Map.MapGenerator.MapGenerator;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Logic.Files;

// T100: UncivFiles.kt, with GameSerializationTests' save/load cases (canSaveAndLoad*, successfulSaveReportsCompletion, failedSaveReportsException)
// on an in-memory file tree. The Kotlin tests' serializedLaziesTest and writer-only handle are Gdx/JVM specific.
[Collection("RulesetCache")]
public sealed class UncivFilesTests : IDisposable
{
    private readonly bool zippedBackup = UncivFiles.SaveZipped;
    private readonly RulesetClass ruleset;

    public UncivFilesTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose()
    {
        UncivFiles.SaveZipped = zippedBackup;
        RulesetCache.Clear();
    }

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private sealed class MemFile(MemFile? parent, string name, Func<long> clock) : IUncivFileHandle
    {
        private readonly Dictionary<string, MemFile> children = [];
        public byte[] Content = [];
        public bool Exists { get; set; }
        public bool Directory { get; set; }
        public long LastModified { get; set; }
        public string Name => name;
        public string NameWithoutExtension => name;
        public bool IsInternal => false;
        public bool IsDirectory => Exists && Directory;
        public long Length => Exists ? Content.Length : 0;
        public IUncivFileHandle Parent => parent!;

        public IUncivFileHandle Child(string childName)
        {
            if (!children.TryGetValue(childName, out var child)) children[childName] = child = new MemFile(this, childName, clock);
            return child;
        }

        public void MakeDirs()
        {
            Exists = true;
            Directory = true;
            parent?.MakeDirs();
        }

        public bool Delete()
        {
            var was = Exists;
            Exists = false;
            Content = [];
            return was;
        }

        public IReadOnlyList<IUncivFileHandle> List() =>
            children.Values.Where(it => it.Exists).OrderBy(it => it.Name, StringComparer.Ordinal).ToList();

        public Stream Read() => new MemoryStream(Content);

        public Stream Write(bool append)
        {
            if (Directory) throw new UnauthorizedAccessException("is a directory");
            return new Capture(this, clock);
        }

        private sealed class Capture(MemFile file, Func<long> clock) : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
                file.Content = ToArray();
                file.Exists = true;
                file.LastModified = clock();
                base.Dispose(disposing);
            }
        }
    }

    private long tick;
    private UncivFiles NewFiles() => new(new MemFile(null, "", () => ++tick));

    // Civilization has no JSON wiring yet (TODO(T140)): a game with civs cannot be serialized, and GameInfo.SetTransients refuses a game
    // without a human civ. So these games have no civs and a load that parsed the JSON and passed the version check ends in that
    // InvalidOperationException (see LoadsUpToSetTransients); full round trips and the T011 state hash check belong to T140.
    private GameInfo NewGame(int turns = 123)
    {
        var mapParameters = new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny };
        var game = new GameInfo
        {
            GameParameters = new GameParameters { BaseRuleset = ruleset.Name },
            TileMap = new GeneratorClass(ruleset).GenerateMap(mapParameters),
            Difficulty = "Prince",
            GameId = "test-game",
            Turns = turns,
            CurrentPlayer = "Rome",
        };
        game.TileMap.GameInfo = game;
        return game;
    }

    private static void LoadsUpToSetTransients(Action load) => Assert.Throws<InvalidOperationException>(load);

    [Fact]
    public void SaveUncompressedWritesPlainJson_WithTheCurrentVersion()
    {
        var files = NewFiles();
        UncivFiles.SaveZipped = false;
        var game = NewGame();
        game.Version = CompatibilityVersion.FirstWithout;

        var file = files.SaveGame(game, "slot");
        var text = Encoding.UTF8.GetString(ReadAll(file));

        Assert.StartsWith("{", text, StringComparison.Ordinal);
        Assert.Equal(CompatibilityVersion.CurrentCompatibilityVersion, game.Version);
        Assert.Equal(game.Turns, UncivJson.Deserialize<GameInfo>(text)!.Turns);
        Assert.Equal(CompatibilityVersion.CurrentCompatibilityVersion, UncivJson.Deserialize<GameInfo>(text)!.Version);
        Assert.Same(file, files.GetSave("slot"));
        LoadsUpToSetTransients(() => files.LoadGameFromFile(file));
    }

    [Fact]
    public void SaveCompressedWritesGzipBase64()
    {
        var files = NewFiles();
        UncivFiles.SaveZipped = true;
        var game = NewGame();

        var file = files.SaveGame(game, "slot");
        var text = Encoding.UTF8.GetString(ReadAll(file));

        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
        Assert.Equal(123, UncivJson.Deserialize<GameInfo>(FileConversions.Unzip(text))!.Turns);
        LoadsUpToSetTransients(() => files.LoadGameByName("slot"));
    }

    [Fact]
    public void SuccessfulSaveReportsCompletion()
    {
        var files = NewFiles();
        UncivFiles.SaveZipped = false;
        var calls = new List<Exception?>();
        files.SaveGame(NewGame(), "slot", calls.Add);
        Assert.Single(calls);
        Assert.Null(calls[0]);
    }

    [Fact]
    public void FailedSaveReportsException_AndRethrowsByDefault()
    {
        var files = NewFiles();
        UncivFiles.SaveZipped = false;
        var game = NewGame();
        ((MemFile)files.GetSave("dir")).MakeDirs();
        var calls = new List<Exception?>();
        files.SaveGame(game, files.GetSave("dir"), calls.Add);
        Assert.Single(calls);
        Assert.IsType<UnauthorizedAccessException>(calls[0]);
        Assert.Throws<UnauthorizedAccessException>(() => files.SaveGame(game, files.GetSave("dir")));
    }

    [Fact]
    public void EmptyFileIsASerializationError()
    {
        var files = NewFiles();
        var file = files.GetSave("empty");
        file.Write(false).Dispose();
        var ex = Assert.Throws<JsonException>(() => files.LoadGameFromFile(file));
        Assert.Contains("empty is empty", ex.Message, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => files.LoadGamePreviewFromFile(file));
    }

    [Fact]
    public void LiteralNullIsCorrupted()
    {
        var files = NewFiles();
        WriteText(files.GetSave("null"), "null");
        var ex = Assert.Throws<UncivShowableException>(() => files.LoadGameFromFile(files.GetSave("null")));
        Assert.Equal("The file data seems to be corrupted.", ex.Message);
    }

    private static byte[] ReadAll(IUncivFileHandle file)
    {
        using var stream = file.Read();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static void WriteText(IUncivFileHandle file, string text)
    {
        using var stream = file.Write(false);
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    [Fact]
    public void NewerVersionIsIncompatible()
    {
        var files = NewFiles();
        UncivFiles.SaveZipped = false;
        var game = NewGame();
        game.Version = CompatibilityVersion.CurrentCompatibilityVersion;
        var json = UncivJson.Serialize(game).Replace("\"number\":4", "\"number\":99", StringComparison.Ordinal);
        Assert.Contains("\"number\":99", json, StringComparison.Ordinal);
        WriteText(files.GetSave("future"), json);
        var ex = Assert.Throws<IncompatibleGameInfoVersionException>(() => files.LoadGameByName("future"));
        Assert.Equal(99, ex.Version.Number);
        Assert.Contains(CompatibilityVersion.CurrentCompatibilityVersion.CreatedWith.Text, ex.Message, StringComparison.Ordinal);
        Assert.Throws<IncompatibleGameInfoVersionException>(() => UncivFiles.GameInfoFromString(json));
        Assert.Throws<IncompatibleGameInfoVersionException>(() => UncivFiles.GameInfoFromString(FileConversions.Zip(json)));
    }

    [Fact]
    public void UnparseableGameReportsItsVersion()
    {
        var files = NewFiles();
        WriteText(files.GetSave("broken"), "{\"version\":{\"number\":7,\"createdWith\":{\"text\":\"9.9\",\"number\":1}},\"turns\":\"x\"}");
        var ex = Assert.Throws<IncompatibleGameInfoVersionException>(() => files.LoadGameByName("broken"));
        Assert.Equal(7, ex.Version.Number);
        Assert.NotNull(ex.InnerException);
        Assert.Throws<IncompatibleGameInfoVersionException>(() => UncivFiles.GameInfoFromString(Encoding.UTF8.GetString(ReadAll(files.GetSave("broken")))));
    }

    [Fact]
    public void GameInfoToString_ZippedAndPlain_AndWhitespaceIsIgnoredOnRead()
    {
        var game = NewGame();
        var zipped = UncivFiles.GameInfoToString(game, forceZip: true, updateChecksum: true);
        Assert.NotEqual("", game.Checksum);
        Assert.Equal(game.Checksum, UncivJson.Deserialize<GameInfo>(FileConversions.Unzip(zipped))!.Checksum);
        LoadsUpToSetTransients(() => UncivFiles.GameInfoFromString("  " + zipped.Insert(10, "\r\n") + "\n"));
        UncivFiles.SaveZipped = false;
        var plain = UncivFiles.GameInfoToString(game);
        Assert.StartsWith("{", plain, StringComparison.Ordinal);
        LoadsUpToSetTransients(() => UncivFiles.GameInfoFromString(plain));
        UncivFiles.SaveZipped = true;
        Assert.DoesNotContain("{", UncivFiles.GameInfoToString(game), StringComparison.Ordinal);
        Assert.StartsWith("{", UncivFiles.GameInfoToString(game, forceZip: false), StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewRoundTripMigratesEmptyCivIds()
    {
        var files = NewFiles();
        var preview = new GameInfoPreview(NewGame());
        Assert.Equal("test-game", preview.GameId);
        preview.Civilizations.Add(new CivilizationInfoPreview { CivName = "Rome", CivId = "" });
        var file = files.SaveMultiplayerGamePreview(preview, "mp");
        Assert.Equal("MultiplayerGames", file.Parent.Name);
        Assert.Equal("Rome", files.LoadGamePreviewFromFile(file).Civilizations[0].CivId);
        Assert.Single(files.GetMultiplayerSaves());

        preview.Civilizations[0].CivId = "";
        var restored = UncivFiles.GameInfoPreviewFromString(UncivFiles.GameInfoToString(preview));
        Assert.Equal("Rome", restored.Civilizations[0].CivId);

        var failed = new List<Exception?>();
        files.SaveMultiplayerGamePreview(preview, files.GetSave("x"), failed.Add);
        Assert.Null(failed[0]);
    }

    [Fact]
    public void GetSavesCanExcludeAutosaves_AndDeleteRemovesTheFile()
    {
        var files = NewFiles();
        foreach (var name in new[] { "Autosave", "Autosave-Rome-5", "Mine" })
            WriteText(files.GetSave(name), "{}");
        Assert.Equal(["Autosave", "Autosave-Rome-5", "Mine"], files.GetSaves().Select(it => it.Name));
        Assert.Equal(["Mine"], files.GetSaves(autoSaves: false).Select(it => it.Name));
        Assert.True(files.DeleteSave("Mine"));
        Assert.False(files.DeleteSave("Mine"));
        Assert.Empty(files.GetMultiplayerSaves());
        Assert.Equal(files.GetLocalFile(""), files.GetDataFolder());
    }

    [Fact]
    public void AutoSave_WithoutNextTurnWritesOnlyTheUnnumberedFile()
    {
        var files = NewFiles();
        UncivFiles.SaveZipped = false;
        Assert.False(files.Autosaves.AutosaveExists());
        files.Autosaves.AutoSave(NewGame(), 3);
        Assert.True(files.Autosaves.AutosaveExists());
        Assert.Equal(["Autosave"], files.GetSaves().Select(it => it.Name));
        LoadsUpToSetTransients(() => files.Autosaves.LoadLatestAutosave());
    }

    [Fact]
    public void AutoSave_NextTurnKeepsTheYoungestMaxPlusOneFiles()
    {
        var files = NewFiles();
        UncivFiles.SaveZipped = false;
        for (var turn = 1; turn <= 5; turn++)
            files.Autosaves.AutoSave(NewGame(turn), 2, nextTurn: true);
        Assert.Equal(["Autosave", "Autosave-Rome-4", "Autosave-Rome-5"], files.GetSaves().Select(it => it.Name));
    }

    [Fact]
    public void LoadLatestAutosave_FallsBackToTheNewestNumberedOne()
    {
        var files = NewFiles();
        WriteText(files.GetSave("Autosave"), "garbage");
        // the version number of each (unloadable, newer than ours) file tells which one was picked
        WriteText(files.GetSave("Autosave-Rome-7"), "{\"version\":{\"number\":71,\"createdWith\":{\"text\":\"a\",\"number\":1}}}");
        WriteText(files.GetSave("Autosave-Rome-8"), "{\"version\":{\"number\":81,\"createdWith\":{\"text\":\"a\",\"number\":1}}}");
        WriteText(files.GetSave("Unrelated"), "{\"version\":{\"number\":91,\"createdWith\":{\"text\":\"a\",\"number\":1}}}");
        var ex = Assert.Throws<IncompatibleGameInfoVersionException>(() => files.Autosaves.LoadLatestAutosave());
        Assert.Equal(81, ex.Version.Number);
    }

    [Fact]
    public void LoadLatestAutosave_ThrowsWhenNothingToFallBackTo()
    {
        var files = NewFiles();
        Assert.ThrowsAny<Exception>(() => files.Autosaves.LoadLatestAutosave());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" a", false)]
    [InlineData("a ", false)]
    [InlineData("a b", true)]
    public void IsValidFileName(string name, bool expected) => Assert.Equal(expected, UncivFiles.IsValidFileName(name));
}
