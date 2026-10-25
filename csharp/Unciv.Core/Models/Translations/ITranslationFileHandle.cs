using Unciv.Models.Ruleset;

namespace Unciv.Models.Translations;

public interface ITranslationFileHandle
{
    bool Exists { get; }
    string Name { get; }
    TextReader OpenReader();
    ITranslationFileHandle Child(string relativePath);
}

public sealed class StringTranslationFileHandle(string name, string content) : ITranslationFileHandle
{
    public bool Exists => true;
    public string Name => name;
    public TextReader OpenReader() => new StringReader(content);
    public ITranslationFileHandle Child(string relativePath) => new MissingTranslationFileHandle(relativePath);
}

public sealed class MissingTranslationFileHandle(string name) : ITranslationFileHandle
{
    public bool Exists => false;
    public string Name => name;
    public TextReader OpenReader() => throw new FileNotFoundException($"File not found: {name}");
    public ITranslationFileHandle Child(string relativePath) => new MissingTranslationFileHandle(relativePath);
}

/// <summary>An <see cref="ITranslationFileHandle"/> over a ruleset folder entry (the game's or a mod's), what Kotlin reads through a libGDX <c>FileHandle</c>.</summary>
public sealed class RulesetFolderTranslationFileHandle(IRulesetFolder file) : ITranslationFileHandle
{
    public bool Exists => file.Exists;
    public string Name => file.Name;
    public TextReader OpenReader() => new StringReader(file.ReadString());
    public ITranslationFileHandle Child(string relativePath) => new RulesetFolderTranslationFileHandle(file.Child(relativePath));
}
