using Unciv.Client.Net.Hosting;
using Unciv.GodotClient.Settings;

namespace Unciv.Core.Tests.Net;

/// <summary>T334: two profiles under one user folder are two players; the default profile keeps the identity it already had.</summary>
public sealed class TwoProfilesTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("unciv-profiles-").FullName;

    public void Dispose() => Directory.Delete(root, true);

    private string Key(string? profile)
    {
        using var identity = HostFiles.LoadIdentity(new ProfileFiles(root, profile).Identity);
        return identity.PlayerKey;
    }

    [Fact]
    public void TwoProfiles_AreDistinctPlayers_AndEachKeyIsStable()
    {
        string a = Key("alpha"), b = Key("beta"), d = Key(null);
        Assert.Equal(3, new[] { a, b, d }.Distinct().Count());
        Assert.Equal((a, b, d), (Key("alpha"), Key("beta"), Key(null)));
    }

    [Fact]
    public void ExistingPlayer_KeepsTheirIdentityWhenNoProfileIsGiven()
    {
        Directory.CreateDirectory(root);
        string legacyPath = Path.Combine(root, "identity.key"); // where every earlier version put it
        string before;
        using (var created = HostFiles.LoadIdentity(legacyPath)) before = created.PlayerKey;
        Assert.Equal(before, Key(null));
        Assert.NotEqual(before, Key("alpha"));
        Assert.Equal(before, Key(null));
    }
}
