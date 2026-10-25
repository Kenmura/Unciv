using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Unciv.Net.Identity;

/// <summary>Salted PBKDF2-SHA256 hash of the lobby password. Only this string is stored (<c>game.json</c>), never the password.</summary>
public sealed class JoinPassword
{
    public const int DefaultIterations = 120_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int MaxPasswordBytes = 256;
    private const int MaxIterations = 10_000_000;
    private const string Scheme = "pbkdf2-sha256";

    private readonly int iterations;
    private readonly byte[] salt;
    private readonly byte[] hash;

    private JoinPassword(int iterations, byte[] salt, byte[] hash)
    {
        this.iterations = iterations;
        this.salt = salt;
        this.hash = hash;
    }

    public static JoinPassword Create(string password, ISecureRandom random, int iterations = DefaultIterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        var salt = random.Bytes(SaltBytes);
        return new JoinPassword(iterations, salt, Derive(password, salt, iterations));
    }

    /// <summary>Constant-time check. Over-long or null input is simply wrong.</summary>
    public bool Verify(string? candidate)
    {
        if (candidate == null || Encoding.UTF8.GetByteCount(candidate) > MaxPasswordBytes) return false;
        return CryptographicOperations.FixedTimeEquals(Derive(candidate, salt, iterations), hash);
    }

    /// <summary>Storage form <c>pbkdf2-sha256$iterations$salt$hash</c> (base64url).</summary>
    public string Serialize() => string.Join('$', Scheme, iterations.ToString(CultureInfo.InvariantCulture), Base64Url.Encode(salt), Base64Url.Encode(hash));

    public static JoinPassword? TryParse(string? stored)
    {
        var parts = stored?.Split('$');
        if (parts is not [Scheme, var iter, var saltText, var hashText]) return null;
        if (!int.TryParse(iter, NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations is < 1 or > MaxIterations) return null;
        var salt = Base64Url.TryDecode(saltText);
        var hash = Base64Url.TryDecode(hashText);
        if (salt is not { Length: SaltBytes } || hash is not { Length: HashBytes }) return null;
        return new JoinPassword(iterations, salt, hash);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        try { return Rfc2898DeriveBytes.Pbkdf2(bytes, salt, iterations, HashAlgorithmName.SHA256, HashBytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
