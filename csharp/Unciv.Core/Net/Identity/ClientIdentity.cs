using System.Security.Cryptography;
using System.Text;

namespace Unciv.Net.Identity;

/// <summary>Persistence for the client's private key (PKCS#8 bytes). The Godot client implements this on user://.</summary>
public interface IIdentityKeyStore
{
    byte[]? Load();
    void Save(byte[] pkcs8PrivateKey);
}

public sealed class InMemoryIdentityKeyStore : IIdentityKeyStore
{
    private byte[]? data;
    public byte[]? Load() => data?.ToArray();
    public void Save(byte[] pkcs8PrivateKey) => data = pkcs8PrivateKey.ToArray();
}

/// <summary>Domain-separated message that is signed to prove possession of a slot key (SERVER.md 6.2).</summary>
public static class AuthMessage
{
    private const string Domain = "unciv-auth-v1";

    /// <summary>Length-prefixed so (sessionId, challenge) pairs cannot be shifted into each other.</summary>
    public static byte[] Build(string sessionId, string challenge)
    {
        var session = Encoding.UTF8.GetBytes(sessionId);
        var chal = Encoding.UTF8.GetBytes(challenge);
        var domain = Encoding.UTF8.GetBytes(Domain);
        var result = new byte[4 + domain.Length + 4 + session.Length + 4 + chal.Length];
        var span = result.AsSpan();
        var pos = 0;
        foreach (var part in new[] { domain, session, chal })
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(span[pos..], part.Length);
            pos += 4;
            part.CopyTo(span[pos..]);
            pos += part.Length;
        }
        return result;
    }
}

/// <summary>The client's ECDSA P-256 identity. <see cref="PlayerKey"/> (base64url SPKI) is what goes into <c>Join.PlayerKey</c>.</summary>
public sealed class ClientIdentity : IDisposable
{
    private readonly ECDsa key;

    private ClientIdentity(ECDsa key)
    {
        this.key = key;
        PlayerKey = Base64Url.Encode(key.ExportSubjectPublicKeyInfo());
    }

    public string PlayerKey { get; }

    public static ClientIdentity Generate() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>Loads the stored key, or generates and stores a new one. A corrupt or wrong-curve stored key is an error, never silently replaced.</summary>
    public static ClientIdentity LoadOrCreate(IIdentityKeyStore store)
    {
        var stored = store.Load();
        if (stored == null)
        {
            var created = Generate();
            store.Save(created.key.ExportPkcs8PrivateKey());
            return created;
        }
        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportPkcs8PrivateKey(stored, out _);
            if (!PlayerKeys.IsP256(ecdsa))
                throw new CryptographicException("Stored identity key is not ECDSA P-256");
        }
        catch
        {
            ecdsa.Dispose();
            throw;
        }
        return new ClientIdentity(ecdsa);
    }

    /// <summary>Proves possession for this session and challenge (<c>Auth.Proof</c>).</summary>
    public string Sign(string sessionId, string challenge) =>
        Base64Url.Encode(key.SignData(AuthMessage.Build(sessionId, challenge), HashAlgorithmName.SHA256));

    public void Dispose() => key.Dispose();
}

/// <summary>Host-side check of a player key and its proof. Never throws on hostile input.</summary>
public static class PlayerKeys
{
    /// <summary>True when the text is a well-formed P-256 public key.</summary>
    public static bool IsValid(string? playerKey) => TryImport(playerKey) is { } k && Dispose(k);

    public static bool VerifyProof(string? playerKey, string sessionId, string challenge, string? proof)
    {
        var signature = Base64Url.TryDecode(proof);
        if (signature == null || signature.Length != 64) return false;
        using var ecdsa = TryImport(playerKey);
        if (ecdsa == null) return false;
        try { return ecdsa.VerifyData(AuthMessage.Build(sessionId, challenge), signature, HashAlgorithmName.SHA256); }
        catch (CryptographicException) { return false; }
    }

    internal static bool IsP256(ECDsa key) =>
        key.KeySize == 256 && string.Equals(key.ExportParameters(false).Curve.Oid.Value, ECCurve.NamedCurves.nistP256.Oid.Value, StringComparison.Ordinal);

    private static bool Dispose(ECDsa k)
    {
        k.Dispose();
        return true;
    }

    private static ECDsa? TryImport(string? playerKey)
    {
        var spki = Base64Url.TryDecode(playerKey);
        if (spki == null) return null;
        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportSubjectPublicKeyInfo(spki, out var read);
            if (read != spki.Length || !IsP256(ecdsa))
            {
                ecdsa.Dispose();
                return null;
            }
            return ecdsa;
        }
        catch (CryptographicException)
        {
            ecdsa.Dispose();
            return null;
        }
        catch (ArgumentException)
        {
            ecdsa.Dispose();
            return null;
        }
    }
}
