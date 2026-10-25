using System.Globalization;
using Unciv.Commands;
using Unciv.Commands.DevConsole;
using Unciv.Commands.Log;
using Unciv.DevConsole;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapGenerator;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.DevConsole;

// T229: the dev console as a logged command. Babylon (current player) and Greece are human, Rome is AI; Babylon and Greece have a city.
[Collection("RulesetCache")]
public sealed class ConsoleCommandTests : IDisposable
{
    private readonly RulesetClass ruleset;
    private readonly GameSession session;
    private readonly GameInfo game;
    private readonly CityObject city;
    private readonly Civilization babylon;
    private readonly ConsoleSelection onCity;

    public ConsoleCommandTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
        game = GameStarter.StartNewGame(Setup(godMode: true), 7L, 1000L);
        foreach (string name in new[] { "Babylon", "Greece" })
        {
            var civ = game.GetCivilization(name);
            var settler = civ.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
            civ.AddCity(settler.CurrentTile.Position, settler);
        }
        session = new GameSession(game);
        babylon = game.GetCivilization("Babylon");
        city = babylon.Cities[0];
        onCity = new ConsoleSelection(city.GetCenterTile().Position);
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

    private GameSetupInfo Setup(bool godMode) => new(
        new GameParameters
        {
            BaseRuleset = ruleset.Name,
            NumberOfCityStates = 0,
            GodMode = godMode,
            VictoryTypes = ruleset.Victories.Keys.ToList(),
            Players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.Human), new Player("Rome")],
        },
        new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny });

    private static ulong Hash(GameInfo g) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(g, forceZip: false)));

    private static RunConsoleCommand Line(string line, ConsoleSelection? selection = null) =>
        new() { Line = line, Tile = selection?.Tile, UnitId = selection?.UnitId, Civ = selection?.Civ };

    private void Ok(string line, ConsoleSelection? selection = null)
    {
        var result = session.Submit("Babylon", Line(line, selection));
        Assert.True(result.Accepted, line + ": " + result.Validation.Message);
    }

    private string Rejected(string line, string code, ConsoleSelection? selection = null, string actor = "Babylon")
    {
        ulong before = Hash(game);
        int logged = session.Log.Count;
        var result = session.Submit(actor, Line(line, selection));
        Assert.False(result.Accepted);
        Assert.Equal(code, result.Validation.Code);
        Assert.Equal(before, Hash(game));
        Assert.Equal(logged, session.Log.Count);
        return result.Validation.Message;
    }

    private string Error(string line, ConsoleSelection? selection = null) => Rejected(line, CommandErrors.InvalidTarget, selection);

    private Tile FreeLandTileNextToCity() =>
        city.GetCenterTile().Neighbors.First(it => it.IsLand && !it.IsCityCenter() && it.GetFirstUnit() == null);

    // --- the gate

    [Fact]
    public void TheConsoleNeedsGodModeFromTheGameSetup()
    {
        var plain = new GameSession(GameStarter.StartNewGame(Setup(godMode: false), 7L, 1000L));
        ulong before = Hash(plain.Game);
        var result = plain.Submit("Babylon", Line("civ addstat gold 5"));
        Assert.False(result.Accepted);
        Assert.Equal(CommandErrors.NotAllowed, result.Validation.Code);
        Assert.Contains("God mode", result.Validation.Message, StringComparison.Ordinal);
        Assert.Equal(before, Hash(plain.Game));
        Assert.Empty(plain.Log);
        Assert.True(session.Submit("Babylon", Line("civ addstat gold 5")).Accepted);
    }

    [Fact]
    public void OnlyHumanPlayersMayUseTheConsoleAndItDoesNotNeedTheirTurn()
    {
        Rejected("civ addstat gold 5", CommandErrors.NotHuman, actor: "Rome");
        Rejected("civ addstat gold 5", CommandErrors.UnknownCiv, actor: "Atlantis");
        Assert.Equal(CommandErrors.NotHuman, session.SubmitAsServer(Line("civ addstat gold 5")).Validation.Code);
        Assert.Equal(CommandCodes.MalformedCommand, session.Submit("server", Line("civ addstat gold 5")).Validation.Code);
        Assert.True(session.Submit("Greece", Line("civ addstat gold 5 Greece")).Accepted);
    }

    [Fact]
    public void ErrorsAndHintsAreRejectedSoNeitherIsLogged()
    {
        Assert.StartsWith("Invalid command.\nAvailable commands:\n- city\n- civ\n- diplomacy\n- game\n- tile\n- unit", Error("nonsense"), StringComparison.Ordinal);
        Assert.Equal("Format: civ addstat <stat> <amount> [civ]", Rejected("civ addstat", CommandErrors.NotAllowed));
        Assert.Equal("Available commands: city, civ, diplomacy, game, tile, unit", Rejected("", CommandErrors.NotAllowed));
        Assert.Contains("Available commands: ", Rejected("civ", CommandErrors.NotAllowed), StringComparison.Ordinal);
        Assert.Equal("true", Rejected("tile checkfilter Land", CommandErrors.NotAllowed, onCity));
        Assert.Equal("Select tile", Error("tile checkfilter Land"));
        Assert.Contains("too long", Error(new string('x', ConsoleRunner.MaxLineLength + 1)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandRoundTripsThroughItsEnvelope()
    {
        var command = Line("tile setowner babylon", onCity);
        var envelope = session.Registry.ToEnvelope(1, "Babylon", command);
        Assert.Equal("run-console-command", envelope.Type);
        var back = (RunConsoleCommand)session.Registry.FromEnvelope(envelope);
        Assert.Equal(command.Line, back.Line);
        Assert.Equal(command.Tile, back.Tile);
        Assert.Null(back.UnitId);
    }

    // --- the tree

    [Fact]
    public void EveryFormatMatchesItsPathInTheTree()
    {
        var commands = ConsoleRunner.EnumerateCommands().ToList();
        Assert.True(commands.Count > 50);
        foreach (var (nodes, format) in commands)
        {
            string prefix = string.Join(' ', nodes);
            Assert.True(format == prefix || format.StartsWith(prefix + " ", StringComparison.Ordinal), $"{format} does not match {prefix}");
        }
    }

    // --- game and civ

    [Fact]
    public void GameCommands()
    {
        Ok("game setturn 42");
        Assert.Equal(42, game.Turns);
        Assert.Equal("Number out of range", Error("game setturn -1"));
        Assert.Equal("Unrecognized difficulty", Error("game setdifficulty nonsense"));
        string other = ruleset.Difficulties.Keys.First(it => it != game.Difficulty);
        Ok($"game setdifficulty \"{other}\"");
        Assert.Equal(other, game.Difficulty);
        Ok("game add-spectator");
        Assert.Contains(game.Civilizations, it => it.Nation.IsSpectator);
        Assert.Equal("Spectator already exists", Error("game add-spectator"));
    }

    [Fact]
    public void CivCommands()
    {
        int gold = babylon.Gold;
        Ok("civ addstat gold 25");
        Assert.Equal(gold + 25, babylon.Gold);
        Ok("civ addstat gold 5 Greece");
        Assert.Equal("Production is not civ-wide", Error("civ addstat production 5"));
        Assert.Equal("Unknown civ: atlantis", Error("civ addstat gold 5 Atlantis"));

        Ok("civ setplayertype Greece ai");
        Assert.Equal(PlayerType.AI, game.GetCivilization("Greece").PlayerType);
        Assert.Equal("Can only change player type for major civs", Error("civ setplayertype Barbarians human"));

        Ok("civ addtechnology Babylon Pottery");
        Assert.True(babylon.Tech.IsResearched("Pottery"));
        Assert.Equal("Babylon already has researched Pottery", Rejected("civ addtechnology Babylon Pottery", CommandErrors.NotAllowed));
        Assert.Equal("Unrecognized technology", Error("civ addtechnology Babylon Nonsense"));
        Ok("civ removetechnology Babylon Pottery");
        Assert.False(babylon.Tech.IsResearched("Pottery"));
        Assert.Equal("Babylon does not have Pottery", Rejected("civ removetechnology Babylon Pottery", CommandErrors.NotAllowed));

        Ok("civ addpolicy Babylon Tradition");
        Assert.True(babylon.Policies.IsAdopted("Tradition"));
        Assert.Contains("already has adopted", Rejected("civ addpolicy Babylon Tradition", CommandErrors.NotAllowed), StringComparison.Ordinal);
        Assert.Equal("Unrecognized policy", Error("civ addpolicy Babylon Nonsense"));
        Ok("civ removepolicy Babylon Tradition");
        Assert.False(babylon.Policies.IsAdopted("Tradition"));
        Assert.Contains("does not have", Rejected("civ removepolicy Babylon Tradition", CommandErrors.NotAllowed), StringComparison.Ordinal);

        var rome = game.GetCivilization("Rome");
        Ok("civ revealmap Rome");
        Assert.All(game.TileMap.Values, it => Assert.True(it.IsExplored(rome)));
        Ok("civ revealmap", new ConsoleSelection(Civ: "Greece"));

        Assert.Equal("Invalid countable", Rejected("civ checkcountable nonsense", CommandErrors.NotAllowed));
        Assert.Equal(game.Turns.ToString(CultureInfo.InvariantCulture), Rejected("civ checkcountable turns", CommandErrors.NotAllowed));
    }

    // --- unit

    [Fact]
    public void UnitCommands()
    {
        var tile = FreeLandTileNextToCity();
        var at = new ConsoleSelection(tile.Position);
        Assert.Equal("Select tile with units", Error("unit sethealth 50", at));
        Ok("unit add Babylon Warrior", at);
        var unit = tile.GetUnits().Single();
        Assert.Equal("Warrior", unit.Name);
        Assert.Equal("true", Rejected("unit checkfilter Land", CommandErrors.NotAllowed, at));
        Assert.Equal("Unknown civ: atlantis", Error("unit add Atlantis Warrior", at));
        Assert.Contains("is not a valid BaseUnit", Error("unit add Babylon Nonsense", at), StringComparison.Ordinal);

        var sel = new ConsoleSelection(tile.Position, unit.Id);
        Ok("unit sethealth 40", sel);
        Assert.Equal(40, unit.Health);
        Ok("unit sethealth", sel);
        Assert.Equal(100, unit.Health);
        Assert.Equal("Number out of range", Error("unit sethealth 101", sel));
        Ok("unit setmovement 1.5", sel);
        Assert.Equal(1.5f, unit.CurrentMovement);
        Ok("unit setmovement", sel);
        Assert.Equal(unit.GetMaxMovement(), unit.CurrentMovement);
        Assert.Equal("Number out of range", Error("unit setmovement -1", sel));
        Ok("unit setxp 30", sel);
        Assert.Equal(30, unit.Promotions.XP);
        Assert.Equal("No XP provided", Error("unit setxp", sel));
        Assert.Equal("Number out of range", Error("unit setxp -3", sel));

        const string promotion = "Shock I";
        Ok($"unit addpromotion \"{promotion}\"", sel);
        Assert.Contains(promotion, unit.Promotions.Promotions);
        Ok($"unit removepromotion \"{promotion}\"", sel);
        Assert.DoesNotContain(promotion, unit.Promotions.Promotions);
        Assert.Equal("Promotion not found on unit", Error($"unit removepromotion \"{promotion}\"", sel));

        Ok("unit remove", sel);
        Assert.Empty(tile.GetUnits());
        Ok("unit remove all");
        Assert.Empty(game.Civilizations.SelectMany(it => it.Units.GetCivUnits()));
    }

    // --- city

    [Fact]
    public void CityCommands()
    {
        Ok("city setpop 4", onCity);
        Assert.Equal(4, city.Population.Population);
        Assert.Equal("Population must be at least 1", Error("city setpop 0", onCity));
        Assert.Equal("Select tile", Error("city setpop 3"));
        Assert.Equal("Select tile belonging to city", Error("city setpop 3", new ConsoleSelection(game.TileMap.Values.First(it => it.GetCity() == null).Position)));
        Ok("city setname \"New Ur\"", onCity);
        Assert.Equal("New Ur", city.Name);
        Assert.Equal("true", Rejected("city checkfilter All", CommandErrors.NotAllowed, onCity));

        int max = city.GetMaxHealth();
        Ok("city sethealth 50", onCity);
        Assert.Equal(50, city.Health);
        Ok("city sethealth", onCity);
        Assert.Equal(max, city.Health);
        Assert.Equal("Number out of range", Error($"city sethealth {max + 1}", onCity));

        Ok("city addbuilding Monument", onCity);
        Assert.True(city.CityConstructions.IsBuilt("Monument"));
        Ok("city removebuilding Monument", onCity);
        Assert.False(city.CityConstructions.IsBuilt("Monument"));
        Assert.Equal("Unknown building", Error("city addbuilding Nonsense", onCity));
        Ok("city setpuppet true", onCity);
        Assert.True(city.IsPuppet);
        Ok("city setpuppet false", onCity);
        Assert.False(city.IsPuppet);

        var neighbor = FreeLandTileNextToCity();
        var greece = game.GetCivilization("Greece");
        Assert.Equal("Tile already contains a city center", Error("city add Greece", onCity));
        Assert.Equal("Can only add cities to major civs or city states", Error("city add Barbarians", new ConsoleSelection(neighbor.Position)));

        Ok("city removetile", new ConsoleSelection(neighbor.Position));
        Assert.Null(neighbor.GetCity());
        Ok($"city addtile \"{city.Name}\"", new ConsoleSelection(neighbor.Position));
        Assert.Same(city, neighbor.GetCity());
        Ok($"city addtile \"{city.Name}\" 1", new ConsoleSelection(neighbor.Position));
        Assert.Equal("Cannot transfer city center", Error($"city addtile \"{city.Name}\"", onCity));
        Assert.Equal("Tile is not adjacent to any tile already owned by the city", Error($"city addtile \"{greece.Cities[0].Name}\"", onCity));
        Assert.Equal("'x' is not a known religion", Error("city religion x 5", onCity));

        var far = game.TileMap.Values.First(it => it.IsLand && it.GetCity() == null && it.AerialDistanceTo(city.GetCenterTile()) > 6 && it.GetFirstUnit() == null
            && it.AerialDistanceTo(greece.Cities[0].GetCenterTile()) > 6);
        Ok("city add Rome", new ConsoleSelection(far.Position));
        Assert.True(far.IsCityCenter());
        Ok("city remove", new ConsoleSelection(far.Position));
        Assert.False(far.IsCityCenter());
    }

    // --- tile

    [Fact]
    public void TileCommands()
    {
        var tile = FreeLandTileNextToCity();
        var at = new ConsoleSelection(tile.Position);

        Ok("tile setimprovement Farm", at);
        Assert.Equal("Farm", tile.Improvement);
        Ok("tile setpillaged true", at);
        Assert.True(tile.ImprovementIsPillaged);
        Ok("tile setpillaged false", at);
        Assert.False(tile.ImprovementIsPillaged);
        Ok("tile removeimprovement", at);
        Assert.Null(tile.Improvement);
        Ok("tile setimprovement Farm Babylon", at);
        Assert.Contains("is not a valid", Error("tile setimprovement Nonsense", at), StringComparison.Ordinal);
        Ok("tile removeroad", at);

        Ok("tile addfeature Forest", at);
        Assert.Contains("Forest", tile.TerrainFeatures);
        Ok("tile removefeature Forest", at);
        Assert.DoesNotContain("Forest", tile.TerrainFeatures);
        Assert.StartsWith("Rivers cannot be removed", Rejected("tile removefeature River", CommandErrors.NotAllowed, at), StringComparison.Ordinal);
        Ok("tile addfeature River", at);

        string baseTerrain = tile.BaseTerrain;
        string other = baseTerrain == "Plains" ? "Grassland" : "Plains";
        Ok($"tile setterrain {other}", at);
        Assert.Equal(other, tile.BaseTerrain);
        Assert.Equal("Changing terrain type is not allowed", Error("tile setterrain Ocean", at));

        Ok("tile setresource Iron 3", at);
        Assert.Equal("Iron", tile.Resource);
        Assert.Equal(3, tile.ResourceAmount);
        Assert.Equal("Can't set resource amount for Luxury resource", Error("tile setresource Gems 3", at));
        Assert.Equal("Invalid resource amount", Error("tile setresource Iron 0", at));
        Ok("tile setresource Wheat", at);
        Ok("tile removeresource", at);
        Assert.Null(tile.Resource);

        Ok("tile setowner Babylon", at);
        Assert.Same(city, tile.GetCity());
        Ok("tile setowner", at);
        Assert.Null(tile.GetCity());
        Ok($"tile setowner \"{city.Name}\"", at);
        Assert.Same(city, tile.GetCity());
        Assert.Equal("atlantis is neither a city nor a civilization", Error("tile setowner Atlantis", at));

        Ok("tile spawn-barbarian-encampment", new ConsoleSelection(game.TileMap.Values.First(it => it.IsLand && it.GetCity() == null && it.GetFirstUnit() == null && it.AerialDistanceTo(city.GetCenterTile()) > 5).Position));
        Assert.NotEmpty(game.Barbarians.Encampments);
    }

    [Fact]
    public void RiverCommands()
    {
        var tile = game.TileMap.Values.First(it => it.IsLand && Enum.GetValues<RiverGenerator.RiverDirections>().Any(d => RiverGenerator.GetNeighborTile(d, it) is { IsLand: true }));
        var direction = Enum.GetValues<RiverGenerator.RiverDirections>().First(d => RiverGenerator.GetNeighborTile(d, tile) is { IsLand: true });
        var neighbor = RiverGenerator.GetNeighborTile(direction, tile)!;
        var at = new ConsoleSelection(tile.Position);
        Ok($"tile addriver {direction}", at);
        Assert.True(tile.IsConnectedByRiver(neighbor));
        Ok($"tile removeriver {direction}", at);
        Assert.False(tile.IsConnectedByRiver(neighbor));
        Assert.Contains("is not a valid", Error("tile addriver Sideways", at), StringComparison.Ordinal);
    }

    [Fact]
    public void NaturalWondersAreSetThroughSetTerrain()
    {
        var wonder = ruleset.Terrains.Values.FirstOrDefault(it => it.Type == Unciv.Models.Ruleset.Tile.TerrainType.NaturalWonder);
        if (wonder == null) return;
        var tile = FreeLandTileNextToCity();
        Ok($"tile setterrain \"{wonder.Name}\"", new ConsoleSelection(tile.Position));
        Assert.Equal(wonder.Name, tile.NaturalWonder);
    }

    // --- diplomacy

    [Fact]
    public void DiplomacyCommands()
    {
        var greece = game.GetCivilization("Greece");
        Assert.Equal("not met", Rejected("diplomacy list Babylon Rome", CommandErrors.NotAllowed));
        Ok("diplomacy meet Babylon Rome");
        Assert.NotNull(babylon.GetDiplomacyManager("Rome"));
        Assert.StartsWith("Relation: ", Rejected("diplomacy list Babylon Rome", CommandErrors.NotAllowed), StringComparison.Ordinal);
        Ok("diplomacy declare-war Babylon Rome");
        Assert.Equal(DiplomaticStatus.War, babylon.GetDiplomacyManager("Rome")!.DiplomaticStatus);
        Ok("diplomacy make-peace Babylon Rome");
        Assert.Equal(DiplomaticStatus.Peace, babylon.GetDiplomacyManager("Rome")!.DiplomaticStatus);

        Ok("diplomacy setflag Babylon Rome DeclarationOfFriendship 5");
        Assert.True(babylon.GetDiplomacyManager("Rome")!.HasFlag(DiplomacyFlags.DeclarationOfFriendship));
        Ok("diplomacy removeflag Babylon Rome DeclarationOfFriendship");
        Assert.False(babylon.GetDiplomacyManager("Rome")!.HasFlag(DiplomacyFlags.DeclarationOfFriendship));
        Ok("diplomacy addmodifier Babylon Rome DeclaredWarOnUs 7.5");
        Assert.True(babylon.GetDiplomacyManager("Rome")!.HasModifier(DiplomaticModifiers.DeclaredWarOnUs));
        Ok("diplomacy removemodifier Babylon Rome DeclaredWarOnUs");
        Assert.False(babylon.GetDiplomacyManager("Rome")!.HasModifier(DiplomaticModifiers.DeclaredWarOnUs));
        Ok("diplomacy denounce Babylon Greece");
        Assert.True(babylon.GetDiplomacyManager(greece)!.HasFlag(DiplomacyFlags.Denunciation));

        Assert.Equal("first civ must be the city-state, second a major civ", Error("diplomacy setinfluence Babylon Rome 5"));
        Assert.Equal("command needs two different civs", Error("diplomacy meet Babylon Babylon"));
        Assert.Equal("Unknown civ: atlantis", Error("diplomacy meet Babylon Atlantis"));
        Assert.Contains("Format: diplomacy meet", Rejected("diplomacy meet Babylon", CommandErrors.NotAllowed), StringComparison.Ordinal);
    }

    // --- autocomplete

    [Fact]
    public void AutocompleteCompletesCommandsAndParameters()
    {
        var sub = ConsoleRunner.Autocomplete(game, "Babylon", "uni")!.Value;
        Assert.Equal("unit ", sub.Result.Text);
        Assert.Equal(3, sub.ReplaceLength);

        Assert.Equal("addstat ", ConsoleRunner.Autocomplete(game, "Babylon", "civ adds")!.Value.Result.Text);
        Assert.Equal("babylon ", ConsoleRunner.Autocomplete(game, "Babylon", "civ addpolicy bab")!.Value.Result.Text);
        Assert.Equal("gold ", ConsoleRunner.Autocomplete(game, "Babylon", "civ addstat go")!.Value.Result.Text);
        Assert.Null(ConsoleRunner.Autocomplete(game, "Babylon", "civ addstat zzz"));
        Assert.Null(ConsoleRunner.Autocomplete(game, "Babylon", "city setpop 4 5 6"));
        Assert.Null(ConsoleRunner.Autocomplete(game, "Babylon", "city remove "));

        // a bare command word gets an empty parameter added: the completion replaces nothing but starts with the second token
        var bare = ConsoleRunner.Autocomplete(game, "Babylon", "tile addriver")!.Value;
        Assert.Equal(8, bare.ReplaceLength); // the second token is replaced, the completion starts with it again
        Assert.StartsWith("addriver ", bare.Result.Text, StringComparison.Ordinal);

        var promotions = ConsoleRunner.Autocomplete(game, "Babylon", "unit addpromotion ", onCityWithUnit());
        Assert.NotNull(promotions);
        Assert.NotNull(ConsoleRunner.Autocomplete(game, "Babylon", "city addbuilding mon"));
    }

    [Fact]
    public void EveryParameterTypeListsItsOptions()
    {
        var console = new ConsoleContext(game, "Babylon", onCity);
        string[] types =
        [
            "civName", "unitName", "promotionName", "improvementName", "featureName", "terrainName", "resourceName", "stat", "religionName",
            "buildingName", "direction", "policyName", "techName", "cityName", "difficulty", "boolean", "diplomacyFlag", "diplomaticModifier",
            "newCivName",
        ];
        foreach (string type in types.Where(it => it != "religionName"))
            Assert.NotEmpty(ConsoleParameterType.Options(type, console));
        Assert.Empty(ConsoleParameterType.Options("religionName", console));
        Assert.Empty(ConsoleParameterType.Options("noSuchType", console));
        Assert.Contains("Babylon", ConsoleParameterType.Options("civName", console));
        Assert.Contains(city.Name, ConsoleParameterType.Options("cityName", console));
        Assert.Contains("Tradition", ConsoleParameterType.Options("policyName", console)); // branches and policies
        Assert.Contains("Forest", ConsoleParameterType.Options("featureName", console));
        Assert.DoesNotContain("Forest", ConsoleParameterType.Options("terrainName", console));
        Assert.Equal(["true", "false"], ConsoleParameterType.Options("boolean", console));
        Assert.Equal(["North", "NorthEast", "SouthEast", "South", "SouthWest", "NorthWest"], ConsoleParameterType.Options("direction", console));
        Assert.DoesNotContain("Babylon", ConsoleParameterType.Options("newCivName", console));
        Assert.Equal(ConsoleParameterType.Options("boolean", console).Count + 2, ConsoleParameterType.MultiOptions("boolean|stat|boolean", console).Count - ConsoleParameterType.Options("stat", console).Count);
    }

    private ConsoleSelection onCityWithUnit()
    {
        var tile = FreeLandTileNextToCity();
        Ok("unit add Babylon Warrior", new ConsoleSelection(tile.Position));
        return new ConsoleSelection(tile.Position);
    }

    // --- hostile input

    [Fact]
    public void HostileLinesAreRejectedCleanlyWithoutChangingState()
    {
        string[] lines =
        [
            "", " ", "\"", "\"\"", "\" \"", "civ \"addstat", "civ addstat gold", "civ addstat gold abc", "civ addstat gold 99999999999999999999",
            "civ addstat gold 1.5", "civ addstat \u00e9\u4e2d gold 1", "unit", "unit add", "unit add Babylon", "unit add Babylon Nonsense", "unit add Atlantis Warrior",
            "unit setmovement NaN", "unit setmovement Infinity", "unit setmovement -1", "unit sethealth 0", "unit sethealth 101", "unit setxp -5", "unit setxp x",
            "city setpop 0", "city setpop -3", "city setpop abc", "city setname", "city add Nonsense", "tile setresource Iron -4", "tile setresource Iron 0",
            "tile setowner Atlantis", "tile addriver nowhere", "tile setterrain", "tile setterrain Ocean", "tile addfeature Nonsense", "tile setpillaged maybe",
            "game setturn 99999999999999999999", "game setturn x", "game setdifficulty", "diplomacy", "diplomacy declarewar", "diplomacy declarewar Babylon Babylon",
            "diplomacy declarewar Atlantis Rome", "civ setplayertype Babylon robot", "civ checkcountable \"", "civ checkcountable [", new string('a', 900),
            "civ " + new string('a', 900), "civ addpolicy Babylon " + new string('\"', 500), "\0\0", "civ\taddstat\tgold\t1\n", "CIV ADDSTAT GOLD 1",
        ];
        foreach (string line in lines)
        {
            ulong before = Hash(game);
            int logged = session.Log.Count;
            var result = session.Submit("Babylon", Line(line, onCity));
            if (result.Accepted) continue; // a line that is valid on its own terms is allowed to change state, it was logged
            Assert.Equal(before, Hash(game));
            Assert.Equal(logged, session.Log.Count);
        }
    }

    // --- replay

    [Fact]
    public void ConsoleLinesReplayToTheSameStateAndAreRefusedWithoutGodMode()
    {
        var setup = Setup(godMode: true);
        var header = CommandLogHeader.Create(setup, 11L, 1000L);
        var live = GameStarter.StartNewGame(setup, 11L, 1000L);
        var liveSession = new GameSession(live);
        var text = new StringWriter(CultureInfo.InvariantCulture);
        var writer = new CommandLogWriter(text, header);

        var civ = live.GetCivilization("Babylon");
        var settler = civ.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
        var place = new ConsoleSelection(settler.CurrentTile.Position);
        string[] lines = ["civ addstat gold 100", "civ addtechnology Babylon Pottery", "unit add Babylon Warrior", "tile setresource Iron 4", "game setturn 9"];
        foreach (string line in lines)
        {
            var result = liveSession.Submit("Babylon", new RunConsoleCommand { Line = line, Tile = place.Tile });
            Assert.True(result.Accepted, line + ": " + result.Validation.Message);
        }
        writer.Sync(liveSession);
        writer.WriteCheckpoint(liveSession.LastSeq, live.Turns, ReplayVerifier.FullHash(live));

        var log = CommandLogReader.Read(text.ToString());
        Assert.Equal(lines.Length, log.Lines.Count(it => it is CommandLogLine.Command { Envelope.Type: "run-console-command" }));
        var replay = ReplayVerifier.Verify(log);
        Assert.True(replay.Matches, $"diverged at {replay.DivergedAtSeq}");
        Assert.Equal(ReplayVerifier.FullHash(live), replay.FinalHash);
        Assert.Equal(9, replay.Game.Turns);
    }
}
