using System.Text;

namespace Unciv.Assets;

/// <summary>FNV-1a, 32 bit, over UTF-8. Identical on every machine and process, unlike <c>string.GetHashCode</c>.</summary>
public static class StableHash
{
    private const uint OffsetBasis = 2166136261;
    private const uint Prime = 16777619;

    public static uint Of(string text)
    {
        unchecked
        {
            var hash = OffsetBasis;
            foreach (var b in Encoding.UTF8.GetBytes(text)) hash = (hash ^ b) * Prime;
            return hash;
        }
    }

    /// <summary>Avalanche mix of a seed with two coordinates; used for per-pixel noise.</summary>
    public static uint Mix(uint seed, int x, int y)
    {
        unchecked
        {
            var h = seed ^ (uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA6Bu;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }
}
