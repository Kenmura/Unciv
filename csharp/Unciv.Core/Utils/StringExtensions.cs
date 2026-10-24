using System.Globalization;

namespace Unciv.Utils;

// core/src/com/unciv/utils/StringExtensions.kt (isRunFromJar is JVM packaging, not ported)
public static class StringExtensions
{
    /// <summary>
    /// Tries to convert a string to a UUID, returning null on failure. Mirrors java.util.UUID.fromString of OpenJDK 25,
    /// which is far more lenient than the canonical form: five dash separated hex numbers of any length, an optional
    /// leading '+', Unicode decimal digits, up to 36 characters in total.
    /// </summary>
    public static JavaUuid? ToUUIDOrNull(this string text)
    {
        if (text.Length > 36) return null;
        int dash1 = text.IndexOf('-', 0);
        int dash2 = text.IndexOf('-', dash1 + 1);
        int dash3 = text.IndexOf('-', dash2 + 1);
        int dash4 = text.IndexOf('-', dash3 + 1);
        int dash5 = text.IndexOf('-', dash4 + 1);
        // For any valid input, dash1 through dash4 will be positive and dash5 negative, but it's enough to check dash4 and dash5
        if (dash4 < 0 || dash5 >= 0) return null;

        if (!TryParseHexLong(text, 0, dash1, out long p1)
            || !TryParseHexLong(text, dash1 + 1, dash2, out long p2)
            || !TryParseHexLong(text, dash2 + 1, dash3, out long p3)
            || !TryParseHexLong(text, dash3 + 1, dash4, out long p4)
            || !TryParseHexLong(text, dash4 + 1, text.Length, out long p5))
            return null;

        long mostSigBits = p1 & 0xffffffffL;
        mostSigBits <<= 16;
        mostSigBits |= p2 & 0xffffL;
        mostSigBits <<= 16;
        mostSigBits |= p3 & 0xffffL;
        long leastSigBits = p4 & 0xffffL;
        leastSigBits <<= 48;
        leastSigBits |= p5 & 0xffffffffffffL;
        return new JavaUuid(mostSigBits, leastSigBits);
    }

    /// <summary>Checks if a string is a valid UUID in the sense of <see cref="ToUUIDOrNull"/>.</summary>
    public static bool IsUUID(this string text) => text.ToUUIDOrNull() != null;

    // Long.parseLong(text, begin, end, 16): optional sign, at least one digit, overflow is an error
    private static bool TryParseHexLong(string text, int begin, int end, out long result)
    {
        result = 0;
        if (begin >= end) return false;
        int i = begin;
        if (text[i] == '+' || text[i] == '-') // '-' cannot occur here (it is the separator), '+' can
        {
            if (text[i] == '-' || end - begin == 1) return false;
            i++;
        }
        for (; i < end; i++)
        {
            int digit = HexDigit(text[i]);
            if (digit < 0 || result > (long.MaxValue - digit) / 16) return false;
            result = result * 16 + digit;
        }
        return true;
    }

    // Character.digit(ch, 16): ASCII, fullwidth Latin letters, and any Unicode decimal digit
    private static int HexDigit(char c)
    {
        if (c is >= '0' and <= '9') return c - '0';
        if (c is >= 'a' and <= 'f') return c - 'a' + 10;
        if (c is >= 'A' and <= 'F') return c - 'A' + 10;
        if (c is >= 'Ａ' and <= 'Ｆ') return c - 'Ａ' + 10;
        if (c is >= 'ａ' and <= 'ｆ') return c - 'ａ' + 10;
        if (char.GetUnicodeCategory(c) == UnicodeCategory.DecimalDigitNumber)
        {
            int value = CharUnicodeInfo.GetDecimalDigitValue(c);
            if (value >= 0 && value < 16) return value;
        }
        return -1;
    }

    /// <summary>
    /// Kotlin <c>Char.isWhitespace()</c> on the JVM: <c>Character.isWhitespace(c) || Character.isSpaceChar(c)</c>, that is space, line and paragraph
    /// separators (including the no-break ones), the ASCII controls TAB, LF, VT, FF, CR, and FS, GS, RS, US. Unlike <c>char.IsWhiteSpace</c>
    /// it does not include U+0085, and it does include U+001C..U+001F.
    /// </summary>
    public static bool IsKotlinWhitespace(this char c) =>
        c is '\t' or '\n' or '\v' or '\f' or '\r' or (>= '\u001C' and <= '\u001F')
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    /// <summary>Kotlin <c>String.trimStart()</c>.</summary>
    public static string KotlinTrimStart(this string text)
    {
        int start = 0;
        while (start < text.Length && text[start].IsKotlinWhitespace())
        {
            start++;
        }

        return text[start..];
    }

    /// <summary>Kotlin <c>CharSequence.isBlank()</c>.</summary>
    public static bool IsKotlinBlank(this string text) => text.All(IsKotlinWhitespace);
}
