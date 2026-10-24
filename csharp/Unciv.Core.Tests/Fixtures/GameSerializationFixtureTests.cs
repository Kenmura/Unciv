using System.Text;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Core.Tests.Fixtures;

// T151: port of tests/src/com/unciv/logic/GameSerializationTests.kt over a real GameStarter game (1 human + 1 AI, Tiny map, no city states),
// a founded city, met civs and a barbarian encampment, saved through UncivFiles to an in-memory file tree.
// serializedLaziesTest is JVM-specific (kotlin.SynchronizedLazyImpl fields in libGDX Json); System.Text.Json has no lazy-delegate fields to leak.
[Collection("RulesetCache")]
public sealed class GameSerializationFixtureTests : IDisposable
{
    private readonly bool saveZippedBackup = UncivFiles.SaveZipped;
    private readonly GameInfo game;

    public GameSerializationFixtureTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);

        // Must be 1 human otherwise GameInfo.SetTransients crashes on the `if (currentPlayer == "")` line
        var param = new GameParameters
        {
            NumberOfCityStates = 0,
            Players = [new Player("Rome", PlayerType.Human), new Player("Greece")],
        };
        var setup = new GameSetupInfo(param, new MapParameters { MapSize = MapSize.Tiny, Seed = 42L });
        game = GameStarter.StartNewGame(setup, 7, 1000L);

        // Found a city otherwise too many classes have no instance and are not tested
        var civ = game.GetCurrentPlayerCivilization();
        var unit = civ.Units.GetCivUnits().First(it => it.HasUnique(UniqueType.FoundCity));
        var tile = unit.GetTile();
        unit.Civ.AddCity(tile.Position);
        if (tile.Ruleset.TileImprovements.ContainsKey(Constants.CityCenter))
            tile.SetImprovement(Constants.CityCenter);
        unit.Destroy();

        // Ensure some diplomacy objects are instantiated
        civ.DiplomacyFunctions.MakeCivilizationsMeet(game.GetCivilization("Greece"));

        // Ensure a barbarian encampment is included
        game.Barbarians.PlaceBarbarianEncampment(forTesting: true);
    }

    public void Dispose()
    {
        UncivFiles.SaveZipped = saveZippedBackup;
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

    private sealed class MemFile(string name) : IUncivFileHandle
    {
        public byte[] Content = [];
        public bool Exists { get; private set; }
        public bool Directory { get; init; }
        public string Name => name;
        public string NameWithoutExtension => name;
        public bool IsInternal => false;
        public bool IsDirectory => Directory;
        public long Length => Content.Length;
        public long LastModified => 0;
        public IUncivFileHandle Parent => throw new NotSupportedException();
        public IUncivFileHandle Child(string childName) => throw new NotSupportedException();
        public IReadOnlyList<IUncivFileHandle> List() => [];
        public void MakeDirs() => throw new NotSupportedException();
        public bool Delete() => throw new NotSupportedException();
        public Stream Read() => new MemoryStream(Content);

        public Stream Write(bool append)
        {
            if (Directory) throw new UnauthorizedAccessException("is a directory");
            return new Capture(this);
        }

        public string Text => Encoding.UTF8.GetString(Content);

        private sealed class Capture(MemFile file) : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
                file.Content = ToArray();
                file.Exists = true;
                base.Dispose(disposing);
            }
        }
    }

    private static UncivFiles NewFiles() => new(new MemFile(""));

    [Fact]
    public void CanSerializeGame()
    {
        var json = UncivJson.Serialize(game);
        Assert.True(json.Length != 0, "This test will only pass when a game can be serialized");
    }

    [Fact]
    public void CanSaveAndLoadUncompressedGame()
    {
        var saveFile = new MemFile("save");
        UncivFiles.SaveZipped = false;
        game.Turns = 123;
        game.Version = CompatibilityVersion.FirstWithout;

        NewFiles().SaveGame(game, saveFile);
        var loadedGame = NewFiles().LoadGameFromFile(saveFile);

        Assert.StartsWith("{", saveFile.Text, StringComparison.Ordinal);
        Assert.Equal(game.GameId, loadedGame.GameId);
        Assert.Equal(game.Turns, loadedGame.Turns);
        Assert.Equal(CompatibilityVersion.CurrentCompatibilityVersion, loadedGame.Version);
        Assert.Equal(game.GetCivilization("Rome").Cities.Count, loadedGame.GetCivilization("Rome").Cities.Count);
        Assert.Single(loadedGame.GetCivilization("Rome").Cities);
        Assert.Equal(game.Barbarians.Encampments.Count, loadedGame.Barbarians.Encampments.Count);
    }

    [Fact]
    public void CanSaveAndLoadCompressedGame()
    {
        var saveFile = new MemFile("save");
        UncivFiles.SaveZipped = true;
        game.Turns = 123;

        NewFiles().SaveGame(game, saveFile);
        var loadedGame = NewFiles().LoadGameFromFile(saveFile);

        Assert.DoesNotContain("{", saveFile.Text, StringComparison.Ordinal);
        Assert.Equal(game.GameId, loadedGame.GameId);
        Assert.Equal(game.Turns, loadedGame.Turns);
        Assert.Equal(game.GetCivilization("Rome").Cities.Count, loadedGame.GetCivilization("Rome").Cities.Count);
    }

    [Fact]
    public void UncompressedSaveWritesDirectlyToFile()
    {
        UncivFiles.SaveZipped = false;
        var saveFile = new MemFile("writer-only");

        NewFiles().SaveGame(game, saveFile);
        var loadedGame = UncivFiles.GameInfoFromString(saveFile.Text);

        Assert.Equal(game.GameId, loadedGame.GameId);
    }

    [Fact]
    public void SuccessfulSaveReportsCompletion()
    {
        UncivFiles.SaveZipped = false;
        var calls = new List<Exception?>();

        NewFiles().SaveGame(game, new MemFile("save"), calls.Add);

        Assert.Single(calls);
        Assert.Null(calls[0]);
    }

    [Fact]
    public void FailedSaveReportsException()
    {
        UncivFiles.SaveZipped = false;
        var calls = new List<Exception?>();

        NewFiles().SaveGame(game, new MemFile("dir") { Directory = true }, calls.Add);

        Assert.Single(calls);
        Assert.NotNull(calls[0]);
    }
}
