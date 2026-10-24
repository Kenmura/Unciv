namespace Unciv.Utils;

// The JVM's hashCode() for the types whose hash feeds game state (see Randomness.StateBasedRandom). `string.GetHashCode`
// is randomized per process and banned; everything that hashes into state goes through here. All arithmetic wraps.
public static class JavaHash
{
    // java.lang.String.hashCode: 31 * h + c over UTF-16 code units
    public static int HashCode(string value)
    {
        int h = 0;
        foreach (char c in value) h = 31 * h + c;
        return h;
    }

    public static int HashCode(int value) => value;

    public static int HashCode(bool value) => value ? 1231 : 1237;

    // Long.hashCode: high word xor low word
    public static int HashCode(long value) => (int)(value ^ (long)((ulong)value >> 32));

    // Float.hashCode = floatToIntBits: every NaN becomes the canonical 0x7fc00000, -0f stays distinct from 0f
    public static int HashCode(float value) => float.IsNaN(value) ? 0x7fc00000 : BitConverter.SingleToInt32Bits(value);

    // Double.hashCode = Long.hashCode(doubleToLongBits): NaN canonicalized to 0x7ff8000000000000
    public static int HashCode(double value) =>
        HashCode(double.IsNaN(value) ? 0x7ff8000000000000L : BitConverter.DoubleToInt64Bits(value));
}
