using Unciv.Assets;
using Unciv.DevConsole;
using Unciv.GodotClient.Settings;

namespace Unciv.Core.Tests.Assets;

// C046: the history, completion and tone rules of the dev console popup.
public class ConsoleModelTests
{
    private static List<string> Numbered(int count) => Enumerable.Range(1, count).Select(it => "cmd " + it).ToList();

    [Fact]
    public void AddHistoryAppendsTrimmedLinesInOrder()
    {
        var history = ConsoleModel.AddHistory([], "  civ addstat gold 5  ");
        history = ConsoleModel.AddHistory(history, "game setturn 3");
        Assert.Equal(["civ addstat gold 5", "game setturn 3"], history);
    }

    [Fact]
    public void AddHistoryDoesNotChangeItsInput()
    {
        List<string> before = ["a"];
        var after = ConsoleModel.AddHistory(before, "b");
        Assert.Equal(["a"], before);
        Assert.Equal(["a", "b"], after);
        Assert.NotSame(before, ConsoleModel.AddHistory(before, "  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ABlankLineIsNotRemembered(string line) => Assert.Equal(["a"], ConsoleModel.AddHistory(["a"], line));

    [Fact]
    public void ALineEqualToTheLastOneIgnoringCaseIsNotRepeatedButAnOlderOneIs()
    {
        Assert.Equal(["a", "B"], ConsoleModel.AddHistory(["a", "B"], "b"));
        Assert.Equal(["a", "b", "a"], ConsoleModel.AddHistory(["a", "b"], "a"));
    }

    [Fact]
    public void AFullHistoryDropsTheOldestEntry()
    {
        var full = Numbered(ConsoleModel.MaxHistory);
        var next = ConsoleModel.AddHistory(full, "new");
        Assert.Equal(ConsoleModel.MaxHistory, next.Count);
        Assert.Equal("cmd 2", next[0]);
        Assert.Equal("new", next[^1]);
    }

    [Fact]
    public void AHistoryOneShortOfFullJustGrows()
    {
        var almost = Numbered(ConsoleModel.MaxHistory - 1);
        var next = ConsoleModel.AddHistory(almost, "new");
        Assert.Equal(ConsoleModel.MaxHistory, next.Count);
        Assert.Equal("cmd 1", next[0]);
    }

    [Fact]
    public void AFullHistoryRemovesAnEarlierEqualEntryInsteadOfTheOldest()
    {
        var full = Numbered(ConsoleModel.MaxHistory);
        var next = ConsoleModel.AddHistory(full, "CMD 5");
        Assert.Equal(ConsoleModel.MaxHistory, next.Count);
        Assert.Equal("cmd 1", next[0]);
        Assert.DoesNotContain("cmd 5", next);
        Assert.Equal("CMD 5", next[^1]);
    }

    [Fact]
    public void SanitizeKeepsTheNewestLinesAndDropsBlankAndOverlongOnes()
    {
        Assert.Empty(ConsoleModel.SanitizeHistory(null));
        var stored = new List<string> { "", "  ", new string('x', ConsoleRunner.MaxLineLength + 1), "ok" };
        stored.AddRange(Numbered(ConsoleModel.MaxHistory + 3));
        var clean = ConsoleModel.SanitizeHistory(stored);
        Assert.Equal(ConsoleModel.MaxHistory, clean.Count);
        Assert.Equal("cmd 4", clean[0]);
        Assert.Equal("cmd 45", clean[^1]);
        Assert.Single(ConsoleModel.SanitizeHistory([new string('x', ConsoleRunner.MaxLineLength)]));
    }

    [Fact]
    public void SanitizeDropsNullAndControlCharacterLinesAndTrimsTheRest()
    {
        var clean = ConsoleModel.SanitizeHistory(["bad\nline", "esc\u001b[31m", "nul\0", null!, "  civ addstat gold 5  ", "tab\there", "ünï ok"]);
        Assert.Equal(["civ addstat gold 5", "ünï ok"], clean);
    }

    [Theory]
    [InlineData(0, 0, -1, -1)]
    [InlineData(0, 0, 1, -1)]
    [InlineData(3, 3, -1, 2)]
    [InlineData(3, 2, -1, 1)]
    [InlineData(3, 0, -1, 0)]
    [InlineData(3, 1, 1, 2)]
    [InlineData(3, 2, 1, 2)]
    [InlineData(3, 3, 0, 2)]
    public void StepMovesWithinTheHistory(int count, int current, int delta, int expected) =>
        Assert.Equal(expected, ConsoleModel.Step(count, current, delta));

    [Theory]
    [InlineData("ci", 2, "civ ", "civ ")]
    [InlineData("civ adds", 4, "addstat ", "civ addstat ")]
    [InlineData("civ ", 0, "addstat ", "civ addstat ")]
    [InlineData("", 0, "civ ", "civ ")]
    [InlineData("ab", 5, "xy", "xy")]
    [InlineData("tile set \"Ro", 3, "\"Road", "tile set \"Road")]
    public void ACompletionReplacesTheEndOfTheLine(string line, int replace, string completion, string expected) =>
        Assert.Equal(expected, ConsoleModel.ApplyCompletion(line, replace, completion));

    [Fact]
    public void TheEngineKindsMapToTonesAndAnythingElseIsAnError()
    {
        Assert.Equal(ConsoleTone.Ok, ConsoleModel.ToneOf(nameof(ConsoleResponseKind.Ok)));
        Assert.Equal(ConsoleTone.Hint, ConsoleModel.ToneOf(nameof(ConsoleResponseKind.Hint)));
        Assert.Equal(ConsoleTone.Error, ConsoleModel.ToneOf(nameof(ConsoleResponseKind.Error)));
        Assert.Equal(ConsoleTone.Error, ConsoleModel.ToneOf("Bogus"));
        Assert.Equal(ConsoleTone.Error, ConsoleModel.ToneOf(""));
    }

    [Fact]
    public void EachToneHasItsOwnColour()
    {
        Assert.Equal(PlaceholderPalette.HealthGood, ConsoleModel.ColourOf(ConsoleTone.Ok));
        Assert.Equal(PlaceholderPalette.HealthWarn, ConsoleModel.ColourOf(ConsoleTone.Hint));
        Assert.Equal(PlaceholderPalette.ErrorText, ConsoleModel.ColourOf(ConsoleTone.Error));
    }

    [Fact]
    public void TheConsoleKeyIsTheKotlinBacktickAndCanBeRebound()
    {
        Assert.Equal(HotkeyAction.OpenConsole, Hotkeys.Resolve(new Dictionary<string, string>(), "Quoteleft"));
        Assert.Equal("Quoteleft", Hotkeys.Normalize("QuoteLeft"));
        Assert.Equal("Quoteleft", Hotkeys.Defaults[HotkeyAction.OpenConsole]);
        var bound = Hotkeys.Rebind(new Dictionary<string, string>(), HotkeyAction.OpenConsole, "F12", out _);
        Assert.Equal(HotkeyAction.OpenConsole, Hotkeys.Resolve(bound!, "F12"));
        Assert.Null(Hotkeys.Resolve(bound!, "Quoteleft"));
    }

    [Fact]
    public void TheHistoryAndKeepOpenAreSettingsThatSurviveSanitizing()
    {
        var settings = (ClientSettings.Default with { ConsoleHistory = ["", "civ addstat gold 5"], KeepConsoleOpen = true }).Sanitized();
        Assert.Equal(["civ addstat gold 5"], settings.ConsoleHistory);
        Assert.True(settings.KeepConsoleOpen);
        Assert.Empty(ClientSettings.Default.ConsoleHistory);
        Assert.False(ClientSettings.Default.KeepConsoleOpen);
        Assert.Empty((ClientSettings.Default with { ConsoleHistory = null! }).Sanitized().ConsoleHistory);
    }
}
