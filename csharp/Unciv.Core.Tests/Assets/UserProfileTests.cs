using Unciv.GodotClient.Settings;

namespace Unciv.Core.Tests.Assets;

/// <summary>T334: which profile an instance runs as and where its files live.</summary>
public sealed class UserProfileTests
{
    [Fact]
    public void NoArgumentAndNoEnvironment_IsTheDefaultProfile()
    {
        Assert.Equal((null, null), UserProfile.Resolve(["--exit-on-menu"], null));
        Assert.Equal((null, null), UserProfile.Resolve([], ""));
    }

    [Theory]
    [InlineData("--profile=alice", "alice")]
    [InlineData("--profile", "bob")]
    public void ArgumentSelectsTheProfile(string first, string expected)
    {
        string[] args = first.Contains('=', StringComparison.Ordinal) ? ["--x", first] : ["--x", first, expected];
        Assert.Equal((expected, null), UserProfile.Resolve(args, null));
    }

    [Fact]
    public void EnvironmentSelectsTheProfile_ButTheArgumentWins()
    {
        Assert.Equal(("env", null), UserProfile.Resolve([], "env"));
        Assert.Equal(("arg", null), UserProfile.Resolve(["--profile=arg"], "env"));
    }

    [Fact]
    public void ProfileWithoutAValue_IsAnError()
    {
        var (name, error) = UserProfile.Resolve(["--profile"], null);
        Assert.Null(name);
        Assert.Contains("needs a name", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("/abs")]
    [InlineData("C:")]
    [InlineData(".hidden")]
    [InlineData("-lead")]
    [InlineData("a b")]
    [InlineData("")]
    [InlineData("name\0x")]
    [InlineData("alpha\n")]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("Com1")]
    [InlineData("alpha.")]
    [InlineData("\u0663")]
    [InlineData("\u00e9")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void PathTraversalAndOddNames_AreRejectedFromBothSources(string bad)
    {
        Assert.False(UserProfile.IsValidName(bad));
        var (name, error) = UserProfile.Resolve([$"--profile={bad}"], null);
        if (bad.Length == 0) return; // "--profile=" is an empty name: invalid as well, checked below
        Assert.Null(name);
        Assert.Contains("Invalid profile name", error, StringComparison.Ordinal);
        Assert.Contains("Invalid profile name", UserProfile.Resolve([], bad).Error, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyArgumentValue_IsRejected()
    {
        Assert.Contains("Invalid profile name", UserProfile.Resolve(["--profile="], null).Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Friend_2")]
    [InlineData("x-y")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void PlainNames_AreAccepted(string good) => Assert.True(UserProfile.IsValidName(good));

    [Fact]
    public void DefaultProfile_KeepsEveryFileWhereItAlwaysWas()
    {
        var files = new ProfileFiles("/data", null);
        Assert.Equal(Path.Combine("/data", "identity.key"), files.Identity);
        Assert.Equal(Path.Combine("/data", "settings.json"), files.Settings);
        Assert.Equal(Path.Combine("/data", "saves"), files.Saves);
        Assert.Equal(Path.Combine("/data", "host-key.pfx"), files.HostKey);
        Assert.Equal(Path.Combine("/data", "known-hosts.json"), files.KnownHosts);
        Assert.Equal(Path.Combine("/data", "last-join.txt"), files.LastJoin);
        Assert.Equal(Path.Combine("/data", "relay.json"), files.Relay);
        Assert.Equal(Path.Combine("/data", "hosted"), files.HostedGames);
        Assert.Equal(Path.Combine("/data", "owner-memory"), files.OwnerMemory);
        Assert.Equal(Path.Combine("/data", "mods"), files.Mods);
    }

    [Fact]
    public void NamedProfile_HasItsOwnFolder_ExceptTheSharedMods()
    {
        var files = new ProfileFiles("/data", "alice");
        string folder = Path.Combine("/data", "profiles", "alice");
        Assert.All(new[] { files.Identity, files.Settings, files.Saves, files.HostKey, files.KnownHosts, files.LastJoin, files.HostedGames, files.OwnerMemory }, p => Assert.StartsWith(folder, p, StringComparison.Ordinal));
        Assert.Equal(Path.Combine("/data", "mods"), files.Mods);
    }

    [Fact]
    public void CaseVariants_ShareOneFolder() => Assert.Equal(new ProfileFiles("/d", "Alpha").Identity, new ProfileFiles("/d", "alpha").Identity);

    [Fact]
    public void WindowTitle_NamesTheProfile() => Assert.Equal(("Unciv", "Unciv - bob"), (UserProfile.WindowTitle(null), UserProfile.WindowTitle("bob")));
}
