using System.Security.Cryptography;

namespace Unciv.Net.Identity;

/// <summary>
/// One connection's challenge (<c>Welcome.Challenge</c>). A proof is accepted at most once, so a recorded
/// <c>Auth</c> frame cannot be replayed on the same session, and it is bound to the session id so it cannot be
/// replayed on another.
/// </summary>
public sealed class HostChallenge
{
    private int used;

    public HostChallenge(ISecureRandom random)
    {
        SessionId = Base64Url.Encode(random.Bytes(16));
        Challenge = Base64Url.Encode(random.Bytes(32));
    }

    public string SessionId { get; }
    public string Challenge { get; }

    /// <summary>Verifies <paramref name="proof"/> for <paramref name="playerKey"/>. The challenge is consumed by the first attempt, success or not.</summary>
    public bool Verify(string? playerKey, string? proof)
    {
        if (Interlocked.Exchange(ref used, 1) != 0) return false;
        return PlayerKeys.VerifyProof(playerKey, SessionId, Challenge, proof);
    }
}

/// <summary>Host key fingerprint (SERVER.md 6.4) and trust-on-first-use pinning.</summary>
public static class HostKey
{
    /// <summary>First 128 bits of SHA-256 over the SubjectPublicKeyInfo, as 26 base32 characters grouped in fives, e.g. <c>ABCDE-FGHIJ-...-Z</c>.</summary>
    public static string Fingerprint(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(subjectPublicKeyInfo, hash);
        return Base32Group(hash[..16]);
    }

    public static string Fingerprint(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate) =>
        Fingerprint(certificate.PublicKey.ExportSubjectPublicKeyInfo());

    /// <summary>Canonicalises user-typed or invite text (case, spaces, dashes); null when it is not a fingerprint.</summary>
    public static string? Normalize(string? text)
    {
        if (text == null) return null;
        var compact = new string(text.Where(c => c is not ('-' or ' ')).ToArray()).ToUpperInvariant();
        if (compact.Length != 26 || compact.Any(c => !(c is >= 'A' and <= 'Z' or >= '2' and <= '7'))) return null;
        return Group(compact);
    }

    /// <summary>Constant-time comparison of two fingerprints (after normalisation); false when either is malformed.</summary>
    public static bool Matches(string? a, string? b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na == null || nb == null) return false;
        return CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(na), System.Text.Encoding.ASCII.GetBytes(nb));
    }

    private static string Base32Group(ReadOnlySpan<byte> bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var chars = new char[26];
        var buffer = 0;
        var bits = 0;
        var n = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                chars[n++] = alphabet[(buffer >> (bits - 5)) & 31];
                bits -= 5;
            }
        }
        if (bits > 0) chars[n++] = alphabet[(buffer << (5 - bits)) & 31];
        return Group(new string(chars, 0, n));
    }

    private static string Group(string compact) =>
        string.Join('-', Enumerable.Range(0, (compact.Length + 4) / 5).Select(i => compact.Substring(i * 5, Math.Min(5, compact.Length - i * 5))));
}

/// <summary>Outcome of checking a presented host fingerprint against the pinned one.</summary>
public enum HostTrust
{
    /// <summary>Nothing pinned for this host: ask the user, then <see cref="KnownHosts.Pin"/>.</summary>
    Unknown,
    Trusted,
    /// <summary>Hard stop: "host key changed". Never silently accepted.</summary>
    Mismatch,
}

/// <summary>The client's <c>known-hosts.json</c> content: host address to pinned fingerprint.</summary>
public sealed class KnownHosts
{
    private readonly Dictionary<string, string> pins = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Pins => pins;

    public HostTrust Check(string hostAddress, string presentedFingerprint)
    {
        if (!pins.TryGetValue(hostAddress, out var pinned)) return HostTrust.Unknown;
        return HostKey.Matches(pinned, presentedFingerprint) ? HostTrust.Trusted : HostTrust.Mismatch;
    }

    /// <summary>Pins a fingerprint the user accepted (or that came from an invite). Refuses to overwrite a different pin.</summary>
    public bool Pin(string hostAddress, string fingerprint)
    {
        var normalized = HostKey.Normalize(fingerprint);
        if (normalized == null) return false;
        if (pins.TryGetValue(hostAddress, out var existing)) return HostKey.Matches(existing, normalized);
        pins[hostAddress] = normalized;
        return true;
    }

    /// <summary>Explicit user action after a verified key change.</summary>
    public void Forget(string hostAddress) => pins.Remove(hostAddress);
}
