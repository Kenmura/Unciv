namespace Unciv.Logic.Files;

// Engine-owned stand-in for the libGDX FileHandle members the save/load code uses (no Gdx.files in the engine).
// The host (T100's UncivFiles, or a test fake) implements it.

/// <summary>A file or folder, possibly not existing yet. <c>FileHandle</c> equivalent.</summary>
public interface IUncivFileHandle
{
    /// <summary><c>name()</c>: the last path element.</summary>
    string Name { get; }

    /// <summary><c>nameWithoutExtension()</c></summary>
    string NameWithoutExtension { get; }

    /// <summary><c>type() == Files.FileType.Internal</c>: part of the bundled assets rather than a local file.</summary>
    bool IsInternal { get; }

    /// <summary><c>exists()</c></summary>
    bool Exists { get; }

    /// <summary><c>isDirectory</c></summary>
    bool IsDirectory { get; }

    /// <summary><c>length()</c>: 0 when the file does not exist.</summary>
    long Length { get; }

    /// <summary><c>lastModified()</c>: epoch milliseconds, an input read from the host file system.</summary>
    long LastModified { get; }

    /// <summary><c>parent()</c></summary>
    IUncivFileHandle Parent { get; }

    /// <summary><c>child(name)</c>: need not exist.</summary>
    IUncivFileHandle Child(string name);

    /// <summary><c>list()</c>. Implementations must return a deterministic order (ordinal by name).</summary>
    IReadOnlyList<IUncivFileHandle> List();

    /// <summary><c>mkdirs()</c></summary>
    void MakeDirs();

    /// <summary><c>delete()</c>: <c>true</c> if something was deleted.</summary>
    bool Delete();

    /// <summary><c>read()</c></summary>
    Stream Read();

    /// <summary><c>write(append)</c>: the caller disposes the stream.</summary>
    Stream Write(bool append);
}
