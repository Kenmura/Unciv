using System.Globalization;

namespace Unciv.Models.Translations;

public static class TranslationFileReader
{
    public const string CommentUniqueText = "Comment [comment]";

    public static Dictionary<string, string> Read(TextReader reader)
    {
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
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

    public static Dictionary<string, string> Read(ITranslationFileHandle file)
    {
        using var reader = file.OpenReader();
        return Read(reader);
    }

    public static Dictionary<string, string> Read(string content)
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
            int separatorIndex = line.IndexOf(" = ", StringComparison.Ordinal);
            if (separatorIndex < 0) continue;

            string language = line[..separatorIndex];
            string valueStr = line[(separatorIndex + 3)..];
            if (int.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent))
            {
                percentages[language] = percent;
            }
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
