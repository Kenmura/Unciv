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
    [InlineData(1366, 768, 1f)] // the common laptop panel
    [InlineData(1920, 1080, 1.5f)]
    [InlineData(1920, 1200, 1.5f)] // 16:10, height limited
    [InlineData(2560, 1440, 1.75f)]
    [InlineData(2560, 1600, 1.75f)] // 16:10
    [InlineData(3440, 1440, 1.75f)] // ultrawide: the height decides, not the extra width
    [InlineData(3840, 1080, 1.5f)] // super ultrawide
    [InlineData(3840, 2160, 2.5f)]
    [InlineData(5120, 2880, 2.75f)] // 5K
    [InlineData(7680, 4320, 3.5f)] // 8K: the curve's cap
    [InlineData(15360, 8640, 3.5f)]
    [InlineData(1920, 720, 1f)]
    [InlineData(1700, 1000, 1.25f)]
    [InlineData(0, 0, 1f)]
    public void AutoScaleFollowsTheCurveInQuarterSteps(int width, int height, float expected) => Assert.Equal(expected, UiTokens.AutoScale(width, height));

    [Theory]
    [InlineData(3840, 2160, 2f, 3f)] // a 4K laptop at 200%: the 1080p look, doubled in pixels
    [InlineData(2560, 1440, 2f, 2f)] // a 1440p panel at 200%: the 720p look
    [InlineData(1920, 1080, 1.25f, 1.5f)] // 125% on 1080p: 1.2 x 1.25
    [InlineData(3840, 2160, 1.5f, 2.75f)] // 150% on 4K: curve(1440p) 1.9 x 1.5
    [InlineData(3840, 2160, 1f, 2.5f)] // the same 4K monitor at 100%
    [InlineData(1920, 1080, 0f, 1.5f)] // unknown or nonsense factors mean none
    [InlineData(1920, 1080, -2f, 1.5f)]
    [InlineData(1920, 1080, float.NaN, 1.5f)]
    [InlineData(1920, 1080, 0.5f, 1.5f)] // a factor below 1 is ignored
    [InlineData(15360, 8640, 4f, 4f)] // capped in total
    [InlineData(15360, 8640, 100f, 4f)]
    public void AutoScaleHonoursTheOsDisplayScale(int width, int height, float osScale, float expected) => Assert.Equal(expected, UiTokens.AutoScale(width, height, osScale));

    [Fact]
    public void ScaleCurveIsMonotonicBoundedAndSublinearBeyond1080p()
    {
        float previous = 0f;
        for (float fit = 0f; fit <= 12f; fit += 0.05f)
        {
            float scale = UiTokens.ScaleCurve(fit);
            Assert.InRange(scale, 1f, UiTokens.MaxCurveScale);
            Assert.True(scale >= previous, $"fit {fit}");
            if (fit > 1.5f) Assert.True(scale < fit, $"fit {fit}: scale {scale} is not below the 1:1 line");
            previous = scale;
        }
        Assert.Equal(1f, UiTokens.ScaleCurve(float.NaN));
        Assert.Equal(UiTokens.MaxCurveScale, UiTokens.ScaleCurve(float.PositiveInfinity));
    }

    [Fact]
    public void AutoScaleKeepsComfortableLogicalWindowsAtEveryCommonResolution()
    {
        foreach (var (w, h) in new[] { (1366, 768), (1920, 1080), (1920, 1200), (2560, 1440), (2560, 1600), (3440, 1440), (3840, 2160), (5120, 2880), (7680, 4320) })
        {
            float scale = UiTokens.AutoScale(w, h);
            float logicalWidth = w / scale, logicalHeight = h / scale;
            Assert.True(logicalWidth >= UiTokens.ReferenceWidth && logicalHeight >= UiTokens.ReferenceHeight, $"{w}x{h}: logical {logicalWidth}x{logicalHeight} is below the design window");
            Assert.True(logicalHeight <= 1250f, $"{w}x{h}: logical height {logicalHeight} wastes the screen on tiny controls"); // at most about 8K-at-cap room
            Assert.InRange(UiTokens.Scaled(UiTokens.ControlMinHeight, scale), UiTokens.ControlMinHeight, 112); // a button is never smaller than at 1x nor huge
        }
    }

    [Theory]
    [InlineData(2400, 1080, 400f, 2.5f)] // a 400 ppi phone: density 2.5, logical 960x432
    [InlineData(2400, 1080, 560f, 2.5f)] // very dense: capped at 2.6, floored to 2.5
    [InlineData(2400, 1080, 320f, 2f)]
    [InlineData(1920, 1080, 440f, 2f)] // limited by 1920/900 = 2.13
    [InlineData(1280, 720, 320f, 1.25f)] // limited by 1280/900 = 1.42
    [InlineData(1280, 720, 100f, 1f)] // never below 1
    [InlineData(2560, 1600, 280f, 1.75f)] // a tablet
    [InlineData(1920, 1080, 0f, 1.5f)] // unknown density: the pixel based AutoScale
    [InlineData(0, 0, 400f, 1f)]
    public void TouchAutoScaleFollowsTheDensityWithinTheLogicalViewportFloor(int width, int height, float dpi, float expected) =>
        Assert.Equal(expected, UiTokens.TouchAutoScale(width, height, dpi));

    [Fact]
    public void TouchAutoScaleKeepsALogicalViewportOfAtLeastTheHudSize()
    {
        foreach (var (w, h, dpi) in new[] { (2400, 1080, 400f), (2340, 1080, 480f), (1920, 1080, 420f), (3200, 1440, 560f), (1280, 720, 320f) })
        {
            float scale = UiTokens.TouchAutoScale(w, h, dpi);
            Assert.InRange(scale, 1f, UiTokens.MaxTouchScale);
            Assert.True(w / scale >= UiTokens.TouchMinLogicalWidth && h / scale >= UiTokens.TouchMinLogicalHeight, $"{w}x{h}@{dpi}: scale {scale}");
        }
    }

    [Theory]
    [InlineData(48f, 400f, 120)]
    [InlineData(48f, 160f, 48)]
    [InlineData(8f, 400f, 20)]
    [InlineData(48f, 0f, 0)]
    public void DpToPxUsesAndroidsOneHundredSixtyDpPerInch(float dp, float dpi, int expected) => Assert.Equal(expected, UiTokens.DpToPx(dp, dpi));

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
