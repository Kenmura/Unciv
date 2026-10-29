using System.Security.Cryptography;
using System.Text;

namespace Unciv.Net.Identity;

/// <summary>Single-use, expiring invite tokens (SERVER.md 6.2). Only SHA-256 hashes of tokens are held; the token itself exists only in the invite.</summary>
public sealed class InviteTokens
{
    public const int MaxOutstanding = 64;

    private readonly TimeProvider clock;
    private readonly ISecureRandom random;
    private readonly Lock gate = new();
    private readonly Dictionary<string, DateTimeOffset> expiryByHash = new(StringComparer.Ordinal);

    public InviteTokens(TimeProvider clock, ISecureRandom random)
    {
        this.clock = clock;
        this.random = random;
    }

    /// <summary>Mints a token valid for <paramref name="lifetime"/>; null when too many are outstanding.</summary>
    public string? Mint(TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var expired in expiryByHash.Where(p => p.Value <= now).Select(p => p.Key).ToList()) expiryByHash.Remove(expired);
            if (expiryByHash.Count >= MaxOutstanding) return null;
            var token = Base64Url.Encode(random.Bytes(16));
            expiryByHash[Hash(token)] = now + lifetime;
            return token;
        }
    }

    /// <summary>Consumes the token. True exactly once, and only before it expires.</summary>
    public bool Redeem(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return false;
        var hash = Hash(token);
        lock (gate)
        {
            if (!expiryByHash.Remove(hash, out var expiry)) return false;
            return clock.GetUtcNow() < expiry;
        }
    }

    /// <summary>True when the token is outstanding and unexpired, without consuming it (a mod sync connection must not spend the invite the re-join needs).</summary>
    public bool IsValid(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return false;
        var hash = Hash(token);
        lock (gate) return expiryByHash.TryGetValue(hash, out var expiry) && clock.GetUtcNow() < expiry;
    }

    public int Outstanding
    {
        get { lock (gate) return expiryByHash.Count; }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
