using System.Globalization;
using System.Numerics;
using System.Text;

namespace Unciv.Models.Translations;

/// <summary>
/// The subset of <c>java.text.DecimalFormat</c> that <c>TranslationCache</c> relies on, for the locale data in <see cref="JavaLocaleData"/>:
/// grouping, the locale's own digits and separators, at most 3 fraction digits, <c>HALF_EVEN</c> rounding, <c>-0</c> handling,
/// and <c>parse(String, ParsePosition)</c>.
/// </summary>
/// <remarks>
/// <c>DecimalFormat.format(double)</c> works on the shortest decimal digits of the double (<c>FloatingDecimal</c>) and uses the knowledge whether
/// those digits are exact, rounded up or truncated to decide <c>HALF_EVEN</c> ties; this class derives the same facts from the exact binary value.
/// It agrees with the JVM on all inputs tried (see <c>JavaDecimalFormatTests</c>), except for the doubles
/// where the JVM's historic digit generator does not produce the shortest digits (such as 1.0E23, which it formats as 9.999999999999999E22).
/// </remarks>
public sealed class JavaDecimalFormat
{
    private const int MaxFractionDigits = 3;

    private readonly JavaNumberSymbols symbols;

    public JavaDecimalFormat(JavaNumberSymbols symbols)
    {
        this.symbols = symbols;
    }

    public char ZeroDigit => symbols.ZeroDigit;

    public char GroupingSeparator => symbols.GroupingSeparator;

    public int GroupingSize => symbols.GroupingSize;

    public string NegativePrefix => symbols.NegativePrefix;

    public string NegativeSuffix => "";

    public string Format(long value)
    {
        bool negative = value < 0;
        BigInteger magnitude = BigInteger.Abs(value);
        string digits = magnitude.ToString(CultureInfo.InvariantCulture);
        return Assemble(negative, digits, digits.Length, true);
    }

    public string Format(double value)
    {
        if (double.IsNaN(value))
        {
            return symbols.NaN;
        }

        bool negative = value < 0.0 || (value == 0.0 && double.IsNegative(value));
        if (double.IsInfinity(value))
        {
            return (negative ? symbols.NegativePrefix : "") + "∞";
        }

        (string digits, int decimalAt) = RoundedDigits(Math.Abs(value));
        return Assemble(negative, digits, decimalAt, false);
    }

    /// <summary>
    /// <c>DecimalFormat.parse(text, pos)</c>: returns a <see cref="long"/> or a <see cref="double"/> boxed (like the JVM's Long/Double),
    /// or null if nothing could be parsed, in which case <paramref name="index"/> is unchanged.
    /// </summary>
    public object? Parse(string text, ref int index)
    {
        int pos = index;
        bool positive = true; // the positive prefix is empty, so it always matches
        if (NegativePrefix.Length > 0 && string.CompareOrdinal(text, pos, NegativePrefix, 0, NegativePrefix.Length) == 0)
        {
            positive = false; // longer than the empty positive prefix
            pos += NegativePrefix.Length;
        }

        // DigitList of the parse: significant digits (no leading zeros) and the position of the decimal point relative to them
        var digits = new StringBuilder();
        int decimalAt = 0;
        int digitCount = 0;
        bool sawDecimal = false;
        bool sawDigit = false;
        int backup = -1;
        for (; pos < text.Length; pos++)
        {
            char ch = text[pos];
            int digit = ch - symbols.ZeroDigit;
            if (digit < 0 || digit > 9)
            {
                digit = char.IsDigit(ch) ? (int)char.GetNumericValue(ch) : -1;
            }

            if (digit == 0)
            {
                backup = -1; // cancel out the backup of the grouping handler
                sawDigit = true;
                if (digits.Length == 0)
                {
                    if (!sawDecimal)
                    {
                        continue; // leading zeros in the integer part are ignored
                    }

                    decimalAt--;
                }
                else
                {
                    digitCount++;
                    digits.Append('0');
                }
            }
            else if (digit > 0 && digit <= 9)
            {
                sawDigit = true;
                digitCount++;
                digits.Append((char)('0' + digit));
                backup = -1;
            }
            else if (ch == symbols.DecimalSeparator)
            {
                if (sawDecimal)
                {
                    break;
                }

                decimalAt = digitCount; // not digits.Length, leading zeros after the point are handled above
                sawDecimal = true;
            }
            else if (ch == symbols.GroupingSeparator)
            {
                if (sawDecimal)
                {
                    break;
                }

                // Ignore grouping characters, but require that they are followed by a digit; otherwise back up
                backup = pos;
            }
            else
            {
                break;
            }
        }

        if (backup != -1)
        {
            pos = backup;
        }

        if (!sawDigit)
        {
            return null;
        }

        index = pos;
        if (!sawDecimal)
        {
            decimalAt = digitCount;
        }

        // DigitList.fitsIntoLong(isPositive, ignoreNegativeZero = false)
        string significant = digits.ToString().TrimEnd('0');
        if (significant.Length == 0)
        {
            // a negative zero can only be represented as a double
            return positive ? 0L : -0.0;
        }

        if (decimalAt >= significant.Length && decimalAt <= 19)
        {
            var integer = BigInteger.Parse(significant.PadRight(decimalAt, '0'), CultureInfo.InvariantCulture);
            if (integer <= long.MaxValue)
            {
                return positive ? (long)integer : -(long)integer;
            }

            if (!positive && integer == (BigInteger)long.MaxValue + 1)
            {
                return long.MinValue;
            }
        }

        double result = double.Parse("0." + significant + "E" + decimalAt.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return positive ? result : -result;
    }

    /// <summary>Formats the result of <see cref="Parse"/>.</summary>
    public string Format(object number) => number switch
    {
        long l => Format(l),
        double d => Format(d),
        _ => throw new ArgumentException("Expected a long or a double", nameof(number)),
    };

    // DecimalFormat's subformat for a non-negative digit list with `decimalAt` digits before the point
    private string Assemble(bool negative, string digits, int decimalAt, bool isInteger)
    {
        var sb = new StringBuilder();
        if (negative)
        {
            sb.Append(symbols.NegativePrefix);
        }

        int digitCount = digits.Length;
        if (digitCount == 0)
        {
            decimalAt = 0;
        }

        int intCount = Math.Max(1, decimalAt);
        int digitIndex = 0;
        for (int i = intCount - 1; i >= 0; i--)
        {
            if (i < decimalAt && digitIndex < digitCount)
            {
                sb.Append((char)(symbols.ZeroDigit + (digits[digitIndex++] - '0')));
            }
            else
            {
                sb.Append(symbols.ZeroDigit);
            }

            if (i > 0 && symbols.GroupingSize != 0 && i % symbols.GroupingSize == 0)
            {
                sb.Append(symbols.GroupingSeparator);
            }
        }

        bool fractionPresent = !isInteger && digitIndex < digitCount;
        if (fractionPresent)
        {
            sb.Append(symbols.DecimalSeparator);
            for (int i = 0; i < MaxFractionDigits; i++)
            {
                if (digitIndex >= digitCount)
                {
                    break;
                }

                if (-1 - i > decimalAt - 1)
                {
                    sb.Append(symbols.ZeroDigit);
                    continue;
                }

                sb.Append((char)(symbols.ZeroDigit + (digits[digitIndex++] - '0')));
            }
        }

        return sb.ToString();
    }

    // DigitList.set(isNegative, double, maximumDigits = 3, fixedPoint = true) with HALF_EVEN:
    // the significant digits (no trailing zeros, empty for zero) and the position of the decimal point relative to them
    private static (string Digits, int DecimalAt) RoundedDigits(double value)
    {
        if (value == 0.0)
        {
            return ("", 0);
        }

        (string digits, int decimalAt) = ShortestDigits(value);
        (BigInteger exactNumerator, int exactDecimalAt) = ExactDigits(value);

        // compare the shortest digit string with the exact value to find out: exact / rounded up / truncated
        int comparison = CompareDecimal(digits, decimalAt, exactNumerator.ToString(CultureInfo.InvariantCulture), exactDecimalAt);
        bool roundedUp = comparison > 0;
        bool exact = comparison == 0;

        if (-decimalAt > MaxFractionDigits)
        {
            return ("", 0);
        }

        if (-decimalAt == MaxFractionDigits)
        {
            return ShouldRoundUp(digits, 0, roundedUp, exact) ? ("1", decimalAt + 1) : ("", 0);
        }

        digits = digits.TrimEnd('0');
        if (digits.Length == 0)
        {
            return ("", 0);
        }

        int maximumDigits = MaxFractionDigits + decimalAt;
        if (maximumDigits >= 0 && maximumDigits < digits.Length)
        {
            if (ShouldRoundUp(digits, maximumDigits, roundedUp, exact))
            {
                int i = maximumDigits;
                while (--i >= 0 && digits[i] == '9')
                {
                }

                if (i < 0)
                {
                    digits = "1";
                    decimalAt++;
                }
                else
                {
                    digits = digits[..i] + (char)(digits[i] + 1);
                }
            }
            else
            {
                digits = digits[..maximumDigits].TrimEnd('0');
            }
        }
        else if (maximumDigits < 0)
        {
            return ("", 0);
        }

        return digits.Length == 0 ? ("", 0) : (digits, decimalAt);
    }

    private static bool ShouldRoundUp(string digits, int maximumDigits, bool alreadyRounded, bool valueExactAsDecimal)
    {
        if (maximumDigits >= digits.Length)
        {
            return false;
        }

        if (digits[maximumDigits] == '5')
        {
            if (maximumDigits == digits.Length - 1)
            {
                if (alreadyRounded)
                {
                    return false;
                }

                if (!valueExactAsDecimal)
                {
                    return true;
                }

                return maximumDigits > 0 && (digits[maximumDigits - 1] - '0') % 2 != 0;
            }

            for (int i = maximumDigits + 1; i < digits.Length; i++)
            {
                if (digits[i] != '0')
                {
                    return true;
                }
            }

            return false;
        }

        return digits[maximumDigits] > '5';
    }

    // Shortest digits that round-trip (.NET Core 3.0+ "R" is shortest), as (digits without trailing zeros, decimalAt)
    private static (string Digits, int DecimalAt) ShortestDigits(double value)
    {
        string shortest = value.ToString("R", CultureInfo.InvariantCulture);
        int exponent = 0;
        int ePos = shortest.IndexOfAny(['E', 'e']);
        string mantissa = shortest;
        if (ePos >= 0)
        {
            exponent = int.Parse(shortest[(ePos + 1)..], CultureInfo.InvariantCulture);
            mantissa = shortest[..ePos];
        }

        int dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        string intPart = dot >= 0 ? mantissa[..dot] : mantissa;
        string fracPart = dot >= 0 ? mantissa[(dot + 1)..] : "";
        string all = intPart + fracPart;
        int decimalAt = intPart.Length + exponent;
        int lead = 0;
        while (lead < all.Length - 1 && all[lead] == '0')
        {
            lead++;
        }

        all = all[lead..];
        decimalAt -= lead;
        return (all.TrimEnd('0'), decimalAt);
    }

    // The exact decimal expansion of a positive finite double: digits as an integer and decimalAt like above
    private static (BigInteger Digits, int DecimalAt) ExactDigits(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int exp = (int)((bits >> 52) & 0x7FF);
        long mant = bits & 0xFFFFFFFFFFFFFL;
        if (exp == 0)
        {
            exp++;
        }
        else
        {
            mant |= 1L << 52;
        }

        exp -= 1075; // value = mant * 2^exp
        BigInteger numerator;
        int scale; // value = numerator / 10^scale
        if (exp >= 0)
        {
            numerator = new BigInteger(mant) << exp;
            scale = 0;
        }
        else
        {
            numerator = new BigInteger(mant) * BigInteger.Pow(5, -exp);
            scale = -exp;
        }

        string s = numerator.ToString(CultureInfo.InvariantCulture);
        return (BigInteger.Parse(s.TrimEnd('0'), CultureInfo.InvariantCulture), s.Length - scale);
    }

    private static int CompareDecimal(string a, int aDecimalAt, string b, int bDecimalAt)
    {
        if (aDecimalAt != bDecimalAt)
        {
            return aDecimalAt.CompareTo(bDecimalAt);
        }

        int n = Math.Max(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            char ca = i < a.Length ? a[i] : '0';
            char cb = i < b.Length ? b[i] : '0';
            if (ca != cb)
            {
                return ca.CompareTo(cb);
            }
        }

        return 0;
    }
}
