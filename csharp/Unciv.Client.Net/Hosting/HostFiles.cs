using System.Security.Cryptography.X509Certificates;
using Unciv.Net.Identity;
using Unciv.Server.Transport;

namespace Unciv.Client.Net.Hosting;

/// <summary>An <see cref="IIdentityKeyStore"/> on one file (the player's identity key, PKCS#8 bytes). A missing file is "no key yet".</summary>
public sealed class FileIdentityKeyStore(string path) : IIdentityKeyStore
{
    public byte[]? Load() => File.Exists(path) ? File.ReadAllBytes(path) : null;

    public void Save(byte[] pkcs8PrivateKey)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; // a private key: only this user
        using var stream = new FileStream(path, options);
        stream.Write(pkcs8PrivateKey);
    }
}

/// <summary>C034: the host's long-lived files under the user folder: the player identity (who they are to other hosts and in their own games) and the host key friends pin.</summary>
public static class HostFiles
{
    /// <summary>The player's identity from <paramref name="identityPath"/>, created on first use.</summary>
    public static ClientIdentity LoadIdentity(string identityPath) => ClientIdentity.LoadOrCreate(new FileIdentityKeyStore(identityPath));

    /// <summary>The host key certificate from <paramref name="pfxPath"/>, created on first use (so the invite fingerprint stays the same between sessions).</summary>
    public static X509Certificate2 LoadCertificate(string pfxPath)
    {
        var certificate = HostCertificate.LoadOrGenerate(pfxPath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(pfxPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); // holds the private key
        return certificate;
    }
}
