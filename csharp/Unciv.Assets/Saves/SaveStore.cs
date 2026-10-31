using System.Globalization;
using System.Text;
using System.Text.Json;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Files;

namespace Unciv.GodotClient.Saves;

/// <summary>What the save list shows about one save, read from the save's own text.</summary>
public sealed record SaveInfo(int Turn, string Civ, string Difficulty, string Ruleset, IReadOnlyList<string> Mods);

/// <summary>One file of the save folder. <see cref="Info"/> is null (and <see cref="Problem"/> says why) for a file that is not a readable save: it stays listed so the player can delete it.</summary>
public sealed record SaveEntry(string Name, DateTime ModifiedUtc, long Size, SaveInfo? Info, string? Problem)
{
    public bool IsAutosave => SaveStore.IsAutosaveName(Name);
}

/// <summary>The outcome of a file operation: refusals and I/O failures are results with a message for the player, never exceptions.</summary>
public readonly record struct SaveResult(bool Ok, string? Error)
{
    public static SaveResult Success { get; } = new(true, null);

    public static SaveResult Fail(string error) => new(false, error);
}

/// <summary>Reads the list-row facts out of a save's text (gzip+base64 like the engine writes, or plain JSON), without building the game.</summary>
public static class SaveMetadata
{
    public static SaveInfo? TryRead(string text, out string? problem)
    {
        problem = null;
        try
        {
            string json;
            string trimmed = text.Trim();
            try { json = FileConversions.Unzip(trimmed); }
            catch (Exception e) when (e is FormatException or InvalidDataException) { json = LenientJson.QuoteBareKeys(trimmed); }
            var preview = UncivJson.Deserialize<GameInfoPreview>(json);
            if (preview == null || preview.Civilizations.Count == 0 || preview.Turns < 0)
            {
                problem = "Not a Unciv save";
                return null;
            }
            var human = preview.Civilizations.FirstOrDefault(it => it.IsPlayerCivilization());
            string civ = human?.CivName ?? preview.CurrentPlayer;
            return new SaveInfo(preview.Turns, civ, preview.Difficulty, preview.GameParameters.BaseRuleset, preview.GameParameters.Mods.ToList());
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException or ArgumentException or FormatException or InvalidDataException)
        {
            problem = "Damaged save: " + e.Message;
            return null;
        }
    }
}

/// <summary>
/// C016: the folder of save files, as plain files named exactly as the player named them. Godot-free so it is unit tested. Every method that touches the disk
/// reports failure as a result (a missing folder lists as empty, a damaged file is listed with a problem, a refused write leaves the old file intact);
/// writes go to a temporary file in the same folder which then replaces the target, so a crash never leaves half a save.
/// Deliberate difference from Kotlin: numbered autosaves are purged by turn number rather than by file time, and the list is newest first with a name tiebreak.
/// </summary>
public sealed class SaveStore(string folder)
{
    public const string AutosaveName = "Autosave";
    public const int MaxNameLength = 100;
    public const long MaxSaveBytes = 256L * 1024 * 1024;

    private const string TempSuffix = ".tmp";
    private static readonly char[] ForbiddenChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];
    private static readonly string[] ReservedNames = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    private readonly Dictionary<string, (long Size, long Ticks, SaveInfo? Info, string? Problem)> metadataCache = new(StringComparer.Ordinal);

    public string Folder { get; } = folder;

    public static bool IsAutosaveName(string name) => name.StartsWith(AutosaveName, StringComparison.Ordinal);

    /// <summary>Null when <paramref name="name"/> is a usable save name, else the reason it is not.</summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "Enter a name";
        if (name.Length > MaxNameLength) return $"The name is longer than {MaxNameLength.ToString(CultureInfo.InvariantCulture)} characters";
        if (name != name.Trim() || name.EndsWith('.')) return "The name may not start or end with a space, or end with a dot";
        if (name.Any(char.IsControl)) return "The name contains a control character";
        if (name.IndexOfAny(ForbiddenChars) >= 0) return "The name may not contain \\ / : * ? \" < > |";
        if (name.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase)) return "The name may not end with .tmp";
        if (name.StartsWith('.')) return "The name may not start with a dot";
        if (ReservedNames.Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase)) return "That name is reserved by the operating system";
        return null;
    }

    private string PathOf(string name) => Path.Combine(Folder, name);

    /// <summary>The saves, newest first (name order for equal times); empty when the folder is missing or unreadable. Temporary and hidden files are not saves.</summary>
    public IReadOnlyList<SaveEntry> List()
    {
        var entries = new List<SaveEntry>();
        try
        {
            if (!Directory.Exists(Folder)) return entries;
            foreach (var file in new DirectoryInfo(Folder).EnumerateFiles())
            {
                if (file.Name.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase) || file.Name.StartsWith('.') || ValidateName(file.Name) != null) continue;
                var (info, problem) = MetadataOf(file);
                entries.Add(new SaveEntry(file.Name, file.LastWriteTimeUtc, file.Length, info, problem));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // an unreadable folder lists what was found so far; nothing else can be done about it here
        }
        return entries.OrderByDescending(it => it.ModifiedUtc).ThenBy(it => it.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>The newest save that is a readable game, or null.</summary>
    public SaveEntry? Latest() => List().FirstOrDefault(it => it.Info != null);

    private (SaveInfo? Info, string? Problem) MetadataOf(FileInfo file)
    {
        long ticks = file.LastWriteTimeUtc.Ticks;
        if (metadataCache.TryGetValue(file.Name, out var cached) && cached.Size == file.Length && cached.Ticks == ticks) return (cached.Info, cached.Problem);
        SaveInfo? info = null;
        string? problem;
        if (file.Length == 0) problem = "Empty file";
        else if (file.Length > MaxSaveBytes) problem = "File is too large to be a save";
        else
        {
            try { info = SaveMetadata.TryRead(File.ReadAllText(file.FullName, Encoding.UTF8), out problem); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { problem = "Cannot read: " + e.Message; }
        }
        metadataCache[file.Name] = (file.Length, ticks, info, problem);
        return (info, problem);
    }

    /// <summary>Writes <paramref name="text"/> as save <paramref name="name"/>, replacing a save of that name atomically. A refused name or a failed write changes nothing.</summary>
    public SaveResult Write(string name, string text)
    {
        if (ValidateName(name) is { } invalid) return SaveResult.Fail(invalid);
        if (string.IsNullOrEmpty(text)) return SaveResult.Fail("Nothing to save");
        string temp = PathOf(name) + TempSuffix;
        try
        {
            Directory.CreateDirectory(Folder);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encoding.UTF8.GetBytes(text));
                stream.Flush(true);
            }
            File.Move(temp, PathOf(name), overwrite: true);
            return SaveResult.Success;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); }
            catch (Exception d) when (d is IOException or UnauthorizedAccessException) { }
            return SaveResult.Fail("Could not write the save: " + e.Message);
        }
    }

    /// <summary>The text of save <paramref name="name"/>, or the reason it cannot be read (missing, unreadable, too large, bad name).</summary>
    public SaveResult TryRead(string name, out string text)
    {
        text = "";
        if (ValidateName(name) is { } invalid) return SaveResult.Fail(invalid);
        try
        {
            var file = new FileInfo(PathOf(name));
            if (!file.Exists) return SaveResult.Fail("The save does not exist");
            if (file.Length > MaxSaveBytes) return SaveResult.Fail("File is too large to be a save");
            text = File.ReadAllText(file.FullName, Encoding.UTF8);
            return SaveResult.Success;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return SaveResult.Fail("Could not read the save: " + e.Message);
        }
    }

    public SaveResult Delete(string name)
    {
        if (ValidateName(name) is { } invalid) return SaveResult.Fail(invalid);
        try
        {
            if (!File.Exists(PathOf(name))) return SaveResult.Fail("The save does not exist");
            File.Delete(PathOf(name));
            metadataCache.Remove(name);
            return SaveResult.Success;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return SaveResult.Fail("Could not delete the save: " + e.Message);
        }
    }

    /// <summary>
    /// Writes "Autosave" (always the latest) and "Autosave-&lt;turn&gt;", then deletes the numbered autosaves beyond the newest <paramref name="keep"/> turns.
    /// Player-named saves, including ones that merely start with "Autosave", are never deleted (only names of the exact form Autosave-&lt;number&gt;).
    /// </summary>
    public SaveResult Autosave(string text, int turn, int keep)
    {
        var result = Write(AutosaveName, text);
        if (!result.Ok) return result;
        result = Write($"{AutosaveName}-{turn.ToString(CultureInfo.InvariantCulture)}", text);
        if (!result.Ok) return result;
        var numbered = List().Select(it => (it.Name, Turn: NumberedAutosaveTurn(it.Name))).Where(it => it.Turn != null)
            .OrderByDescending(it => it.Turn).ThenBy(it => it.Name, StringComparer.Ordinal).Skip(Math.Max(keep, 1));
        foreach (var (name, _) in numbered.ToList()) Delete(name);
        return SaveResult.Success;
    }

    private static int? NumberedAutosaveTurn(string name) =>
        name.StartsWith(AutosaveName + "-", StringComparison.Ordinal) && int.TryParse(name.AsSpan(AutosaveName.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int turn) ? turn : null;

    /// <summary>Whether the turn that just began is one to autosave: autosave is on (<paramref name="everyTurns"/> above 0) and the turn is a multiple of it.</summary>
    public static bool AutosaveDue(int turn, int everyTurns) => everyTurns > 0 && turn > 0 && turn % everyTurns == 0;
}
