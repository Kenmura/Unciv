using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Unciv.Models.StatTypes;

namespace Unciv.DevConsole;

/// <summary>Minor user error in console input; <see cref="Hint"/> is user-readable, never translated. Kotlin: <c>ConsoleHintException</c>.</summary>
public sealed class ConsoleHintException(string hint) : Exception(hint)
{
    public string Hint { get; } = hint;
}

/// <summary>User error in console input; <see cref="Error"/> is user-readable, never translated. Kotlin: <c>ConsoleErrorException</c>.</summary>
public class ConsoleErrorException(string error) : Exception(error)
{
    public string Error { get; } = error;
}

/// <summary>Kotlin <c>ConsoleUniqueErrorException</c>: a user error involving a Unique text.</summary>
public sealed class ConsoleUniqueErrorException(string error) : ConsoleErrorException(error);

/// <summary>Result of an autocomplete request: the text replacing the last parameter (null: no change) and a message listing the matches (null: none).</summary>
public sealed record AutocompleteResult(string? Text, string? Message);

/// <summary>
/// Kotlin: <c>CliInput</c>. The method used to convert/display ruleset object (or other) names in console input.
/// Method 1 (Dashed): lowercase with spaces replaced by '-': <c>mechanized-infantry</c>.
/// Method 2 (Quoted): <c>"Mechanized Infantry"</c> (the closing quote may be missing for autocomplete).
/// Comparison between instances and strings is method-agnostic; both sides are converted consistently.
/// </summary>
public sealed class CliInput : IComparable<CliInput>, IEquatable<CliInput>
{
    public enum Method
    {
        Dashed,
        Quoted,
    }

    private readonly string original;

    public CliInput(string parameter, Method? method = null)
    {
        original = parameter;
        MethodUsed = method ?? (parameter.StartsWith('"') ? Method.Quoted : Method.Dashed);
        Content = MethodUsed == Method.Dashed ? ToDashedRepresentation(parameter) : ToQuotedRepresentation(parameter);
    }

    public static CliInput Empty { get; } = new("");

    /// <summary>Kotlin <c>method</c>.</summary>
    public Method MethodUsed { get; }

    /// <summary>The massaged parameter text.</summary>
    public string Content { get; }

    private static Method Or(Method a, Method b) => a == Method.Dashed && b == Method.Dashed ? Method.Dashed : Method.Quoted;

    public override string ToString() => MethodUsed == Method.Dashed ? Content : $"\"{Content}\"";

    public int CompareTo(CliInput? other)
    {
        if (other == null) return 1;
        if (MethodUsed == Method.Dashed && other.MethodUsed == Method.Dashed) return string.CompareOrdinal(Content, other.Content);
        if (MethodUsed == Method.Quoted && other.MethodUsed == Method.Quoted)
            return string.Compare(Content, other.Content, StringComparison.OrdinalIgnoreCase);
        return MethodUsed == Method.Dashed
            ? string.CompareOrdinal(Content, ToDashedRepresentation(other.original))
            : string.CompareOrdinal(ToDashedRepresentation(original), other.Content);
    }

    public int CompareTo(string other, Method method) => CompareTo(new CliInput(other, method));

    public bool Equals(CliInput? other) => other != null && (ReferenceEquals(this, other) || CompareTo(other) == 0);

    /// <summary>Case-insensitive equality with a string; square brackets of the string are ignored (Kotlin <c>equals(String)</c>).</summary>
    public bool Equals(string other) => CompareTo(other.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal), MethodUsed) == 0;

    public override bool Equals(object? obj) => obj switch
    {
        CliInput input => Equals(input),
        string text => Equals(text),
        _ => false,
    };

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(GetDashedRepresentation());

    private string GetDashedRepresentation() => MethodUsed == Method.Dashed ? Content : ToDashedRepresentation(original);

    private string GetAutocompleteString(Method paramMethod, int? upTo = null, string toAppend = "")
    {
        int limit = upTo ?? Content.Length;
        if (paramMethod == Method.Dashed && MethodUsed == Method.Dashed)
            return Content[..Math.Min(limit, Content.Length)] + toAppend;
        string source = MethodUsed == Method.Quoted ? Content : ToQuotedRepresentation(original);
        string suffix = toAppend.Length > 0 ? "\"" + toAppend : "";
        return "\"" + source[..Math.Min(limit, source.Length)] + suffix;
    }

    public CliInput ToMethod(Method method) => MethodUsed == method ? this : new CliInput(original, method);

    /// <summary>Like equality in method treatment, but compares the common prefix only.</summary>
    public bool StartsWith(CliInput other)
    {
        if (MethodUsed == Method.Dashed && other.MethodUsed == Method.Dashed) return Content.StartsWith(other.Content, StringComparison.Ordinal);
        if (MethodUsed == Method.Quoted && other.MethodUsed == Method.Quoted) return Content.StartsWith(other.Content, StringComparison.OrdinalIgnoreCase);
        return MethodUsed == Method.Dashed
            ? Content.StartsWith(ToDashedRepresentation(other.original), StringComparison.Ordinal)
            : ToDashedRepresentation(original).StartsWith(other.Content, StringComparison.Ordinal);
    }

    public bool IsEmpty() => Content.Length == 0;

    public bool IsNotEmpty() => Content.Length != 0;

    /// <summary>Length of the original parameter, for autocomplete replacing.</summary>
    public int OriginalLength() => original.Length;

    /// <summary>The original parameter with any outer quotes removed.</summary>
    public string OriginalUnquoted() => RemoveOuterQuotes(original);

    public int ToInt() =>
        int.TryParse(Content, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new ConsoleErrorException($"'{this}' is not a valid number.");

    // Deviation from Kotlin: NaN and infinities are refused, they would poison the deterministic state.
    public float ToFloat() =>
        float.TryParse(Content, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value)
            ? value
            : throw new ConsoleErrorException($"'{this}' is not a valid number.");

    public bool ToBoolean() => Content switch
    {
        "true" => true,
        "false" => false,
        _ => throw new ConsoleErrorException($"'{this}' is not a valid boolean value."),
    };

    public Stat ToStat() => EnumValueOrNull<Stat>() ?? throw new ConsoleErrorException($"'{this}' is not an acceptable Stat.");

    /// <summary>Finds the enum member whose name equals this parameter, in declaration order.</summary>
    public T? EnumValueOrNull<T>() where T : struct, Enum
    {
        foreach (var value in Enum.GetValues<T>())
            if (Equals(value.ToString())) return value;
        return null;
    }

    public T EnumValue<T>() where T : struct, Enum =>
        EnumValueOrNull<T>()
        ?? throw new ConsoleErrorException($"'{this}' is not a valid {typeof(T).Name}. Options are: [{string.Join(", ", Enum.GetNames<T>())}].");

    public string? FindOrNull(IEnumerable<string> options) => options.FirstOrDefault(Equals);

    public T? FindOrNull<T>(IEnumerable<T> options, Func<T, string> name) where T : class => options.FirstOrDefault(it => Equals(name(it)));

    public T Find<T>(IEnumerable<T> options, Func<T, string> name) where T : class
    {
        var list = options.ToList();
        return list.FirstOrDefault(it => Equals(name(it)))
            ?? throw new ConsoleErrorException($"'{this}' is not a valid {typeof(T).Name}. Options are: {JoinLimited(list.Select(name))}.");
    }

    /// <summary>Representation for strings that will pass through translation: Dashed in square brackets, Quoted as <c>"[text]"</c>.</summary>
    public string ToStringAsPlaceholder() => MethodUsed == Method.Dashed ? $"[{Content}]" : $"\"[{OriginalUnquoted()}]\"";

    private static string RemoveOuterQuotes(string text)
    {
        if (text.StartsWith('"')) text = text[1..];
        return text.EndsWith('"') ? text[..^1] : text;
    }

    private static string ToDashedRepresentation(string text) =>
        RemoveOuterQuotes(text).ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);

    private static bool CharIsNotAQuote(char c) => c != '"';

    private static string ToQuotedRepresentation(string text)
    {
        var sb = new StringBuilder(text.Length);
        int start = Math.Max(Array.FindIndex(text.ToCharArray(), CharIsNotAQuote), 0);
        int end = Array.FindLastIndex(text.ToCharArray(), CharIsNotAQuote) + 1;
        if (end > start)
        {
            sb.Append(char.ToUpperInvariant(text[start]));
            if (end > start + 1)
                sb.Append(RepeatedWhiteSpace.Replace(text[(start + 1)..end], ""));
        }
        return sb.ToString();
    }

    // Any whitespace sequence preceded by whitespace (lookbehind, so the preceding whitespace is not part of the match).
    private static readonly Regex RepeatedWhiteSpace = new(@"(?<=\s)\s+", RegexOptions.CultureInvariant);

    // A quoted phrase (closing quote optional at the end), OR consecutive non-whitespace, OR a terminal empty string preceded by whitespace.
    private static readonly Regex SplitString = new("\"[^\"]+(?:\"|$)|\\S+|(?:(?<=\\s)$)", RegexOptions.CultureInvariant);

    public static IReadOnlyList<CliInput> SplitToCliInput(string text) =>
        SplitString.Matches(text).Select(it => new CliInput(it.Value)).ToList();

    /// <summary>Kotlin <c>getAutocompleteString</c>: the text replacing <paramref name="lastWord"/> from <paramref name="allOptions"/>, plus the list of matches if ambiguous.</summary>
    public static AutocompleteResult GetAutocompleteString(CliInput lastWord, IEnumerable<CliInput> allOptions)
    {
        var matching = allOptions.Where(it => it.StartsWith(lastWord)).ToList();
        if (matching.Count == 0) return new AutocompleteResult(null, null);
        if (matching.Count == 1) return new AutocompleteResult(matching[0].GetAutocompleteString(lastWord.MethodUsed, toAppend: " "), null);

        var showMethod = Or(lastWord.MethodUsed, matching[0].MethodUsed);
        string message = JoinLimited(matching.Select(it => it.ToMethod(showMethod)).OrderBy(it => it.Content, StringComparer.Ordinal).Select(it => it.ToString()), prefix: "Matching completions: ");

        var firstOption = matching[0];
        string dashed = firstOption.GetDashedRepresentation();
        for (int index = 0; index < dashed.Length; index++)
        {
            char c = dashed[index];
            if (matching.Any(it => it.GetDashedRepresentation().Length - 1 < index) || matching.Any(it => it.GetDashedRepresentation()[index] != c))
                return new AutocompleteResult(firstOption.GetAutocompleteString(lastWord.MethodUsed, index), message);
        }
        return new AutocompleteResult(firstOption.GetAutocompleteString(lastWord.MethodUsed), message); // no space: drill-i might be drill-ii
    }

    public static AutocompleteResult GetAutocompleteString(CliInput lastWord, IEnumerable<string> allOptions) =>
        GetAutocompleteString(lastWord, allOptions.Select(it => new CliInput(it)));

    private static string JoinLimited(IEnumerable<string> items, string separator = ", ", string prefix = "", int limit = 42)
    {
        var list = items.ToList();
        return prefix + string.Join(separator, list.Take(limit)) + (list.Count > limit ? $"{separator}... ({list.Count - limit} not shown)" : "");
    }
}
