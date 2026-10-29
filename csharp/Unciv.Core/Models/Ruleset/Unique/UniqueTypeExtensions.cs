using System.Collections.Immutable;
using Unciv.Models.Translations;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unique;

/// <summary>
/// Accessors for <see cref="UniqueType"/>, ported from the members of <c>UniqueType</c> in
/// <c>core/src/com/unciv/models/ruleset/unique/UniqueType.kt</c>.
/// <para>
/// Not ported: <c>getDeprecationAnnotation</c> (JVM reflection; no entry carries <c>@Deprecated</c>, deprecated
/// uniques live in <c>DeprecatedUniqueType</c>, T029) and <c>UniqueParameterErrorSeverity.getRulesetErrorSeverity</c>
/// (an extension in <c>RulesetErrorSeverityExtensions</c>, T036; the enum itself is <see cref="UniqueParameterErrorSeverity"/>).
/// </para>
/// </summary>
public static class UniqueTypeExtensions
{
    private sealed record Info(
        string Text,
        string PlaceholderText,
        IReadOnlySet<UniqueTarget> TargetTypes,
        IReadOnlySet<UniqueFlag> Flags,
        string? DocDescription,
        IReadOnlyList<IReadOnlyList<UniqueParameterType>> ParameterTypeMap);

    /// <summary>All entries in declaration (= ordinal) order, like Kotlin's <c>UniqueType.entries</c>.</summary>
    public static readonly IReadOnlyList<UniqueType> Entries = Enum.GetValues<UniqueType>();

    private static readonly ImmutableArray<Info> Infos = BuildInfos();

    /// <summary>
    /// Kotlin <c>UniqueType.uniqueTypeMap</c>: <c>entries.associateBy { it.placeholderText }</c>
    /// (a later entry replaces an earlier one with the same placeholder text; there are none today).
    /// Lookup only - never enumerate it for anything that feeds game state.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, UniqueType> UniqueTypeMap = BuildUniqueTypeMap();

    /// <summary>The unique's text with <c>[parameter]</c> placeholders, e.g. <c>[stats] [cityFilter]</c>.</summary>
    public static string Text(this UniqueType type) => Infos[(int)type].Text;

    /// <summary>Text with the parameters blanked out, e.g. <c>[] []</c>; the key of <see cref="UniqueTypeMap"/>.</summary>
    public static string PlaceholderText(this UniqueType type) => Infos[(int)type].PlaceholderText;

    /// <summary>
    /// The targets this unique type is declared for. Kotlin keeps these in a <c>HashSet</c> whose iteration order is
    /// arbitrary (identity hash of the enum constants); here it is deterministic: declaration order, duplicates dropped.
    /// </summary>
    public static IReadOnlySet<UniqueTarget> TargetTypes(this UniqueType type) => Infos[(int)type].TargetTypes;

    public static IReadOnlySet<UniqueFlag> Flags(this UniqueType type) => Infos[(int)type].Flags;

    public static string? DocDescription(this UniqueType type) => Infos[(int)type].DocDescription;

    /// <summary>
    /// A map of allowed <see cref="UniqueParameterType"/>s per parameter position: the placeholder is split at
    /// <c>/</c>, trailing digits are removed, and each part is looked up with <see cref="UniqueParameterTypeExtensions.SafeValueOf"/>;
    /// unknown parts are dropped.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<UniqueParameterType>> ParameterTypeMap(this UniqueType type) =>
        Infos[(int)type].ParameterTypeMap;

    /// <summary>
    /// Checks whether a specific <paramref name="uniqueTarget"/> as e.g. given by <c>IHasUniques.getUniqueTarget</c>
    /// works with this unique type.
    /// </summary>
    public static bool CanAcceptUniqueTarget(this UniqueType type, UniqueTarget uniqueTarget)
    {
        foreach (var target in Infos[(int)type].TargetTypes)
        {
            if (uniqueTarget.CanAcceptUniqueTarget(target)) return true;
        }
        return false;
    }

    private static ImmutableArray<Info> BuildInfos()
    {
        var rows = UniqueTypeData.Rows.AddRange(UniqueTypeDataBnw.Rows);
        if (rows.Length != Entries.Count)
            throw new InvalidOperationException($"UniqueTypeData has {rows.Length} rows for {Entries.Count} UniqueType entries");

        var builder = ImmutableArray.CreateBuilder<Info>(rows.Length);
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            builder.Add(new Info(
                row.Text,
                row.Text.GetPlaceholderText(),
                new LinkedHashSet<UniqueTarget>(row.Targets),
                new LinkedHashSet<UniqueFlag>(row.Flags),
                row.DocDescription,
                BuildParameterTypeMap(row.Text)));
        }
        return builder.MoveToImmutable();
    }

    private static Dictionary<string, UniqueType> BuildUniqueTypeMap()
    {
        var map = new Dictionary<string, UniqueType>(Infos.Length, StringComparer.Ordinal);
        foreach (var type in Entries)
            map[Infos[(int)type].PlaceholderText] = type;
        return map;
    }

    private static List<IReadOnlyList<UniqueParameterType>> BuildParameterTypeMap(string text)
    {
        var map = new List<IReadOnlyList<UniqueParameterType>>();
        foreach (var placeholder in text.GetPlaceholderParameters())
        {
            var matchingParameterTypes = new List<UniqueParameterType>();
            foreach (var part in placeholder.Split('/'))
            {
                var parameterType = UniqueParameterTypeExtensions.SafeValueOf(StripTrailingDigits(part));
                if (parameterType != null) matchingParameterTypes.Add(parameterType.Value);
            }
            map.Add(matchingParameterTypes);
        }
        return map;
    }

    /// <summary>Kotlin <c>replace(Regex("\\d+$"), "")</c>: Java's <c>\d</c> is ASCII-only.</summary>
    private static string StripTrailingDigits(string text)
    {
        int end = text.Length;
        while (end > 0 && text[end - 1] is >= '0' and <= '9') end--;
        return end == text.Length ? text : text[..end];
    }
}
