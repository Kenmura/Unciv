using System.Net;
using Unciv.Net.Relay;
using Unciv.Relay;

namespace Unciv.Core.Tests.Relay;

// T337: the pieces that need no running relay: command line and config, PROXY headers, policy files, codes.
public sealed class RelayUnitTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("unciv-relay-u-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private static RelayOptions Parse(params string[] args) => RelayOptions.Parse(args);

    private static string Usage(params string[] args) => Assert.Throws<RelayUsageException>(() => RelayOptions.Parse(args)).Message;

    [Fact]
    public void DefaultsMatchTheDesign()
    {
        var o = Parse("--self-signed", "--public-name", "Relay.Example.org");
        Assert.Equal(RelayTlsMode.SelfSigned, o.TlsMode);
        Assert.Equal(["relay.example.org"], o.PublicNames);
        Assert.Equal(47830, o.TunnelPort);
        Assert.Equal(47831, o.HttpPort);
        Assert.Equal(200, o.Limits.MaxRooms);
        Assert.Equal(600, o.Limits.MaxSplices);
        Assert.Equal(2, o.Limits.MaxRoomsPerNetwork);
        Assert.Equal(40, o.Limits.MaxConnectionsPerNetwork);
        Assert.Equal(16, o.Limits.MaxJoinersPerRoom);
        Assert.Equal(8, o.Limits.MaxPendingPerRoom);
        Assert.Equal(3, o.Limits.MaxJoinersPerNetwork);
        Assert.Equal(2L * 1024 * 1024, o.Limits.SpliceRate);
        Assert.Equal(48L * 1024 * 1024, o.Limits.SpliceBurst);
        Assert.Equal(20L * 1024 * 1024 * 1024, o.Limits.RoomBytesPerDay);
        Assert.Equal(120, o.Limits.IdleSeconds);
        Assert.Equal(7, o.LogRetentionDays);
        Assert.Equal(ListingPolicy.Open, o.Listing);
        Assert.Equal(2, o.TrustedProxies.Count); // loopback only
        Assert.Null(new RelayOptions().TlsMode);
    }

    [Fact]
    public void EveryNumericOptionIsParsedWithItsRange()
    {
        var o = Parse("--tls", "none", "--public-name", "x.example.org", "--max-rooms", "5", "--splice-rate", "1000", "--max-gib-per-month", "3", "--bind", "::1", "--listing", "allowlist",
            "--trusted-proxy", "10.0.0.0/8", "--trusted-proxy", "192.168.1.1", "--proxy-protocol", "--no-http", "--metrics", "--name", "N", "--contact", "c", "--motd", "m", "--log-level", "warn");
        Assert.Equal(5, o.Limits.MaxRooms);
        Assert.Equal(1000, o.Limits.SpliceRate);
        Assert.Equal(3, o.Limits.MaxGibPerMonth);
        Assert.Equal(ListingPolicy.Allowlist, o.Listing);
        Assert.Equal(2, o.TrustedProxies.Count);
        Assert.True(o.TrustedProxies[0].Contains(IPAddress.Parse("10.1.2.3")));
        Assert.True(o.ProxyProtocol && o.NoHttp && o.Metrics);
        Assert.Equal(("N", "c", "m", "warn"), (o.Name, o.Contact, o.Motd, o.LogLevel));
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("stray")]
    [InlineData("--")]
    public void UnknownOptionsAndStrayArgumentsAreUsageErrors(string arg) => Assert.NotEmpty(Usage("--self-signed", "--public-name", "x", arg));

    [Fact]
    public void BadOptionCombinationsAreUsageErrors()
    {
        Assert.Contains("needs a value", Usage("--max-rooms"), StringComparison.Ordinal);
        Assert.Contains("given twice", Usage("--max-rooms", "1", "--max-rooms", "2"), StringComparison.Ordinal);
        Assert.Contains("whole number", Usage("--max-rooms", "0"), StringComparison.Ordinal);
        Assert.Contains("whole number", Usage("--max-rooms", "-1"), StringComparison.Ordinal);
        Assert.Contains("--cert and --key", Usage("--cert", "a"), StringComparison.Ordinal);
        Assert.Contains("choose one", Usage("--cert", "a", "--key", "b", "--self-signed", "--public-name", "x"), StringComparison.Ordinal);
        Assert.Contains("--public-name is required", Usage("--tls", "none"), StringComparison.Ordinal);
        Assert.Contains("--public-name is required", Usage("--self-signed"), StringComparison.Ordinal);
        Assert.Contains("--tls must be", Usage("--tls", "yes"), StringComparison.Ordinal);
        Assert.Contains("--http-tls", Usage("--tls", "none", "--public-name", "x", "--http-tls"), StringComparison.Ordinal);
        Assert.Contains("exclude", Usage("--self-signed", "--public-name", "x", "--http-tls", "--no-http"), StringComparison.Ordinal);
        Assert.Contains("CIDR", Usage("--trusted-proxy", "nope"), StringComparison.Ordinal);
        Assert.Contains("--listing", Usage("--listing", "maybe"), StringComparison.Ordinal);
        Assert.Contains("--log-level", Usage("--log-level", "loud"), StringComparison.Ordinal);
        Assert.Contains("not an IP", Usage("--bind", "host"), StringComparison.Ordinal);
        Assert.Contains("must not be empty", Usage("--self-signed", "--public-name", " "), StringComparison.Ordinal);
    }

    [Fact]
    public void HelpIsRecognisedAndPublicNamesAreCanonical()
    {
        Assert.True(Parse("--help").Help);
        Assert.True(Parse("-h").Help);
        Assert.Contains("--public-name", RelayOptions.HelpText, StringComparison.Ordinal);
        Assert.Equal("2001:db8::1", RelayOptions.NormalizeName("[2001:DB8::1]"));
        Assert.Equal("relay.example.org", RelayOptions.NormalizeName(" RELAY.example.ORG "));
    }

    [Fact]
    public void AConfigFileFillsInWhatTheCommandLineLeavesOut()
    {
        var json = """
            { // comment
              "tls": "none", "public-name": ["a.example.org", "b.example.org"], "max-rooms": 7, "metrics": true, "proxy-protocol": false, "contact": "me",
            }
            """;
        var o = RelayOptions.ParseWithConfig(["--data-dir", dir, "--contact", "cli"], path => path.EndsWith("relay.json", StringComparison.Ordinal) ? json : null);
        Assert.Equal(RelayTlsMode.None, o.TlsMode);
        Assert.Equal(["a.example.org", "b.example.org"], o.PublicNames);
        Assert.Equal(7, o.Limits.MaxRooms);
        Assert.True(o.Metrics);
        Assert.False(o.ProxyProtocol);
        Assert.Equal("cli", o.Contact); // the command line wins
    }

    [Fact]
    public void ConfigFileErrorsAreUsageErrors()
    {
        RelayUsageException Fail(string? json, params string[] args) => Assert.Throws<RelayUsageException>(() => RelayOptions.ParseWithConfig(args, _ => json));
        Assert.Contains("cannot be read", Fail(null, "--config", "missing.json").Message, StringComparison.Ordinal);
        Assert.Contains("not valid JSON", Fail("{", "--self-signed").Message, StringComparison.Ordinal);
        Assert.Contains("JSON object", Fail("[]", "--self-signed").Message, StringComparison.Ordinal);
        Assert.Contains("unknown key", Fail("""{"nope": 1}""", "--self-signed").Message, StringComparison.Ordinal);
        Assert.Contains("inside a config", Fail("""{"config": "x"}""", "--self-signed").Message, StringComparison.Ordinal);
        Assert.Contains("true or false", Fail("""{"metrics": 3}""", "--self-signed").Message, StringComparison.Ordinal);
        Assert.Contains("text or a number", Fail("""{"contact": {}}""", "--self-signed").Message, StringComparison.Ordinal);
        Assert.True(RelayOptions.ParseWithConfig(["--help"], _ => null).Help);
        Assert.Null(RelayOptions.ParseWithConfig([], _ => null).TlsMode); // no file is fine unless one was named
    }

    [Fact]
    public async Task ProxyHeadersOfBothVersionsAreParsed()
    {
        static async Task<IPAddress?> Read(byte[] bytes) => await ProxyProtocol.ReadAsync(new MemoryStream(bytes), CancellationToken.None);
        Assert.Equal(IPAddress.Parse("1.2.3.4"), await Read("PROXY TCP4 1.2.3.4 5.6.7.8 1 2\r\n"u8.ToArray()));
        Assert.Equal(IPAddress.Parse("2001:db8::1"), await Read("PROXY TCP6 2001:db8::1 ::1 1 2\r\n"u8.ToArray()));
        Assert.Null(await Read("PROXY UNKNOWN\r\n"u8.ToArray()));
        byte[] sig = [0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A];
        Assert.Null(await Read([.. sig, 0x20, 0x00, 0x00, 0x00])); // LOCAL
        Assert.Equal(IPAddress.Parse("9.8.7.6"), await Read([.. sig, 0x21, 0x11, 0x00, 0x0C, 9, 8, 7, 6, 1, 1, 1, 1, 0, 1, 0, 2]));
        var v6 = new byte[36];
        IPAddress.Parse("2001:db8::7").TryWriteBytes(v6, out _);
        Assert.Equal(IPAddress.Parse("2001:db8::7"), await Read([.. sig, 0x21, 0x21, 0x00, 0x24, .. v6]));
        Assert.Null(await Read([.. sig, 0x21, 0x31, 0x00, 0x00])); // an address family we do not read
        foreach (var bad in new[] { "GET / HTTP/1.1\r\n", "PROXY TCP4 nope\r\n", "PROXY TCP4 1.2.3.4 5.6.7.8 1\r\n", "PROXY ", "PROXY TCP4 " + new string('1', 200) + "\r\n" })
            await Assert.ThrowsAsync<InvalidDataException>(() => Read(System.Text.Encoding.ASCII.GetBytes(bad)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read([.. sig[..6], 1, 2, 3, 4, 5, 6, 7, 8, 9, 10])); // broken signature tail
        await Assert.ThrowsAsync<InvalidDataException>(() => Read([.. sig, 0x11, 0x11, 0x00, 0x00])); // version 1 in a v2 frame
        await Assert.ThrowsAsync<InvalidDataException>(() => Read([.. sig, 0x23, 0x11, 0x00, 0x00])); // unknown command
        await Assert.ThrowsAsync<InvalidDataException>(() => Read([.. sig, 0x21, 0x11, 0x0F, 0xFF])); // longer than any header
    }

    [Fact]
    public void PolicyFilesParseBansAllowAndDenyAndReportBadLines()
    {
        File.WriteAllText(Path.Combine(dir, "bans.txt"), "# c\n10.0.0.0/8\n2001:db8::1\n::ffff:9.9.9.9\nkey ABCDE-FGHIJ-KLMNO-PQRST-UVWXY-Z\nkey 0000000000000000000000000000000000000000000000000000000000000001\nkey nonsense\nname ^bad\nname (\nwhat is this\n");
        File.WriteAllText(Path.Combine(dir, "allow-keys.txt"), "garbage\n");
        var files = new RelayPolicyFiles(dir);
        Assert.True(files.Reload());
        Assert.False(files.Reload());
        Assert.Equal(4, files.Warnings.Count); // garbage allow key, nonsense key, bad regex, unreadable line
        Assert.True(files.IsAddressBanned(IPAddress.Parse("10.2.3.4")));
        Assert.True(files.IsAddressBanned(IPAddress.Parse("2001:db8::1")));
        Assert.True(files.IsAddressBanned(IPAddress.Parse("9.9.9.9")));
        Assert.False(files.IsAddressBanned(IPAddress.Parse("11.0.0.1")));
        Assert.True(files.IsNameDenied("Bad game"));
        Assert.False(files.IsNameDenied("Good game"));
        Assert.False(files.IsKeyBanned(new byte[] { 1, 2, 3 }));
        Assert.False(files.IsKeyAllowed(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void ARunawayRegexFailsClosed()
    {
        File.WriteAllText(Path.Combine(dir, "deny-names.txt"), "^(a+)+$\n");
        var files = new RelayPolicyFiles(dir);
        files.Reload();
        Assert.True(files.IsNameDenied(new string('a', 40) + "!"));
    }

    [Fact]
    public void RoomCodesAreCrockfordBase32AndNetworksKeyOnTheIpv6Prefix()
    {
        var code = RelayProtocol.RoomCodeFromHash(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1 });
        Assert.Equal("ZZZZZZZZZZ", code);
        Assert.Equal("0000000000", RelayProtocol.RoomCodeFromHash(new byte[8]));
        Assert.Equal("K7QF-3MXP4A", RelayProtocol.FormatRoom("K7QF3MXP4A"));
        Assert.Equal("K7QF3MXP4A", RelayProtocol.NormalizeRoom("k7qf-3mxp 4a"));
        Assert.Equal("0113456789", RelayProtocol.NormalizeRoom("O1l3-456789"));
        Assert.Null(RelayProtocol.NormalizeRoom("K7QF-3MXP4"));
        Assert.Null(RelayProtocol.NormalizeRoom("K7QF-3MXP4U"));
        Assert.Null(RelayProtocol.NormalizeRoom(null));
        Assert.Null(RelayProtocol.NormalizeRoom(new string('0', 40)));
        Assert.Equal("1.2.3.4", RelayProtocol.NetworkKey(IPAddress.Parse("::ffff:1.2.3.4")));
        Assert.Equal(RelayProtocol.NetworkKey(IPAddress.Parse("2001:db8:1:2::1")), RelayProtocol.NetworkKey(IPAddress.Parse("2001:db8:1:2:3:4:5:6")));
        Assert.NotEqual(RelayProtocol.NetworkKey(IPAddress.Parse("2001:db8:1:2::1")), RelayProtocol.NetworkKey(IPAddress.Parse("2001:db8:1:3::1")));
        Assert.Equal(RelayProtocol.GameTag("a"), RelayProtocol.GameTag("a"));
        Assert.NotEqual(RelayProtocol.GameTag("a"), RelayProtocol.GameTag("b"));
        Assert.Equal(24, RelayProtocol.GameTag("a").Length);
    }
}
