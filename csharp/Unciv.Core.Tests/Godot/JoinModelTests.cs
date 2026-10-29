using Unciv.GodotClient.Setup;
using Unciv.Net.Identity;

namespace Unciv.Tests.Godot;

// C006: the join screen's texts and address parsing (Godot-free).
public sealed class JoinModelTests
{
    private static readonly string Fingerprint = HostKey.Fingerprint([1, 2, 3, 4]);

    [Theory]
    [InlineData("192.168.1.20:47900", "192.168.1.20", 47900)]
    [InlineData("  example.org  ", "example.org", 47821)]
    [InlineData("example.org:1", "example.org", 1)]
    [InlineData("example.org:65535", "example.org", 65535)]
    [InlineData("[::1]:5000", "::1", 5000)]
    [InlineData("[fe80::1]", "fe80::1", 47821)]
    [InlineData("fe80::1", "fe80::1", 47821)]
    public void Parse_AcceptsAddresses(string text, string host, int port)
    {
        var parsed = JoinModel.Parse(text);
        Assert.Null(parsed.Problem);
        Assert.Equal((host, port), (parsed.Target!.Host, parsed.Target.Port));
        Assert.Null(parsed.Target.Fingerprint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":47821")]
    [InlineData("host:0")]
    [InlineData("host:65536")]
    [InlineData("host:-1")]
    [InlineData("host:abc")]
    [InlineData("host:")]
    [InlineData("[::1")]
    [InlineData("[::1]x")]
    [InlineData("two words:1")]
    [InlineData("host/path")]
    [InlineData("unciv://nonsense")]
    [InlineData("unciv://host:1/not-a-fingerprint")]
    public void Parse_RefusesWithASentence(string? text)
    {
        var parsed = JoinModel.Parse(text);
        Assert.Null(parsed.Target);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Problem));
    }

    [Fact]
    public void Parse_Invite_CarriesFingerprintAndToken()
    {
        var parsed = JoinModel.Parse(InviteCode.Format("203.0.113.7", 47891, Fingerprint, "tok123"));
        Assert.Null(parsed.Problem);
        var target = parsed.Target!;
        Assert.Equal(("203.0.113.7", 47891, "tok123"), (target.Host, target.Port, target.Token));
        Assert.True(HostKey.Matches(Fingerprint, target.Fingerprint));
        Assert.Equal("203.0.113.7:47891", target.Address);
    }

    [Fact]
    public void Address_IsTheKnownHostsKey_LowerCasedAndBracketedForIpv6()
    {
        Assert.Equal("example.org:47821", new JoinTarget("Example.ORG", 47821, null, null).Address);
        Assert.Equal("[::1]:5", new JoinTarget("::1", 5, null, null).Address);
        Assert.Equal(JoinModel.Parse("EXAMPLE.org").Target!.Address, JoinModel.Parse("example.org:47821").Target!.Address);
    }

    [Theory]
    [InlineData("connect-failed", "Could not reach the host")]
    [InlineData("handshake-timeout", "did not answer in time")]
    [InlineData("host-untrusted", "did not trust")]
    [InlineData("host-key-mismatch", "does not match the invite")]
    [InlineData("host-key-changed", "has changed")]
    [InlineData("protocol-mismatch", "same version")]
    [InlineData("bad-credentials", "Wrong password")]
    [InlineData("ruleset-mismatch", "different rules")]
    public void RefusalText_SaysWhatToDo(string code, string fragment) => Assert.Contains(fragment, JoinModel.RefusalText(code), StringComparison.Ordinal);

    [Fact]
    public void RefusalText_UnknownCode_NamesIt() => Assert.Contains("weird-code", JoinModel.RefusalText("weird-code"), StringComparison.Ordinal);

    [Fact]
    public void TrustText_ShowsTheFingerprintAndTheAddress()
    {
        var text = JoinModel.TrustText("host:1", Fingerprint);
        Assert.Contains("host:1", text, StringComparison.Ordinal);
        Assert.Contains(HostKey.Normalize(Fingerprint)!, text, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyChangedText_OffersNoWayToContinue()
    {
        var text = JoinModel.KeyChangedText("host:1");
        Assert.Contains("changed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("continue anyway", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Connection_HiddenWhileConnectedAndWaitingWhileNot()
    {
        Assert.False(JoinModel.Connection(true, false, null).Visible);
        var lost = JoinModel.Connection(false, false, null);
        Assert.True(lost.Visible);
        Assert.False(lost.Final);
        Assert.Equal("Connection lost", lost.Title);
        var host = JoinModel.Connection(false, true, null);
        Assert.Equal("The host went away", host.Title);
        Assert.Equal("The host went away", JoinModel.Connection(true, true, null).Title);
    }

    [Fact]
    public void Connection_ReconnectFailed_SaysTheSeatIsKept()
    {
        var view = JoinModel.Connection(false, false, "reconnect-failed");
        Assert.Contains("seat is kept", view.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", view.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", JoinModel.RefusalText("reconnect-failed"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("reconnect-failed")]
    [InlineData("connection-lost")]
    [InlineData("kicked")]
    public void Connection_EndedIsFinalWhateverTheConnectionSays(string reason)
    {
        var view = JoinModel.Connection(true, false, reason);
        Assert.True(view.Visible);
        Assert.True(view.Final);
        Assert.Equal("Disconnected", view.Title);
        Assert.False(string.IsNullOrWhiteSpace(view.Detail));
    }
}
