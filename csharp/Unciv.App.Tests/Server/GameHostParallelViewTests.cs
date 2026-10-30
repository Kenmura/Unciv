using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Empire;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Turns;
using Unciv.Server;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Server;

/// <summary>T297: the optional parallel view building must send exactly the frames of the sequential path, in the same order, and stay out of the way below two recipients.</summary>
[Collection("RulesetCache")]
public sealed class GameHostParallelViewTests : IDisposable
{
    private static readonly string[] Humans = ["Babylon", "Greece", "Egypt", "Rome"];
    private readonly RulesetClass ruleset;

    public GameHostParallelViewTests()
    {
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

    private GameInfo NewGame()
    {
        var players = Humans.Select(it => new Player(it, PlayerType.Human)).Append(new Player("Persia", PlayerType.AI)).ToList();
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters { BaseRuleset = ruleset.Name, NumberOfCityStates = 1, VictoryTypes = ruleset.Victories.Keys.ToList(), Players = players },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Small }),
            1L, 1000L);
        game.StartSimultaneousTurns();
        return game;
    }

    private sealed class RecordingTransport(ITransport inner, List<string> frames) : ITransport
    {
        public bool IsOpen => inner.IsOpen;
        public ValueTask SendAsync(string frame, CancellationToken cancellationToken = default)
        {
            lock (frames) frames.Add(frame);
            return inner.SendAsync(frame, cancellationToken);
        }
        public ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default) => inner.ReceiveAsync(cancellationToken);
        public ValueTask CloseAsync() => inner.CloseAsync();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    /// <summary>Plays a scripted multi-recipient game (client commands, server commands, turn resolution) and returns every frame each recipient was sent.</summary>
    private async Task<(Dictionary<string, List<string>> Frames, int ParallelRounds)> PlayAsync(HostLimits limits, string[] recipients, int rounds)
    {
        var game = NewGame();
        var clock = new ManualClock(1000L);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };
        await using var host = new GameHost(session, clock: clock, limits: limits);
        var frames = recipients.ToDictionary(it => it, _ => new List<string>());
        foreach (var civ in recipients)
        {
            var (_, hostSide) = InMemoryTransport.CreatePair();
            await host.AttachSessionAsync("s-" + civ, civ, new RecordingTransport(hostSide, frames[civ]));
        }

        var researched = 0;
        for (var round = 0; round < rounds; round++)
        {
            foreach (var civ in recipients)
            {
                var tech = ruleset.Technologies.Keys.Where(k => !game.GetCivilization(civ).Tech.IsResearched(k)).ElementAt(round);
                await host.HandleInboundAsync("s-" + civ, new Command
                {
                    ClientCmdId = $"c-{civ}-{round}",
                    CommandType = "set-research-queue",
                    Payload = JsonSerializer.SerializeToElement(new SetResearchQueue { Techs = [tech] })
                });
                researched++;
            }
            await host.EnqueueAsync(() =>
            {
                foreach (var civ in Humans.Where(it => !game.HasEndedTurn(game.GetCivilization(it))))
                    host.ApplyServerCommand(new ForceEndTurn { CivId = civ });
                clock.Advance(1000);
                host.Tick();
                return Task.CompletedTask;
            });
        }
        Assert.True(researched > 0);
        await host.EnqueueAsync(() => Task.CompletedTask);
        return (frames, host.ParallelViewRounds);
    }

    private static void AssertSameFrames(Dictionary<string, List<string>> expected, Dictionary<string, List<string>> actual)
    {
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var civ in expected.Keys)
        {
            Assert.True(expected[civ].Count > 4, $"{civ} got only {expected[civ].Count} frames");
            Assert.Equal(expected[civ].Count, actual[civ].Count);
            for (var i = 0; i < expected[civ].Count; i++) Assert.True(expected[civ][i] == actual[civ][i], $"{civ} frame {i} differs");
        }
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(4, 2)]
    [InlineData(2, 4)]
    public async Task ParallelFrames_EqualSequentialFrames(int recipientCount, int cap)
    {
        var recipients = Humans.Take(recipientCount).ToArray();
        var (sequential, seqRounds) = await PlayAsync(new HostLimits(), recipients, 3);
        Assert.Equal(0, seqRounds);
        var (parallel, parRounds) = await PlayAsync(new HostLimits { ParallelViewBuilding = true, MaxViewParallelism = cap }, recipients, 3);
        Assert.True(parRounds > 0);
        AssertSameFrames(sequential, parallel);
    }

    [Fact]
    public async Task ParallelFrames_RepeatedTwentyTimes_AlwaysEqual()
    {
        var recipients = Humans.ToArray();
        var (sequential, _) = await PlayAsync(new HostLimits(), recipients, 2);
        for (var run = 0; run < 20; run++)
        {
            var (parallel, _) = await PlayAsync(new HostLimits { ParallelViewBuilding = true }, recipients, 2);
            AssertSameFrames(sequential, parallel);
        }
    }

    [Fact]
    public async Task ClosedSession_AmongRecipients_IsSkippedLikeInTheSequentialPath()
    {
        async Task<(List<string> A, List<string> C, int Rounds)> Run(HostLimits limits)
        {
            var game = NewGame();
            var clock = new ManualClock(1000L);
            await using var host = new GameHost(new GameSession(game) { Clock = () => clock.NowMillis }, clock: clock, limits: limits);
            List<string> a = [], b = [], c = [];
            var (_, ha) = InMemoryTransport.CreatePair();
            var (_, hb) = InMemoryTransport.CreatePair();
            var (_, hc) = InMemoryTransport.CreatePair();
            await host.AttachSessionAsync("s-a", "Babylon", new RecordingTransport(ha, a));
            await host.AttachSessionAsync("s-b", "Greece", new RecordingTransport(hb, b));
            await host.AttachSessionAsync("s-c", "Egypt", new RecordingTransport(hc, c));
            await hb.CloseAsync();
            for (var i = 0; i < 3; i++)
                await host.EnqueueAsync(() =>
                {
                    host.ApplyServerCommand(new ForceEndTurn { CivId = Humans[i % 3] });
                    return Task.CompletedTask;
                });
            return (a, c, host.ParallelViewRounds);
        }

        var seq = await Run(new HostLimits());
        var par = await Run(new HostLimits { ParallelViewBuilding = true });
        Assert.True(par.Rounds > 0);
        Assert.Equal(seq.A, par.A);
        Assert.Equal(seq.C, par.C);
    }

    [Fact]
    public async Task SingleRecipient_NeverUsesTasks_EvenWhenOn()
    {
        var (frames, rounds) = await PlayAsync(new HostLimits { ParallelViewBuilding = true }, ["Babylon"], 2);
        Assert.Equal(0, rounds);
        Assert.NotEmpty(frames["Babylon"]);
    }

    [Fact]
    public async Task CapBelowTwo_TurnsTheOptionOff()
    {
        var (_, rounds) = await PlayAsync(new HostLimits { ParallelViewBuilding = true, MaxViewParallelism = 1 }, Humans, 1);
        Assert.Equal(0, rounds);
    }

    [Fact]
    public void Default_IsOff() => Assert.False(HostLimits.Default.ParallelViewBuilding);
}
