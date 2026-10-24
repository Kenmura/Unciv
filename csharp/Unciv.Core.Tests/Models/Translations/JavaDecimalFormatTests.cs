using System.Globalization;
using Unciv.Models.Translations;

namespace Unciv.Core.Tests.Models.Translations;

/// <summary>
/// Checks <see cref="JavaDecimalFormat"/> against <c>java.text.DecimalFormat</c> for the locale of every LocaleCode language
/// (JDK 25, CLDR), through <c>NumberFormat.getInstance(LocaleCode.getLocale(language))</c> exactly as Kotlin's
/// <c>TranslationCache.getNumberFormat</c> does. <c>NumberFormat.golden.tsv</c> rows (result characters are hex UTF-16 units, comma separated):
/// <c>N lang kind(L/S/D) value result</c> a fixed list of numbers for every language, including ties, negative zero, NaN, infinities and Long limits;
/// <c>P lang input result</c> parse then format (as <c>reformat</c> does; <c>null</c> = nothing parsed, <c>partial</c> = not everything consumed);
/// <c>D lang doubleBits result</c> and <c>L lang long result</c> a seeded sample of a much larger fuzz run (228,000 doubles over 6 languages,
/// all agreeing) with decimal ties (k/1000, k/8000), floats widened to double and random magnitudes.
/// The only known disagreement is for doubles where the JVM's historic digit generator is not shortest (1.0E23): not in the golden.
/// </summary>
[Collection("TranslationState")]
public class JavaDecimalFormatTests
{
    private static string Unhex(string hex) => hex.Length == 0 ? "" : string.Concat(hex.Split(',').Select(x => (char)Convert.ToInt32(x, 16)));

    [Fact]
    public void MatchesJvmDecimalFormat()
    {
        var failures = new List<string>();
        int checkedRows = 0;
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Models", "Translations", "NumberFormat.golden.tsv")))
        {
            string[] t = line.Split('\t');
            var format = TranslationCache.GetNumberFormatForLanguage(t[1]);
            string expected;
            string actual;
            switch (t[0])
            {
                case "N":
                    expected = Unhex(t[4]);
                    actual = t[2] switch
                    {
                        "L" => format.Format(long.Parse(t[3], CultureInfo.InvariantCulture)),
                        "S" => format.Format((double)BitConverter.Int32BitsToSingle(Convert.ToInt32(t[3], 16))),
                        _ => format.Format(BitConverter.Int64BitsToDouble(Convert.ToInt64(t[3], 16))),
                    };
                    break;
                case "D":
                    expected = Unhex(t[3]);
                    actual = format.Format(BitConverter.Int64BitsToDouble(Convert.ToInt64(t[2], 16)));
                    break;
                case "L":
                    expected = Unhex(t[3]);
                    actual = format.Format(long.Parse(t[2], CultureInfo.InvariantCulture));
                    break;
                default:
                    expected = t[3] is "null" or "partial" ? t[3] : Unhex(t[3]);
                    string input = Unhex(t[2]);
                    int index = 0;
                    object? parsed = format.Parse(input, ref index);
                    actual = parsed == null ? "null" : index == input.Length ? format.Format(parsed) : "partial";
                    break;
            }

            checkedRows++;
            if (actual != expected)
            {
                failures.Add($"{t[0]} {t[1]} {t[2]} {(t.Length > 3 ? t[3] : "")}: expected '{expected}' but was '{actual}'");
            }
        }

        Assert.True(checkedRows > 6000);
        Assert.True(failures.Count == 0, $"{failures.Count} of {checkedRows} differ, first: {string.Join("; ", failures.Take(5))}");
    }

    [Theory]
    [InlineData("English", 1234, "1,234")]
    [InlineData("English", -1234567, "-1,234,567")]
    [InlineData("Afrikaans", 1234567, "1 234 567")]
    [InlineData("German", 1234567, "1.234.567")]
    [InlineData("Toki_Pona", 1234567, "1\u00A023\u00A045\u00A067")] // grouping size 2
    public void IntegersAreGrouped(string language, long value, string expected)
    {
        Assert.Equal(expected, TranslationCache.GetNumberFormatForLanguage(language).Format(value));
        Assert.Equal(expected, ((int)value).Tr(language));
    }

    [Theory]
    [InlineData("English", 0.0625, "0.062")] // exact tie, HALF_EVEN
    [InlineData("English", 0.0625f, "0.062")]
    [InlineData("English", 0.1875, "0.188")] // exact tie, odd neighbour rounds up
    [InlineData("English", 0.0005, "0.001")] // above the tie in binary
    [InlineData("English", -0.0004, "-0")]
    [InlineData("German", 1234.5678, "1.234,568")]
    public void FractionsRoundHalfEven(string language, double value, string expected) =>
        Assert.Equal(expected, value.Tr(language));
}
