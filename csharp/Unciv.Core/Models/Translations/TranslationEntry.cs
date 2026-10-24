namespace Unciv.Models.Translations;

/// <summary>
/// One translatable string.
/// </summary>
/// <param name="entry">
/// Original translatable string as defined in the game,
/// including [placeholders] or {subsentences}.
/// </param>
public sealed class TranslationEntry(string entry) : Dictionary<string, string>(StringComparer.Ordinal)
{
    public string Entry { get; } = entry;
}
