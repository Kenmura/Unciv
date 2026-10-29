using System.Text;
using System.Text.Json;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Logic.Files;

// T100/T140: UncivFiles.kt, with GameSerializationTests' save/load cases (canSaveAndLoad*, successfulSaveReportsCompletion, failedSaveReportsException)
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

    private GameInfo NewGame(int turns = 123)
    {
        var game = StartedGame();
        game.GameId = "test-game";
        game.Turns = turns;
        return game;
    }

    private GameInfo StartedGame(long seed = 7)
    {
        var parameters = new GameParameters
        {
            BaseRuleset = ruleset.Name,
            NumberOfCityStates = 2,
            Players = [new Player("Babylon", PlayerType.Human), new Player("Greece"), new Player("Rome")],
        };
        var setup = new GameSetupInfo(parameters, new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Small });
        return GameStarter.StartNewGame(setup, seed, 1000L);
    }

    private static ulong HashOf(GameInfo game) => StateHasher.ComputeHash(h =>
    {
        h.Add(game.GameId).Add(game.Turns).Add(game.CurrentPlayer).Add(game.Difficulty);
        foreach (var civ in game.Civilizations)
        {
            h.Add(civ.CivId).Add(civ.Gold).Add((int)civ.PlayerType).Add(civ.Cities.Count);
            foreach (var city in civ.Cities) h.Add(city.Id).Add(city.Name).Add(city.Population.Population);
            foreach (var key in civ.Diplomacy.Keys) h.Add(key);
            // the unit order is the creation order in a fresh game, but the tile order once loaded (UnitManager.setTransients)
            foreach (var unit in civ.Units.GetCivUnits().Select(it => $"{it.Name}@{it.CurrentTile.Position}").Order(StringComparer.Ordinal)) h.Add(unit);
        }
        foreach (var tile in game.TileMap.Values) h.Add(tile.Position).Add(tile.BaseTerrain);
    });

    /// <summary>A started game whose civs own cities (wired by hand: Civilization.AddCity needs the city automation, T087), know each other and carry state in most managers.</summary>
    private GameInfo BusyGame()
    {
        var game = StartedGame();
        var land = game.TileMap.Values.Where(it => it.IsLand && !it.IsImpassible()).ToList();
        var majors = game.Civilizations.Where(it => it.IsMajorCiv()).ToList();
        for (var i = 0; i < majors.Count; i++)
        {
            var civ = majors[i];
            var city = new CityObject { Id = civ.CivId + "-city", Name = civ.CivId + " City", Location = land[i * 12].Position };
            civ.Cities = [city];
            city.SetTransients(civ);
            city.CityConstructions.ChooseNextConstruction(); // loading chooses for an empty queue (T158), so start from the settled state
            civ.AddGold(100 + i);
            civ.Tech.AddTechnology("Agriculture", showNotification: false);
            civ.NaturalWonders.Add("Krakatoa");
            civ.AddFlag("someFlag", 5 + i);
            civ.ResourceStockpiles.Add("Iron", 3);
            civ.LeaderTitle = "King [leaderName]";
            civ.AttacksSinceTurnStart.Add(new Civilization.HistoricalAttackMemory("Warrior", land[0].Position, land[1].Position));
            civ.LastSeenImprovement[land[2].Position] = "Farm";
            civ.AddNotification("Hello [Rome]", land[3].Position, Notification.NotificationCategory.General, "NotificationIcons/Test");
            civ.PopupAlerts.Add(new PopupAlert(AlertType.FirstContact, "Rome"));
            civ.StatsHistory.RecordRankingStats(civ);
        }
        var religion = new Religion("Test Faith", game, majors[0], 3);
        game.Religions["Test Faith"] = religion;
        majors[0].EspionageManager.SpyList.Add(new Spy("Alice", 2));
        majors[0].EspionageManager.ErasSpyEarnedFor.Add("Medieval era");
        majors[0].TemporaryUniques.Add(new TemporaryUnique(new Unique("[+10]% Strength <for [5] turns>"), 5));
        majors[0].Policies.FreePolicies = 2;
        majors[0].GoldenAges.AddHappiness(50);
        majors[0].ReligionManager.StoredFaith = 12;
        majors[0].DiplomacyFunctions.MakeCivilizationsMeet(majors[1]);
        var diplomacy = majors[0].GetDiplomacyManager(majors[1])!;
        diplomacy.AddModifier(DiplomaticModifiers.DeclaredFriendshipWithOurEnemies, 15f);
        diplomacy.SetFlag(DiplomacyFlags.DeclinedLuxExchange, 4);
        var trade = new Trade();
        trade.OurOffers.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 2, 30));
        trade.TheirOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold, 50));
        majors[1].TradeRequests.Add(new TradeRequest(majors[0].CivId, trade));
        diplomacy.Trades.Add(trade.Clone());
        foreach (var civ in majors) civ.Cache.UpdateCitiesConnectedToCapital(); // derived state that loading recomputes too
        return game;
    }

    [Fact]
    public void BusyGameRoundTripsThroughAFileAndKeepsItsStateHash()
    {
        var files = NewFiles();
        UncivFiles.SaveZipped = true;
        var game = BusyGame();
        var before = HashOf(game);

        var loaded = files.LoadGameFromFile(files.SaveGame(game, "busy"));

        var babylon = loaded.GetCivilization("Babylon");
        Assert.Equal(before, HashOf(loaded));
        Assert.Single(babylon.Cities);
        Assert.Equal(game.GetCivilization("Babylon").Gold, babylon.Gold);
        Assert.Contains("Agriculture", babylon.Tech.TechsResearched);
        Assert.Contains(babylon.PopupAlerts, it => it.Type == AlertType.FirstContact && it.Value == "Rome");
        Assert.Contains(babylon.Notifications, it => it.Text == "Hello [Rome]" && it.Actions.Count == 1);
        Assert.Equal(15f, babylon.GetDiplomacyManager("Greece")!.DiplomaticModifiers[DiplomaticModifiers.DeclaredFriendshipWithOurEnemies.ToString()]);
        var request = Assert.Single(loaded.GetCivilization("Greece").TradeRequests);
        Assert.Equal("Babylon", request.RequestingCiv);
        Assert.Equal(2, request.Trade.OurOffers.Single().Amount);
        Assert.Equal("Test Faith", loaded.Religions["Test Faith"].Name);
        Assert.Equal("Alice", babylon.EspionageManager.SpyList.Single().Name);
        Assert.Equal(["Medieval era"], babylon.EspionageManager.ErasSpyEarnedFor);
        Assert.Single(babylon.TemporaryUniques);
        Assert.Equal(2, babylon.Policies.FreePolicies);
        Assert.Equal(12, babylon.ReligionManager.StoredFaith);
        Assert.Equal(UncivJson.Serialize(game), UncivJson.Serialize(loaded));
        Assert.Equal(before, HashOf(UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(loaded))));
    }

    [Fact]
    public void GameRoundTripsAfterEveryOfSeveralTurns()
    {
        var files = NewFiles();
        UncivFiles.SaveZipped = true;
        var game = BusyGame();
        for (var turn = 0; turn < 6; turn++)
        {
            game.NextTurn(2000L + turn);
            var loaded = files.LoadGameFromFile(files.SaveGame(game, "turn"));
            Assert.Equal(UncivJson.Serialize(game), UncivJson.Serialize(loaded));
            Assert.Equal(HashOf(game), HashOf(loaded));
            Assert.Equal(game.Turns, loaded.Turns);
            // the loaded game carries on exactly like the original
            game = loaded;
        }
    }

    [Fact]
    public void ReligionBeliefsAndCivFreeBuildingsSurviveAJsonRoundTrip()
    {
        var religion = UncivJson.Deserialize<Religion>("""{"name":"R","foundingCivName":"X","founderBeliefs":["z","a"],"followerBeliefs":["q"]}""")!;
        Assert.Contains("\"founderBeliefs\":[\"a\",\"z\"]", UncivJson.Serialize(religion), StringComparison.Ordinal);
        Assert.Contains("\"followerBeliefs\":[\"q\"]", UncivJson.Serialize(religion), StringComparison.Ordinal);

        var json = """{"freeBuildings":{"c1":["B1"]},"freeStatBuildingsProvided":{"Food":["c2"]},"freeSpecificBuildingsProvided":{"B2":["c3"]},"boughtItemsWithIncreasingPrice":{"Bank":2}}""";
        var back = UncivJson.Serialize(UncivJson.Deserialize<CivConstructions>(json));
        Assert.Contains("\"freeBuildings\":{\"c1\":[\"B1\"]}", back, StringComparison.Ordinal);
        Assert.Contains("\"freeStatBuildingsProvided\":{\"Food\":[\"c2\"]}", back, StringComparison.Ordinal);
        Assert.Contains("\"freeSpecificBuildingsProvided\":{\"B2\":[\"c3\"]}", back, StringComparison.Ordinal);
        Assert.Contains("\"boughtItemsWithIncreasingPrice\":{\"Bank\":2}", back, StringComparison.Ordinal);

        var ruins = UncivJson.Serialize(UncivJson.Deserialize<RuinsManager>("""{"lastChosenRewards":["a","b"]}"""));
        Assert.Contains("\"lastChosenRewards\":[\"a\",\"b\"]", ruins, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedGameWithCivsRoundTripsThroughAFile(bool zipped)
    {
        var files = NewFiles();
        UncivFiles.SaveZipped = zipped;
        var game = StartedGame();
        var before = HashOf(game);

        var file = files.SaveGame(game, "slot");
        var json = UncivJson.Serialize(game);
        var loaded = files.LoadGameFromFile(file);

        Assert.Equal(game.GameId, loaded.GameId);
        Assert.Equal(game.Turns, loaded.Turns);
        Assert.Equal(game.Civilizations.Select(it => it.CivId), loaded.Civilizations.Select(it => it.CivId));
        Assert.Equal(CompatibilityVersion.CurrentCompatibilityVersion, loaded.Version);
        Assert.Equal(before, HashOf(loaded));
        Assert.Equal(json, UncivJson.Serialize(loaded));
    }

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
        Assert.Equal(game.Turns, files.LoadGameFromFile(file).Turns);
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
        Assert.Equal(123, files.LoadGameByName("slot").Turns);
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
        var json = UncivJson.Serialize(game).Replace($"\"number\":{CompatibilityVersion.CurrentCompatibilityNumber}", "\"number\":99", StringComparison.Ordinal);
        Assert.Contains("\"number\":99", json, StringComparison.Ordinal);
        WriteText(files.GetSave("future"), json);
        var ex = Assert.Throws<IncompatibleGameInfoVersionException>(() => files.LoadGameByName("future"));
        Assert.Equal(99, ex.Version.Number);
        Assert.Contains(CompatibilityVersion.CurrentCompatibilityVersion.CreatedWith.Text, ex.Message, StringComparison.Ordinal);
        Assert.Throws<IncompatibleGameInfoVersionException>(() => UncivFiles.GameInfoFromString(json));
        Assert.Throws<IncompatibleGameInfoVersionException>(() => UncivFiles.GameInfoFromString(FileConversions.Zip(json)));
    }

    [Fact]
    public void OlderVersionStillLoads()
    {
        var files = NewFiles();
        UncivFiles.SaveZipped = false;
        var game = NewGame();
        game.Version = CompatibilityVersion.CurrentCompatibilityVersion;
        var json = UncivJson.Serialize(game).Replace($"\"number\":{CompatibilityVersion.CurrentCompatibilityNumber}", $"\"number\":{CompatibilityVersion.CurrentCompatibilityNumber - 1}", StringComparison.Ordinal);
        Assert.Contains($"\"number\":{CompatibilityVersion.CurrentCompatibilityNumber - 1}", json, StringComparison.Ordinal);
        WriteText(files.GetSave("older"), json);
        Assert.NotNull(files.LoadGameByName("older"));
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
        Assert.Equal(game.GameId, UncivFiles.GameInfoFromString("  " + zipped.Insert(10, "\r\n") + "\n").GameId);
        UncivFiles.SaveZipped = false;
        var plain = UncivFiles.GameInfoToString(game);
        Assert.StartsWith("{", plain, StringComparison.Ordinal);
        Assert.Equal(game.GameId, UncivFiles.GameInfoFromString(plain).GameId);
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
        preview.Civilizations.Clear();
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
        Assert.Equal("test-game", files.Autosaves.LoadLatestAutosave().GameId);
    }

    [Fact]
    public void AutoSave_NextTurnKeepsTheYoungestMaxPlusOneFiles()
    {
        var files = NewFiles();
        UncivFiles.SaveZipped = false;
        for (var turn = 1; turn <= 5; turn++)
            files.Autosaves.AutoSave(NewGame(turn), 2, nextTurn: true);
        Assert.Equal(["Autosave", "Autosave-Babylon-4", "Autosave-Babylon-5"], files.GetSaves().Select(it => it.Name));
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
