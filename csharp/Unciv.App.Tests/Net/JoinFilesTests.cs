using Unciv.Client.Net.Hosting;
using Unciv.Net.Identity;

namespace Unciv.Core.Tests.Net;

// C006: the join side's files (known hosts, last address).
public sealed class JoinFilesTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("unciv-join-files-").FullName;
    private static readonly string Fingerprint = HostKey.Fingerprint([1, 2, 3, 4]);
    private static readonly string Other = HostKey.Fingerprint([9, 9, 9]);

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void KnownHosts_RoundTrip_KeepsPinsAndDetectsAChange()
    {
        var path = Path.Combine(root, "sub", "known-hosts.json");
        var known = new KnownHosts();
        Assert.True(known.Pin("b.example:1", Fingerprint));
        Assert.True(known.Pin("a.example:2", Other));
        JoinFiles.SaveKnownHosts(path, known);

        var loaded = JoinFiles.LoadKnownHosts(path);
        Assert.Equal(["a.example:2", "b.example:1"], loaded.Pins.Keys.OrderBy(it => it, StringComparer.Ordinal));
        Assert.Equal(HostTrust.Trusted, loaded.Check("b.example:1", Fingerprint));
        Assert.Equal(HostTrust.Mismatch, loaded.Check("b.example:1", Other));
        Assert.Equal(HostTrust.Unknown, loaded.Check("c.example:3", Other));
    }

    [Fact]
    public void KnownHosts_ForgetThenSave_DropsThePin()
    {
        var path = Path.Combine(root, "known-hosts.json");
        var known = new KnownHosts();
        known.Pin("h:1", Fingerprint);
        known.Forget("h:1");
        JoinFiles.SaveKnownHosts(path, known);
        Assert.Empty(JoinFiles.LoadKnownHosts(path).Pins);
    }

    [Fact]
    public void KnownHosts_MissingOrDamagedFile_IsEmptyNotACrash()
    {
        Assert.Empty(JoinFiles.LoadKnownHosts(Path.Combine(root, "none.json")).Pins);
        var damaged = Path.Combine(root, "damaged.json");
        File.WriteAllText(damaged, "{ not json");
        Assert.Empty(JoinFiles.LoadKnownHosts(damaged).Pins);
        File.WriteAllText(damaged, "[1,2]");
        Assert.Empty(JoinFiles.LoadKnownHosts(damaged).Pins);
    }

    [Fact]
    public void KnownHosts_EntryWithAnInvalidFingerprint_IsDropped()
    {
        var path = Path.Combine(root, "known-hosts.json");
        File.WriteAllText(path, "{\"good:1\":\"" + Fingerprint + "\",\"bad:2\":\"nope\"}");
        var loaded = JoinFiles.LoadKnownHosts(path);
        Assert.Equal(["good:1"], loaded.Pins.Keys);
    }

    [Fact]
    public void LastAddress_RoundTripsAndIsEmptyWhenMissing()
    {
        var path = Path.Combine(root, "dir", "last-join.txt");
        Assert.Equal("", JoinFiles.LoadLastAddress(path));
        JoinFiles.SaveLastAddress(path, "192.168.1.20:47821");
        Assert.Equal("192.168.1.20:47821", JoinFiles.LoadLastAddress(path));
    }

    [Fact]
    public void LastAddress_UnwritableLocation_IsIgnored()
    {
        var blocker = Path.Combine(root, "file");
        File.WriteAllText(blocker, "x");
        JoinFiles.SaveLastAddress(Path.Combine(blocker, "inside", "last-join.txt"), "a:1"); // the parent is a file: must not throw
        Assert.Equal("", JoinFiles.LoadLastAddress(Path.Combine(blocker, "inside", "last-join.txt")));
    }
}
