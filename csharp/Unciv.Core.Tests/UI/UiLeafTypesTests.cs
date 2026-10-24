using Unciv.Logic.Map;
using Unciv.Mathematics;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.UI.Components;
using Unciv.UI.Components.Extensions;
using Unciv.UI.Screens.MapEditorScreen;
using Unciv.UI.Screens.OverviewScreen;
using Unciv.UI.Screens.VictoryScreen;

namespace Unciv.Core.Tests.UI;

// No Kotlin fixtures. Golden values were derived by an independent re-implementation of the Kotlin arithmetic
// (see T050 Notes); labels, ids and strings are copied from the Kotlin sources.
public class UiLeafTypesTests
{
    private static string Maya(int baktun, int katun, int tun) =>
        $"{(char)(0x2170 + baktun)}{MayaCalendar.Baktun}{(char)(0x2170 + katun)}{MayaCalendar.Katun}{(char)(0x2170 + tun)}{MayaCalendar.Tun}";

    [Theory]
    [InlineData(-3000, 0, 5, 14)]
    [InlineData(-3114, 13, 0, 0)]
    [InlineData(-1000, 5, 7, 3)]
    [InlineData(0, 7, 17, 18)]
    [InlineData(1, 7, 17, 19)]
    [InlineData(2012, 12, 19, 19)]
    [InlineData(2024, 13, 0, 11)]
    [InlineData(3000, 15, 10, 2)]
    public void YearToMayaDate(int year, int baktun, int katun, int tun)
    {
        Assert.Equal(Maya(baktun, katun, tun), MayaCalendar.YearToMayaDate(year));
        Assert.Equal(baktun, MayaCalendar.YearToBaktuns(year));
    }

    [Fact]
    public void MayaSymbolsAndNumerals()
    {
        Assert.Equal(20, MayaCalendar.Digits.Count);
        Assert.Equal(23, MayaCalendar.AllSymbols.Count);
        Assert.Equal((MayaCalendar.Tun, "MayaCalendar/Tun"), MayaCalendar.AllSymbols[0]);
        Assert.Equal((MayaCalendar.Baktun, "MayaCalendar/Baktun"), MayaCalendar.AllSymbols[2]);
        Assert.Equal(('Ↄ', "MayaCalendar/19"), MayaCalendar.AllSymbols[22]);
        Assert.Equal("MayaCalendar/7", MayaCalendar.DigitIcon('ⅷ'));
        Assert.Equal("ⅰ", 0.ToMayaNumerals());
        Assert.Equal("Ↄ", 19.ToMayaNumerals());
        Assert.Equal("ⅱⅰ", 20.ToMayaNumerals());
        Assert.Equal("\u2171\u2171\u2173", 423.ToMayaNumerals());
    }

    [Fact]
    public void RankingTypeLabelsIdsAndFilter()
    {
        Assert.Equal("Growth", RankingType.Growth.Label());
        Assert.Equal("Tiles Explored", RankingType.TilesExplored.Label());
        Assert.Equal("Score", RankingType.Score.Label());
        Assert.Equal("SNCPGTFHWAE", string.Concat(Enum.GetValues<RankingType>().Select(it => it.IdForSerialization())));
        Assert.Equal(RankingType.Technologies, RankingTypes.FromIdForSerialization('W'));
        Assert.Null(RankingTypes.FromIdForSerialization('Z'));
        Assert.False(RankingType.TilesExplored.IsVanilla());
        Assert.Equal(10, RankingTypes.FilteredEntries(new GameParameters()).Count());
        Assert.Equal(11, RankingTypes.FilteredEntries(new GameParameters { ShowAdditionalRankingTypes = true }).Count());
        Assert.Equal("OtherIcons/Score", RankingType.Score.IconName());
        Assert.Null(RankingType.Gold.IconName());
    }

    [Fact]
    public void UnitMovementMemoryTypeOrder() =>
        Assert.Equal(["UnitMoved", "UnitAttacked", "UnitWithdrew", "UnitTeleported"], Enum.GetNames<UnitMovementMemoryType>());

    [Fact]
    public void EmpireOverviewCategoriesData()
    {
        Assert.Equal("CSTUPRFWN", string.Concat(Enum.GetValues<EmpireOverviewCategories>().Select(it => it.ShortcutChar())));
        Assert.Equal(9, Enum.GetValues<EmpireOverviewCategories>().Length);
        Assert.Equal("StatIcons/Faith", EmpireOverviewCategories.Religion.IconName());
        Assert.Equal(10, EmpireOverviewCategories.Cities.ScrollAlign());
        Assert.Equal(2, EmpireOverviewCategories.Trades.ScrollAlign());
        Assert.Equal(10, EmpireOverviewCategories.Resources.ScrollAlign());
        Assert.Equal(10, EmpireOverviewCategories.Units.ScrollAlign());
        Assert.Equal(2, EmpireOverviewCategories.Notifications.ScrollAlign());
    }

    [Fact]
    public void MapGeneratorStepsLabelsAndCopiers()
    {
        Assert.Equal("", MapGeneratorSteps.None.Label());
        Assert.Equal("Let the rivers flow", MapGeneratorSteps.Rivers.Label());
        Assert.Null(MapGeneratorSteps.All.CopyParameters());
        Assert.Null(MapGeneratorSteps.Ice.CopyParameters());

        var source = new MapParameters
        {
            Type = "Perlin",
            WaterThreshold = 0.25f,
            Seed = 77,
            TemperatureIntensity = 0.9f,
            TemperatureShift = 0.1f,
            ElevationExponent = 0.5f,
            MaxCoastExtension = 9,
            VegetationRichness = 0.8f,
            RareFeaturesRichness = 0.3f,
            ResourceRichness = 0.7f,
        };
        var target = new MapParameters();
        MapGeneratorSteps.Landmass.CopyParameters()!(source, target);
        Assert.Equal(("Perlin", 0.25f, 77L), (target.Type, target.WaterThreshold, target.Seed));
        Assert.Equal(0.6f, target.TemperatureIntensity);
        MapGeneratorSteps.HumidityAndTemperature.CopyParameters()!(source, target);
        Assert.Equal((0.9f, 0.1f), (target.TemperatureIntensity, target.TemperatureShift));
        MapGeneratorSteps.Elevation.CopyParameters()!(source, target);
        MapGeneratorSteps.LakesAndCoast.CopyParameters()!(source, target);
        MapGeneratorSteps.Vegetation.CopyParameters()!(source, target);
        MapGeneratorSteps.RareFeatures.CopyParameters()!(source, target);
        MapGeneratorSteps.Resources.CopyParameters()!(source, target);
        Assert.Equal((0.5f, 9, 0.8f, 0.3f, 0.7f),
            (target.ElevationExponent, target.MaxCoastExtension, target.VegetationRichness, target.RareFeaturesRichness, target.ResourceRichness));
    }

    [Fact]
    public void Percent()
    {
        Assert.Equal(1.25f, 25.ToPercent());
        Assert.Equal(1.25f, "25".ToPercent());
        Assert.Equal(0.5f, (-50f).ToPercent());
    }

    [Fact]
    public void AmountStrings()
    {
        Assert.Equal("{Consumes [2] [Iron]}", "Iron".GetConsumesAmountString(2, false));
        Assert.Equal("{Consumes [2] [Iron]} /⏳ ({[0] available})", "Iron".GetConsumesAmountString(2, true, 0));
        Assert.Equal("{Costs [3] [Coal]}", "Coal".GetCostsAmountString(3));
        Assert.Equal("{Costs [3] [Coal]} ({[5] available})", "Coal".GetCostsAmountString(3, 5));
        Assert.Equal("{Costs [3] [Coal]}", "Coal".GetCostsAmountString(3, -1));
        Assert.Equal("Need [4] more [Horses]", "Horses".GetNeedMoreAmountString(4));
    }

    [Fact]
    public void SignedStrings()
    {
        Assert.Equal("+5", 5.ToStringSigned());
        Assert.Equal("0", 0.ToStringSigned());
        Assert.Equal("-5", (-5).ToStringSigned());
    }

    [Fact]
    public void DurationFormatting()
    {
        var d = new TimeSpan(2, 3, 4, 5);
        Assert.Equal("[2] Days, [3] Hours, [4] Minutes, [5] Seconds", d.Format());
        Assert.Equal("2 Days", d.FormatShort());
        Assert.Equal("3 Hours", TimeSpan.FromHours(3).FormatShort());
        Assert.Equal("[1] Days, [1] Seconds", TimeSpan.FromSeconds(86401).Format());
        Assert.Equal("0 Seconds", TimeSpan.Zero.FormatShort());
        Assert.Equal("", TimeSpan.Zero.Format());
        Assert.Equal("[1] Hours", TimeSpan.FromMinutes(60).Format());
    }

    [Fact]
    public void TimeComparisons()
    {
        Assert.True(TimeSpan.FromSeconds(2).IsLargerThan(TimeSpan.FromSeconds(1)));
        Assert.False(TimeSpan.FromSeconds(1).IsLargerThan(TimeSpan.FromSeconds(1)));
        var t = new DateTimeOffset(2021, 4, 11, 14, 43, 33, TimeSpan.Zero);
        Assert.True(t.AddSeconds(1).IsLargerThan(t));
        Assert.False(t.IsLargerThan(t));
    }

    [Fact]
    public void DateFormat()
    {
        var date = "2021-04-11T14:43:33Z".ParseDate();
        Assert.Equal("2021-04-11 14:43", date.FormatDate());
        Assert.Throws<FormatException>(() => "2021-04-11".ParseDate());
    }

    [Fact]
    public void PrettyVector()
    {
        Assert.Equal("(3,-4)", new Vector2(3.9f, -4.9f).ToPrettyString());
        Assert.Equal("(0,0)", new Vector2(0f, 0f).ToPrettyString());
    }

    [Fact]
    public void ColorFromRgb()
    {
        var c = ColorExtensions.ColorFromRGB(255, 0, 51);
        Assert.Equal((1f, 0f, 51 / 255f, 1f), (c.R, c.G, c.B, c.A));
        Assert.Equal(ColorExtensions.ColorFromRGB(1, 2, 3), ColorExtensions.ColorFromRGB([1, 2, 3]));
        Assert.Equal(1f, ColorExtensions.ColorFromRGB(300, 0, 0).R); // clamped like libGDX Color
    }

    [Fact]
    public void ColorMutationAndClamp()
    {
        var c = new EngineColor(0.5f, 0.5f, 0.5f, 1f);
        c.Mul(3f);
        Assert.Equal((1f, 1f, 1f, 1f), (c.R, c.G, c.B, c.A));
        c.Sub(new EngineColor(0.25f, 0.5f, 1f, 1f));
        Assert.Equal((0.75f, 0.5f, 0f, 0f), (c.R, c.G, c.B, c.A));
        c.Lerp(EngineColor.Black, 0.5f);
        Assert.Equal(0.375f, c.R);
        c.Add(new EngineColor(1f, 0f, 0f, 0f));
        Assert.Equal(1f, c.R);
        Assert.Same(c, c.Set(0f, 0f, 0f, 0f));
    }

    [Fact]
    public void ColorEqualityAndFormat()
    {
        Assert.Equal(new EngineColor(1f, 0f, 0f, 1f), new EngineColor(1f, 0f, 0f, 1f));
        Assert.NotEqual(new EngineColor(1f, 0f, 0f, 1f), new EngineColor(0f, 1f, 0f, 1f));
        Assert.Equal("b22222ff", EngineColor.Firebrick.ToString());
        Assert.Equal(unchecked((int)0xff000000), EngineColor.Black.ToIntBits());
        Assert.Equal("3f3f3fff", EngineColor.DarkGray.ToString());
    }

    [Fact]
    public void ImmutableColorThrowsOnEveryMutation()
    {
        var c = new ImmutableColor(new EngineColor(0.1f, 0.2f, 0.3f, 1f));
        var other = new EngineColor(1f, 1f, 1f, 1f);
        Assert.Throws<NotSupportedException>(() => c.Set(other));
        Assert.Throws<NotSupportedException>(() => c.Set(0f, 0f, 0f, 0f));
        Assert.Throws<NotSupportedException>(() => c.Add(other));
        Assert.Throws<NotSupportedException>(() => c.Sub(other));
        Assert.Throws<NotSupportedException>(() => c.Mul(2f));
        Assert.Throws<NotSupportedException>(() => c.Lerp(other, 0.5f));
        Assert.Throws<NotSupportedException>(() => EngineColor.White.Mul(0.5f));
        Assert.Equal(0.1f, c.R);
        var copy = c.Cpy();
        copy.Mul(0.5f);
        Assert.Equal(0.05f, copy.R);
        Assert.Equal(0.1f, c.R);
    }
}
