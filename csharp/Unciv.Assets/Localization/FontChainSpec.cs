namespace Unciv.Assets.Localization;

/// <summary>One font file of the fallback chain and the scripts it is there for.</summary>
public sealed record FontChainEntry(string File, string Family, IReadOnlyList<TextScript> Scripts);

/// <summary>
/// The ordered fallback chain behind the registry key <c>font/default</c>. The engine's built-in font comes first (Latin, Greek, Cyrillic and most of Vietnamese,
/// so English text and every existing golden look exactly as before); these files are tried in order for any glyph it lacks. All are Noto fonts under the SIL OFL (ASSETS.md).
/// Order matters only where scripts share code points; CJK comes after the alphabetic scripts so a Latin letter missing from the built-in font is not drawn in a CJK face.
/// </summary>
public static class FontChainSpec
{
    public static IReadOnlyList<FontChainEntry> Entries { get; } =
    [
        new("NotoSansArabic.ttf", "Noto Sans Arabic", [TextScript.Arabic]),
        new("NotoSansHebrew.ttf", "Noto Sans Hebrew", [TextScript.Hebrew]),
        new("NotoSansThai.ttf", "Noto Sans Thai", [TextScript.Thai]),
        new("NotoSansDevanagari.ttf", "Noto Sans Devanagari", [TextScript.Devanagari]),
        new("NotoSansBengali.ttf", "Noto Sans Bengali", [TextScript.Bengali]),
        new("NotoSans.ttf", "Noto Sans", [TextScript.Latin, TextScript.Cyrillic, TextScript.Greek]),
        new("NotoSansCJK-subset.otf", "Noto Sans CJK SC (subset)", [TextScript.Han, TextScript.Kana, TextScript.Hangul]),
    ];

    /// <summary>The scripts some entry covers.</summary>
    public static IEnumerable<TextScript> CoveredScripts => Entries.SelectMany(e => e.Scripts).Distinct();

    /// <summary>The entry that is there for <paramref name="script"/>, or null (Common has none: the built-in font draws it).</summary>
    public static FontChainEntry? For(TextScript script) => Entries.FirstOrDefault(e => e.Scripts.Contains(script));
}
