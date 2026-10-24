using System.Text;

namespace Unciv.Json;

/// <summary>
/// The ruleset files are read by libGDX's lenient <c>JsonReader</c>, which accepts unquoted object keys (<c>{ text: "x" }</c>, used by the global Tutorials.json).
/// System.Text.Json does not, so this quotes them. Only that leniency is covered; comments and trailing commas are handled by <see cref="UncivJson.DefaultOptions"/>.
/// </summary>
public static class LenientJson
{
    public static string QuoteBareKeys(string json)
    {
        var result = new StringBuilder(json.Length + 16);
        var i = 0;
        var previousSignificant = '\0';
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
                if (!char.IsWhiteSpace(c)) previousSignificant = c;
                i++;
            }
        }
        return result.ToString();
    }
}
