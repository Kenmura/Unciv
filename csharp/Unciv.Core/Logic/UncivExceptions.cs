using Unciv.Models.Translations;

namespace Unciv.Logic;

// core/src/com/unciv/logic/UncivExceptions.kt

/// <summary>
/// An exception marking an error as suitable to be shown to the user. <c>Message</c> is the untranslated error text.
/// </summary>
public class UncivShowableException(string errorText, Exception? cause = null) : Exception(errorText, cause)
{
    /// <summary>The translated message (Kotlin <c>getLocalizedMessage() = message.tr()</c>).</summary>
    public string GetLocalizedMessage() => Message.Tr();
}

/// <summary>
/// A game or map cannot be loaded because mods are missing. The message lists only the first five names;
/// <see cref="MissingMods"/> keeps all of them.
/// </summary>
public class MissingModsException(IEnumerable<string> missingMods) : UncivShowableException($"Missing mods: [{Shorten(missingMods)}]")
{
    public IEnumerable<string> MissingMods { get; } = missingMods;

    // Kotlin joinToString(limit = 5): separator ", ", then ", ..." when there are more
    private static string Shorten(IEnumerable<string> missingMods)
    {
        var shown = new List<string>();
        bool truncated = false;
        foreach (string mod in missingMods)
        {
            if (shown.Count == 5) { truncated = true; break; }
            shown.Add(mod);
        }
        return string.Join(", ", shown) + (truncated ? ", ..." : "");
    }
}

/// <summary>The nation a save needs is not in the loaded rulesets; <paramref name="modNames"/> is insertion ordered (a LinkedHashSet in Kotlin).</summary>
public class MissingNationException(string errorText, IReadOnlyCollection<string> modNames) : UncivShowableException(errorText)
{
    public IReadOnlyCollection<string> ModNames { get; } = modNames;
}
