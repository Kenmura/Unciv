using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.UI.Screens.Civilopedia;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.UI.ObjectDescriptions;

// core/src/com/unciv/ui/objectdescriptions/DescriptionHelpers.kt
// The Kotlin `leadingSeparator: (suspend SequenceScope<FormattedLine>.() -> Unit)?` (default: yield one empty line, null: no separator) is
// a Func returning the separator lines, with `noLeadingSeparator` standing in for the Kotlin null (a C# default argument cannot be a lambda).
// The Kotlin FormattedLine(Unique) constructor reads the current ruleset from UncivGame, so these take a CivilopediaContext.

public static class DescriptionHelpers
{
    /// <summary>Stand-in for <c>UncivGame.Current.settings.getCollatorFromLocale()</c>: the context's collator, else the invariant culture's comparer.</summary>
    public static IComparer<string> GetCollator(this CivilopediaContext context) => context.Collator ?? StringComparer.InvariantCulture;

    /// <summary>
    /// Appends user-visible Uniques as translated text to a line collection. Follows json order.
    /// </summary>
    /// <param name="source">The ruleset object</param>
    /// <param name="lineList">Target collection, will be mutated. Defaults to an empty List for easier use with consumer-only client code.</param>
    /// <param name="exclude">Predicate that can exclude Uniques by returning <c>true</c> (null returns <c>false</c>).</param>
    /// <returns>the <paramref name="lineList"/> with added, translated info on the uniques - for chaining</returns>
    public static ICollection<string> UniquesToDescription(this IHasUniques source, ICollection<string>? lineList = null, Func<UniqueObject, bool>? exclude = null)
    {
        lineList ??= [];
        foreach (var unique in source.UniqueObjects)
        {
            if (unique.IsHiddenToUsers()) continue;
            if (exclude?.Invoke(unique) ?? false) continue;
            lineList.Add(unique.GetDisplayText().Tr());
        }
        return lineList;
    }

    /// <summary>A Sequence of user-visible Uniques as <see cref="FormattedLine"/>s.</summary>
    /// <param name="source">The ruleset object</param>
    /// <param name="context">Ruleset (and game, if any) for automatic links</param>
    /// <param name="leadingSeparator">Emits any kind of separator when content follows. Null: one empty line.</param>
    /// <param name="noLeadingSeparator">Set to have no separator at all (the Kotlin <c>null</c>)</param>
    /// <param name="colorConsumesResources">If set, ConsumesResources Uniques get a reddish color.</param>
    /// <param name="exclude">Predicate that can exclude Uniques by returning <c>true</c> (null returns <c>false</c>).</param>
    public static IEnumerable<FormattedLine> UniquesToCivilopediaTextLines(
        this IHasUniques source,
        CivilopediaContext context,
        Func<IEnumerable<FormattedLine>>? leadingSeparator = null,
        bool noLeadingSeparator = false,
        bool colorConsumesResources = false,
        Func<UniqueObject, bool>? exclude = null)
    {
        var orderedUniques = source.UniqueObjects
            .Where(it => !(it.IsHiddenToUsers() || (exclude?.Invoke(it) ?? false)));

        var index = 0;
        foreach (var unique in orderedUniques)
        {
            if (!noLeadingSeparator && index == 0)
            {
                if (leadingSeparator == null) yield return new FormattedLine();
                else
                    foreach (var line in leadingSeparator())
                        yield return line;
            }
            index++;
            // Optionally special-case ConsumesResources to give it a reddish color. Also ensures link always points to the resource
            // (the other constructor guesses the first object by name in the Unique parameters).
            yield return colorConsumesResources && unique.Type == UniqueType.ConsumesResources
                ? new FormattedLine(unique.GetDisplayText(), link: $"Resources/{unique.Params[1]}", color: "#F42")
                : new FormattedLine(unique, context);
        }
    }

    /// <summary>Appends user-visible Uniques as <see cref="FormattedLine"/>s to <paramref name="lineList"/>.</summary>
    public static void UniquesToCivilopediaTextLines(
        this IHasUniques source,
        ICollection<FormattedLine> lineList,
        CivilopediaContext context,
        Func<IEnumerable<FormattedLine>>? leadingSeparator = null,
        bool noLeadingSeparator = false,
        bool colorConsumesResources = false,
        Func<UniqueObject, bool>? exclude = null)
    {
        foreach (var line in source.UniquesToCivilopediaTextLines(context, leadingSeparator, noLeadingSeparator, colorConsumesResources, exclude))
            lineList.Add(line);
    }
}
