using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Screens.Civilopedia;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.UI.ObjectDescriptions;

// core/src/com/unciv/ui/objectdescriptions/FormattedLineListBuilder.kt
// Kotlin's `add(text = defaults().text, ...)` default arguments are nullable parameters here: null means "take it from defaults()".
// The Kotlin `Iterable` and `Sequence` overloads are one IEnumerable overload. The `context(source: IHasUniques)` receiver of addUniques is the first parameter.
// The Kotlin FormattedLine(Unique) constructor reads the current ruleset from UncivGame, so the builder is created with a CivilopediaContext.

public enum SeparatorType
{
    None,
    Space,
    Line,
}

/// <summary>
/// A builder for use in <see cref="ICivilopediaText.GetCivilopediaTextLines"/>, see <see cref="FormattedLineListBuilder.BuildCivilopediaText"/>.
/// </summary>
public interface IFormattedLineListBuilder
{
    /// <summary>Called automatically, not part of the client API</summary>
    List<FormattedLine> Build();

    /// <summary>Flexible line addition, allows all parameters the <see cref="FormattedLine"/> constructor does, but defaults each to the template set in <see cref="Defaults()"/>.</summary>
    void Add(
        string? text = null,
        string? link = null,
        string? icon = null,
        string? extraImage = null,
        float? imageSize = null,
        int? size = null,
        int? header = null,
        int? indent = null,
        float? padding = null,
        string? color = null,
        bool? separator = null,
        bool? starred = null,
        bool? centered = null,
        bool? iconCrossed = null);

    /// <summary>Add a complete <see cref="FormattedLine"/>, ignoring defaults.</summary>
    void Add(FormattedLine line);

    /// <summary>Add a line for a Unique, ignoring defaults except for indent.</summary>
    void Add(UniqueObject unique, int? indent = null);

    /// <summary>Add an image, see <see cref="FormattedLine.ExtraImage"/>.</summary>
    void Add(string extraImage, float imageSize);

    /// <summary>Add several lines, ignoring defaults.</summary>
    void Add(IEnumerable<FormattedLine> newLines);

    /// <summary>Add several lines, ignoring defaults. Each is built from one input element, transformed by <paramref name="transform"/>.</summary>
    void Add<T>(IEnumerable<T> input, Func<T, FormattedLine> transform);

    /// <summary>Add several lines for ruleset objects, only simple name and link, ignoring defaults.</summary>
    void AddObjects(IEnumerable<IRulesetObject> input) => Add(input, it => new FormattedLine(it.Name, it.MakeLink()));

    /// <summary>Add a vertical separator of type <paramref name="separator"/>. size (line thickness) and color are used for type <see cref="SeparatorType.Line"/>.</summary>
    void Add(SeparatorType separator, int? size = null, string? color = null);

    /// <summary>Add a vertical space half the height of a normal line.</summary>
    void Space();

    /// <summary>Add a horizontal line separator. size is the line thickness.</summary>
    void Separator(int? size = null, string? color = null);

    /// <summary>
    /// Add lines for the Uniques of <paramref name="source"/>, ignoring defaults. Supports automatic links for ruleset objects mentioned in Unique parameters.
    /// </summary>
    void AddUniques(
        IHasUniques source,
        SeparatorType leadingSeparator = SeparatorType.Space,
        bool colorConsumesResources = false,
        Action? extraSeparator = null,
        Func<UniqueObject, bool>? exclude = null) =>
        AddUniques(source.UniqueObjects, leadingSeparator, colorConsumesResources, extraSeparator, exclude);

    /// <summary>
    /// Add lines for <paramref name="uniques"/>, ignoring defaults. Supports automatic links for ruleset objects mentioned in Unique parameters.
    /// </summary>
    /// <param name="uniques">The Uniques to list</param>
    /// <param name="leadingSeparator">Used only when actual uniques content follows, calls <see cref="Add(SeparatorType, int?, string?)"/>.</param>
    /// <param name="colorConsumesResources">If set, ConsumesResources Uniques get a reddish color.</param>
    /// <param name="extraSeparator">If set, this is called after the separator is added to the builder.</param>
    /// <param name="exclude">Predicate that can exclude Uniques by returning <c>true</c> (null returns <c>false</c>).</param>
    void AddUniques(
        IEnumerable<UniqueObject> uniques,
        SeparatorType leadingSeparator = SeparatorType.Space,
        bool colorConsumesResources = false,
        Action? extraSeparator = null,
        Func<UniqueObject, bool>? exclude = null);

    /// <summary>Adds a see-also list including header and separator, but does nothing if <paramref name="seeAlso"/> is empty.</summary>
    void AddSeeAlso(IEnumerable<IRulesetObject> seeAlso);

    /// <summary>Change the template default values are drawn from</summary>
    void Defaults(FormattedLine line);

    /// <summary>Retrieve the template default values are drawn from</summary>
    FormattedLine Defaults();
}

public static class FormattedLineListBuilder
{
    /// <summary>Starts a builder resulting in a <c>List&lt;FormattedLine&gt;</c></summary>
    /// <param name="context">Ruleset (and game, if any) for automatic links from Uniques.</param>
    /// <param name="block">Your code, having direct access to the builder methods.</param>
    /// <param name="defaults">An instance of <see cref="FormattedLine"/> to copy default values from. Ignored by some Add overloads.</param>
    /// <param name="capacity">Initial capacity.</param>
    public static List<FormattedLine> BuildCivilopediaText(CivilopediaContext context, Action<IFormattedLineListBuilder> block, FormattedLine? defaults = null, int capacity = 16)
    {
        var builder = new FormattedLineListBuilderImpl(context, defaults ?? new FormattedLine(), capacity);
        block(builder);
        return builder.Build();
    }
}

internal sealed class FormattedLineListBuilderImpl(CivilopediaContext context, FormattedLine defaults, int capacity) : IFormattedLineListBuilder
{
    private readonly List<FormattedLine> lines = new(capacity);
    private FormattedLine defaultsLine = defaults;

    public List<FormattedLine> Build() => lines;

    public void Add(
        string? text = null,
        string? link = null,
        string? icon = null,
        string? extraImage = null,
        float? imageSize = null,
        int? size = null,
        int? header = null,
        int? indent = null,
        float? padding = null,
        string? color = null,
        bool? separator = null,
        bool? starred = null,
        bool? centered = null,
        bool? iconCrossed = null)
    {
        var d = defaultsLine;
        lines.Add(new FormattedLine(
            text ?? d.Text, link ?? d.Link, icon ?? d.Icon, extraImage ?? d.ExtraImage, imageSize ?? d.ImageSize, size ?? d.Size,
            header ?? d.Header, indent ?? d.Indent, padding ?? d.Padding, color ?? d.Color, separator ?? d.Separator,
            starred ?? d.Starred, centered ?? d.Centered, iconCrossed ?? d.IconCrossed));
    }

    public void Add(FormattedLine line) => lines.Add(line);

    public void Add(UniqueObject unique, int? indent = null) =>
        lines.Add(new FormattedLine(unique, context, indent ?? defaultsLine.Indent));

    public void Add(string extraImage, float imageSize) =>
        lines.Add(new FormattedLine(extraImage: extraImage, imageSize: imageSize));

    public void Add(SeparatorType separator, int? size = null, string? color = null)
    {
        switch (separator)
        {
            case SeparatorType.None:
                break;
            case SeparatorType.Space:
                Space();
                break;
            case SeparatorType.Line:
                Separator(size ?? defaultsLine.Size, color ?? defaultsLine.Color);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(separator));
        }
    }

    public void Space() => lines.Add(new FormattedLine());

    public void Separator(int? size = null, string? color = null) =>
        lines.Add(new FormattedLine(separator: true, size: size ?? defaultsLine.Size, color: color ?? defaultsLine.Color));

    public void Add(IEnumerable<FormattedLine> newLines) => lines.AddRange(newLines);

    public void Add<T>(IEnumerable<T> input, Func<T, FormattedLine> transform)
    {
        foreach (var item in input)
            Add(transform(item));
    }

    public void AddUniques(
        IEnumerable<UniqueObject> uniques,
        SeparatorType leadingSeparator = SeparatorType.Space,
        bool colorConsumesResources = false,
        Action? extraSeparator = null,
        Func<UniqueObject, bool>? exclude = null)
    {
        var orderedUniques = uniques.Where(it => !(it.IsHiddenToUsers() || (exclude?.Invoke(it) ?? false)));

        var index = 0;
        foreach (var unique in orderedUniques)
        {
            if (index++ == 0)
            {
                Add(leadingSeparator);
                extraSeparator?.Invoke();
            }
            // Optionally special-case ConsumesResources to give it a reddish color. Also ensures link always points to the resource
            // (the other constructor guesses the first object by name in the Unique parameters).
            if (colorConsumesResources && unique.Type == UniqueType.ConsumesResources)
                Add(unique.GetDisplayText(), link: $"Resources/{unique.Params[1]}", color: "#F42");
            else Add(unique);
        }
    }

    public void AddSeeAlso(IEnumerable<IRulesetObject> seeAlso)
    {
        using var iterator = seeAlso.GetEnumerator();
        if (!iterator.MoveNext()) return;
        Space();
        Add("{See also}:");
        do
        {
            var item = iterator.Current;
            Add(item.Name, item.MakeLink(), indent: 1);
        }
        while (iterator.MoveNext());
    }

    public void Defaults(FormattedLine line) => defaultsLine = line;

    public FormattedLine Defaults() => defaultsLine;
}
