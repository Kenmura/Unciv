using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using Unciv.Net.Identity;

namespace Unciv.Net.Relay;

/// <summary>A listing that passed every client check (<see cref="ListingVerifier"/>): the host key signed this body, the key is the one named by <see cref="FingerprintHex"/>, and every text obeys the listing rules.</summary>
/// <param name="Room">The relay's room code in display form (<c>K7QF-3MXP4A</c>); it is where the relay splices a join, not a claim of the host.</param>
/// <param name="FingerprintHex">SHA-256 of the host key as the relay reports it, 64 lower-case hex characters.</param>
/// <param name="Fingerprint">The same key in the 26-character spelling of invites and known hosts; what a join pins in the inner TLS.</param>
/// <param name="Info">The verified body of the listing: all of it claims of the host.</param>
/// <param name="AgeSeconds">How long the relay says the listing exists; unsigned, informational.</param>
public sealed record ListedGame(string Room, string FingerprintHex, string Fingerprint, ListingInfo Info, long AgeSeconds)
{
    /// <summary>The first three groups of <see cref="Fingerprint"/> (<c>ABCDE-FGHIJ-KLMNO</c>), enough to compare by eye with what a friend reads out.</summary>
    public string ShortFingerprint => string.Join('-', Fingerprint.Split('-').Take(3));
}

/// <summary>
/// T339 (design/RELAY.md section 7, "Client verification"; R7, A9): the client does not believe a relay. For every entry of <c>/v1/games</c> it checks that the certificate parses and is an ECDSA P-256 key, that the SHA-256 of
/// its public key is the <c>fp</c> shown, that the host key signed exactly the <c>body</c> string (under the domain-separation prefix), and that the body obeys the same schema and text rules the relay applies. An entry that fails
/// any of it is dropped, never shown. A copied listing therefore cannot be joined as the original: its key is whoever signed it.
/// </summary>
public static class ListingVerifier
{
    /// <summary>Longest certificate (base64 characters) and signature accepted; a self-signed P-256 certificate is about 600 characters.</summary>
    public const int MaxCertChars = 4096;
    public const int MaxSignatureChars = 256;

    /// <summary>Verifies one entry of the relay's games list. The reason is for logs and tests; the browser only counts dropped entries.</summary>
    public static (ListedGame? Game, string? Problem) Verify(JsonNode? entry)
    {
        if (entry is not JsonObject obj) return (null, "entry is not an object");
        var room = Text(obj, "room");
        var fpHex = Text(obj, "fp");
        var body = Text(obj, "body");
        var sig = Text(obj, "sig");
        var cert = Text(obj, "cert");
        if (room == null || fpHex == null || body == null || sig == null || cert == null) return (null, "missing field");
        if (RelayProtocol.NormalizeRoom(room) is not { } canonical) return (null, "bad room code");
        if (cert.Length > MaxCertChars || sig.Length > MaxSignatureChars) return (null, "oversized certificate or signature");
        try
        {
            using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(cert));
            var spki = certificate.PublicKey.ExportSubjectPublicKeyInfo();
            if (!string.Equals(RelayProtocol.FingerprintHex(spki), fpHex, StringComparison.Ordinal)) return (null, "fingerprint does not match the certificate");
            using var key = certificate.GetECDsaPublicKey();
            if (key == null || key.KeySize != 256) return (null, "not a P-256 key");
            var signed = Encoding.UTF8.GetBytes(RelayProtocol.ListingSignedText(body));
            if (!key.VerifyData(signed, Convert.FromBase64String(sig), HashAlgorithmName.SHA256)) return (null, "bad signature");
            var (info, error) = ListingRules.Parse(body);
            if (info == null) return (null, "body: " + error);
            return (new ListedGame(RelayProtocol.FormatRoom(canonical), fpHex, HostKey.Fingerprint(spki), info, Math.Max(0, Int(obj, "ageSeconds"))), null);
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
        {
            return (null, "unreadable certificate or signature");
        }
    }

    private static string? Text(JsonObject obj, string name) => obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static long Int(JsonObject obj, string name) => obj[name] is JsonValue value && (value.TryGetValue<long>(out var number) || (value.TryGetValue<int>(out var small) && (number = small) == small)) ? number : 0;
}
