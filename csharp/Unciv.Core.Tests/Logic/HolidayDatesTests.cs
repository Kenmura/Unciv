using static Unciv.Logic.HolidayDates;

namespace Unciv.Core.Tests.Logic;

/// <summary>
/// Ported from <c>tests/src/com/unciv/logic/HolidayDatesTest.kt</c>, plus a sweep against golden values generated
/// from the real Kotlin <c>HolidayDates</c> on the JVM (run with <c>-Duser.timezone=UTC</c>, see the Qingming remark on the class) (<c>Golden/HolidayDates.golden.tsv</c>:
/// <c>R</c> rows are every holiday for 2019..2101, <c>D</c> rows are the days 2023-01-01..2030-12-31 that
/// <c>getHolidayByDate</c> answers with a holiday; every other day must answer null).
/// </summary>
public class HolidayDatesTests
{
    [Fact]
    public void TestEasterExamples()
    {
        DateRange easter1961 = GetHolidayByYear(Holidays.Easter, 1961);
        DateRange knownEaster1961 = DateRange.Of(1961, 3, 31, 4);
        DateRange easter2022 = GetHolidayByYear(Holidays.Easter, 2022);
        DateRange knownEaster2022 = DateRange.Of(2022, 4, 15, 4);
        Assert.True(easter1961 == knownEaster1961 || easter1961.Equals(knownEaster1961), $"Easter 1961 is calculated as {easter1961} but should be {knownEaster1961}");
        Assert.True(easter2022.Equals(knownEaster2022), $"Easter 2022 is calculated as {easter2022} but should be {knownEaster2022}");
    }

    [Fact]
    public void TestGetHolidayByDate()
    {
        var date = new DateOnly(2022, 4, 15);
        Holidays? holiday = GetHolidayByDate(date);
        Assert.True(holiday == Holidays.Easter, $"{date} should be easter but is calculated to be {holiday}");
    }

    [Fact]
    public void EasterOverrideAndMonth()
    {
        Assert.Equal(Holidays.TowelDay, GetHolidayByDate(new DateOnly(2022, 1, 1), "TowelDay"));
        Assert.Null(GetHolidayByDate(new DateOnly(2022, 1, 1), "NoSuchHoliday"));
        Assert.Equal(7, GetMonth(new DateOnly(2022, 7, 4)));
        Assert.Equal(12, GetMonth(new DateOnly(2022, 7, 4), 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => GetMonth(new DateOnly(2022, 7, 4), 13));
    }

    [Fact]
    public void NeverRangeIsEmpty()
    {
        Assert.Equal(0, DateRange.Never.Length);
        Assert.False(DateRange.Never.Contains(new DateOnly(2024, 1, 1)));
        Assert.Equal(DateRange.Never, GetHolidayByYear(Holidays.Diwali, 2200));
    }

    [Fact]
    public void GoldenSweepMatchesKotlin()
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Logic", "Golden", "HolidayDates.golden.tsv"));
        var expectedDays = new Dictionary<DateOnly, string>();
        int ranges = 0;
        foreach (string line in lines)
        {
            string[] t = line.Split('\t');
            if (t[0] == "R")
            {
                DateRange range = GetHolidayByYear(Enum.Parse<Holidays>(t[1]), int.Parse(t[2]));
                string actual = range.Equals(DateRange.Never) ? "never" : range.ToString();
                Assert.True(t[3] == actual, $"{t[1]} {t[2]}: expected {t[3]} but was {actual}");
                if (t[3] != "never")
                {
                    Assert.Equal(int.Parse(t[4]), range.Length);
                    Assert.Equal(int.Parse(t[5]), range.GetHashCode());
                }

                ranges++;
            }
            else
            {
                expectedDays[DateOnly.Parse(t[1])] = t[2];
            }
        }

        Assert.Equal(15 * 83, ranges);

        for (var day = new DateOnly(2023, 1, 1); day < new DateOnly(2031, 1, 1); day = day.AddDays(1))
        {
            string expected = expectedDays.GetValueOrDefault(day, "null");
            string actual = GetHolidayByDate(day)?.ToString() ?? "null";
            Assert.True(expected == actual, $"{day:yyyy-MM-dd}: expected {expected} but was {actual}");
        }
    }
}
