using System.Globalization;

namespace Unciv.Assets.Localization;

/// <summary>The writing systems the client has to find a font for. <see cref="Common"/> is digits, punctuation and symbols shared by all of them.</summary>
public enum TextScript
{
    Common,
    Latin,
    Cyrillic,
    Greek,
    Han,
    Kana,
    Hangul,
    Arabic,
    Hebrew,
    Thai,
    Devanagari,
    Bengali,
    Other,
}

/// <summary>Maps a code point to its <see cref="TextScript"/> by Unicode block; only the scripts the client ships fonts for are told apart.</summary>
public static class ScriptClassifier
{
    // Ordered, non-overlapping ranges, searched linearly (the table is tiny).
    private static readonly (int From, int To, TextScript Script)[] Ranges =
    [
        (0x0041, 0x005A, TextScript.Latin), (0x0061, 0x007A, TextScript.Latin),
        (0x00AA, 0x00AA, TextScript.Latin), (0x00BA, 0x00BA, TextScript.Latin), (0x00C0, 0x024F, TextScript.Latin),
        (0x0250, 0x02FF, TextScript.Latin), // IPA extensions and spacing modifier letters (the okina and the apostrophe letter of Lithuanian and Navajo)
        (0x0370, 0x03FF, TextScript.Greek), (0x1F00, 0x1FFF, TextScript.Greek),
        (0x0400, 0x052F, TextScript.Cyrillic), (0x1C80, 0x1C8F, TextScript.Cyrillic), (0x2DE0, 0x2DFF, TextScript.Cyrillic), (0xA640, 0xA69F, TextScript.Cyrillic),
        (0x0590, 0x05FF, TextScript.Hebrew), (0xFB1D, 0xFB4F, TextScript.Hebrew),
        (0x0600, 0x06FF, TextScript.Arabic), (0x0750, 0x077F, TextScript.Arabic), (0x08A0, 0x08FF, TextScript.Arabic), (0xFB50, 0xFDFF, TextScript.Arabic), (0xFE70, 0xFEFF, TextScript.Arabic),
        (0x0900, 0x097F, TextScript.Devanagari),
        (0x0980, 0x09FF, TextScript.Bengali),
        (0x0E00, 0x0E7F, TextScript.Thai),
        (0x1100, 0x11FF, TextScript.Hangul), (0x3130, 0x318F, TextScript.Hangul), (0xA960, 0xA97F, TextScript.Hangul), (0xAC00, 0xD7FF, TextScript.Hangul),
        (0x1E00, 0x1EFF, TextScript.Latin), // Latin extended additional (Vietnamese)
        (0x2C60, 0x2C7F, TextScript.Latin), (0xA720, 0xA7FF, TextScript.Latin),
        (0x3040, 0x30FF, TextScript.Kana), (0x31F0, 0x31FF, TextScript.Kana),
        (0x2E80, 0x2FDF, TextScript.Han), (0x3400, 0x4DBF, TextScript.Han), (0x4E00, 0x9FFF, TextScript.Han), (0xF900, 0xFAFF, TextScript.Han), (0x20000, 0x2FA1F, TextScript.Han),
        (0x3000, 0x303F, TextScript.Han), (0xFF00, 0xFFEF, TextScript.Han), // CJK punctuation and full-width forms
    ];

    /// <summary>Unciv's own icon font puts the Maya numerals (B'ak'tun, K'atun, Tun) on three Sinhala letters; they are icons, not Sinhala text, so they count as <see cref="TextScript.Common"/> and are never required of a text font.</summary>
    private static bool IsMayaNumeralIcon(int codePoint) => codePoint is >= 0x0DB8 and <= 0x0DBA;

    public static TextScript Of(int codePoint)
    {
        if (IsMayaNumeralIcon(codePoint)) return TextScript.Common;
        foreach (var (from, to, script) in Ranges)
        {
            if (codePoint >= from && codePoint <= to) return script;
        }
        return codePoint <= 0x7F || IsCommon(codePoint) ? TextScript.Common : TextScript.Other;
    }

    private static bool IsCommon(int codePoint)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
        return category is not (UnicodeCategory.OtherLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.UppercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter);
    }

    /// <summary>
    /// The code points a font must be able to show for a text to read correctly: letters, marks and digits. Symbols, punctuation, spaces and controls are
    /// excluded because Unciv's strings also carry icon symbols the original game draws from its own icon font; those are the icon system's job (C007), not a script's.
    /// </summary>
    public static IEnumerable<int> RequiredCodePoints(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            int cp = char.ConvertToUtf32(text, i);
            if (char.IsHighSurrogate(text[i])) i++;
            if (IsMayaNumeralIcon(cp)) continue;
            var category = CharUnicodeInfo.GetUnicodeCategory(cp);
            if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                or UnicodeCategory.DecimalDigitNumber)
            {
                yield return cp;
            }
        }
    }
}
