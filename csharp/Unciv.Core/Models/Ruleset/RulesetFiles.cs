using System.Text;

namespace Unciv.Models.Ruleset;

// Replaces the libGDX FileHandle / Gdx.files / UncivGame.Current.files uses of Ruleset.kt and RulesetCache.kt: the engine never touches the
// disk itself, it asks these.

/// <summary>The part of a libGDX <c>FileHandle</c> that loading a ruleset needs: a file or directory that may or may not exist.</summary>
public interface IRulesetFolder
{
    /// <summary><c>name()</c>: the last path element.</summary>
    string Name { get; }

    bool Exists { get; }

    bool IsDirectory { get; }

    /// <summary><c>child(name)</c>: need not exist.</summary>
    IRulesetFolder Child(string name);

    /// <summary><c>list()</c>. The Kotlin order is whatever <c>File.listFiles</c> yields, which is unspecified; implementations must return a deterministic one (ordinal by name).</summary>
    IReadOnlyList<IRulesetFolder> List();

    /// <summary><c>readString()</c>, UTF-8.</summary>
    string ReadString();
}

/// <summary>
/// Where <see cref="RulesetCache.LoadRulesets"/> finds the files: the <c>jsons/</c> folder of the game, and the folder holding the mods.
/// </summary>
public interface IRulesetFileSource
{
    /// <summary><c>jsons/${ruleset.fullName}</c></summary>
    IRulesetFolder GetBuiltinRulesetFolder(string fullName);

    /// <summary>
    /// The <c>jsons</c> folder holding the global <c>Tutorials.json</c>; <c>null</c> when running in console mode,
    /// which is what Kotlin's <c>Gdx.files != null</c> distinguishes.
    /// </summary>
    IRulesetFolder? GlobalJsonsFolder { get; }

    /// <summary><c>UncivGame.Current.files.getModsFolder()</c> (or <c>FileHandle("mods")</c> in console mode).</summary>
    IRulesetFolder ModsFolder { get; }
}

/// <summary>A <see cref="IRulesetFolder"/> over a real directory or file.</summary>
public sealed class DirectoryRulesetFolder(string path) : IRulesetFolder
{
    public string Name => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public bool Exists => Directory.Exists(path) || File.Exists(path);

    public bool IsDirectory => Directory.Exists(path);

    public IRulesetFolder Child(string name) => new DirectoryRulesetFolder(Path.Combine(path, name));

    public IReadOnlyList<IRulesetFolder> List()
    {
        if (!Directory.Exists(path)) return [];
        return Directory.GetFileSystemEntries(path)
            .Order(StringComparer.Ordinal)
            .Select(it => (IRulesetFolder)new DirectoryRulesetFolder(it))
            .ToList();
    }

    public string ReadString() => File.ReadAllText(path, Encoding.UTF8);
}

/// <summary>A <see cref="IRulesetFileSource"/> over real directories.</summary>
public sealed class DirectoryRulesetFileSource(string jsonsFolder, string modsFolder, bool consoleMode = false) : IRulesetFileSource
{
    public IRulesetFolder GetBuiltinRulesetFolder(string fullName) => new DirectoryRulesetFolder(Path.Combine(jsonsFolder, fullName));

    public IRulesetFolder? GlobalJsonsFolder => consoleMode ? null : new DirectoryRulesetFolder(jsonsFolder);

    public IRulesetFolder ModsFolder => new DirectoryRulesetFolder(modsFolder);
}
