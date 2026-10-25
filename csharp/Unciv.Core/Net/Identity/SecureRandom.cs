using System.Security.Cryptography;

namespace Unciv.Net.Identity;

/// <summary>Source of unpredictable bytes for salts, challenges and tokens. Network security only; never game state.</summary>
public interface ISecureRandom
{
    void Fill(Span<byte> buffer);
}

/// <summary>OS CSPRNG. The single place in Unciv.Core allowed to touch <c>RandomNumberGenerator</c>.</summary>
public sealed class SystemSecureRandom : ISecureRandom
{
    public static readonly SystemSecureRandom Instance = new();

    public void Fill(Span<byte> buffer)
    {
#pragma warning disable RS0030 // JUSTIFY: T173 salts, challenges and invite tokens must be unpredictable, not seeded; never feeds game state
        RandomNumberGenerator.Fill(buffer);
#pragma warning restore RS0030
    }
}

internal static class SecureRandomExtensions
{
    public static byte[] Bytes(this ISecureRandom random, int count)
    {
        var buffer = new byte[count];
        random.Fill(buffer);
        return buffer;
    }
}

internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decodes unpadded base64url; null for any malformed input (never throws).</summary>
    public static byte[]? TryDecode(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 8192) return null;
        var padded = text.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 1: return null;
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        try { return Convert.FromBase64String(padded); }
        catch (FormatException) { return null; }
    }
}
