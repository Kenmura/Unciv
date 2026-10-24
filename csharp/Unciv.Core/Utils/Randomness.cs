namespace Unciv.Utils;

// core/src/com/unciv/utils/Randomness.kt
public static class Randomness
{
    public static int WithHash(this int value, int hash) => value * 31 + hash;

    // hashOf(vararg hashes): starts at 0 (not 1 like java.util.Arrays.hashCode), so hashOf(a) == a
    public static int HashOf(params int[] hashes)
    {
        int finalHash = 0;
        foreach (int hash in hashes) finalHash = finalHash.WithHash(hash);
        return finalHash;
    }

    // GameContext.stateBasedRandom(caller, seed): Random(hashOf(caller.hashCode(), seed, this.hashCode())).
    // stateHash is GameContext.hashCode(), which the GameContext port computes (tile positions, civ ids, ...).
    public static GameRandom StateBasedRandom(string caller, int seed, int stateHash) =>
        new(HashOf(JavaHash.HashCode(caller), seed, stateHash));

    // pseudoRandomUuid(rng: Random). The SecureRandom overload is not ported: it is not deterministic.
    public static JavaUuid PseudoRandomUuid(GameRandom rng)
    {
        // RFC requires the high 4 bits of the 7th byte to be the _version_ 0x4 (aka random),
        long mostSigBits = (rng.NextLong() & ~0xf000L) | 0x4000L;
        // and the 2 most-significant bits of the ninth byte to be the _variant_ 0x8 (aka OSF DCE).
        long leastSigBits = (rng.NextLong() & 0x3fffffffffffffffL) | (1L << 63);
        return new JavaUuid(mostSigBits, leastSigBits);
    }
}

// java.util.UUID: two longs, printed as 8-4-4-4-12 lowercase hex. (System.Guid has a different byte layout and string form.)
public readonly record struct JavaUuid(long MostSignificantBits, long LeastSignificantBits)
{
    public override string ToString()
    {
        static string Digits(long value, int digits) => ((ulong)value & ((1UL << (digits * 4)) - 1)).ToString("x" + digits, System.Globalization.CultureInfo.InvariantCulture);
        return Digits(MostSignificantBits >> 32, 8) + "-" + Digits(MostSignificantBits >> 16, 4) + "-" + Digits(MostSignificantBits, 4)
            + "-" + Digits(LeastSignificantBits >> 48, 4) + "-" + Digits(LeastSignificantBits, 12);
    }
}
