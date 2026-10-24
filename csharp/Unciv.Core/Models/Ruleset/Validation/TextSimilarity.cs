using Unciv.Utils;

namespace Unciv.Models.Ruleset.Validation;

// Ported from core/src/com/unciv/models/ruleset/validation/TextSimilarity.kt (top-level functions getTextDistance and getRelativeTextDistance).

/// <summary>
/// Approximate string distance used by the ruleset validator for misspelling suggestions. Algorithm:
///  - Keep an index for each string.
///  - Iteratively advance by one character in each string.
///      - If the characters at the indices differ, pause: find the minimum number of characters to skip in the first string to find the current
///        character of the second, and vice versa. If neither can be found, skip both by one and continue.
///        Otherwise skip ahead in whichever string requires the lower offset.
///  - Stop when either string has ended. The distance is the approximate number of characters skipped plus half the unaccounted-for tail.
/// </summary>
public static class TextSimilarity
{
    /// <summary>Kotlin <c>getTextDistance</c>. Comparisons are on UTF-16 chars, as in Kotlin; <c>lowercaseChar</c> is the invariant lower-casing of a char.</summary>
    public static int GetTextDistance(string text1, string text2)
    {
        int dist = 0;
        int i1 = 0;
        int i2 = 0;

        while (i1 < text1.Length && i2 < text2.Length)
        {
            char char1 = text1[i1];
            char char2 = text2[i2];
            if (char1 == char2)
            {
                i1++;
                i2++;
            }
            else if (char.ToLowerInvariant(char1) == char.ToLowerInvariant(char2))
            {
                dist++;
                i1++;
                i2++;
            }
            else
            {
                int? firstMatchIndex1 = FirstIndexOf(text1, i1, char2);
                int? firstMatchIndex2 = FirstIndexOf(text2, i2, char1);
                if (firstMatchIndex1 == null && firstMatchIndex2 == null)
                {
                    dist++;
                    i1++;
                    i2++;
                    continue;
                }
                int? firstMatchOffset1 = firstMatchIndex1 - i1;
                int? firstMatchOffset2 = firstMatchIndex2 - i2;
                // Preferential behaviour when the offsets are equal does make the operation slightly non-commutative.
                if (firstMatchOffset2 == null || (firstMatchOffset1 != null && firstMatchOffset1 < firstMatchOffset2))
                {
                    dist += firstMatchOffset1.NotNull();
                    i1 = firstMatchIndex1.NotNull() + 1;
                    i2++;
                }
                else if (firstMatchOffset1 == null || firstMatchOffset1 >= firstMatchOffset2)
                {
                    dist += firstMatchOffset2.Value;
                    i1++;
                    i2 = firstMatchIndex2.NotNull() + 1;
                }
                else
                {
                    throw new InvalidOperationException($"Can't compare Strings:\n\t{text1}\n\t{text2}");
                }
            }
        }
        dist += (text1.Length - i1 + (text2.Length - i2)) / 2;
        return dist;
    }

    private static int? FirstIndexOf(string text, int start, char c)
    {
        for (int i = start; i < text.Length; i++)
            if (text[i] == c) return i;
        return null;
    }

    /// <summary>
    /// Kotlin <c>getRelativeTextDistance</c>: the <see cref="GetTextDistance"/> of two strings relative to their average length.
    /// The original algorithm is very weak to short strings with errors at the start, so it runs twice, once with the strings reversed.
    /// </summary>
    public static double GetRelativeTextDistance(string text1, string text2)
    {
        double TextDistance(string a, string b) => (double)GetTextDistance(a, b) / (text1.Length + text2.Length) * 2.0;
        return Math.Min(TextDistance(text1, text2), TextDistance(Reversed(text1), Reversed(text2)));
    }

    private static string Reversed(string text)
    {
        char[] chars = text.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }
}
