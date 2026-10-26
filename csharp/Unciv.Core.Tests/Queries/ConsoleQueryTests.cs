using System.Text.Json;
using Unciv.Core.Tests.Support;
using Unciv.DevConsole;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Queries;
using Unciv.Queries.DevConsole;
using Unciv.Utils;

namespace Unciv.Core.Tests.Queries;

// C046: the two queries behind the dev console popup. They must say what the logged run-console-command would do, without changing the game.
[Collection("RulesetCache")]
public class ConsoleQueryTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization player;
    private readonly QueryRegistry registry = new(UncivJson.DefaultOptions);

    public ConsoleQueryTests()
    {
        testGame.MakeHexagonalMap(4, "Grassland");
        player = testGame.AddCiv(testGame.Ruleset.Nations["Germany"], isPlayer: true);
        testGame.GameInfo.GameParameters.GodMode = true;
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
    }

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    private T Ask<T>(IQuery<T> query) where T : notnull
    {
        ulong before = Hash(testGame.GameInfo);
        var envelope = registry.ToEnvelope(player.CivName, query);
        var wire = JsonSerializer.Deserialize<QueryEnvelope>(JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions), UncivJson.DefaultOptions)!;
        var response = registry.Execute(wire, new QueryContext(testGame.GameInfo));
        Assert.True(response.Ok, $"{response.Code}: {response.Message}");
        Assert.Equal(before, Hash(testGame.GameInfo));
        return response.Result!.Value.Deserialize<T>(UncivJson.DefaultOptions)!;
    }

    private ConsolePlanResult Plan(string line, HexCoord? tile = null) => Ask(new ConsolePlanQuery(line, tile));

    private ConsoleAutocompleteResult Complete(string line) => Ask(new ConsoleAutocompleteQuery(line));

    [Fact]
    public void AGoodLinePlansAsOkWithoutRunningIt()
    {
        int gold = player.Gold;
        var plan = Plan("civ addstat gold 5");
        Assert.Equal("Ok", plan.Kind);
        Assert.Null(plan.Message);
        Assert.Equal(gold, player.Gold);
    }

    [Fact]
    public void UsageAndQueryAnswersAreHintsAndUserErrorsAreErrors()
    {
        Assert.Equal(new ConsolePlanResult("Hint", "Format: civ addstat <stat> <amount> [civ]"), Plan("civ addstat"));
        Assert.Equal(new ConsolePlanResult("Hint", "Available commands: city, civ, diplomacy, game, tile, unit"), Plan(""));
        Assert.Equal(new ConsolePlanResult("Error", "Unknown civ: atlantis"), Plan("civ addstat gold 5 Atlantis"));
        Assert.Equal("Error", Plan("nonsense").Kind);
    }

    [Fact]
    public void TheSelectionTravelsWithTheLine()
    {
        Assert.Equal(new ConsolePlanResult("Error", "Select tile"), Plan("tile checkfilter Land"));
        Assert.Equal(new ConsolePlanResult("Hint", "true"), Plan("tile checkfilter Land", new HexCoord(0, 0)));
        var civ = Ask(new ConsolePlanQuery("civ addstat gold 5", Civ: "Atlantis"));
        Assert.Equal("Ok", civ.Kind);
    }

    [Fact]
    public void AGameWithoutGodModeRefusesBothQueriesWithTheCommandsText()
    {
        testGame.GameInfo.GameParameters.GodMode = false;
        Assert.Equal(new ConsolePlanResult("Error", ConsoleRunner.GodModeRequired), Plan("civ addstat gold 5"));
        Assert.Equal(new ConsoleAutocompleteResult(0, null, ConsoleRunner.GodModeRequired, true), Complete("civ"));
    }

    [Fact]
    public void ALineOverTheLimitIsRefusedButTheLimitItselfIsFine()
    {
        Assert.Equal(new ConsolePlanResult("Error", ConsoleRunner.LineTooLong), Plan(new string('x', ConsoleRunner.MaxLineLength + 1)));
        Assert.Equal(new ConsoleAutocompleteResult(0, null, ConsoleRunner.LineTooLong, true), Complete(new string('x', ConsoleRunner.MaxLineLength + 1)));
        Assert.NotEqual(ConsoleRunner.LineTooLong, Plan(new string('x', ConsoleRunner.MaxLineLength)).Message);
        Assert.NotEqual(ConsoleRunner.LineTooLong, Complete(new string('x', ConsoleRunner.MaxLineLength)).Message);
    }

    [Fact]
    public void AutocompleteCompletesTheLastWordAndListsAmbiguousChoices()
    {
        Assert.Equal(new ConsoleAutocompleteResult(2, "ci", "Matching completions: city, civ", false), Complete("ci"));
        Assert.Equal(new ConsoleAutocompleteResult(2, "game ", null, false), Complete("ga"));
        Assert.Equal(new ConsoleAutocompleteResult(4, "addstat ", null, false), Complete("civ adds"));
        var ambiguous = Complete("civ add");
        Assert.Equal(3, ambiguous.ReplaceLength);
        Assert.Equal("add ", ambiguous.Text);
        Assert.StartsWith("Matching completions: ", ambiguous.Message, StringComparison.Ordinal);
        Assert.False(ambiguous.IsError);
        Assert.Equal(new ConsoleAutocompleteResult(0, null, null, false), Complete("zzz"));
    }

    [Fact]
    public void AutocompleteOfAnUnknownNameReportsTheUserErrorAsAnError()
    {
        Assert.Equal(new ConsoleAutocompleteResult(0, null, null, false), Complete("civ addstat gold 5 Atl"));
        Assert.Equal(new ConsoleAutocompleteResult(0, null, "Select tile", true), Complete("unit addpromotion "));
        var emptyLine = Complete("");
        Assert.False(emptyLine.IsError);
        Assert.Equal(0, emptyLine.ReplaceLength);
    }

    [Fact]
    public void BothQueriesAreRegisteredUnderTheirWireNames()
    {
        Assert.Contains("console.Plan", registry.RegisteredTypes);
        Assert.Contains("console.Autocomplete", registry.RegisteredTypes);
        Assert.Equal("console.Plan", new ConsolePlanQuery("x").Type);
        Assert.Equal("console.Autocomplete", new ConsoleAutocompleteQuery("x").Type);
    }
}
