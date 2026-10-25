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

    /// <summary>Decodes canonical unpadded base64url (one spelling per value); null for any malformed input (never throws).</summary>
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
        if (text.Contains('=') || text.Any(char.IsWhiteSpace)) return null;
        try
        {
            var bytes = Convert.FromBase64String(padded);
            // canonical only: rejects non-zero trailing bits so one value has exactly one spelling
            return string.Equals(Encode(bytes), text, StringComparison.Ordinal) ? bytes : null;
        }
        catch (FormatException) { return null; }
    }
}
