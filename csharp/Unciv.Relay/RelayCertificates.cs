using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Unciv.Net.Identity;
using Unciv.Net.Turns;

namespace Unciv.Relay;

/// <summary>The relay's own TLS certificate: a PEM pair that is re-read when it changes, or a generated self-signed one (RELAY.md section 8).</summary>
public static class RelayCertificates
{
    /// <summary>Loads a PEM certificate chain and key. The key is round-tripped through PKCS#12 so SslStream can use it on every platform.</summary>
    public static X509Certificate2 LoadPem(string certFile, string keyFile)
    {
        try
        {
            using var pem = X509Certificate2.CreateFromPemFile(certFile, keyFile);
            return X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pfx), null);
        }
        catch (Exception e) when (e is CryptographicException or IOException or ArgumentException)
        {
            throw new InvalidDataException($"cannot read the certificate '{certFile}' with key '{keyFile}': {e.Message}", e);
        }
    }

    /// <summary>Loads <c>relay.pfx</c> from the data dir or creates it (ECDSA P-256, ten years, the public names as subject alternative names).</summary>
    public static X509Certificate2 LoadOrCreateSelfSigned(string dataDir, IReadOnlyList<string> publicNames)
    {
        var path = Path.Combine(dataDir, "relay.pfx");
        if (File.Exists(path)) return X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.Exportable);
        Directory.CreateDirectory(dataDir);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=UncivRelay", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        foreach (var name in publicNames)
        {
            if (IPAddress.TryParse(name, out var ip)) names.AddIpAddress(ip);
            else names.AddDnsName(name);
        }
        request.CertificateExtensions.Add(names.Build());
        var notBefore = DateTimeOffset.FromUnixTimeMilliseconds(SystemClock.Instance.NowMillis).AddDays(-1); // calendar validity: the real clock even in tests
        using var created = request.CreateSelfSigned(notBefore, notBefore.AddYears(10));
        var pfx = created.Export(X509ContentType.Pfx, (string?)null);
        File.WriteAllBytes(path, pfx);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>The DNS names (lower case) in the certificate's subject alternative names.</summary>
    public static IReadOnlyList<string> DnsNames(X509Certificate2 certificate)
    {
        var names = new List<string>();
        foreach (var extension in certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>())
        {
            names.AddRange(extension.EnumerateDnsNames().Select(n => n.ToLowerInvariant()));
            names.AddRange(extension.EnumerateIPAddresses().Select(a => a.ToString()));
        }
        return names;
    }

    /// <summary>The pin clients put in <c>unciv-relay://host:port?fp=</c>: the same fingerprint spelling as a host key.</summary>
    public static string Fingerprint(X509Certificate2 certificate) => HostKey.Fingerprint(certificate);
}
