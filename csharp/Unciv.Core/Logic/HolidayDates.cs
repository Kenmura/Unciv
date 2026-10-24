using Unciv.Utils;

namespace Unciv.Logic;

/// <summary>
/// Known holidays (for easter egg use); see <see cref="HolidaysExtensions"/> for the per-holiday data. Ported from <c>core/src/com/unciv/logic/HolidayDates.kt</c>.
/// </summary>
/// <remarks>
/// Deviations from upstream, all for determinism (the engine never reads the clock, the time zone or system properties):
/// <list type="bullet">
/// <item><c>LocalDate.now()</c> defaults are gone: callers pass today's date explicitly.</item>
/// <item>The <c>easterEgg</c> and <c>month</c> system properties become explicit parameters.</item>
/// <item><see cref="DateRange.Never"/> is a fixed empty range instead of one anchored at today (it contains no date either way).</item>
/// <item>Qingming uses the UTC date of the equinox instant instead of the system time zone's.</item>
/// <item>Dates are <see cref="DateOnly"/>, so years outside 1..9999 throw where java.time would accept them.</item>
/// </list>
/// </remarks>
public static class HolidayDates
{
    /// <summary>
    /// Known holidays. <see cref="HolidaysExtensions.GetChance"/> is the chance a holiday is shown on a day inside its range,
    /// the name is used to look for art and can be used as-is as an easter egg override.
    /// </summary>
    public enum Holidays
    {
        Easter,
        Samhain,
        Xmas,
        DiaDeLosMuertos,
        YuleGoat,
        Qingming,
        Diwali,
        LunarNewYear,
        AprilFoolsDay,
        PrideDay,
        TowelDay,
        UncivBirthday,
        Friday13th,
        StarWarsDay,
        Passover,
    }

    /// <summary>A closed range of dates, equal by value.</summary>
    public class DateRange
    {
        public DateOnly Start { get; }

        public DateOnly EndInclusive { get; }

        /// <summary>Never contains any date; an empty range.</summary>
        public static DateRange Never { get; } = new(new DateOnly(1970, 1, 1), new DateOnly(1969, 12, 31));

        public DateRange(DateOnly start, DateOnly endInclusive)
        {
            Start = start;
            EndInclusive = endInclusive;
        }

        public int Length => Math.Max(EndInclusive.DayNumber - Start.DayNumber, 0);

        public bool Contains(DateOnly date) => date >= Start && date <= EndInclusive;

        public bool IsEmpty() => Start > EndInclusive;

        public static DateRange Of(DateOnly date) => new(date, date);

        public static DateRange Of(int year, int month, int day) => Of(new DateOnly(year, month, day));

        public static DateRange Of(DateOnly date, int duration) => new(date, date.AddDays(duration - 1));

        public static DateRange Of(int year, int month, int day, int duration) => Of(new DateOnly(year, month, day), duration);

        public override string ToString() => $"{Start:yyyy-MM-dd}..{EndInclusive:yyyy-MM-dd}";

        public override bool Equals(object? obj) =>
            ReferenceEquals(this, obj) || (obj is DateRange other && Start == other.Start && EndInclusive == other.EndInclusive);

        public override int GetHashCode() => Randomness.HashOf(JavaLocalDateHashCode(Start), JavaLocalDateHashCode(EndInclusive));
    }

    public static Holidays? GetHolidayByDate(DateOnly date, string? easterEggOverride = null)
    {
        // Upstream draws lazily, only for holidays whose range contains the date, in enum order
        var rng = new GameRandom(JavaLocalDateHashCode(date));
        if (easterEggOverride != null)
        {
            Holidays? forced = HolidaysExtensions.SafeValueOf(easterEggOverride);
            if (forced != null)
            {
                return forced;
            }
        }

        foreach (Holidays holiday in Enum.GetValues<Holidays>())
        {
            DateRange range = holiday.GetByYear(date.Year);
            if (range.Contains(date) && rng.NextFloat() <= holiday.GetChance())
            {
                return holiday;
            }
        }

        return null;
    }

    public static DateRange GetHolidayByYear(Holidays holiday, int year) => holiday.GetByYear(year);

    /// <summary>Month 1..12 of <paramref name="today"/>, or <paramref name="monthOverride"/> (the <c>month</c> system property upstream).</summary>
    public static int GetMonth(DateOnly today, int? monthOverride = null)
    {
        if (monthOverride is null)
        {
            return today.Month;
        }

        if (monthOverride < 1 || monthOverride > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(monthOverride), monthOverride, "Invalid value for Month: must be 1..12");
        }

        return monthOverride.Value;
    }

    /// <summary>java.time.LocalDate.hashCode(), as the Kotlin code seeds its Random with it.</summary>
    internal static int JavaLocalDateHashCode(DateOnly date) =>
        (date.Year & unchecked((int)0xFFFFF800)) ^ ((date.Year << 11) + (date.Month << 6) + date.Day);

    internal static DateOnly ClosestWeekday(this DateOnly date, DayOfWeek day)
    {
        // java.time.DayOfWeek ordinals run Monday(0)..Sunday(6)
        int delta = (7 + JavaOrdinal(date.DayOfWeek) - JavaOrdinal(day)) % 7;
        return delta < 4 ? date.AddDays(delta) : date.AddDays(-(7 - delta));
    }

    private static int JavaOrdinal(DayOfWeek day) => ((int)day + 6) % 7;

    internal static class Tables
    {
        // http://www.astropixels.com/ephemeris/soleq2001.html
        // Upstream stores the UTC instants and converts them with ZoneId.systemDefault(); the port keeps the UTC calendar date
        // (the minute-precision time of day is not needed for it). Deliberate: a wall-clock-zone dependency has no place in the engine.
        public static readonly Dictionary<int, DateOnly> Equinoxes = new()
        {
            [2024] = new DateOnly(2024, 3, 20), // 03:07Z
            [2025] = new DateOnly(2025, 3, 20), // 09:02Z
            [2026] = new DateOnly(2026, 3, 20), // 14:46Z
            [2027] = new DateOnly(2027, 3, 20), // 20:25Z
            [2028] = new DateOnly(2028, 3, 20), // 02:17Z
            [2029] = new DateOnly(2029, 3, 20), // 08:01Z
            [2030] = new DateOnly(2030, 3, 20), // 13:51Z
            [2031] = new DateOnly(2031, 3, 20), // 19:41Z
            [2032] = new DateOnly(2032, 3, 20), // 01:23Z
            [2033] = new DateOnly(2033, 3, 20), // 07:23Z
            [2034] = new DateOnly(2034, 3, 20), // 13:18Z
            [2035] = new DateOnly(2035, 3, 20), // 19:03Z
            [2036] = new DateOnly(2036, 3, 20), // 01:02Z
            [2037] = new DateOnly(2037, 3, 20), // 06:50Z
            [2038] = new DateOnly(2038, 3, 20), // 12:40Z
            [2039] = new DateOnly(2039, 3, 20), // 18:32Z
            [2040] = new DateOnly(2040, 3, 20), // 00:11Z
            [2041] = new DateOnly(2041, 3, 20), // 06:07Z
            [2042] = new DateOnly(2042, 3, 20), // 11:53Z
            [2043] = new DateOnly(2043, 3, 20), // 17:29Z
            [2044] = new DateOnly(2044, 3, 19), // 23:20Z
            [2045] = new DateOnly(2045, 3, 20), // 05:08Z
            [2046] = new DateOnly(2046, 3, 20), // 10:58Z
            [2047] = new DateOnly(2047, 3, 20), // 16:52Z
            [2048] = new DateOnly(2048, 3, 19), // 22:34Z
            [2049] = new DateOnly(2049, 3, 20), // 04:28Z
            [2050] = new DateOnly(2050, 3, 20), // 10:20Z
            [2051] = new DateOnly(2051, 3, 20), // 15:58Z
            [2052] = new DateOnly(2052, 3, 19), // 21:56Z
            [2053] = new DateOnly(2053, 3, 20), // 03:46Z
            [2054] = new DateOnly(2054, 3, 20), // 09:35Z
            [2055] = new DateOnly(2055, 3, 20), // 15:28Z
            [2056] = new DateOnly(2056, 3, 19), // 21:11Z
            [2057] = new DateOnly(2057, 3, 20), // 03:08Z
            [2058] = new DateOnly(2058, 3, 20), // 09:04Z
            [2059] = new DateOnly(2059, 3, 20), // 14:44Z
            [2060] = new DateOnly(2060, 3, 19), // 20:37Z
            [2061] = new DateOnly(2061, 3, 20), // 02:26Z
            [2062] = new DateOnly(2062, 3, 20), // 08:07Z
            [2063] = new DateOnly(2063, 3, 20), // 13:59Z
            [2064] = new DateOnly(2064, 3, 19), // 19:40Z
            [2065] = new DateOnly(2065, 3, 20), // 01:27Z
            [2066] = new DateOnly(2066, 3, 20), // 07:19Z
            [2067] = new DateOnly(2067, 3, 20), // 12:55Z
            [2068] = new DateOnly(2068, 3, 19), // 18:51Z
            [2069] = new DateOnly(2069, 3, 20), // 00:44Z
            [2070] = new DateOnly(2070, 3, 20), // 06:35Z
            [2071] = new DateOnly(2071, 3, 20), // 12:36Z
            [2072] = new DateOnly(2072, 3, 19), // 18:19Z
            [2073] = new DateOnly(2073, 3, 20), // 00:12Z
            [2074] = new DateOnly(2074, 3, 20), // 06:09Z
            [2075] = new DateOnly(2075, 3, 20), // 11:48Z
            [2076] = new DateOnly(2076, 3, 19), // 17:37Z
            [2077] = new DateOnly(2077, 3, 19), // 23:30Z
            [2078] = new DateOnly(2078, 3, 20), // 05:11Z
            [2079] = new DateOnly(2079, 3, 20), // 11:03Z
            [2080] = new DateOnly(2080, 3, 19), // 16:43Z
            [2081] = new DateOnly(2081, 3, 19), // 22:34Z
            [2082] = new DateOnly(2082, 3, 20), // 04:32Z
            [2083] = new DateOnly(2083, 3, 20), // 10:08Z
            [2084] = new DateOnly(2084, 3, 19), // 15:58Z
            [2085] = new DateOnly(2085, 3, 19), // 21:53Z
            [2086] = new DateOnly(2086, 3, 20), // 03:36Z
            [2087] = new DateOnly(2087, 3, 20), // 09:27Z
            [2088] = new DateOnly(2088, 3, 19), // 15:16Z
            [2089] = new DateOnly(2089, 3, 19), // 21:07Z
            [2090] = new DateOnly(2090, 3, 20), // 03:03Z
            [2091] = new DateOnly(2091, 3, 20), // 08:40Z
            [2092] = new DateOnly(2092, 3, 19), // 14:33Z
            [2093] = new DateOnly(2093, 3, 19), // 20:35Z
            [2094] = new DateOnly(2094, 3, 20), // 02:20Z
            [2095] = new DateOnly(2095, 3, 20), // 08:14Z
            [2096] = new DateOnly(2096, 3, 19), // 14:03Z
            [2097] = new DateOnly(2097, 3, 19), // 19:49Z
            [2098] = new DateOnly(2098, 3, 20), // 01:38Z
            [2099] = new DateOnly(2099, 3, 20), // 07:17Z
            [2100] = new DateOnly(2100, 3, 20), // 13:04Z
        };

        // https://jaysage.org/Passover_Dates.pdf, some confirmed via https://duckduckgo.com/?q=passover+date+NNNN
        public static readonly Dictionary<int, DateOnly> Passover = new()
        {
            [2023] = new DateOnly(2023, 4, 1).AddDays(5),
            [2024] = new DateOnly(2024, 4, 1).AddDays(22),
            [2025] = new DateOnly(2025, 4, 1).AddDays(12),
            [2026] = new DateOnly(2026, 4, 1).AddDays(1),
            [2027] = new DateOnly(2027, 4, 1).AddDays(21),
            [2028] = new DateOnly(2028, 4, 1).AddDays(10),
            [2029] = new DateOnly(2029, 4, 1).AddDays(-2),
            [2030] = new DateOnly(2030, 4, 1).AddDays(17),
            [2031] = new DateOnly(2031, 4, 1).AddDays(7),
            [2032] = new DateOnly(2032, 4, 1).AddDays(-6),
            [2033] = new DateOnly(2033, 4, 1).AddDays(13),
            [2034] = new DateOnly(2034, 4, 1).AddDays(3),
            [2035] = new DateOnly(2035, 4, 1).AddDays(23),
            [2036] = new DateOnly(2036, 4, 1).AddDays(11),
            [2037] = new DateOnly(2037, 4, 1).AddDays(-2),
            [2038] = new DateOnly(2038, 4, 1).AddDays(19),
            [2039] = new DateOnly(2039, 4, 1).AddDays(8),
            [2040] = new DateOnly(2040, 4, 1).AddDays(-4),
            [2041] = new DateOnly(2041, 4, 1).AddDays(15),
            [2042] = new DateOnly(2042, 4, 1).AddDays(4),
            [2043] = new DateOnly(2043, 4, 1).AddDays(24),
            [2044] = new DateOnly(2044, 4, 1).AddDays(11),
            [2045] = new DateOnly(2045, 4, 1).AddDays(1),
            [2046] = new DateOnly(2046, 4, 1).AddDays(20),
            [2047] = new DateOnly(2047, 4, 1).AddDays(10),
            [2048] = new DateOnly(2048, 4, 1).AddDays(-4),
            [2049] = new DateOnly(2049, 4, 1).AddDays(16),
            [2050] = new DateOnly(2050, 4, 1).AddDays(6),
        };

        // from e.g. https://www.theholidayspot.com/diwali/calendar.htm
        public static readonly Dictionary<int, DateOnly> Diwali = new()
        {
            [2024] = new DateOnly(2024, 11, 1),
            [2025] = new DateOnly(2025, 10, 21),
            [2026] = new DateOnly(2026, 11, 8),
            [2027] = new DateOnly(2027, 10, 29),
            [2028] = new DateOnly(2028, 10, 17),
            [2029] = new DateOnly(2029, 11, 5),
            [2030] = new DateOnly(2030, 10, 26),
            [2031] = new DateOnly(2031, 11, 14),
            [2032] = new DateOnly(2032, 11, 2),
            [2033] = new DateOnly(2033, 10, 22),
            [2034] = new DateOnly(2034, 11, 10),
            [2035] = new DateOnly(2035, 10, 20),
            [2036] = new DateOnly(2036, 10, 19),
            [2037] = new DateOnly(2037, 11, 7),
            [2038] = new DateOnly(2038, 10, 27),
            [2039] = new DateOnly(2039, 10, 17),
            [2040] = new DateOnly(2040, 11, 4),
        };

        // from https://github.com/00-Evan/shattered-pixel-dungeon/blob/master/core/src/main/java/com/shatteredpixel/shatteredpixeldungeon/utils/Holiday.java
        public static readonly Dictionary<int, DateOnly> LunarNewYear = new()
        {
            [2024] = new DateOnly(2024, 1, 1).AddDays(31 + 10 - 1),
            [2025] = new DateOnly(2025, 1, 1).AddDays(29 - 1),
            [2026] = new DateOnly(2026, 1, 1).AddDays(31 + 17 - 1),
            [2027] = new DateOnly(2027, 1, 1).AddDays(31 + 6 - 1),
            [2028] = new DateOnly(2028, 1, 1).AddDays(26 - 1),
            [2029] = new DateOnly(2029, 1, 1).AddDays(31 + 13 - 1),
            [2030] = new DateOnly(2030, 1, 1).AddDays(31 + 3 - 1),
            [2031] = new DateOnly(2031, 1, 1).AddDays(23 - 1),
            [2032] = new DateOnly(2032, 1, 1).AddDays(31 + 11 - 1),
            [2033] = new DateOnly(2033, 1, 1).AddDays(31 - 1),
            [2034] = new DateOnly(2034, 1, 1).AddDays(31 + 19 - 1),
            [2035] = new DateOnly(2035, 1, 1).AddDays(31 + 8 - 1),
            [2036] = new DateOnly(2036, 1, 1).AddDays(28 - 1),
            [2037] = new DateOnly(2037, 1, 1).AddDays(31 + 15 - 1),
            [2038] = new DateOnly(2038, 1, 1).AddDays(31 + 4 - 1),
            [2039] = new DateOnly(2039, 1, 1).AddDays(24 - 1),
            [2040] = new DateOnly(2040, 1, 1).AddDays(31 + 12 - 1),
            [2041] = new DateOnly(2041, 1, 1).AddDays(31 + 1 - 1),
            [2042] = new DateOnly(2042, 1, 1).AddDays(22 - 1),
            [2043] = new DateOnly(2043, 1, 1).AddDays(31 + 10 - 1),
            [2044] = new DateOnly(2044, 1, 1).AddDays(30 - 1),
            [2045] = new DateOnly(2045, 1, 1).AddDays(31 + 17 - 1),
            [2046] = new DateOnly(2046, 1, 1).AddDays(31 + 6 - 1),
            [2047] = new DateOnly(2047, 1, 1).AddDays(26 - 1),
            [2048] = new DateOnly(2048, 1, 1).AddDays(31 + 14 - 1),
            [2049] = new DateOnly(2049, 1, 1).AddDays(31 + 2 - 1),
            [2050] = new DateOnly(2050, 1, 1).AddDays(23 - 1),
            [2051] = new DateOnly(2051, 1, 1).AddDays(31 + 11 - 1),
            [2052] = new DateOnly(2052, 1, 1).AddDays(31 + 1 - 1),
            [2053] = new DateOnly(2053, 1, 1).AddDays(31 + 19 - 1),
            [2054] = new DateOnly(2054, 1, 1).AddDays(31 + 8 - 1),
            [2055] = new DateOnly(2055, 1, 1).AddDays(28 - 1),
            [2056] = new DateOnly(2056, 1, 1).AddDays(31 + 15 - 1),
            [2057] = new DateOnly(2057, 1, 1).AddDays(31 + 4 - 1),
            [2058] = new DateOnly(2058, 1, 1).AddDays(24 - 1),
            [2059] = new DateOnly(2059, 1, 1).AddDays(31 + 12 - 1),
            [2060] = new DateOnly(2060, 1, 1).AddDays(31 + 2 - 1),
            [2061] = new DateOnly(2061, 1, 1).AddDays(21 - 1),
            [2062] = new DateOnly(2062, 1, 1).AddDays(31 + 9 - 1),
            [2063] = new DateOnly(2063, 1, 1).AddDays(29 - 1),
            [2064] = new DateOnly(2064, 1, 1).AddDays(31 + 17 - 1),
            [2065] = new DateOnly(2065, 1, 1).AddDays(31 + 5 - 1),
            [2066] = new DateOnly(2066, 1, 1).AddDays(26 - 1),
            [2067] = new DateOnly(2067, 1, 1).AddDays(31 + 14 - 1),
            [2068] = new DateOnly(2068, 1, 1).AddDays(31 + 3 - 1),
            [2069] = new DateOnly(2069, 1, 1).AddDays(23 - 1),
            [2070] = new DateOnly(2070, 1, 1).AddDays(31 + 11 - 1),
            [2071] = new DateOnly(2071, 1, 1).AddDays(31 - 1),
            [2072] = new DateOnly(2072, 1, 1).AddDays(31 + 19 - 1),
            [2073] = new DateOnly(2073, 1, 1).AddDays(31 + 7 - 1),
            [2074] = new DateOnly(2074, 1, 1).AddDays(27 - 1),
            [2075] = new DateOnly(2075, 1, 1).AddDays(31 + 15 - 1),
            [2076] = new DateOnly(2076, 1, 1).AddDays(31 + 5 - 1),
            [2077] = new DateOnly(2077, 1, 1).AddDays(24 - 1),
            [2078] = new DateOnly(2078, 1, 1).AddDays(31 + 12 - 1),
            [2079] = new DateOnly(2079, 1, 1).AddDays(31 + 2 - 1),
            [2080] = new DateOnly(2080, 1, 1).AddDays(22 - 1),
            [2081] = new DateOnly(2081, 1, 1).AddDays(31 + 9 - 1),
            [2082] = new DateOnly(2082, 1, 1).AddDays(29 - 1),
            [2083] = new DateOnly(2083, 1, 1).AddDays(31 + 17 - 1),
            [2084] = new DateOnly(2084, 1, 1).AddDays(31 + 6 - 1),
            [2085] = new DateOnly(2085, 1, 1).AddDays(26 - 1),
            [2086] = new DateOnly(2086, 1, 1).AddDays(31 + 14 - 1),
            [2087] = new DateOnly(2087, 1, 1).AddDays(31 + 3 - 1),
            [2088] = new DateOnly(2088, 1, 1).AddDays(24 - 1),
            [2089] = new DateOnly(2089, 1, 1).AddDays(31 + 10 - 1),
            [2090] = new DateOnly(2090, 1, 1).AddDays(30 - 1),
            [2091] = new DateOnly(2091, 1, 1).AddDays(31 + 18 - 1),
            [2092] = new DateOnly(2092, 1, 1).AddDays(31 + 7 - 1),
            [2093] = new DateOnly(2093, 1, 1).AddDays(27 - 1),
            [2094] = new DateOnly(2094, 1, 1).AddDays(31 + 15 - 1),
            [2095] = new DateOnly(2095, 1, 1).AddDays(31 + 5 - 1),
            [2096] = new DateOnly(2096, 1, 1).AddDays(25 - 1),
            [2097] = new DateOnly(2097, 1, 1).AddDays(31 + 12 - 1),
            [2098] = new DateOnly(2098, 1, 1).AddDays(31 + 1 - 1),
            [2099] = new DateOnly(2099, 1, 1).AddDays(21 - 1),
            [2100] = new DateOnly(2100, 1, 1).AddDays(31 + 9 - 1),
        };
    }
}

public static class HolidaysExtensions
{
    public static float GetChance(this HolidayDates.Holidays holiday) => holiday switch
    {
        HolidayDates.Holidays.DiaDeLosMuertos => 0.5f,
        HolidayDates.Holidays.Diwali => 0.2f,
        HolidayDates.Holidays.PrideDay => 0.333f,
        HolidayDates.Holidays.Passover => 0.2f,
        _ => 1f,
    };

    public static HolidayDates.DateRange GetByYear(this HolidayDates.Holidays holiday, int year) => holiday switch
    {
        HolidayDates.Holidays.Easter => EasterByYear(year),
        HolidayDates.Holidays.Samhain => HolidayDates.DateRange.Of(year, 10, 31),
        HolidayDates.Holidays.Xmas => HolidayDates.DateRange.Of(year, 12, 24, 4),
        // https://en.wikipedia.org/wiki/Day_of_the_Dead
        HolidayDates.Holidays.DiaDeLosMuertos => HolidayDates.DateRange.Of(year, 11, 1, 2),
        // https://en.m.wikipedia.org/wiki/Advent:
        // "Advent begins with First Vespers (Evening Prayer I) of the Sunday that falls on or closest to 30 November"
        HolidayDates.Holidays.YuleGoat => HolidayDates.DateRange.Of(HolidayDates.ClosestWeekday(new DateOnly(year, 11, 30), DayOfWeek.Sunday)),
        HolidayDates.Holidays.Qingming => QingmingByYear(year),
        HolidayDates.Holidays.Diwali => HolidayDates.Tables.Diwali.TryGetValue(year, out DateOnly diwali)
            ? HolidayDates.DateRange.Of(diwali.AddDays(-2), 5)
            : HolidayDates.DateRange.Never,
        HolidayDates.Holidays.LunarNewYear => HolidayDates.Tables.LunarNewYear.TryGetValue(year, out DateOnly newYear)
            ? HolidayDates.DateRange.Of(newYear)
            : HolidayDates.DateRange.Never,
        HolidayDates.Holidays.AprilFoolsDay => HolidayDates.DateRange.Of(year, 4, 1),
        // https://en.wikipedia.org/wiki/LGBT_pride
        // Actually, let's not make this a month. Beginning on original Christopher Street is fine IMO.
        HolidayDates.Holidays.PrideDay => HolidayDates.DateRange.Of(year, 6, 28, 3),
        HolidayDates.Holidays.TowelDay => HolidayDates.DateRange.Of(year, 5, 25),
        // First commit according to github
        HolidayDates.Holidays.UncivBirthday => HolidayDates.DateRange.Of(year, 11, 21),
        HolidayDates.Holidays.Friday13th => Friday13thByYear(year),
        HolidayDates.Holidays.StarWarsDay => HolidayDates.DateRange.Of(year, 5, 4),
        // Should start at sundown the day before - we simplify that away
        HolidayDates.Holidays.Passover => HolidayDates.Tables.Passover.TryGetValue(year, out DateOnly passover)
            ? HolidayDates.DateRange.Of(passover.AddDays(-2), 5)
            : HolidayDates.DateRange.Never,
        _ => throw new ArgumentOutOfRangeException(nameof(holiday), holiday, null),
    };

    public static HolidayDates.Holidays? SafeValueOf(string name) =>
        Enum.GetValues<HolidayDates.Holidays>().Select(h => (HolidayDates.Holidays?)h).FirstOrDefault(h => h.ToString() == name);

    private static HolidayDates.DateRange EasterByYear(int year)
    {
        // https://en.wikipedia.org/wiki/Date_of_Easter
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int g = ((8 * b) + 13) / 25;
        int h = ((19 * a) + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        int m = (a + (11 * h) + (19 * l)) / 433;
        int n = (h + l - (7 * m) + 90) / 25;
        int p = (h + l - (7 * m) + (33 * n) + 19) % 32;
        var sunday = new DateOnly(year, n, p);
        return HolidayDates.DateRange.Of(sunday.AddDays(-2), 4);
    }

    // https://en.wikipedia.org/wiki/Qingming_Festival
    // "it falls on the first day of the fifth solar term (also called Qingming) of the traditional Chinese lunisolar calendar.
    // This makes it the 15th day after the Spring Equinox, either 4, 5 or 6 April in a given year"
    private static HolidayDates.DateRange QingmingByYear(int year) =>
        HolidayDates.Tables.Equinoxes.TryGetValue(year, out DateOnly springEquinox)
            ? HolidayDates.DateRange.Of(springEquinox.AddDays(15))
            : HolidayDates.DateRange.Never;

    // Several a year are possible, but our HolidayDates.DateRange format won't support that directly - choose one randomly
    private static HolidayDates.DateRange Friday13thByYear(int year)
    {
        List<DateOnly> candidates = Enumerable.Range(1, 12)
            .Select(month => new DateOnly(year, month, 13))
            .Where(date => date.DayOfWeek == DayOfWeek.Friday)
            .ToList();
        if (candidates.Count == 0)
        {
            return HolidayDates.DateRange.Never;
        }

        // kotlin.random.Random(Int) is the same generator as GameRandom(Int); randomOrNull draws nextInt(size)
        var random = new GameRandom(year);
        return HolidayDates.DateRange.Of(candidates[random.NextInt(candidates.Count)]);
    }
}
