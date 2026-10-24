namespace Unciv.Models.Translations;

/// <summary>
/// <c>Char.uppercase(locale)</c> / <c>Char.lowercase(locale)</c> as the JVM does them (<c>String.valueOf(c).toUpperCase(locale)</c>):
/// the result can be several characters (<c>ß</c> -> <c>SS</c>) and depends on the locale (Turkish <c>i</c> -> <c>İ</c>).
/// Backed by <see cref="JavaLocaleData.CaseMappings"/>, which holds only the differences from <c>char.ToUpperInvariant</c>/<c>ToLowerInvariant</c>.
/// </summary>
public static class JavaCasing
{
    /// <param name="c">The character; JVM <c>Char</c> is a UTF-16 unit, so is this.</param>
    /// <param name="localeLanguage">The language subtag of the locale, such as <c>tr</c>; unknown ones behave like <c>Locale.ROOT</c>.</param>
    public static string ToUpper(char c, string localeLanguage) =>
        Table(localeLanguage).TryGetValue(c, out var mapped) ? mapped.Upper : char.ToUpperInvariant(c).ToString();

    public static string ToLower(char c, string localeLanguage) =>
        Table(localeLanguage).TryGetValue(c, out var mapped) ? mapped.Lower : char.ToLowerInvariant(c).ToString();

    private static IReadOnlyDictionary<char, (string Upper, string Lower)> Table(string localeLanguage) =>
        JavaLocaleData.CaseMappings.TryGetValue(localeLanguage, out var table) ? table : JavaLocaleData.CaseMappings[""];
}
