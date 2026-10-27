using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Unciv.Net.Identity;

namespace Unciv.Server.Transport;

/// <summary>
/// Long-lived self-signed ECDSA P-256 certificate for the host (SERVER.md 6.4).
/// Generated on first run and stored on disk (e.g. <c>host-key.pfx</c>).
/// </summary>
public static class HostCertificate
{
    private const string Subject = "CN=UncivServer";

    /// <summary>Generates a new self-signed ECDSA P-256 host certificate.</summary>
    public static X509Certificate2 Generate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(Subject, ecdsa, HashAlgorithmName.SHA256);

        // JUSTIFY: T179 X.509 host certificate generation requires calendar timestamps for certificate validity
#pragma warning disable RS0030
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
#pragma warning restore RS0030
        var notAfter = notBefore.AddYears(10);
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    /// <summary>
    /// Loads an existing host certificate from PKCS#12 / PFX file, or generates and saves a new one.
    /// </summary>
    public static X509Certificate2 LoadOrGenerate(string pfxPath, string? password = null)
    {
        if (File.Exists(pfxPath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password, X509KeyStorageFlags.Exportable);
        }

        var directory = Path.GetDirectoryName(pfxPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var certificate = Generate();
        var pfxBytes = certificate.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(pfxPath, pfxBytes);
        return certificate;
    }

    /// <summary>Gets the base32 fingerprint for an X509 certificate's public key.</summary>
    public static string GetFingerprint(X509Certificate2 certificate) => HostKey.Fingerprint(certificate);
}
