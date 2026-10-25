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
using Unciv.Logic.Trade;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using MapUnitObject = Unciv.Logic.Map.MapUnits.MapUnit;
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
            "civ " + new string('a', 900), "civ addpolicy Babylon " + new string('\"', 500),
            "civ activatetrigger \"Gain [amount] [stat]\" 30", "civ activatetrigger \"Gain [amount] [stat]\" a b c d e", "civ activatetrigger \"Gain [20] [Gold]\" extra",
            "civ activatetrigger \"\"", "civ activatetrigger \"[\"", "civ activatetrigger Atlantis \"Gain [20] [Gold]\"", "civ add", "civ add Atlantis", "civ remove", "civ remove Atlantis",
            "civ remove Babylon", "game remove-spectator x", "tile find", "tile find \"", "\0\0", "civ\taddstat\tgold\t1\n", "CIV ADDSTAT GOLD 1",
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

    // --- T234: activatetrigger, civ add/remove, game remove-spectator, tile find

    [Fact]
    public void CivActivateTriggerPlansWithoutChangeAndAppliesAsALoggedCommand()
    {
        int gold = babylon.Gold;
        Ok("civ activatetrigger \"Gain [20] [Gold]\"", onCity);
        Assert.NotEqual(gold, babylon.Gold);
        int greece = game.GetCivilization("Greece").Gold;
        Ok("civ activatetrigger Greece \"Gain [amount] [stat]\" 30 Gold");
        Assert.Equal(greece + 30, game.GetCivilization("Greece").Gold);
        Assert.Contains("not found in UniqueTypes", Error("civ activatetrigger \"No such unique [x]\""), StringComparison.Ordinal);
        Assert.Equal("Parameter triggeredUnique missing", Error("civ activatetrigger Greece"));
        Assert.Contains("is not a Triggerable", Error("civ activatetrigger Unbuildable"), StringComparison.Ordinal);
    }

    [Fact]
    public void UnitActivateTriggerNeedsASelectedUnit()
    {
        Assert.Equal("Select tile", Error("unit activatetrigger \"[This Unit] heals [10] HP\""));
        var unit = babylon.Units.GetCivUnits().First();
        var at = new ConsoleSelection(unit.CurrentTile.Position, unit.Id);
        unit.Health = 50;
        Ok("unit activatetrigger \"[This Unit] heals [10] HP\"", at);
        Assert.Equal(60, unit.Health);
    }

    [Fact]
    public void TriggerTemplatesAreOfferedQuotedPerTarget()
    {
        var civ = ConsoleParameterType.GetOptions("triggeredCivUniqueTemplate", new ConsoleContext(game, "Babylon", new ConsoleSelection()));
        Assert.NotEmpty(civ);
        Assert.All(civ, it => Assert.Equal(CliInput.Method.Quoted, it.MethodUsed));
        Assert.Contains(civ, it => it.OriginalUnquoted() == "Free [unit] appears");
        var unit = ConsoleParameterType.GetOptions("triggeredUnitUniqueTemplate", new ConsoleContext(game, "Babylon", new ConsoleSelection()));
        Assert.NotEmpty(unit);
        Assert.Contains(unit, it => it.OriginalUnquoted() == "[unitTriggerTarget] heals [positiveAmount] HP");
    }

    [Fact]
    public void CivAddPlacesASettlerAndCivRemoveAnnihilates()
    {
        var spot = FreeLandTileNextToCity();
        var at = new ConsoleSelection(spot.Position);
        string nation = ruleset.Nations.Values.First(it => it.IsMajorCiv && game.Civilizations.All(c => c.Nation != it)).Name;
        Assert.Equal("Select tile", Error($"civ add \"{nation}\""));
        Ok($"civ add \"{nation}\"", at);
        var added = game.GetCivilization(nation);
        Assert.Equal(Hash(game), Hash(UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(game, forceZip: false)))); // live equals reloaded
        Assert.Single(added.Units.GetCivUnits());
        Assert.True(added.Units.GetCivUnits().Single().BaseUnit.IsCityFounder());
        Assert.Contains("is not a valid Nation", Error("civ add Babylon", at), StringComparison.Ordinal);

        Assert.Equal("Suicide is not yet supported", Error("civ remove Babylon"));
        Ok($"civ remove \"{nation}\"");
        Assert.DoesNotContain(game.Civilizations, it => it.CivId == nation);
        Assert.Null(game.GetCivilizationOrNull(nation));
        Assert.Null(spot.GetFirstUnit());
        Assert.All(game.Civilizations, it => Assert.False(it.Diplomacy.ContainsKey(nation)));

        string cityState = ruleset.Nations.Values.First(it => it.IsCityState).Name;
        Ok($"civ add \"{cityState}\"", at);
        Assert.True(game.GetCivilization(cityState).IsCityState);
        Ok($"civ remove \"{cityState}\"");
        Assert.Null(game.GetCivilizationOrNull(cityState));

        Ok("civ remove Greece");
        Assert.Null(game.GetCivilizationOrNull("Greece"));
        Assert.DoesNotContain(game.TileMap.TileList, it => it.GetOwner()?.CivId == "Greece");
    }

    [Fact]
    public void GameRemoveSpectatorUndoesAddSpectator()
    {
        Assert.Equal("No Spectator in this game", Error("game remove-spectator"));
        Ok("game add-spectator");
        Ok("game remove-spectator");
        Assert.DoesNotContain(game.Civilizations, it => it.Nation.IsSpectator);
        Assert.Null(game.GetCivilizationOrNull("Spectator"));
        Assert.All(game.Civilizations, it => Assert.False(it.Diplomacy.ContainsKey("Spectator")));
        Ok("game add-spectator");
    }

    [Fact]
    public void TileFindReportsMatchingPositionsAsAHint()
    {
        var all = ConsoleRunner.FindTiles(game, "Land");
        Assert.Equal(game.TileMap.TileList.Where(it => it.IsLand).Select(it => it.Position), all);
        Assert.Empty(ConsoleRunner.FindTiles(game, "land")); // case sensitive
        string message = Rejected("tile find Land", CommandErrors.NotAllowed);
        Assert.StartsWith($"tile find [land]: {all.Count} matches: (", message, StringComparison.Ordinal);
        Assert.Equal("None found", Rejected("tile find land", CommandErrors.NotAllowed));
    }

    [Fact]
    public void RemovingACivWhileASequentialTurnIsPendingKeepsTheTurnCycle()
    {
        var order = game.Civilizations.Select(it => it.CivId).ToList();
        Assert.True(order.IndexOf("Greece") > 0);
        Ok("civ add \"" + ruleset.Nations.Values.First(it => it.IsMajorCiv && game.Civilizations.All(c => c.Nation != it)).Name + "\"", new ConsoleSelection(FreeLandTileNextToCity().Position));
        string victim = game.Civilizations.First(it => it.CivId != "Babylon" && it.CivId != "Greece" && game.Civilizations.IndexOf(it) < game.Civilizations.IndexOf(game.GetCivilization("Greece"))).CivId;
        game.EndPlayerTurn(1000000, game.GetCivilization(game.CurrentPlayer));
        var phase = game.TurnPhase;
        var next = ((TurnPhase.AwaitingInput)phase).Civ.CivId;
        var human = game.Civilizations.First(it => it.IsHuman() && it.CivId != victim).CivId;
        var result = session.Submit(human, Line("civ remove " + victim));
        Assert.True(result.Accepted, result.Validation.Message);
        var after = Assert.IsType<TurnPhase.AwaitingInput>(game.TurnPhase);
        Assert.Equal(next, after.Civ.CivId);
    }

    [Fact]
    public void RemovingTheLastCivWhileItIsPendingWrapsTheRound()
    {
        var last = game.Civilizations[^1];
        game.EndPlayerTurn(1000000, game.GetCivilization(game.CurrentPlayer));
        // run the cycle until the last civ is the pending one
        int turns = game.Turns;
        var pendingPhase = game.TurnPhase;
        while (!ReferenceEquals(PendingCiv(pendingPhase), last))
        {
            if (pendingPhase is TurnPhase.AutoPlayers) pendingPhase = game.RunAutoPlayers();
            else
            {
                var civ = ((TurnPhase.AwaitingInput)pendingPhase).Civ;
                game.BeginPlayerTurn(1000000);
                game.EndPlayerTurn(1000000, civ);
                pendingPhase = game.TurnPhase;
            }
        }
        string actor = game.Civilizations.First(it => it.IsHuman() && it != last).CivId;
        var result = session.Submit(actor, Line("civ remove " + last.CivId));
        Assert.True(result.Accepted, result.Validation.Message);
        Assert.Equal(turns + 1, game.Turns);
        var after = game.TurnPhase;
        Assert.Same(game.Civilizations[0], PendingCiv(after));
    }

    private static Civilization PendingCiv(TurnPhase phase) => phase switch
    {
        TurnPhase.AutoPlayers a => a.NextCiv,
        TurnPhase.AwaitingInput w => w.Civ,
        _ => throw new InvalidOperationException(phase.ToString()),
    };

    [Fact]
    public void RemovingACivMidRoundOfASimultaneousGameKeepsTheResolution()
    {
        game.StartSimultaneousTurns();
        game.EndHumanTurn(babylon);
        var greece = game.GetCivilization("Greece");
        game.EndHumanTurn(greece);
        Assert.Contains("Babylon", game.TurnState!.Ended);
        game.BeginResolving();
        var first = Assert.IsType<ResolveStep.ProcessEndTurn>(game.NextResolveStep());
        game.ProcessEndTurn(game.GetCivilization(first.CivId), 2000L);
        string victim = game.Civilizations.First(it => it.CivId != "Babylon" && it.CivId != "Greece" && game.Civilizations.IndexOf(it) < game.TurnState.Next).CivId;
        var expected = game.NextResolveStep();
        int turns = game.Turns;
        var human = game.Civilizations.First(it => it.IsHuman() && it.CivId != victim).CivId;
        var result = session.Submit(human, Line("civ remove " + victim));
        Assert.True(result.Accepted, result.Validation.Message);
        Assert.Equal(expected, game.NextResolveStep());
        while (game.NextResolveStep() is { } step)
        {
            switch (step)
            {
                case ResolveStep.ProcessEndTurn s: game.ProcessEndTurn(game.GetCivilization(s.CivId), 2000L); break;
                case ResolveStep.RunAiTurn s: game.RunAiTurn(game.GetCivilization(s.CivId)); break;
                case ResolveStep.StartNextTurn: game.StartNextTurn(); break;
                case ResolveStep.BeginTurn s: game.BeginTurn(game.GetCivilization(s.CivId), 2000L); break;
                default: throw new InvalidOperationException(step.ToString());
            }
        }
        Assert.Equal(turns + 1, game.Turns);
        Assert.True(game.IsTurnStarted(babylon) && game.IsTurnStarted(greece));
    }

    private void ResolveRound()
    {
        while (game.NextResolveStep() != null) ResolveOne();
    }

    // Resolves until the only step left is the given one, removes its civ by console as the other human, then plays on for several rounds.
    private void RemoveHumanAtLastStepAndPlayOn(bool lastBeginTurn)
    {
        game.StartSimultaneousTurns();
        var greece = game.GetCivilization("Greece");
        game.EndHumanTurn(babylon);
        game.EndHumanTurn(greece);
        game.BeginResolving();
        string victim;
        string actor;
        if (lastBeginTurn)
        {
            while (game.NextResolveStep() is not ResolveStep.BeginTurn) ResolveOne();
            var first = (ResolveStep.BeginTurn)game.NextResolveStep()!;
            ResolveOne();
            var last = Assert.IsType<ResolveStep.BeginTurn>(game.NextResolveStep());
            victim = last.CivId;
            actor = first.CivId;
        }
        else
        {
            var first = Assert.IsType<ResolveStep.ProcessEndTurn>(game.NextResolveStep());
            ResolveOne();
            var last = Assert.IsType<ResolveStep.ProcessEndTurn>(game.NextResolveStep());
            victim = last.CivId;
            actor = first.CivId;
        }
        Assert.NotEqual(victim, actor);
        var result = session.Submit(actor, Line("civ remove " + victim));
        Assert.True(result.Accepted, result.Validation.Message);
        if (!lastBeginTurn) ResolveRound(); // the AI round, next turn and begin of the remaining human
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState!.Phase);
        Assert.Null(game.NextResolveStep());
        Assert.True(game.IsActive(game.GetCivilization(actor)));
        for (int round = 0; round < 3; round++)
        {
            int turns = game.Turns;
            game.EndHumanTurn(game.GetCivilization(actor));
            Assert.True(game.AllHumansEnded());
            game.BeginResolving();
            ResolveRound();
            Assert.Equal(turns + 1, game.Turns);
            Assert.Equal(SimultaneousPhase.Playing, game.TurnState.Phase);
            Assert.Empty(game.TurnState.Ended);
        }
        ulong live = Hash(game);
        var loaded = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(game, forceZip: false));
        Assert.Equal(live, Hash(loaded));
        Assert.Equal(SimultaneousPhase.Playing, loaded.TurnState!.Phase);
    }

    private void ResolveOne()
    {
        switch (game.NextResolveStep())
        {
            case ResolveStep.ProcessEndTurn s: game.ProcessEndTurn(game.GetCivilization(s.CivId), 2000L); break;
            case ResolveStep.RunAiTurn s: game.RunAiTurn(game.GetCivilization(s.CivId)); break;
            case ResolveStep.StartNextTurn: game.StartNextTurn(); break;
            case ResolveStep.BeginTurn s: game.BeginTurn(game.GetCivilization(s.CivId), 2000L); break;
            default: throw new InvalidOperationException("no step");
        }
    }

    [Fact]
    public void RemovingTheHumanWithTheLastPendingBeginTurnReturnsTheRoundToPlaying() => RemoveHumanAtLastStepAndPlayOn(lastBeginTurn: true);

    [Fact]
    public void RemovingTheHumanWithTheLastPendingProcessEndTurnKeepsTheRoundGoing() => RemoveHumanAtLastStepAndPlayOn(lastBeginTurn: false);

    [Fact]
    public void RemovingAnotherHumanWhilePlayingKeepsTheEndedList()
    {
        game.StartSimultaneousTurns();
        game.EndHumanTurn(babylon);
        game.RemoveCivilization(game.GetCivilization("Greece"));
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState!.Phase);
        Assert.Contains("Babylon", game.TurnState.Ended);
        Assert.True(game.AllHumansEnded());
    }

    [Fact]
    public void RemovingAnEndedHumanDropsItFromTheRoundState()
    {
        game.StartSimultaneousTurns();
        game.EndHumanTurn(babylon);
        game.RemoveCivilization(babylon);
        Assert.DoesNotContain("Babylon", game.TurnState!.Ended);
        Assert.DoesNotContain("Babylon", game.TurnState.Started);
        Assert.False(game.IsTurnStarted(babylon));
        game.EndHumanTurn(game.GetCivilization("Greece"));
        Assert.True(game.AllHumansEnded());
    }

    // Everything that names Rome by id outside Rome: a removed civ is no longer resolvable, so each of these used to throw on a later turn (or never clear).
    private MapUnitObject PoisonWithRome(Civilization rome)
    {
        var greece = game.GetCivilization("Greece");
        greece.DiplomacyFunctions.MakeCivilizationsMeet(babylon);
        var request = new Trade();
        request.TheirOffers.Add(new TradeOffer("Embassy", TradeOfferType.Embassy));
        greece.TradeRequests.Add(new TradeRequest("Rome", request));
        var naming = new Trade();
        naming.OurOffers.Add(new TradeOffer("Rome", TradeOfferType.WarDeclaration));
        babylon.TradeRequests.Add(new TradeRequest("Greece", naming)); // a third civ's request that mentions Rome
        var naming2 = new Trade();
        naming2.TheirOffers.Add(new TradeOffer("Rome", TradeOfferType.Introduction));
        babylon.GetDiplomacyManager(greece)!.Trades.Add(naming2);
        greece.AddPopupAlert(new PopupAlert(AlertType.WarDeclaration, "Rome"));
        greece.AddPopupAlert(new PopupAlert(AlertType.DeclarationOfFriendship, "Rome"));
        greece.AddPopupAlert(new PopupAlert(AlertType.AttackedProtectedMinor, "Rome@Babylon"));
        greece.AddPopupAlert(new PopupAlert(AlertType.GoldenAge, ""));
        babylon.AddPopupAlert(new PopupAlert(AlertType.DemandToStopSpyingOnUs, "Rome"));
        city.FoundingCivObject = rome;
        var unit = greece.Units.GetCivUnits().First();
        unit.OriginalOwner = "Rome";
        var religion = new Religion("Romanism", game, rome, 1);
        game.Religions["Romanism"] = religion;
        city.Religion.AddPressure("Romanism", 100);
        city.Religion.ReligionThisIsTheHolyCityOf = "Romanism";
        unit.Religion = "Romanism";
        game.DiplomaticVictoryVotesCast["Greece"] = "Rome";
        game.DiplomaticVictoryVotesCast["Rome"] = "Greece";
        return unit;
    }

    private void PlaySequentialTurns(int turns)
    {
        int target = game.Turns + turns;
        game.EndPlayerTurn(1000000, game.GetCivilization(game.CurrentPlayer));
        var phase = game.TurnPhase;
        while (game.Turns < target)
        {
            if (phase is TurnPhase.AutoPlayers) phase = game.RunAutoPlayers();
            else
            {
                var civ = ((TurnPhase.AwaitingInput)phase).Civ;
                game.BeginPlayerTurn(1000000);
                game.EndPlayerTurn(1000000, civ);
                phase = game.TurnPhase;
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingACivThatNamesItselfInOtherCivsStateLeavesTheGamePlayable(bool simultaneous)
    {
        var rome = game.GetCivilization("Rome");
        var unit = PoisonWithRome(rome);
        var greece = game.GetCivilization("Greece");
        if (simultaneous) game.StartSimultaneousTurns();
        Ok("civ remove Rome");
        Assert.Empty(greece.TradeRequests);
        Assert.Empty(babylon.TradeRequests);
        Assert.Empty(babylon.GetDiplomacyManager(greece)!.Trades);
        Assert.DoesNotContain(greece.PopupAlerts, it => it.Value.Contains("Rome", StringComparison.Ordinal));
        Assert.Contains(greece.PopupAlerts, it => it.Type == AlertType.GoldenAge);
        Assert.DoesNotContain(babylon.PopupAlerts, it => it.Type == AlertType.DemandToStopSpyingOnUs);
        Assert.Null(city.FoundingCivObject);
        Assert.False(city.IsOriginalCapital);
        Assert.Null(unit.OriginalOwner);
        Assert.Null(unit.Religion);
        Assert.DoesNotContain("Romanism", game.Religions.Keys);
        Assert.Equal(0, city.Religion.GetFollowersOf("Romanism"));
        Assert.Null(city.Religion.ReligionThisIsTheHolyCityOf);
        Assert.Empty(game.DiplomaticVictoryVotesCast);
        for (int round = 0; round < 2; round++)
        {
            int turns = game.Turns;
            if (simultaneous)
            {
                game.EndHumanTurn(babylon);
                game.EndHumanTurn(greece);
                game.BeginResolving();
                ResolveRound();
            }
            else PlaySequentialTurns(1);
            Assert.Equal(turns + 1, game.Turns);
        }
        var loaded = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(game, forceZip: false));
        Assert.Equal(Hash(game), Hash(loaded));
    }

    // TryEndWarWithMajorQuests resolves the attackers of a city-state's war-with-major pseudo-quest by id at every city-state turn start.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingTheAttackerOfACityStateWarQuestLeavesTheGamePlayable(bool simultaneous)
    {
        var at = new ConsoleSelection(FreeLandTileNextToCity().Position);
        string cityStateName = ruleset.Nations.Values.First(it => it.IsCityState).Name;
        Ok($"civ add \"{cityStateName}\"", at);
        var cityState = game.GetCivilization(cityStateName);
        Assert.Equal(Hash(game), Hash(UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(game, forceZip: false)))); // civ add leaves what a load would have made
        var rome = game.GetCivilization("Rome");
        var greece = game.GetCivilization("Greece");
        foreach (var civ in new[] { rome, babylon, greece }) cityState.DiplomacyFunctions.MakeCivilizationsMeet(civ);
        cityState.QuestManager.WasAttackedBy(rome);
        cityState.QuestManager.MilitaryUnitKilledBy(babylon, rome); // also fills the kill counts
        Assert.True(cityState.QuestManager.IsWarWithMajorActive(rome));
        Assert.Equal(1, cityState.QuestManager.UnitsKilledSoFar(rome, babylon));
        if (simultaneous) game.StartSimultaneousTurns();
        Ok("civ remove Rome");
        Assert.False(cityState.QuestManager.IsWarWithMajorActive(rome));
        Assert.Equal(0, cityState.QuestManager.UnitsKilledSoFar(rome, babylon));
        for (int round = 0; round < 2; round++)
        {
            int turns = game.Turns;
            if (simultaneous)
            {
                game.EndHumanTurn(babylon);
                game.EndHumanTurn(greece);
                game.BeginResolving();
                ResolveRound();
            }
            else PlaySequentialTurns(1);
            Assert.Equal(turns + 1, game.Turns);
        }
        var loaded = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(game, forceZip: false));
        Assert.Equal(Hash(game), Hash(loaded));
    }

    [Fact]
    public void NewConsoleCommandsReplayToTheSameState()
    {
        var setup = Setup(godMode: true);
        var header = CommandLogHeader.Create(setup, 11L, 1000L);
        var live = GameStarter.StartNewGame(setup, 11L, 1000L);
        var liveSession = new GameSession(live);
        var text = new StringWriter(CultureInfo.InvariantCulture);
        var writer = new CommandLogWriter(text, header);
        var start = live.GetCivilization("Babylon").Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder()).CurrentTile;
        var spot = start.Neighbors.First(it => it.IsLand && it.GetFirstUnit() == null).Position;
        var greeceTile = live.GetCivilization("Greece").Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder()).CurrentTile.Position;
        Assert.True(liveSession.Submit("Babylon", new RunConsoleCommand { Line = "city add Greece", Tile = greeceTile }).Accepted);
        string nation = ruleset.Nations.Values.First(it => it.IsMajorCiv && live.Civilizations.All(c => c.Nation != it)).Name;
        string[] lines =
        [
            "game add-spectator", $"civ add \"{nation}\"", "civ activatetrigger \"Gain [20] [Gold]\"", "civ activatetrigger Greece \"Gain [amount] [stat]\" 30 Gold",
            $"civ remove \"{nation}\"", "civ remove Greece", "game remove-spectator",
        ];
        foreach (string line in lines)
        {
            var result = liveSession.Submit("Babylon", new RunConsoleCommand { Line = line, Tile = spot });
            Assert.True(result.Accepted, line + ": " + result.Validation.Message);
        }
        Assert.False(liveSession.Submit("Babylon", new RunConsoleCommand { Line = "tile find Land" }).Accepted);
        writer.Sync(liveSession);
        writer.WriteCheckpoint(liveSession.LastSeq, live.Turns, ReplayVerifier.FullHash(live));
        var log = CommandLogReader.Read(text.ToString());
        Assert.Equal(lines.Length + 1, log.Lines.Count(it => it is CommandLogLine.Command { Envelope.Type: "run-console-command" }));
        var replay = ReplayVerifier.Verify(log);
        Assert.True(replay.Matches, $"diverged at {replay.DivergedAtSeq}");
        Assert.Equal(ReplayVerifier.FullHash(live), replay.FinalHash);
        Assert.Null(replay.Game.GetCivilizationOrNull("Greece"));
    }
}
