using Unciv.Assets;
using Unciv.GodotClient.Settings;

namespace Unciv.Core.Tests.Assets;

/// <summary>C047: the AutoPlay button, its menu, the run counter and the options that feed them.</summary>
public sealed class AutoPlayModelTests
{
    [Fact]
    public void Defaults_MatchKotlin_ButtonHiddenTenTurnsFullAi()
    {
        var settings = new AutoPlaySettings();
        Assert.False(settings.ShowButton);
        Assert.False(settings.UntilEnd);
        Assert.Equal(10, settings.MaxTurns);
        Assert.True(settings.FullAutoPlayAI);
        Assert.Equal(settings, ClientSettings.Default.AutoPlay);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(37, 37)]
    [InlineData(200, 200)]
    [InlineData(201, 200)]
    [InlineData(int.MaxValue, 200)]
    [InlineData(int.MinValue, 1)]
    public void Sanitized_KeepsTheTurnCountInRange(int given, int expected) =>
        Assert.Equal(expected, (new AutoPlaySettings { MaxTurns = given }).Sanitized().MaxTurns);

    [Fact]
    public void Entries_ListStartThenEndTurn_WithTheLengthOfEach()
    {
        var entries = AutoPlayModel.Entries(new AutoPlaySettings { MaxTurns = 25 }, true);
        Assert.Equal(["AutoPlayStart", "AutoPlayEndTurn"], entries.Select(it => it.Id));
        Assert.Equal([AutoPlayKind.Start, AutoPlayKind.EndTurn], entries.Select(it => it.Kind));
        Assert.Equal(["Start AutoPlay", "AutoPlay End Turn"], entries.Select(it => it.Label));
        Assert.Equal(["25 turns", "1 turn"], entries.Select(it => it.Detail));
        Assert.All(entries, it =>
        {
            Assert.True(it.Enabled);
            Assert.Null(it.DisabledReason);
        });
    }

    [Fact]
    public void Entries_OnePlainTurnIsSingular_UntilEndSaysSo()
    {
        Assert.Equal("1 turn", AutoPlayModel.Entries(new AutoPlaySettings { MaxTurns = 1 }, true)[0].Detail);
        Assert.Equal("until the game ends", AutoPlayModel.Entries(new AutoPlaySettings { UntilEnd = true, MaxTurns = 5 }, true)[0].Detail);
    }

    [Fact]
    public void Entries_AreGreyedWithAReasonWhenStartingIsNotAllowed()
    {
        var entries = AutoPlayModel.Entries(new AutoPlaySettings(), false);
        Assert.All(entries, it =>
        {
            Assert.False(it.Enabled);
            Assert.Equal(AutoPlayModel.NotYourTurn, it.DisabledReason);
        });
    }

    [Theory]
    [InlineData(HudActionKind.EndTurn, false, true)]
    [InlineData(HudActionKind.NextUnit, false, true)]
    [InlineData(HudActionKind.Decision, false, true)]
    [InlineData(HudActionKind.UndoEndTurn, false, false)]
    [InlineData(HudActionKind.Resolving, false, false)]
    [InlineData(HudActionKind.EndTurn, true, false)]
    [InlineData(HudActionKind.Decision, true, false)]
    public void CanStart_NeedsAnOpenTurnAndAGameThatIsNotOver(HudActionKind action, bool gameOver, bool expected) =>
        Assert.Equal(expected, AutoPlayModel.CanStart(action, gameOver));

    [Fact]
    public void StartCommand_CarriesTheStoredOptions()
    {
        var settings = new AutoPlaySettings { MaxTurns = 42, UntilEnd = true, FullAutoPlayAI = false };
        var start = AutoPlayModel.StartCommand(settings, AutoPlayKind.Start);
        Assert.Equal(42, start.Turns);
        Assert.True(start.UntilEnd);
        Assert.False(start.FullAutoPlayAI);
    }

    [Fact]
    public void StartCommand_EndTurnIsOneTurnWhateverTheOptionsSay()
    {
        var one = AutoPlayModel.StartCommand(new AutoPlaySettings { MaxTurns = 42, UntilEnd = true, FullAutoPlayAI = true }, AutoPlayKind.EndTurn);
        Assert.Equal(1, one.Turns);
        Assert.False(one.UntilEnd);
        Assert.True(one.FullAutoPlayAI);
        Assert.False(AutoPlayModel.StartCommand(new AutoPlaySettings { FullAutoPlayAI = false }, AutoPlayKind.EndTurn).FullAutoPlayAI);
    }

    [Fact]
    public void StartRun_MatchesTheCommand()
    {
        var settings = new AutoPlaySettings { MaxTurns = 7, UntilEnd = true };
        Assert.Equal(new AutoPlayRun(7, true, 0), AutoPlayModel.StartRun(settings, AutoPlayKind.Start));
        Assert.Equal(new AutoPlayRun(1, false, 0), AutoPlayModel.StartRun(settings, AutoPlayKind.EndTurn));
    }

    [Fact]
    public void Run_CountsDownToItsEnd_AndCountsTurnsPlayed()
    {
        var run = new AutoPlayRun(2, false, 0);
        Assert.True(run.Active);
        run = run.AfterTurn();
        Assert.Equal(new AutoPlayRun(1, false, 1), run);
        Assert.True(run.Active);
        run = run.AfterTurn();
        Assert.Equal(new AutoPlayRun(0, false, 2), run);
        Assert.False(run.Active);
        Assert.Equal(0, run.AfterTurn().TurnsLeft); // never below zero
    }

    [Fact]
    public void Run_UntilEndNeverRunsOut()
    {
        var run = new AutoPlayRun(0, true, 0);
        Assert.True(run.Active);
        run = run.AfterTurn().AfterTurn().AfterTurn();
        Assert.True(run.Active);
        Assert.Equal(3, run.TurnsPlayed);
        Assert.Equal(0, run.TurnsLeft);
    }

    [Fact]
    public void ButtonText_IdleRunningAndUntilEnd()
    {
        Assert.Equal("AutoPlay", AutoPlayModel.ButtonText(null));
        Assert.Equal("Stop AutoPlay (4 left)", AutoPlayModel.ButtonText(new AutoPlayRun(4, false, 6)));
        Assert.Equal("Stop AutoPlay", AutoPlayModel.ButtonText(new AutoPlayRun(4, true, 6)));
    }

    [Fact]
    public void FinishedText_SaysWhoEndedItAndHowManyTurnsWerePlayed()
    {
        Assert.Equal("AutoPlay finished after 5 turns.", AutoPlayModel.FinishedText(new AutoPlayRun(0, false, 5), false, ""));
        Assert.Equal("AutoPlay stopped after 1 turn.", AutoPlayModel.FinishedText(new AutoPlayRun(4, false, 1), true, ""));
        Assert.Equal("AutoPlay finished after 0 turns: Auto-play is not running", AutoPlayModel.FinishedText(new AutoPlayRun(4, false, 0), false, "Auto-play is not running"));
    }

    [Theory]
    [InlineData("skip", AutoPlayAnimations.Skip)]
    [InlineData("FAST", AutoPlayAnimations.Fast)]
    [InlineData("Normal", AutoPlayAnimations.Normal)]
    [InlineData("", AutoPlayAnimations.Skip)]
    [InlineData(null, AutoPlayAnimations.Skip)]
    [InlineData("slow", AutoPlayAnimations.Skip)]
    [InlineData("7", AutoPlayAnimations.Skip)]
    public void ParseAnimations_ReadsTheModeAndDefaultsToSkip(string? text, AutoPlayAnimations expected) =>
        Assert.Equal(expected, AutoPlayModel.ParseAnimations(text));

    [Fact]
    public void Animations_DefaultToSkipAndSanitizedNormalizesTheText()
    {
        Assert.Equal(AutoPlayAnimations.Skip, new AutoPlaySettings().AnimationMode);
        Assert.Equal("Fast", new AutoPlaySettings { Animations = "fast" }.Sanitized().Animations);
        Assert.Equal("Skip", new AutoPlaySettings { Animations = "garbage" }.Sanitized().Animations);
    }

    [Fact]
    public void SkipAnimatesNothingFastIsSpedUpAndBoundedNormalWaitsForThem()
    {
        Assert.False(AutoPlayModel.Animates(AutoPlayAnimations.Skip));
        Assert.True(AutoPlayModel.Animates(AutoPlayAnimations.Fast));
        Assert.True(AutoPlayModel.Animates(AutoPlayAnimations.Normal));
        Assert.Equal(1f, AutoPlayModel.Speed(AutoPlayAnimations.Skip));
        Assert.Equal(AutoPlayModel.FastSpeed, AutoPlayModel.Speed(AutoPlayAnimations.Fast));
        Assert.Equal(1f, AutoPlayModel.Speed(AutoPlayAnimations.Normal));
        Assert.Equal(0, AutoPlayModel.WaitFrames(AutoPlayAnimations.Skip));
        Assert.Equal(AutoPlayModel.FastWaitFrames, AutoPlayModel.WaitFrames(AutoPlayAnimations.Fast));
        Assert.Equal(AutoPlayModel.NormalWaitFrames, AutoPlayModel.WaitFrames(AutoPlayAnimations.Normal));
    }

    [Fact]
    public void ASkipRunRedrawsTheScreenEveryTenthTurnAnAnimatedRunAfterEach()
    {
        var skipped = Enumerable.Range(1, 25).Where(turn => AutoPlayModel.RefreshesAfter(turn, AutoPlayAnimations.Skip)).ToArray();
        Assert.Equal(new[] { 10, 20 }, skipped);
        foreach (var mode in new[] { AutoPlayAnimations.Fast, AutoPlayAnimations.Normal })
            Assert.All(Enumerable.Range(1, 12), turn => Assert.True(AutoPlayModel.RefreshesAfter(turn, mode)));
    }
}
