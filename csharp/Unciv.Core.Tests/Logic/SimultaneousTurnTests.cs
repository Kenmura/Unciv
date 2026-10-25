using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Logic;

// T200: TurnState and the simultaneous resolution sequence. The one-human == sequential hash equivalence is in EndToEndDeterminismTests.
[Collection("RulesetCache")]
public sealed class SimultaneousTurnTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public SimultaneousTurnTests()
    {
        RulesetCache.Clear();
        var jsons = Path.Combine(RepoRoot(), "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "android", "assets", "jsons"))) return dir.FullName;
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private GameInfo NewGame(params (string Nation, PlayerType Type)[] players)
    {
        if (players.Length == 0) players = [("Babylon", PlayerType.Human), ("Greece", PlayerType.Human), ("Rome", PlayerType.AI)];
        return GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = players.Select(it => new Player(it.Nation, it.Type)).ToList(),
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            1L, 1000L);
    }

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    private static void EndAll(GameInfo game)
    {
        foreach (var civ in game.Civilizations.Where(game.IsActive).ToList()) game.EndHumanTurn(civ);
        game.BeginResolving();
    }

    private static void Perform(GameInfo game, ResolveStep step)
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

    private static List<ResolveStep> Resolve(GameInfo game)
    {
        var steps = new List<ResolveStep>();
        while (game.NextResolveStep() is { } step)
        {
            steps.Add(step);
            Perform(game, step);
        }
        return steps;
    }

    [Fact]
    public void SequentialGameHasNoTurnStateAndNeverWritesIt()
    {
        var game = NewGame();
        Assert.Null(game.TurnState);
        Assert.Equal(TurnMode.Sequential, game.TurnMode);
        Assert.Null(game.NextResolveStep());
        game.NextTurn(2000L);
        Assert.Null(game.TurnState);
        Assert.DoesNotContain("turnState", UncivFiles.GameInfoToString(game, forceZip: false), StringComparison.Ordinal);
    }

    [Fact]
    public void FirstTurnStartsAllHumansAndOnlyThem()
    {
        var game = NewGame();
        game.StartSimultaneousTurns();
        Assert.Equal(["Babylon", "Greece"], game.TurnState!.Started);
        var (babylon, greece, rome) = (game.GetCivilization("Babylon"), game.GetCivilization("Greece"), game.GetCivilization("Rome"));
        Assert.True(game.IsActive(babylon) && game.IsActive(greece));
        Assert.False(game.IsActive(rome));
        Assert.Equal(new TurnPhase.AwaitingInput(babylon), game.TurnPhase);
        Assert.Throws<InvalidOperationException>(game.StartSimultaneousTurns);
    }

    [Fact]
    public void RoundResolvesInTheDocumentedOrderAndReturnsToPlaying()
    {
        var game = NewGame();
        game.StartSimultaneousTurns();
        var turns = game.Turns;
        var greece = game.GetCivilization("Greece");
        game.EndHumanTurn(game.GetCivilization("Babylon"));
        Assert.False(game.AllHumansEnded());
        Assert.Throws<InvalidOperationException>(game.BeginResolving);
        Assert.Throws<InvalidOperationException>(() => game.EndHumanTurn(game.GetCivilization("Babylon")));
        game.EndHumanTurn(greece);
        Assert.True(game.AllHumansEnded());
        game.UndoEndHumanTurn(greece);
        Assert.False(game.AllHumansEnded());
        game.EndHumanTurn(greece);
        game.BeginResolving();
        Assert.False(game.IsActive(greece));
        Assert.Throws<InvalidOperationException>(() => game.EndHumanTurn(greece));

        var steps = Resolve(game);

        Assert.Equal(
            [
                new ResolveStep.ProcessEndTurn("Babylon"), new ResolveStep.ProcessEndTurn("Greece"),
                .. game.Civilizations.Where(it => !it.IsHuman()).Select(it => (ResolveStep)new ResolveStep.RunAiTurn(it.CivId)),
                new ResolveStep.StartNextTurn(),
                new ResolveStep.BeginTurn("Babylon"), new ResolveStep.BeginTurn("Greece"),
            ],
            steps);
        Assert.Equal(turns + 1, game.Turns);
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState!.Phase);
        Assert.Empty(game.TurnState.Ended);
        Assert.Equal(["Babylon", "Greece"], game.TurnState.Started);
        Assert.True(game.IsActive(greece));
        Assert.Null(game.NextResolveStep());
    }

    [Fact]
    public void StepsOutOfSequenceAreRejected()
    {
        var game = NewGame();
        game.StartSimultaneousTurns();
        EndAll(game);
        var (babylon, greece) = (game.GetCivilization("Babylon"), game.GetCivilization("Greece"));
        Assert.Throws<InvalidOperationException>(() => game.ProcessEndTurn(greece, 2000L));
        Assert.Throws<InvalidOperationException>(game.StartNextTurn);
        Assert.Throws<InvalidOperationException>(() => game.BeginTurn(babylon, 2000L));
        game.ProcessEndTurn(babylon, 2000L);
        Assert.Throws<InvalidOperationException>(() => game.ProcessEndTurn(babylon, 2000L));
    }

    [Fact]
    public void NextResolveStepIsPureAndAHalfResolvedRoundResumesFromASave()
    {
        var game = NewGame();
        game.StartSimultaneousTurns();
        EndAll(game);
        for (var i = 0; i < 3; i++) Perform(game, game.NextResolveStep()!);
        var before = Hash(game);
        var step = game.NextResolveStep();
        Assert.Equal(step, game.NextResolveStep());
        Assert.Equal(before, Hash(game));

        var loaded = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(game, forceZip: false));
        Assert.Equal(before, Hash(loaded));
        Assert.Equal(step, loaded.NextResolveStep());
        Assert.Equal(TurnMode.Simultaneous, loaded.TurnMode);
        Assert.Equal(game.TurnState!.Started, loaded.TurnState!.Started);
        Assert.Equal(game.IsTurnStarted(game.GetCivilization("Greece")), loaded.IsTurnStarted(loaded.GetCivilization("Greece")));
        Assert.Equal(Resolve(game), Resolve(loaded));
        Assert.Equal(game.Turns, loaded.Turns);
        Assert.True(loaded.IsActive(loaded.GetCivilization("Babylon")));
        // Not compared: the hash after the round. Civ.Stats (Happiness...) is a transient cache that a load does not restore, and StartNextTurn
        // records it into the ranking history. A host saves at checkpoints (Playing) or after StartNextTurn, which the next test covers.
    }

    [Fact]
    public void ARoundResumedAfterStartNextTurnGivesTheSameStateFromASave()
    {
        var game = NewGame();
        game.StartSimultaneousTurns();
        EndAll(game);
        while (game.NextResolveStep() is not ResolveStep.BeginTurn) Perform(game, game.NextResolveStep()!);
        Perform(game, game.NextResolveStep()!);
        var loaded = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(game, forceZip: false));
        Assert.Equal(Hash(game), Hash(loaded));
        Assert.Equal(new ResolveStep.BeginTurn("Greece"), loaded.NextResolveStep());
        Assert.Equal(Resolve(game), Resolve(loaded));
        Assert.Equal(Hash(game), Hash(loaded));
    }

    [Fact]
    public void TurnStateRoundTripsAndUnknownMembersAreIgnored()
    {
        var game = NewGame();
        game.StartSimultaneousTurns();
        game.EndHumanTurn(game.GetCivilization("Greece"));
        var text = UncivFiles.GameInfoToString(game, forceZip: false);
        Assert.Contains("\"turnState\"", text, StringComparison.Ordinal);

        var loaded = UncivFiles.GameInfoFromString(text);
        Assert.Equal(["Greece"], loaded.TurnState!.Ended);
        Assert.Equal(text, UncivFiles.GameInfoToString(loaded, forceZip: false));

        // a reader that does not know a member (older readers and "turnState") skips it
        var withUnknown = text.Replace("\"turnState\"", "\"futureMember\":{\"x\":1},\"turnState\"", StringComparison.Ordinal);
        Assert.Equal(["Greece"], UncivFiles.GameInfoFromString(withUnknown).TurnState!.Ended);
        var withoutState = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(NewGame(), forceZip: false));
        Assert.Null(withoutState.TurnState);
    }

    [Fact]
    public void CloneCopiesTheTurnState()
    {
        var game = NewGame();
        game.StartSimultaneousTurns();
        game.EndHumanTurn(game.GetCivilization("Greece"));
        var clone = game.Clone();
        Assert.NotSame(game.TurnState, clone.TurnState);
        Assert.Equal(game.TurnState!.Ended, clone.TurnState!.Ended);
        game.UndoEndHumanTurn(game.GetCivilization("Greece"));
        Assert.Equal(["Greece"], clone.TurnState.Ended);
    }

    [Fact]
    public void DefeatedHumansAndSpectatorsAreAutoCivs()
    {
        var game = NewGame();
        game.StartSimultaneousTurns();
        var greece = game.GetCivilization("Greece");
        greece.HasEverOwnedOriginalCapital = true;
        greece.Cities = [];
        Assert.True(greece.IsDefeated());
        Assert.False(game.IsActive(greece));
        EndAll(game); // only Babylon was waited for
        var steps = Resolve(game);
        Assert.Contains(new ResolveStep.RunAiTurn("Greece"), steps);
        Assert.DoesNotContain(new ResolveStep.ProcessEndTurn("Greece"), steps);
        Assert.DoesNotContain(new ResolveStep.BeginTurn("Greece"), steps);
        Assert.False(game.IsTurnStarted(greece));
        Assert.Equal(["Babylon"], game.TurnState!.Started);
    }
}
