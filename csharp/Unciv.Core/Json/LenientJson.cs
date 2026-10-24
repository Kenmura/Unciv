using System.Globalization;
using System.Text;

namespace Unciv.Json;

/// <summary>
/// The ruleset files are read by libGDX's lenient <c>JsonReader</c>, which accepts unquoted object keys (<c>{ text: "x" }</c>, used by the global Tutorials.json).
/// System.Text.Json does not, so this quotes them. It also quotes libGDX "minimal JSON" unquoted string values (<c>{type:Empty,name:Civ V - Vanilla}</c>, as in old saved maps and games),
/// which run up to the next <c>,</c> <c>}</c> <c>]</c> or line break; <c>true</c>/<c>false</c>/<c>null</c> and numbers stay literals. Strict JSON passes through unchanged.
/// Comments and trailing commas are handled by <see cref="UncivJson.DefaultOptions"/>.
/// </summary>
public static class LenientJson
{
    public static string QuoteBareKeys(string json)
    {
        var result = new StringBuilder(json.Length + 16);
        var i = 0;
        var previousSignificant = '\0';
        var containers = new Stack<char>();
        while (i < json.Length)
        {
            var c = json[i];
            if (c == '"')
            {
                var start = i++;
                while (i < json.Length && json[i] != '"') i += json[i] == '\\' ? 2 : 1;
                i = Math.Min(i + 1, json.Length);
                result.Append(json, start, i - start);
                previousSignificant = '"';
            }
            else if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
            {
                var start = i;
                while (i < json.Length && json[i] != '\n') i++;
                result.Append(json, start, i - start);
            }
            else if (c == '/' && i + 1 < json.Length && json[i + 1] == '*')
            {
                var start = i;
                var end = json.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? json.Length : end + 2;
                result.Append(json, start, i - start);
            }
            else if (IsValuePosition(previousSignificant, containers) && c is not ('{' or '[' or '}' or ']' or ',' or ':') && !char.IsWhiteSpace(c))
            {
                var start = i;
                while (i < json.Length && json[i] is not (',' or '}' or ']' or '\r' or '\n') && !(json[i] == '/' && i + 1 < json.Length && json[i + 1] is '/' or '*')) i++;
                var end = i;
                while (end > start && char.IsWhiteSpace(json[end - 1])) end--;
                var token = json.Substring(start, end - start);
                if (IsLiteral(token)) result.Append(token);
                else result.Append('"').Append(token.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
                result.Append(json, end, i - end);
                previousSignificant = '"';
            }
            else if ((previousSignificant == '{' || previousSignificant == ',') && (char.IsLetter(c) || c == '_' || c == '$'))
            {
                var start = i;
                while (i < json.Length && (char.IsLetterOrDigit(json[i]) || json[i] == '_' || json[i] == '$')) i++;
                var end = i;
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i < json.Length && json[i] == ':')
                {
                    result.Append('"').Append(json, start, end - start).Append('"').Append(json, end, i - end);
                    previousSignificant = '"';
                }
                else
                {
                    result.Append(json, start, i - start);
                    previousSignificant = json[end - 1];
                }
            }
            else
            {
                result.Append(c);
                if (c is '{' or '[') containers.Push(c);
                else if (c is '}' or ']' && containers.Count > 0) containers.Pop();
                if (!char.IsWhiteSpace(c)) previousSignificant = c;
                i++;
            }
        }
        return result.ToString();
    }

    private static bool IsValuePosition(char previousSignificant, Stack<char> containers) =>
        previousSignificant == ':' || (containers.Count > 0 && containers.Peek() == '[' && previousSignificant is '[' or ',');

    private static bool IsLiteral(string token) =>
        token is "true" or "false" or "null" ||
        (token.Length > 0 && (char.IsAsciiDigit(token[^1])) && (char.IsAsciiDigit(token[0]) || token[0] == '-') &&
         double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
}
