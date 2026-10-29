using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

/// <summary>C062: the design tokens (design/ui-redesign.md 2): contrast of the text colours, scaling, breakpoints and the automatic scale.</summary>
public class UiTokensTests
{
    public static TheoryData<string, Rgb> Surfaces => new()
    {
        { "Surface1", UiTokens.Surface1 }, { "Surface2", UiTokens.Surface2 }, { "Surface3", UiTokens.Surface3 },
    };

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void PrimaryAndSecondaryTextReadOnEverySurface(string name, Rgb surface)
    {
        Assert.True(UiTokens.Contrast(UiTokens.TextPrimary, surface) >= 4.5, $"TextPrimary on {name}");
        Assert.True(UiTokens.Contrast(UiTokens.TextSecondary, surface) >= 4.5, $"TextSecondary on {name}");
    }

    [Theory]
    [InlineData("Surface1")]
    [InlineData("Surface2")]
    public void MutedTextReadsOnPanelsAndCards(string name)
    {
        var surface = name == "Surface1" ? UiTokens.Surface1 : UiTokens.Surface2;
        Assert.True(UiTokens.Contrast(UiTokens.TextMuted, surface) >= 3.0, name);
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void DangerTextReadsOnEveryRaisedSurface(string name, Rgb surface) =>
        Assert.True(UiTokens.Contrast(UiTokens.DangerText, surface) >= 4.5, $"DangerText on {name}");

    [Fact]
    public void TextOnAccentReads() => Assert.True(UiTokens.Contrast(UiTokens.OnAccent, UiTokens.Accent) >= 4.5);

    [Fact]
    public void AccentTextReadsOnAnInsetAndOnTheSecondaryButton()
    {
        Assert.True(UiTokens.Contrast(UiTokens.Accent, UiTokens.Inset) >= 4.5);
        Assert.True(UiTokens.Contrast(UiTokens.AccentHi, UiTokens.Surface3) >= 4.5);
    }

    [Fact]
    public void ContrastIsSymmetricAndSpansOneToTwentyOne()
    {
        var black = new Rgb(0, 0, 0);
        var white = new Rgb(255, 255, 255);
        Assert.Equal(21.0, UiTokens.Contrast(black, white), 6);
        Assert.Equal(1.0, UiTokens.Contrast(UiTokens.Accent, UiTokens.Accent), 6);
        Assert.Equal(UiTokens.Contrast(UiTokens.Bg0, UiTokens.Accent), UiTokens.Contrast(UiTokens.Accent, UiTokens.Bg0), 9);
    }

    [Fact]
    public void SurfacesGetLighterWithElevation()
    {
        static int Sum(Rgb c) => c.R + c.G + c.B;
        Assert.True(Sum(UiTokens.Bg0) < Sum(UiTokens.Bg1) && Sum(UiTokens.Bg1) < Sum(UiTokens.Surface1) && Sum(UiTokens.Surface1) < Sum(UiTokens.Surface2) && Sum(UiTokens.Surface2) < Sum(UiTokens.Surface3));
        Assert.True(Sum(UiTokens.Inset) < Sum(UiTokens.Surface1));
    }

    [Fact]
    public void SpacingIsOnTheFourPixelGrid() =>
        Assert.All(new[] { UiTokens.S1, UiTokens.S2, UiTokens.S3, UiTokens.S4, UiTokens.S6, UiTokens.S8 }, s => Assert.Equal(0, s % 4));

    [Fact]
    public void TypeScaleIsOrderedAndNamed()
    {
        Assert.Equal(["Display", "Title", "Heading", "Body", "BodyStrong", "Small", "Overline"], UiTokens.TypeScale.Select(t => t.Name));
        Assert.Equal(UiTokens.TypeScale.Count, UiTokens.TypeScale.Select(t => t.Name).Distinct().Count());
        Assert.Equal(UiTokens.FontSize, UiTokens.Body.Size);
        Assert.True(UiTokens.Body.IsDefault && !UiTokens.Heading.IsDefault);
        Assert.Equal([34, 24, 18, 15, 15, 13, 11], UiTokens.TypeScale.Select(t => t.Size));
        Assert.All(UiTokens.TypeScale.Where(t => t.Serif), t => Assert.Equal(700, t.Weight));
        Assert.Equal(["Display", "Title"], UiTokens.TypeScale.Where(t => t.Serif).Select(t => t.Name));
    }

    [Theory]
    [InlineData(12, 1f, 12)]
    [InlineData(12, 2f, 24)]
    [InlineData(10, 1.5f, 15)]
    [InlineData(15, 1.5f, 23)]
    [InlineData(1, 0.5f, 1)]
    [InlineData(3, 0.5f, 2)]
    [InlineData(0, 2f, 0)]
    public void ScaledRoundsHalfAwayFromZero(int unscaled, float scale, int expected) => Assert.Equal(expected, UiTokens.Scaled(unscaled, scale));

    [Theory]
    [InlineData(1, 0.25f, 1)]
    [InlineData(2, 0.5f, 1)]
    [InlineData(2, 2f, 4)]
    public void BordersNeverVanish(int unscaled, float scale, int expected) => Assert.Equal(expected, UiTokens.ScaledAtLeastOne(unscaled, scale));

    [Theory]
    [InlineData(640, UiBreakpoint.Compact)]
    [InlineData(999.9f, UiBreakpoint.Compact)]
    [InlineData(1000, UiBreakpoint.Regular)]
    [InlineData(1600, UiBreakpoint.Regular)]
    [InlineData(1600.1f, UiBreakpoint.Wide)]
    public void BreakpointFollowsLogicalWidth(float width, UiBreakpoint expected) => Assert.Equal(expected, UiTokens.BreakpointFor(width));

    [Theory]
    [InlineData(640, 480, 1f)]
    [InlineData(1280, 720, 1f)]
    [InlineData(1920, 1080, 1.5f)]
    [InlineData(2560, 1440, 2f)]
    [InlineData(3840, 2160, 2f)]
    [InlineData(1920, 720, 1f)]
    [InlineData(1700, 1000, 1.25f)]
    [InlineData(0, 0, 1f)]
    public void AutoScaleFloorsToAQuarterStepWithinOneAndTwo(int width, int height, float expected) => Assert.Equal(expected, UiTokens.AutoScale(width, height));

    [Fact]
    public void LegacyPaletteNamesAreAliasesOfTheTokens()
    {
        Assert.Equal(UiTokens.Bg1, PlaceholderPalette.Background);
        Assert.Equal(UiTokens.Surface1, PlaceholderPalette.PanelFill);
        Assert.Equal(UiTokens.BorderSubtle, PlaceholderPalette.PanelBorder);
        Assert.Equal(UiTokens.Accent, PlaceholderPalette.Accent);
        Assert.Equal(UiTokens.AccentHi, PlaceholderPalette.AccentHover);
        Assert.Equal(UiTokens.Surface2, PlaceholderPalette.ButtonFill);
        Assert.Equal(UiTokens.Surface3, PlaceholderPalette.ButtonHover);
        Assert.Equal(UiTokens.Inset, PlaceholderPalette.ButtonPressed);
        Assert.Equal(UiTokens.TextPrimary, PlaceholderPalette.Text);
        Assert.Equal(UiTokens.TextMuted, PlaceholderPalette.TextDisabled);
    }
}
