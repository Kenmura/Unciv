using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

/// <summary>C065: the Godot-free half of the shared UI components (delta colouring, inline stat glyphs, table sorting, badge and tooltip text) and the rule that no component builds a stylebox.</summary>
public class UiComponentsTests
{
    [Theory]
    [InlineData(3, "+3", "#5DBB63")]
    [InlineData(-2, "-2", "#EF7B71")]
    [InlineData(0, "0", "#6F7988")]
    public void DeltaIsSignedAndColouredFromTokens(int value, string text, string hex)
    {
        Assert.Equal(text, UiText.DeltaText(value));
        Assert.Equal($"[color={hex}]{text}[/color]", UiText.Delta(value));
    }

    [Fact]
    public void DeltaTextIsInvariantForExtremes()
    {
        Assert.Equal("+2147483647", UiText.DeltaText(int.MaxValue));
        Assert.Equal("-2147483648", UiText.DeltaText(int.MinValue));
    }

    [Theory]
    [InlineData('¤', "Gold")]
    [InlineData('⁂', "Food")]
    [InlineData('⚙', "Production")]
    [InlineData('⍾', "Science")]
    [InlineData('♪', "Culture")]
    [InlineData('⌣', "Happiness")]
    [InlineData('☮', "Faith")]
    [InlineData('x', null)]
    public void GlyphsMapToStats(char glyph, string? stat) => Assert.Equal(stat, UiText.StatForGlyph(glyph));

    [Fact]
    public void RunsSplitAtGlyphs()
    {
        Assert.Equal([new TextRun("3 ", null), new TextRun("¤", "Gold"), new TextRun(" +2 ", null), new TextRun("⚙", "Production")], UiText.Runs("3 ¤ +2 ⚙"));
        Assert.Equal([new TextRun("plain", null)], UiText.Runs("plain"));
        Assert.Equal([new TextRun("⁂", "Food"), new TextRun("⁂", "Food")], UiText.Runs("⁂⁂"));
        Assert.Empty(UiText.Runs(""));
    }

    [Theory]
    [InlineData(-4, "")]
    [InlineData(0, "")]
    [InlineData(1, "1")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    public void BadgeTextCapsAtNinetyNine(int count, string text) => Assert.Equal(text, UiText.BadgeText(count));

    [Fact]
    public void TooltipNamesTheHotkey()
    {
        Assert.Equal("Close (Esc)", UiText.TooltipFor("Close", "Esc"));
        Assert.Equal("Close", UiText.TooltipFor("Close", null));
        Assert.Equal("Close", UiText.TooltipFor("Close", ""));
    }

    private static IReadOnlyList<IReadOnlyList<string>> Rows(params string[][] rows) => rows;

    [Fact]
    public void NumbersSortByValueNotByText()
    {
        var rows = Rows(["a", "12"], ["b", "8"], ["c", "20"], ["d", "-3"], ["e", "+4"]);
        Assert.Equal([3, 4, 1, 0, 2], TableSorter.Order(rows, 1, true));
        Assert.Equal([2, 0, 1, 4, 3], TableSorter.Order(rows, 1, false));
    }

    [Fact]
    public void TextSortsOrdinallyAndEqualCellsKeepInputOrderBothWays()
    {
        var rows = Rows(["b", "x"], ["a", "x"], ["b", "y"], ["a", "x"]);
        Assert.Equal([1, 3, 0, 2], TableSorter.Order(rows, 0, true));
        Assert.Equal([0, 2, 1, 3], TableSorter.Order(rows, 0, false));
    }

    [Fact]
    public void AColumnWithAnyNonNumberSortsAsText()
    {
        // "10%" is text, so "9" sorts after "10%" and the whole column compares as strings
        Assert.Equal([1, 0], TableSorter.Order(Rows(["9"], ["10%"]), 0, true));
    }

    [Fact]
    public void EmptyAndMissingCellsGoLastInBothDirections()
    {
        var rows = Rows(["3"], [""], ["1"], []);
        Assert.Equal([2, 0, 1, 3], TableSorter.Order(rows, 0, true));
        Assert.Equal([0, 2, 1, 3], TableSorter.Order(rows, 0, false));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,200")]
    [InlineData("12%")]
    [InlineData("")]
    public void NonFiniteAndFormattedNumbersAreText(string cell) => Assert.Null(TableSorter.ParseNumber(cell));

    [Fact]
    public void NoClientUiFileBuildsAStylebox()
    {
        string? ui = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null && ui == null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Unciv.Godot", "Ui");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "Unciv.sln"))) ui = candidate;
        }
        Assert.NotNull(ui);
        var offenders = Directory.EnumerateFiles(ui, "*.cs").Where(it => File.ReadAllText(it).Contains("new StyleBox", StringComparison.Ordinal)).Select(Path.GetFileName);
        Assert.Empty(offenders);
    }
}
