using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Unciv.Net.Relay;

namespace Unciv.Core.Tests.Relay;

// T339, RELAY.md R7: a client never trusts the relay's directory; it re-checks every entry itself.
public sealed class ListingVerifierTests
{
    private static JsonObject Entry(TestHostKey key, string body, string room = "k7m2-9xq4ab", string? fp = null, string? sig = null) => new()
    {
        ["room"] = room,
        ["fp"] = fp ?? key.FingerprintHex,
        ["body"] = body,
        ["sig"] = sig ?? key.Sign(RelayProtocol.ListingSignedText(body)),
        ["cert"] = key.CertBase64,
        ["ageSeconds"] = 12,
    };

    [Fact]
    public void AValidEntryVerifiesAndCarriesTheHostsFingerprint()
    {
        using var key = new TestHostKey();
        var (game, problem) = ListingVerifier.Verify(Entry(key, TestHost.ListingBody(3, mods: ["Mod A"])));
        Assert.Null(problem);
        Assert.NotNull(game);
        Assert.Equal("Friendly game", game.Info.Name);
        Assert.Equal(["Mod A"], game.Info.Mods);
        Assert.Equal(key.InviteFingerprint, game.Fingerprint);
        Assert.Equal(key.FingerprintHex, game.FingerprintHex);
        Assert.Equal(12, game.AgeSeconds);
        Assert.Equal(3, game.ShortFingerprint.Split('-').Length);
        Assert.StartsWith(game.ShortFingerprint, game.Fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherCurveOf256BitsIsNotP256()
    {
        ECDsa other;
        try { other = ECDsa.Create(ECCurve.CreateFromFriendlyName("secp256k1")); }
        catch (Exception e) when (e is PlatformNotSupportedException or CryptographicException) { return; } // this platform has no such curve: nothing to forge with
        using (other)
        {
            var certificate = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=K1", other, HashAlgorithmName.SHA256).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            var body = TestHost.ListingBody(1);
            var entry = new JsonObject
            {
                ["room"] = "k7m2-9xq4ab",
                ["fp"] = RelayProtocol.FingerprintHex(certificate.PublicKey.ExportSubjectPublicKeyInfo()),
                ["body"] = body,
                ["sig"] = Convert.ToBase64String(other.SignData(System.Text.Encoding.UTF8.GetBytes(RelayProtocol.ListingSignedText(body)), HashAlgorithmName.SHA256)),
                ["cert"] = Convert.ToBase64String(certificate.RawData),
            };
            var (game, problem) = ListingVerifier.Verify(entry);
            Assert.Null(game);
            Assert.Equal("not a P-256 key", problem);
        }
    }

    [Fact]
    public void AnAlteredBodyFailsTheSignature()
    {
        using var key = new TestHostKey();
        var entry = Entry(key, TestHost.ListingBody(1));
        entry["body"] = TestHost.ListingBody(1, name: "Better game");
        Assert.NotNull(ListingVerifier.Verify(entry).Problem);
    }

    [Fact]
    public void AnotherHostsCertificateFailsTheFingerprint()
    {
        using var key = new TestHostKey();
        using var other = new TestHostKey();
        var entry = Entry(key, TestHost.ListingBody(1));
        entry["cert"] = other.CertBase64;
        Assert.NotNull(ListingVerifier.Verify(entry).Problem);
    }

    [Fact]
    public void AClaimedFingerprintThatIsNotTheCertificatesFails()
    {
        using var key = new TestHostKey();
        using var other = new TestHostKey();
        Assert.NotNull(ListingVerifier.Verify(Entry(key, TestHost.ListingBody(1), fp: other.FingerprintHex)).Problem);
    }

    [Fact]
    public void ASignatureFromAnotherKeyFails()
    {
        using var key = new TestHostKey();
        using var other = new TestHostKey();
        var body = TestHost.ListingBody(1);
        Assert.NotNull(ListingVerifier.Verify(Entry(key, body, sig: other.Sign(RelayProtocol.ListingSignedText(body)))).Problem);
    }

    [Fact]
    public void ABodySignedWithoutTheDomainPrefixFails()
    {
        using var key = new TestHostKey();
        var body = TestHost.ListingBody(1);
        Assert.NotNull(ListingVerifier.Verify(Entry(key, body, sig: key.Sign(body))).Problem);
    }

    [Theory]
    [InlineData("room")]
    [InlineData("fp")]
    [InlineData("body")]
    [InlineData("sig")]
    [InlineData("cert")]
    public void AMissingFieldFails(string field)
    {
        using var key = new TestHostKey();
        var entry = Entry(key, TestHost.ListingBody(1));
        entry.Remove(field);
        Assert.NotNull(ListingVerifier.Verify(entry).Problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NOT-A-ROOM")]
    [InlineData("k7m2-9xq")]
    public void ABadRoomCodeFails(string room)
    {
        using var key = new TestHostKey();
        Assert.NotNull(ListingVerifier.Verify(Entry(key, TestHost.ListingBody(1), room: room)).Problem);
    }

    [Fact]
    public void NonObjectsAndWrongTypesFail()
    {
        Assert.NotNull(ListingVerifier.Verify(null).Problem);
        Assert.NotNull(ListingVerifier.Verify(JsonValue.Create(5)).Problem);
        using var key = new TestHostKey();
        var entry = Entry(key, TestHost.ListingBody(1));
        entry["sig"] = 7;
        Assert.NotNull(ListingVerifier.Verify(entry).Problem);
    }

    [Fact]
    public void OversizeOrMalformedCertificatesAndSignaturesFail()
    {
        using var key = new TestHostKey();
        var body = TestHost.ListingBody(1);
        var big = Entry(key, body);
        big["cert"] = new string('A', ListingVerifier.MaxCertChars + 4);
        Assert.NotNull(ListingVerifier.Verify(big).Problem);
        var junk = Entry(key, body);
        junk["cert"] = "!!!not base64!!!";
        Assert.NotNull(ListingVerifier.Verify(junk).Problem);
        var notCert = Entry(key, body);
        notCert["cert"] = Convert.ToBase64String("hello"u8.ToArray());
        Assert.NotNull(ListingVerifier.Verify(notCert).Problem);
        var longSig = Entry(key, body);
        longSig["sig"] = new string('A', ListingVerifier.MaxSignatureChars + 4);
        Assert.NotNull(ListingVerifier.Verify(longSig).Problem);
    }

    [Fact]
    public void ANonP256KeyFails()
    {
        using var rsa = RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Rsa", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var body = TestHost.ListingBody(1);
        var entry = new JsonObject
        {
            ["room"] = "k7m2-9xq4ab",
            ["fp"] = RelayProtocol.FingerprintHex(cert.PublicKey.ExportSubjectPublicKeyInfo()),
            ["body"] = body,
            ["sig"] = Convert.ToBase64String(rsa.SignData(System.Text.Encoding.UTF8.GetBytes(RelayProtocol.ListingSignedText(body)), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
            ["cert"] = Convert.ToBase64String(cert.RawData),
        };
        Assert.NotNull(ListingVerifier.Verify(entry).Problem);
    }

    [Theory]
    [InlineData("Evil‮game")] // right-to-left override
    [InlineData("Bell\u0007game")] // control
    [InlineData("Zero​width")] // format
    [InlineData("Privateuse")]
    [InlineData("ab")] // too short
    public void AValidlySignedButHostileNameIsStillRefused(string name)
    {
        using var key = new TestHostKey();
        Assert.NotNull(ListingVerifier.Verify(Entry(key, TestHost.ListingBody(1, name: name))).Problem);
    }

    [Fact]
    public void AValidlySignedBodyWithBadNumbersOrJsonIsRefused()
    {
        using var key = new TestHostKey();
        Assert.NotNull(ListingVerifier.Verify(Entry(key, "not json")).Problem);
        Assert.NotNull(ListingVerifier.Verify(Entry(key, TestHost.ListingBody(1, players: 65, slots: 4))).Problem);
        Assert.NotNull(ListingVerifier.Verify(Entry(key, "[]")).Problem);
    }

    [Fact]
    public void CleanNormalisesStripsAndTruncatesWithoutSplittingPairs()
    {
        Assert.Equal("Café".Normalize(System.Text.NormalizationForm.FormC), ListingRules.Clean("Café", 40));
        Assert.Equal("ab", ListingRules.Clean("  a‮b\u0007 ", 40));
        var cut = ListingRules.Clean("ab\U0001F600\U0001F600", 3);
        Assert.Equal("ab", cut);
        Assert.True(ListingRules.TextOk("Hello", 3, 40));
        Assert.False(ListingRules.TextOk(null, 1, 5));
        Assert.False(ListingRules.TextOk("toolong", 1, 5));
    }
}
