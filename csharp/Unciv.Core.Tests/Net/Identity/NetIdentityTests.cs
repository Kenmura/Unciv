using Unciv.Net.Identity;

namespace Unciv.Core.Tests.Net.Identity;

internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan by) => now += by;
}

internal sealed class CountingRandom : ISecureRandom
{
    private byte next;
    public void Fill(Span<byte> buffer)
    {
        for (var i = 0; i < buffer.Length; i++) buffer[i] = unchecked(next++);
    }
}

public class NetIdentityKeyTests
{
    private const string Host = "AOIFR-RXSYD-FUSLC-THMFE-2FHPO-4";

    [Fact]
    public void ProofVerifiesForRightKeySessionAndChallenge()
    {
        using var id = ClientIdentity.Generate();
        var ch = new HostChallenge(Host, SystemSecureRandom.Instance);
        Assert.True(ch.Verify(id.PlayerKey, id.Sign(ch.HostFingerprint, ch.SessionId, ch.Challenge)));
    }

    [Fact]
    public void WrongKeyIsRejected()
    {
        using var owner = ClientIdentity.Generate();
        using var other = ClientIdentity.Generate();
        var ch = new HostChallenge(Host, SystemSecureRandom.Instance);
        Assert.False(ch.Verify(owner.PlayerKey, other.Sign(ch.HostFingerprint, ch.SessionId, ch.Challenge)));
    }

    [Fact]
    public void ReplayOnSameChallengeIsRejected()
    {
        using var id = ClientIdentity.Generate();
        var ch = new HostChallenge(Host, SystemSecureRandom.Instance);
        var proof = id.Sign(ch.HostFingerprint, ch.SessionId, ch.Challenge);
        Assert.True(ch.Verify(id.PlayerKey, proof));
        Assert.False(ch.Verify(id.PlayerKey, proof));
    }

    [Fact]
    public void ProofFromAnotherSessionIsRejected()
    {
        using var id = ClientIdentity.Generate();
        var recorded = new HostChallenge(Host, SystemSecureRandom.Instance);
        var proof = id.Sign(recorded.HostFingerprint, recorded.SessionId, recorded.Challenge);
        var fresh = new HostChallenge(Host, SystemSecureRandom.Instance);
        Assert.False(fresh.Verify(id.PlayerKey, proof));
    }

    [Fact]
    public void FailedAttemptConsumesChallenge()
    {
        using var id = ClientIdentity.Generate();
        var ch = new HostChallenge(Host, SystemSecureRandom.Instance);
        Assert.False(ch.Verify(id.PlayerKey, "garbage"));
        Assert.False(ch.Verify(id.PlayerKey, id.Sign(ch.HostFingerprint, ch.SessionId, ch.Challenge)));
    }

    [Fact]
    public void AuthMessageIsUnambiguous()
    {
        Assert.NotEqual(AuthMessage.Build("h", "ab", "c"), AuthMessage.Build("h", "a", "bc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("QUJD")]
    public void MalformedKeysAndProofsNeverThrow(string? junk)
    {
        using var id = ClientIdentity.Generate();
        Assert.False(PlayerKeys.IsValid(junk));
        Assert.False(PlayerKeys.VerifyProof(junk, "h", "s", "c", id.Sign("h", "s", "c")));
        Assert.False(PlayerKeys.VerifyProof(id.PlayerKey, "h", "s", "c", junk));
    }

    [Fact]
    public void ProofWithWrongLengthIsRejected()
    {
        using var id = ClientIdentity.Generate();
        Assert.False(PlayerKeys.VerifyProof(id.PlayerKey, "h", "s", "c", "AAAA"));
    }

    [Fact]
    public void NonP256KeyIsRejected()
    {
        using var p384 = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
        var key = Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.False(PlayerKeys.IsValid(key));
    }

    [Fact]
    public void KeyStoreRoundTripKeepsIdentity()
    {
        var store = new InMemoryIdentityKeyStore();
        string first;
        using (var a = ClientIdentity.LoadOrCreate(store)) first = a.PlayerKey;
        using var b = ClientIdentity.LoadOrCreate(store);
        Assert.Equal(first, b.PlayerKey);
    }

    [Fact]
    public void CorruptStoredKeyThrowsAndIsNotReplaced()
    {
        var store = new InMemoryIdentityKeyStore();
        store.Save([1, 2, 3]);
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => ClientIdentity.LoadOrCreate(store));
        Assert.Equal(new byte[] { 1, 2, 3 }, store.Load());
    }

    [Fact]
    public void WrongCurveStoredKeyThrows()
    {
        using var p384 = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
        var store = new InMemoryIdentityKeyStore();
        store.Save(p384.ExportPkcs8PrivateKey());
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => ClientIdentity.LoadOrCreate(store));
    }

    [Fact]
    public void SystemRandomFillsDifferentBytes()
    {
        var a = new byte[16];
        var b = new byte[16];
        SystemSecureRandom.Instance.Fill(a);
        SystemSecureRandom.Instance.Fill(b);
        Assert.NotEqual(a, b);
    }
}

public class NetIdentityHostKeyTests
{
    [Fact]
    public void FingerprintIsStableGroupedBase32()
    {
        var fp = HostKey.Fingerprint(new byte[] { 1, 2, 3 });
        Assert.Matches("^([A-Z2-7]{5}-){5}[A-Z2-7]$", fp);
        Assert.Equal(fp, HostKey.Fingerprint(new byte[] { 1, 2, 3 }));
        Assert.NotEqual(fp, HostKey.Fingerprint(new byte[] { 1, 2, 4 }));
        Assert.Equal("AOIFR-RXSYD-FUSLC-THMFE-2FHPO-4", HostKey.Fingerprint(new byte[] { 1, 2, 3 })); // python: b32(sha256(010203)[:16])
    }

    [Fact]
    public void CertificateFingerprintUsesPublicKey()
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=host", key, System.Security.Cryptography.HashAlgorithmName.SHA256);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(1));
        Assert.Equal(HostKey.Fingerprint(key.ExportSubjectPublicKeyInfo()), HostKey.Fingerprint(cert));
    }

    [Fact]
    public void NormalizeAcceptsLooseTextAndRejectsJunk()
    {
        const string fp = "AOIFR-RXSYD-FUSLC-THMFE-2FHPO-4";
        Assert.Equal(fp, HostKey.Normalize("aoifr rxsyd fuslc thmfe 2fhpo 4"));
        Assert.Equal(fp, HostKey.Normalize("AOIFRRXSYDFUSLCTHMFE2FHPO4"));
        Assert.Null(HostKey.Normalize(null));
        Assert.Null(HostKey.Normalize("short"));
        Assert.Null(HostKey.Normalize("AOIFR-RXSYD-FUSLC-THMFE-2FHPO-1"));
        Assert.True(HostKey.Matches(fp, "aoifr-rxsyd-fuslc-thmfe-2fhpo-4"));
        Assert.False(HostKey.Matches(fp, "AAAAA-AAAAA-AAAAA-AAAAA-AAAAA-A"));
        Assert.False(HostKey.Matches(fp, "junk"));
    }

    [Fact]
    public void KnownHostsTofuAndMismatch()
    {
        var hosts = new KnownHosts();
        var fp = HostKey.Fingerprint(new byte[] { 9 });
        var other = HostKey.Fingerprint(new byte[] { 8 });
        Assert.Equal(HostTrust.Unknown, hosts.Check("h:1", fp));
        Assert.True(hosts.Pin("h:1", fp));
        Assert.Equal(HostTrust.Trusted, hosts.Check("H:1", fp));
        Assert.Equal(HostTrust.Mismatch, hosts.Check("h:1", other));
        Assert.False(hosts.Pin("h:1", other));
        Assert.True(hosts.Pin("h:1", fp));
        Assert.False(hosts.Pin("h:2", "junk"));
        hosts.Forget("h:1");
        Assert.Equal(HostTrust.Unknown, hosts.Check("h:1", other));
    }
}

public class NetIdentityAdmissionTests
{
    private static JoinPassword Pw(string text) => JoinPassword.Create(text, new CountingRandom(), 10);

    [Fact]
    public void PasswordHashRoundTripsAndHidesPlaintext()
    {
        var pw = Pw("hunter2");
        Assert.True(pw.Verify("hunter2"));
        Assert.False(pw.Verify("hunter3"));
        Assert.False(pw.Verify(null));
        Assert.False(pw.Verify(new string('x', 1000)));
        var stored = pw.Serialize();
        Assert.DoesNotContain("hunter2", stored);
        var parsed = JoinPassword.TryParse(stored)!;
        Assert.True(parsed.Verify("hunter2"));
        Assert.False(parsed.Verify("nope"));
    }

    [Fact]
    public void PasswordSaltsDiffer()
    {
        var a = JoinPassword.Create("x", SystemSecureRandom.Instance, 10).Serialize();
        var b = JoinPassword.Create("x", SystemSecureRandom.Instance, 10).Serialize();
        Assert.NotEqual(a, b);
        Assert.StartsWith("pbkdf2-sha256$", JoinPassword.Create("x", SystemSecureRandom.Instance).Serialize(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("md5$1$AA$BB")]
    [InlineData("pbkdf2-sha256$0$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("pbkdf2-sha256$99999999999$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("pbkdf2-sha256$10$AA$AA")]
    [InlineData("pbkdf2-sha256$x$AA$AA")]
    public void MalformedStoredPasswordIsRejected(string? stored) => Assert.Null(JoinPassword.TryParse(stored));

    [Fact]
    public void CreateRejectsNonPositiveIterations() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => JoinPassword.Create("x", new CountingRandom(), 0));

    private static (LobbyAdmission adm, FakeClock clock, InviteTokens tokens) Make(string? password, bool inviteOnly = false)
    {
        var clock = new FakeClock();
        var tokens = new InviteTokens(clock, new CountingRandom());
        var adm = new LobbyAdmission(password == null ? null : Pw(password), inviteOnly, tokens, new AttemptLimiter(clock));
        return (adm, clock, tokens);
    }

    [Fact]
    public void OpenLobbyAdmitsAnyone()
    {
        var (adm, _, _) = Make(null);
        Assert.False(adm.RequiresPassword);
        Assert.Equal(AdmissionResult.Admitted, adm.Admit("1.1.1.1", null, null));
    }

    [Fact]
    public void PasswordLobbyNeedsPasswordOrToken()
    {
        var (adm, _, tokens) = Make("pw");
        Assert.True(adm.RequiresPassword);
        Assert.Equal(AdmissionResult.BadCredentials, adm.Admit("a", null, null));
        Assert.Equal(AdmissionResult.BadCredentials, adm.Admit("a", "wrong", null));
        Assert.Equal(AdmissionResult.Admitted, adm.Admit("a", "pw", null));
        Assert.Equal(AdmissionResult.Admitted, adm.Admit("b", null, tokens.Mint(TimeSpan.FromMinutes(5))));
    }

    [Fact]
    public void InviteOnlyLobbyIgnoresPasswordAndTokenIsSingleUse()
    {
        var (adm, _, tokens) = Make(null, inviteOnly: true);
        var token = tokens.Mint(TimeSpan.FromMinutes(5));
        Assert.Equal(AdmissionResult.BadCredentials, adm.Admit("a", "anything", null));
        Assert.Equal(AdmissionResult.Admitted, adm.Admit("a", null, token));
        Assert.Equal(AdmissionResult.BadCredentials, adm.Admit("a", null, token));
    }

    [Fact]
    public void BackOffAfterFiveBadPasswordsEvenForTheRightOne()
    {
        var (adm, clock, _) = Make("pw");
        for (var i = 0; i < AttemptLimiter.MaxFailures; i++)
            Assert.Equal(AdmissionResult.BadCredentials, adm.Admit("ip", "bad", null));
        Assert.Equal(AdmissionResult.RateLimited, adm.Admit("ip", "pw", null));
        Assert.Equal(AdmissionResult.Admitted, adm.Admit("other-ip", "pw", null));
        clock.Advance(AttemptLimiter.BaseLockout + TimeSpan.FromSeconds(1));
        Assert.Equal(AdmissionResult.Admitted, adm.Admit("ip", "pw", null));
    }

    [Fact]
    public void FourFailuresDoNotLockAndSuccessResets()
    {
        var (adm, _, _) = Make("pw");
        for (var i = 0; i < 4; i++) adm.Admit("ip", "bad", null);
        Assert.Equal(AdmissionResult.Admitted, adm.Admit("ip", "pw", null));
        for (var i = 0; i < 4; i++) adm.Admit("ip", "bad", null);
        Assert.Equal(AdmissionResult.Admitted, adm.Admit("ip", "pw", null));
    }
}

public class NetIdentityLimiterTests
{
    [Fact]
    public void LockoutDoublesAndIsCapped()
    {
        var clock = new FakeClock();
        var limiter = new AttemptLimiter(clock);
        for (var i = 0; i < 5; i++) Assert.True(limiter.TryBeginAttempt("k"));
        clock.Advance(AttemptLimiter.BaseLockout + TimeSpan.FromSeconds(1));
        Assert.True(limiter.IsAllowed("k"));
        Assert.True(limiter.TryBeginAttempt("k")); // 6th failure: 60 s
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(limiter.IsAllowed("k"));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(limiter.IsAllowed("k"));
        for (var i = 0; i < 20; i++)
        {
            clock.Advance(AttemptLimiter.MaxLockout + TimeSpan.FromSeconds(1));
            Assert.True(limiter.TryBeginAttempt("k"));
        }
        clock.Advance(AttemptLimiter.MaxLockout - TimeSpan.FromSeconds(1));
        Assert.False(limiter.IsAllowed("k"));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(limiter.IsAllowed("k"));
    }

    [Fact]
    public void FullTableEvictsOldestSoNewSourcesAreStillLimited()
    {
        var clock = new FakeClock();
        var limiter = new AttemptLimiter(clock);
        for (var i = 0; i < AttemptLimiter.MaxTrackedKeys + 10; i++)
        {
            limiter.TryBeginAttempt("k" + i);
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }
        for (var i = 0; i < AttemptLimiter.MaxFailures; i++) Assert.True(limiter.TryBeginAttempt("overflow"));
        Assert.False(limiter.IsAllowed("overflow"));
        Assert.False(limiter.TryBeginAttempt("overflow"));
    }

    [Fact]
    public void IdleEntriesDecay()
    {
        var clock = new FakeClock();
        var limiter = new AttemptLimiter(clock);
        for (var i = 0; i < 4; i++) limiter.TryBeginAttempt("k");
        clock.Advance(TimeSpan.FromHours(1));
        for (var i = 0; i < 4; i++) Assert.True(limiter.TryBeginAttempt("k"));
    }

    [Fact]
    public void ParallelGuessesAreCounted()
    {
        var limiter = new AttemptLimiter(new FakeClock());
        var allowed = 0;
        Parallel.For(0, 200, _ =>
        {
            if (limiter.TryBeginAttempt("ip")) Interlocked.Increment(ref allowed);
        });
        Assert.Equal(AttemptLimiter.MaxFailures, allowed);
    }
}

public class NetIdentityInviteTests
{
    [Fact]
    public void TokenIsSingleUse()
    {
        var tokens = new InviteTokens(new FakeClock(), SystemSecureRandom.Instance);
        var t = tokens.Mint(TimeSpan.FromMinutes(1))!;
        Assert.Equal(1, tokens.Outstanding);
        Assert.True(tokens.Redeem(t));
        Assert.False(tokens.Redeem(t));
        Assert.Equal(0, tokens.Outstanding);
    }

    [Fact]
    public void ExpiredTokenIsRejected()
    {
        var clock = new FakeClock();
        var tokens = new InviteTokens(clock, SystemSecureRandom.Instance);
        var t = tokens.Mint(TimeSpan.FromMinutes(1))!;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(tokens.Redeem(t));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nope")]
    public void UnknownTokenIsRejected(string? t) => Assert.False(new InviteTokens(new FakeClock(), SystemSecureRandom.Instance).Redeem(t));

    [Fact]
    public void OverlongTokenIsRejected() => Assert.False(new InviteTokens(new FakeClock(), SystemSecureRandom.Instance).Redeem(new string('a', 500)));

    [Fact]
    public void MintIsCappedAndExpiredOnesFreeRoom()
    {
        var clock = new FakeClock();
        var tokens = new InviteTokens(clock, SystemSecureRandom.Instance);
        for (var i = 0; i < InviteTokens.MaxOutstanding; i++) Assert.NotNull(tokens.Mint(TimeSpan.FromMinutes(1)));
        Assert.Null(tokens.Mint(TimeSpan.FromMinutes(1)));
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.NotNull(tokens.Mint(TimeSpan.FromMinutes(1)));
        Assert.Equal(1, tokens.Outstanding);
    }

    [Fact]
    public void MintRejectsNonPositiveLifetime() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new InviteTokens(new FakeClock(), SystemSecureRandom.Instance).Mint(TimeSpan.Zero));
}

public class NetIdentitySlotTests
{
    [Fact]
    public void LeakedPasswordCannotTakeBoundSeat()
    {
        using var owner = ClientIdentity.Generate();
        using var intruder = ClientIdentity.Generate();
        var clock = new FakeClock();
        var adm = new LobbyAdmission(JoinPassword.Create("pw", new CountingRandom(), 10), false, new InviteTokens(clock, new CountingRandom()), new AttemptLimiter(clock));
        var slots = new SlotBindings();
        Assert.True(slots.TryBind(0, owner.PlayerKey));

        // intruder knows the password, so admission passes...
        Assert.Equal(AdmissionResult.Admitted, adm.Admit("evil", "pw", null));
        // ...but the seat is bound to the owner's key
        Assert.False(slots.TryBind(0, intruder.PlayerKey));
        Assert.False(slots.IsOwner(0, intruder.PlayerKey));
        // and a resume proof signed by the intruder does not verify for the owner's key
        var ch = new HostChallenge("h", SystemSecureRandom.Instance);
        Assert.False(ch.Verify(owner.PlayerKey, intruder.Sign(ch.HostFingerprint, ch.SessionId, ch.Challenge)));
        Assert.True(slots.IsOwner(0, owner.PlayerKey));
    }

    [Fact]
    public void BindingRules()
    {
        using var a = ClientIdentity.Generate();
        using var b = ClientIdentity.Generate();
        var slots = new SlotBindings();
        Assert.False(slots.TryBind(0, "junk"));
        Assert.True(slots.TryBind(0, a.PlayerKey));
        Assert.True(slots.TryBind(0, a.PlayerKey));
        Assert.False(slots.TryBind(1, a.PlayerKey)); // one seat per key
        Assert.Equal(0, slots.SlotOf(a.PlayerKey));
        Assert.Null(slots.SlotOf(b.PlayerKey));
        Assert.True(slots.Clear(0));
        Assert.False(slots.Clear(0));
        Assert.True(slots.TryBind(0, b.PlayerKey));
    }

    [Fact]
    public void RestoreReplacesBindingsAndDropsInvalid()
    {
        using var a = ClientIdentity.Generate();
        var slots = new SlotBindings();
        slots.TryBind(5, a.PlayerKey);
        var dropped = slots.Restore([KeyValuePair.Create(1, a.PlayerKey), KeyValuePair.Create(2, "junk"), KeyValuePair.Create(3, a.PlayerKey)]);
        Assert.Equal([2, 3], dropped);
        Assert.Single(slots.Bindings);
        Assert.True(slots.IsOwner(1, a.PlayerKey));
    }
}

public class NetIdentityHardeningTests
{
    private const string HostA = "AOIFR-RXSYD-FUSLC-THMFE-2FHPO-4";
    private const string HostB = "BBBBB-BBBBB-BBBBB-BBBBB-BBBBB-B";

    [Fact]
    public void ProofForOneHostCannotBeRelayedToAnother()
    {
        using var id = ClientIdentity.Generate();
        var onB = new HostChallenge(HostB, SystemSecureRandom.Instance);
        // a malicious host A forwards B's session and challenge to the client, which signs them for A
        var relayed = id.Sign(HostA, onB.SessionId, onB.Challenge);
        Assert.False(onB.Verify(id.PlayerKey, relayed));
        var onB2 = new HostChallenge(HostB, SystemSecureRandom.Instance);
        Assert.True(onB2.Verify(id.PlayerKey, id.Sign(HostB, onB2.SessionId, onB2.Challenge)));
    }

    [Fact]
    public void KeyAliasesAreRejected()
    {
        using var id = ClientIdentity.Generate();
        var key = id.PlayerKey;
        Assert.True(PlayerKeys.IsValid(key));
        Assert.False(PlayerKeys.IsValid(key + "="));
        Assert.False(PlayerKeys.IsValid(key.Insert(5, " ")));
        var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var last = key[^1];
        var tweaked = key[..^1] + alphabet.First(c => c != last && alphabet.IndexOf(c, StringComparison.Ordinal) / 4 == alphabet.IndexOf(last, StringComparison.Ordinal) / 4);
        Assert.False(PlayerKeys.IsValid(tweaked));
        var slots = new SlotBindings();
        Assert.True(slots.TryBind(0, key));
        Assert.False(slots.TryBind(1, key + "="));
        Assert.False(slots.TryBind(1, tweaked));
    }

    [Fact]
    public void ExplicitCurveKeyIsRejectedWithoutThrowing()
    {
        var p = System.Security.Cryptography.ECCurve.NamedCurves.nistP256;
        using var named = System.Security.Cryptography.ECDsa.Create(p);
        var ex = named.ExportExplicitParameters(false);
        using var explicitKey = System.Security.Cryptography.ECDsa.Create(ex);
        var key = Convert.ToBase64String(explicitKey.ExportSubjectPublicKeyInfo()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.False(PlayerKeys.IsValid(key));
    }

    [Fact]
    public void OverlongPasswordIsRefusedAtCreate() =>
        Assert.Throws<ArgumentException>(() => JoinPassword.Create(new string('x', JoinPassword.MaxPasswordBytes + 1), new CountingRandom(), 10));

    [Fact]
    public void MaxLengthPasswordRoundTrips()
    {
        var text = new string('x', JoinPassword.MaxPasswordBytes);
        Assert.True(JoinPassword.Create(text, new CountingRandom(), 10).Verify(text));
    }

    [Fact]
    public void ExcessiveStoredIterationsAreRejected() =>
        Assert.Null(JoinPassword.TryParse("pbkdf2-sha256$5000000$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));

    [Fact]
    public void ConcurrentRedeemSucceedsOnce()
    {
        var tokens = new InviteTokens(new FakeClock(), SystemSecureRandom.Instance);
        var t = tokens.Mint(TimeSpan.FromMinutes(1))!;
        var wins = 0;
        Parallel.For(0, 50, _ =>
        {
            if (tokens.Redeem(t)) Interlocked.Increment(ref wins);
        });
        Assert.Equal(1, wins);
    }
}
