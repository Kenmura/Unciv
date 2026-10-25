using Unciv.Logic.Files;

namespace Unciv.Cli;

/// <summary>An <see cref="IUncivFileHandle"/> over a host path, for the saved maps the CLI opens (<c>--map</c>).</summary>
public sealed class PathFileHandle(string path) : IUncivFileHandle
{
    public string Name => Path.GetFileName(path);

    public string NameWithoutExtension => Path.GetFileNameWithoutExtension(path);

    public bool IsInternal => false;

    public bool Exists => File.Exists(path) || Directory.Exists(path);

    public bool IsDirectory => Directory.Exists(path);

    public long Length => File.Exists(path) ? new FileInfo(path).Length : 0;

    public long LastModified => Exists ? new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds() : 0;

    public IUncivFileHandle Parent => new PathFileHandle(Path.GetDirectoryName(Path.GetFullPath(path)) ?? path);

    public IUncivFileHandle Child(string name) => new PathFileHandle(Path.Combine(path, name));

    public IReadOnlyList<IUncivFileHandle> List() =>
        Directory.Exists(path)
            ? Directory.GetFileSystemEntries(path).Order(StringComparer.Ordinal).Select(it => (IUncivFileHandle)new PathFileHandle(it)).ToList()
            : [];

    public void MakeDirs() => Directory.CreateDirectory(path);

    public bool Delete()
    {
        if (File.Exists(path)) File.Delete(path);
        else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else return false;
        return true;
    }

    public Stream Read() => File.OpenRead(path);

    public Stream Write(bool append) => new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write);
}
