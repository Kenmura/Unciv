using System.Globalization;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Models.Translations;

public static class TranslationFileReader
{
    public static string CommentUniqueText => UniqueType.Comment.Text();

    public static OrderedDictionary<string, string> Read(TextReader reader)
    {
        var translations = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.IsKotlinBlank() || line.StartsWith('#')) continue;
            int separatorIndex = line.IndexOf(" = ", StringComparison.Ordinal);
            if (separatorIndex < 0) continue;

            string key = line[..separatorIndex].Replace("\\n", "\n");
            string value = line[(separatorIndex + 3)..].Replace("\\n", "\n");
            if (value.Length > 0)
            {
                translations[key] = value;
            }
        }

        translations[CommentUniqueText] = "[comment]";
        return translations;
    }

    public static OrderedDictionary<string, string> Read(ITranslationFileHandle file)
    {
        using var reader = file.OpenReader();
        return Read(reader);
    }

    public static OrderedDictionary<string, string> Read(string content)
    {
        using var reader = new StringReader(content);
        return Read(reader);
    }

    public static Dictionary<string, int> ReadLanguagePercentages(TextReader reader)
    {
        var percentages = new Dictionary<string, int>(StringComparer.Ordinal);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
            // Upstream does not guard this: a line without " = " or with a non-integer value throws
            string[] splitLine = line.Split(" = ");
            percentages[splitLine[0]] = int.Parse(splitLine[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
        return percentages;
    }

    public static Dictionary<string, int> ReadLanguagePercentages(ITranslationFileHandle? file)
    {
        if (file == null || !file.Exists) return new Dictionary<string, int>(StringComparer.Ordinal);
        using var reader = file.OpenReader();
        return ReadLanguagePercentages(reader);
    }

    public static T? ReadTemplates<T>(ITranslationFileHandle? file, Func<IEnumerable<string>, T> block)
    {
        if (file == null || !file.Exists) return default;
        using var reader = file.OpenReader();
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lines.Add(line);
        }
        return block(lines);
    }
}
