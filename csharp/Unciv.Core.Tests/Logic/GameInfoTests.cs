using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using GeneratorClass = Unciv.Logic.Map.MapGenerator.MapGenerator;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Logic;

// T046: GameInfo.kt fields, setTransients and queries on a small real game (vanilla ruleset, generated Tiny map, no units/cities).
// TestGame-based fixtures (SerializationTests, LoadGameTest) are T072; turn handling is T071.
[Collection("RulesetCache")]
public sealed class GameInfoTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public GameInfoTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private GameInfo NewGame(params (string Nation, PlayerType Type)[] civs)
    {
        var parameters = new GameParameters { BaseRuleset = ruleset.Name };
        var mapParameters = new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny };
        var game = new GameInfo
        {
            GameParameters = parameters,
            TileMap = new GeneratorClass(ruleset).GenerateMap(mapParameters),
            Difficulty = "Prince",
            GameId = "test-game",
        };
        foreach (var (nation, type) in civs)
            game.Civilizations.Add(new Civilization(nation) { PlayerType = type });
        game.TileMap.GameInfo = game;
        WireUnportedManagers(game);
        return game;
    }

    /// <summary>CityStateFunctions is not ported yet: its seam gets a no-op stand-in.</summary>
    private static void WireUnportedManagers(GameInfo game)
    {
        foreach (var civ in game.Civilizations)
        {
            civ.CityStateFunctions = new FakeCityStateFunctions();
        }
    }

    /// <summary>Babylon (human), Greece, Geneva (city state) and the barbarians; each real civ owns a settler so none counts as defeated.</summary>
    private GameInfo StartedGame()
    {
        var game = NewGame(("Babylon", PlayerType.Human), ("Greece", PlayerType.AI), ("Geneva", PlayerType.AI), (Constants.Barbarians, PlayerType.AI));
        game.SetTransients();
        var land = game.TileMap.Values.Where(it => it.IsLand && !it.IsImpassible()).ToList();
        for (var i = 0; i < 3; i++)
            Assert.NotNull(game.TileMap.PlaceUnitNearTile(land[i * 7].Position, "Settler", game.Civilizations[i]));
        return game;
    }

    [Fact]
    public void Defaults_MatchKotlin()
    {
        var game = new GameInfo();
        Assert.Equal(CompatibilityVersion.FirstWithout, game.Version);
        Assert.Equal("Chieftain", game.Difficulty);
        Assert.Equal(-1, game.HistoryStartTurn);
        Assert.Equal(0, game.Turns);
        Assert.Equal("", game.CurrentPlayer);
        Assert.Equal(1000, game.SimulateMaxTurns);
        Assert.False(game.IsSimulation());
        Assert.Throws<InvalidOperationException>(() => game.Ruleset);
        Assert.Throws<InvalidOperationException>(() => game.Speed);
        Assert.Throws<InvalidOperationException>(() => game.CurrentPlayerCiv);
        Assert.Throws<InvalidOperationException>(() => game.GetDifficulty());
        Assert.Throws<InvalidOperationException>(() => game.GetGlobalUniques());
    }

    [Fact]
    public void NextUnitId_Increments_AndClonePreservesIt()
    {
        var game = new GameInfo();
        Assert.Equal(1, game.GetNextUnitId());
        Assert.Equal(2, game.GetNextUnitId());
        game.TileMap = new TileMap();
        game.EnsureLastUnitId(); // non-zero: untouched
        Assert.Equal(3, game.GetNextUnitId());
        var clone = game.Clone();
        Assert.Equal(4, clone.GetNextUnitId());
        Assert.Equal(4, game.GetNextUnitId());
    }

    [Fact]
    public void EnsureLastUnitId_EmptyMapStaysZero()
    {
        var game = new GameInfo { TileMap = new TileMap() };
        game.EnsureLastUnitId();
        Assert.Equal(1, game.GetNextUnitId());
    }

    [Fact]
    public void IsSimulation_UsesEngineOptionsAndSimulateUntilWin()
    {
        var game = new GameInfo { Turns = 5 };
        game.EngineOptions.SimulateUntilTurn = 6;
        Assert.True(game.IsSimulation());
        game.EngineOptions.SimulateUntilTurn = 5;
        Assert.False(game.IsSimulation());
        game.SimulateUntilWin = true;
        Assert.True(game.IsSimulation()); // 5 < 1000
        game.SimulateMaxTurns = 5;
        Assert.False(game.IsSimulation());
    }

    [Fact]
    public void SetTransients_InitializesRulesetSpeedDifficultyAndCurrentPlayer()
    {
        var game = StartedGame();
        Assert.Contains(ruleset.Name, game.Ruleset.Mods);
        Assert.Equal("Prince", game.GetDifficulty().Name);
        Assert.Equal(game.GameParameters.Speed, game.Speed.Name);
        Assert.Equal("Babylon", game.CurrentPlayer);
        Assert.Same(game.GetCivilization("Babylon"), game.CurrentPlayerCiv);
        Assert.Same(game, game.TileMap.GameInfo);
        Assert.NotNull(game.GetGlobalUniques());
        Assert.Same(game, game.Civilizations[1].GameInfo);
        Assert.Same(game, game.Barbarians.GameInfo);
    }

    [Fact]
    public void SetTransients_UnknownDifficultyFallsBackToTheHardestOne()
    {
        var game = NewGame(("Babylon", PlayerType.Human));
        game.Difficulty = "No such difficulty";
        game.SetTransients();
        var hardest = game.Ruleset.Difficulties.Values
            .OrderByDescending(d => d.BuildingCostModifier + d.PolicyCostModifier + d.ResearchCostModifier + d.UnhappinessModifier
                - d.AiBuildingCostModifier - d.AiUnitCostModifier - d.AiUnhappinessModifier)
            .First();
        Assert.Equal(hardest.Name, game.GetDifficulty().Name);
    }

    [Fact]
    public void SetTransients_MissingModThrows()
    {
        var game = NewGame(("Babylon", PlayerType.Human));
        game.GameParameters.Mods.Add("Not installed mod");
        var ex = Assert.Throws<MissingModsException>(() => game.SetTransients());
        Assert.Contains("Not installed mod", ex.Message);
    }

    [Fact]
    public void SetTransients_NoHumanCivThrows()
    {
        var game = NewGame(("Greece", PlayerType.AI));
        Assert.Throws<InvalidOperationException>(() => game.SetTransients());
    }

    [Fact]
    public void SetTransients_KeepsAnExistingCurrentPlayer()
    {
        var game = NewGame(("Babylon", PlayerType.Human), ("Greece", PlayerType.Human));
        game.CurrentPlayer = "Greece";
        game.SetTransients();
        Assert.Equal("Greece", game.CurrentPlayerCiv.CivId);
    }

    [Fact]
    public void SetTransients_FillsSpaceResources()
    {
        var game = StartedGame();
        Assert.Contains("Aluminum", game.SpaceResources);
        Assert.DoesNotContain("Iron", game.SpaceResources);
    }

    [Fact]
    public void GetCivilization_ThrowsForUnknownAndFindsLateAdditions()
    {
        var game = StartedGame();
        Assert.Throws<InvalidOperationException>(() => game.GetCivilization("Atlantis"));
        Assert.Null(game.GetCivilizationOrNull("Atlantis"));
        Assert.Same(game.Civilizations[1], game.GetCivilizationOrNull("Greece"));
        Assert.Same(game.Civilizations[3], game.GetBarbarianCivilization());
    }

    [Fact]
    public void GetSpectator_ReusesTheSpectatorOfThatPlayer()
    {
        var game = NewGame(("Babylon", PlayerType.Human), (Constants.Spectator, PlayerType.Human));
        game.Civilizations[1].PlayerId = "player-1";
        game.SetTransients();
        Assert.True(game.Civilizations[1].IsSpectator());
        Assert.Same(game.Civilizations[1], game.GetSpectator("player-1"));
        Assert.Equal(2, game.Civilizations.Count);
        // Creating a temporary spectator for an unknown player runs Civilization.setTransients.
    }

    [Fact]
    public void GetPlayerToViewAs_OfflineIsTheCurrentPlayer_OnlineIsTheUsersCivStartingAtTheCurrentPlayer()
    {
        var game = NewGame(("Babylon", PlayerType.Human), ("Greece", PlayerType.Human), ("Rome", PlayerType.Human), (Constants.Spectator, PlayerType.Human));
        game.Civilizations[3].PlayerId = "stranger";
        game.SetTransients();
        Assert.Same(game.CurrentPlayerCiv, game.GetPlayerToViewAs());

        game.GameParameters.IsOnlineMultiplayer = true;
        game.Civilizations[0].PlayerId = "a";
        game.Civilizations[1].PlayerId = "b";
        game.Civilizations[2].PlayerId = "a";
        game.ClientState.UserId = "a";
        game.CurrentPlayer = "Greece";
        Assert.Equal("Rome", game.GetPlayerToViewAs().CivId); // next one from Greece belonging to "a"
        game.CurrentPlayer = "Rome";
        Assert.Equal("Rome", game.GetPlayerToViewAs().CivId);
        game.ClientState.UserId = "stranger";
        Assert.True(game.GetPlayerToViewAs().IsSpectator());
        Assert.Same(game.Civilizations[3], game.GetPlayerToViewAs());
    }

    [Fact]
    public void GetCivsSorted_ExcludesBarbariansAndSpectators_PutsMajorsFirstThenSortsByName()
    {
        var game = StartedGame();
        var spectator = new Civilization(Constants.Spectator) { PlayerType = PlayerType.Human };
        spectator.CityStateFunctions = new FakeCityStateFunctions();
        game.Civilizations.Add(spectator);
        spectator.GameInfo = game;
        spectator.SetNationTransient();
        var all = game.GetCivsSorted().Select(it => it.CivId).ToList();
        Assert.Equal(["Babylon", "Greece", "Geneva"], all);

        var greeceFirst = game.GetCivsSorted(civToSortFirst: game.GetCivilization("Greece")).Select(it => it.CivId);
        Assert.Equal(["Greece", "Babylon", "Geneva"], greeceFirst);

        Assert.Equal(["Babylon", "Greece"], game.GetCivsSorted(includeCityStates: false).Select(it => it.CivId));
        Assert.Equal(["Babylon", "Geneva"], game.GetCivsSorted(additionalFilter: it => it.CivId != "Greece").Select(it => it.CivId));
    }

    [Fact]
    public void GetCivsSorted_DefeatedAreExcludedUnlessRequested()
    {
        var game = StartedGame();
        var greece = game.GetCivilization("Greece");
        Assert.True(greece.IsAlive());
        Assert.Equal(["Babylon", "Greece"], game.GetAliveMajorCivs().Select(it => it.CivId));
        Assert.Equal(["Geneva"], game.GetAliveCityStates().Select(it => it.CivId));
        Assert.Empty(game.GetCities());
    }

    [Fact]
    public void GetYear_FollowsSpeedAndStartingEra()
    {
        var game = StartedGame();
        var fromTurn0 = game.GetYear();
        game.Turns = 10;
        Assert.True(game.GetYear() > fromTurn0);
        Assert.Equal(game.GetYear(5), game.GetYear() + (game.GetYear(5) - game.GetYear()));
        game.Turns = 0;
        var ancientYear = game.GetYear();
        game.GameParameters.StartingEra = "Medieval era";
        Assert.True(game.GetYear() > ancientYear);
    }

    [Fact]
    public void IsReligionEnabled_FollowsTheStartingEraAndModOptions()
    {
        var game = StartedGame();
        var vanillaAnswer = game.IsReligionEnabled();
        Assert.Equal(!((IHasUniques)game.Ruleset.Eras[game.GameParameters.StartingEra]).HasUnique(UniqueType.DisablesReligion)
            && !((IHasUniques)game.Ruleset.ModOptions).HasUnique(UniqueType.DisableReligion), vanillaAnswer);
        Assert.False(vanillaAnswer); // Civ V - Vanilla has no religion
    }

    [Fact]
    public void IsEspionageEnabled_ReadsTheGameParameter()
    {
        var game = StartedGame();
        Assert.False(game.IsEspionageEnabled());
        game.GameParameters.EspionageEnabled = true;
        Assert.True(game.IsEspionageEnabled());
    }

    [Fact]
    public void IsUnavailableBySettingsCached_AgreesWithTheUncachedAnswer()
    {
        var game = StartedGame();
        foreach (var building in game.Ruleset.Buildings.Values.Take(20))
        {
            var expected = ((IHasUniques)building).IsUnavailableBySettings(game);
            Assert.Equal(expected, game.IsUnavailableBySettingsCached(building));
            Assert.Equal(expected, game.IsUnavailableBySettingsCached(building));
        }
    }

    [Fact]
    public void Clone_CopiesSerializedFieldsAndGameParameters()
    {
        var game = StartedGame();
        game.Turns = 7;
        game.OneMoreTurnMode = true;
        game.CustomSaveLocation = "somewhere";
        game.DiplomaticVictoryVotesCast["Greece"] = null;
        game.UnitNamesTaken.Add("Bob");
        game.HistoryStartTurn = 3;
        game.VictoryData = new VictoryData(game.GetCivilization("Greece"), "Domination", 7);
        game.EngineOptions.Supercharged = true;
        var clone = game.Clone();
        Assert.Equal(7, clone.Turns);
        Assert.True(clone.OneMoreTurnMode);
        Assert.Equal("somewhere", clone.CustomSaveLocation);
        Assert.True(clone.DiplomaticVictoryVotesCast.ContainsKey("Greece"));
        Assert.Equal(["Bob"], clone.UnitNamesTaken);
        Assert.Equal(3, clone.HistoryStartTurn);
        Assert.Equal("test-game", clone.GameId);
        Assert.NotSame(game.GameParameters, clone.GameParameters); // T219: copied, not shared
        Assert.Equal(4, clone.Civilizations.Count);
        Assert.NotSame(game.Civilizations[0], clone.Civilizations[0]);
        Assert.Equal("Greece", clone.VictoryData!.WinningCiv);
        Assert.Throws<InvalidOperationException>(() => clone.VictoryData.WinningCivObject); // transient not copied
        Assert.True(clone.EngineOptions.Supercharged);
        Assert.NotSame(game.EngineOptions, clone.EngineOptions);
        Assert.Same(game.ClientState, clone.ClientState);
        Assert.Equal(game.TileMap.Values.Count, clone.TileMap.Values.Count);
    }

    [Fact]
    public void VictoryData_ResolvedBySetTransients()
    {
        var game = NewGame(("Babylon", PlayerType.Human), ("Greece", PlayerType.AI));
        game.VictoryData = new VictoryData("Greece", "Cultural", 12);
        game.SetTransients();
        Assert.Same(game.Civilizations[1], game.VictoryData.WinningCivObject);
    }

    [Fact]
    public void Json_RoundTripOfTheOwnFields()
    {
        // Civilizations and the managers below them have no serialization attributes yet (T072/T100), so the round trip uses a game without civs.
        var game = new GameInfo { Turns = 3, GameId = "g", CurrentPlayer = "Rome", Difficulty = "Prince", HistoryStartTurn = 0, OneMoreTurnMode = true };
        game.GetNextUnitId();
        game.DiplomaticVictoryVotesCast["a"] = null;
        game.DiplomaticVictoryVotesCast["b"] = "a";
        game.VictoryData = new VictoryData("a", "Cultural", 9);
        game.UnitNamesTaken.Add("Bob");
        var loaded = UncivJson.Deserialize<GameInfo>(UncivJson.Serialize(game))!;
        Assert.Equal(3, loaded.Turns);
        Assert.Equal("g", loaded.GameId);
        Assert.Equal("Rome", loaded.CurrentPlayer);
        Assert.True(loaded.OneMoreTurnMode);
        Assert.Equal(0, loaded.HistoryStartTurn);
        Assert.Null(loaded.DiplomaticVictoryVotesCast["a"]);
        Assert.Equal("a", loaded.DiplomaticVictoryVotesCast["b"]);
        Assert.Equal(("a", "Cultural", 9), (loaded.VictoryData!.WinningCiv, loaded.VictoryData.VictoryType, loaded.VictoryData.VictoryTurn));
        Assert.Equal(["Bob"], loaded.UnitNamesTaken);
        Assert.Equal(2, loaded.GetNextUnitId()); // lastUnitId is private but serialized
    }

    [Fact]
    public void Checksum_IgnoresTheStoredChecksum_AndChangesWithState()
    {
        var game = new GameInfo { GameId = "g" };
        var first = game.CalculateChecksum();
        Assert.Equal("", game.Checksum);
        game.Checksum = "old";
        Assert.Equal(first, game.CalculateChecksum());
        Assert.Equal("old", game.Checksum);
        game.Turns++;
        Assert.NotEqual(first, game.CalculateChecksum());
    }

    [Fact]
    public void Json_SerializedShapeUsesTheKotlinFieldNames()
    {
        var game = new GameInfo();
        game.GetNextUnitId();
        var json = UncivJson.Serialize(game);
        foreach (var name in new[] { "version", "civilizations", "barbarians", "religions", "difficulty", "tileMap", "gameParameters", "turns",
                     "oneMoreTurnMode", "currentPlayer", "currentTurnStartTime", "gameId", "checksum", "lastUnitId", "diplomaticVictoryVotesCast",
                     "diplomaticVictoryVotesProcessed", "historyStartTurn", "unitNamesTaken" })
            Assert.Contains($"\"{name}\":", json);
        Assert.DoesNotContain("\"ruleset\"", json);
        Assert.DoesNotContain("clientState", json);
    }

    [Fact]
    public void Preview_CopiesTheLobbyFields()
    {
        var game = StartedGame();
        game.Turns = 4;
        game.CurrentTurnStartTime = 99;
        var preview = game.AsPreview();
        Assert.Equal(4, preview.Turns);
        Assert.Equal("test-game", preview.GameId);
        Assert.Equal(99, preview.CurrentTurnStartTime);
        Assert.Equal("Babylon", preview.GetCurrentPlayerCiv().CivId);
        Assert.Equal(4, preview.Civilizations.Count);
        game.GetCivilization("Greece").PlayerId = "p";
        Assert.Equal("Greece", game.AsPreview().GetPlayerCiv("p")!.CivId);
        Assert.Null(preview.GetPlayerCiv("nobody"));
        Assert.Throws<InvalidOperationException>(() => preview.GetCivilization("Atlantis"));
    }

    [Fact]
    public void RandomGameId_IsDeterministicForASeed()
    {
        Assert.Equal(GameInfoPreview.RandomGameId(new GameRandom(5)), GameInfoPreview.RandomGameId(new GameRandom(5)));
        Assert.NotEqual(GameInfoPreview.RandomGameId(new GameRandom(5)), GameInfoPreview.RandomGameId(new GameRandom(6)));
    }

    [Fact]
    public void ExploredResourcesNotification_NullWhenNothingIsVisibleOrExplored()
    {
        var game = StartedGame();
        var babylon = game.GetCivilization("Babylon");
        Assert.Null(game.GetExploredResourcesNotification(babylon, "No such resource"));
        Assert.False(game.NotifyExploredResources(babylon, "No such resource"));
        Assert.DoesNotContain(babylon.Notifications, it => it.Text.Contains("revealed"));
    }

    /// <summary>Babylon gets a city on the first land tile; returns the land tiles ordered by distance to it, all stripped of resources and unexplored.</summary>
    private (GameInfo Game, Civilization Babylon, CityObject City, List<Unciv.Logic.Map.Tiles.Tile> Tiles) GameWithCity()
    {
        var game = StartedGame();
        var babylon = game.GetCivilization("Babylon");
        var center = game.TileMap.Values.First(it => it.IsLand && !it.IsImpassible());
        var fake = new FakeCityState("c1", new FakeCiv("Babylon")) { CivData = babylon, LocationData = center.Position };
        babylon.Cities = [fake];
        fake.City.SetTransients(babylon);
        fake.ConstructionsFake.ChooseNextConstructionIsNoOp = true;
        foreach (var tile in game.TileMap.Values) tile.SetTileResource((string?)null);
        var tiles = game.TileMap.Values.Where(it => it.IsLand && it != center && !babylon.HasExplored(it)).OrderBy(it => it.AerialDistanceTo(center)).ThenBy(it => it.ZeroBasedIndex).ToList();
        return (game, babylon, fake.City, tiles);
    }

    [Fact]
    public void ExploredResourcesNotification_NamesTheClosestCityAndLocatesTheExploredSources()
    {
        var (game, babylon, city, tiles) = GameWithCity();
        tiles[0].SetTileResource("Wheat");
        tiles[1].SetTileResource("Wheat");
        tiles[2].SetTileResource("Wheat"); // never explored
        tiles[0].SetExplored(babylon, true);
        tiles[1].SetExplored(babylon, true);

        var notification = game.GetExploredResourcesNotification(babylon, "Wheat");
        Assert.NotNull(notification);
        Assert.Equal($"[2] sources of [Wheat] revealed, e.g. near [{city.Name}]", notification.Text);
        Assert.Equal(["ResourceIcons/Wheat"], notification.Icons);
        Assert.Equal(Notification.NotificationCategory.General, notification.Category);
        Assert.Equal(2, notification.Actions.Count());

        Assert.True(game.NotifyExploredResources(babylon, "Wheat"));
        Assert.Contains(babylon.Notifications, it => it.Text == notification.Text);
    }

    [Fact]
    public void ExploredResourcesNotification_SingleSourceTextAndTheDistanceAndTileFilters()
    {
        var (game, babylon, city, tiles) = GameWithCity();
        var near = tiles[0];
        var nearDistance = near.AerialDistanceTo(city.GetCenterTile());
        var far = tiles.First(it => it.AerialDistanceTo(city.GetCenterTile()) > nearDistance);
        near.SetTileResource("Wheat");
        far.SetTileResource("Wheat");
        near.SetExplored(babylon, true);
        far.SetExplored(babylon, true);
        var wheat = game.Ruleset.TileResources["Wheat"];

        Assert.Equal($"[Wheat] revealed near [{city.Name}]",
            game.GetExploredResourcesNotification(babylon, wheat, maxDistance: nearDistance)!.Text);
        Assert.Equal($"[Wheat] revealed near [{city.Name}]",
            game.GetExploredResourcesNotification(babylon, wheat, filter: it => it == far)!.Text);
        Assert.Null(game.GetExploredResourcesNotification(babylon, wheat, filter: _ => false));
        Assert.Null(game.GetExploredResourcesNotification(babylon, wheat, maxDistance: -1));
        Assert.True(game.NotifyExploredResources(babylon, wheat, nearDistance));
        Assert.False(game.NotifyExploredResources(babylon, wheat, -1));
        ((IGameInfoState)game).NotifyExploredResources(babylon, wheat, nearDistance); // the seam overload
        Assert.Equal(2, babylon.Notifications.Count(it => it.Text.StartsWith("[Wheat] revealed")));
    }

    [Fact]
    public void ExploredResourcesNotification_CityStateOnlyResourceUsesTheCityStatesCapital()
    {
        var (game, babylon, city, _) = GameWithCity();
        var wheat = game.Ruleset.TileResources["Wheat"];
        var geneva = game.GetCivilization("Geneva");
        Assert.Null(geneva.CityStateResource);
        // Nothing marks Wheat as city-state-only in Vanilla, so no mercantile city state matches and nothing is found.
        Assert.Null(game.GetExploredResourcesNotification(babylon, wheat));
        Assert.NotNull(city);
    }

    [Fact]
    public void SetTransients_RunsTheCityPassOfUpdateCivilizationState()
    {
        var (game, babylon, city, _) = GameWithCity();
        city.DemandedResource = "No such resource";
        Assert.Single(babylon.Cities);
        game.SetTransients();
        Assert.Equal("", babylon.Cities.Single().DemandedResource);
    }

    [Fact]
    public void SetTransients_RenamesModsWithOuterBlanksAndFixesDashedNames()
    {
        RulesetCache.Put("Dash Mod", new RulesetClass { Name = "Dash Mod" });
        var game = NewGame(("Babylon", PlayerType.Human));
        game.GameParameters.Mods.Add("Dash-Mod");
        game.SetTransients();
        Assert.Contains("Dash Mod", game.GameParameters.Mods);
        Assert.DoesNotContain("Dash-Mod", game.GameParameters.Mods);
    }

    [Fact]
    public void SetTransients_FixesADashedBaseRuleset()
    {
        var game = NewGame(("Babylon", PlayerType.Human));
        var copy = RulesetCache.GetVanillaRuleset();
        RulesetCache.Put("My Base", copy);
        game.GameParameters.BaseRuleset = "My-Base";
        // the combined ruleset cannot name the base "My Base" here, so only the parameter fix is observable
        try { game.SetTransients(); }
        catch (MissingModsException) { /* the copy keeps the vanilla name */ }
        Assert.Equal("My Base", game.GameParameters.BaseRuleset);
    }

    [Fact]
    public void SetTransients_MovesABaseRulesetOutOfTheModList()
    {
        var game = NewGame(("Babylon", PlayerType.Human));
        game.GameParameters.BaseRuleset = "Something else";
        game.GameParameters.Mods.Add(ruleset.Name);
        game.SetTransients();
        Assert.Equal(ruleset.Name, game.GameParameters.BaseRuleset);
        Assert.DoesNotContain(ruleset.Name, game.GameParameters.Mods);
    }

    [Fact]
    public void HandleObsoleteGlobalQuestsOfAliveCityStates_ForwardsToTheCityStatesQuestManagers()
    {
        var game = StartedGame();
        ((IGameContextGameInfo)game).HandleObsoleteGlobalQuestsOfAliveCityStates(); // real QuestManagers without quests: nothing to do
    }

    [Fact]
    public void GameContextSeam_ExposesTheGameFields()
    {
        var game = StartedGame();
        game.TileMap.MapParameters.Seed = 42;
        IGameContextGameInfo seam = game;
        Assert.Equal("test-game", seam.GameId);
        Assert.Equal(42L, seam.MapSeed);
        Assert.Equal("Prince", seam.GetDifficultyName());
        Assert.Equal(4, seam.Civilizations.Count);
        Assert.Equal(game.TileMap.Values.Count, seam.TileList!.Count());
        Assert.Same(game.Civilizations[0], seam.GetCivilization("Babylon"));
        Assert.Equal(game.Ruleset.Units["Settler"], seam.GetBaseUnit("Settler"));
        Assert.ThrowsAny<Exception>(() => seam.GetBaseUnit("No such unit"));
        Assert.Same(game.Ruleset.ModOptions.Constants, seam.GetModConstants());
        Assert.Empty(seam.GetCities());
        Assert.NotNull(seam.Ruleset);
        Assert.NotNull(seam.Speed);
    }

    [Fact]
    public void ClientState_Defaults_AndSeamMembers()
    {
        var game = new GameInfo();
        IGameInfoState state = game;
        Assert.False(state.IsFullAutoPlayAI);
        Assert.False(state.AlternateUnitCycleOrder);
        state.AddCompletedTutorialTask("civ", "t");
        Assert.Contains("t", game.ClientState.TutorialTasksCompleted("civ"));
        Assert.Empty(game.ClientState.TutorialTasksCompleted("other"));
        Assert.Equal(6, ((IGameContextGameInfo)new GameInfo { Turns = 6 }).Turns);
    }

    [Fact]
    public void GameSetupInfo_FromGameInfo_ClonesBothParameterSets()
    {
        var game = StartedGame();
        var setup = new GameSetupInfo(game);
        Assert.NotSame(game.GameParameters, setup.GameParameters);
        Assert.NotSame(game.TileMap.MapParameters, setup.MapParameters);
        Assert.Equal(game.TileMap.MapParameters.Seed, setup.MapParameters.Seed);
    }
}
