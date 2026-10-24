namespace Unciv.Logic.Map;

/// <summary>
/// Platform file access for <see cref="OdMapImport"/> and <see cref="OdMapExport"/> (Kotlin took a libGDX <c>FileHandle</c>:
/// <c>file.file()</c> for reading, <c>file.write(false)</c> for writing). The engine owns no file system; the host supplies one.
/// </summary>
public interface IOdMapFile
{
    /// <summary>Opens the file for reading. The caller disposes the stream.</summary>
    Stream OpenRead();
    /// <summary>Opens the file for writing, replacing its content (<c>write(append = false)</c>). The caller disposes the stream.</summary>
    Stream OpenWrite();
}

/// <summary>An <see cref="IOdMapFile"/> on a path of the local file system.</summary>
public sealed class OdMapPathFile(string path) : IOdMapFile
{
    public Stream OpenRead() => File.OpenRead(path);
    public Stream OpenWrite() => File.Create(path);
}
