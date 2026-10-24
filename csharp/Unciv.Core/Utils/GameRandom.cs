using System.Numerics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unciv.Core.Tests")]

namespace Unciv.Utils;

/// <summary>
/// Deterministic random number generator replicating Kotlin's <c>kotlin.random.XorWowRandom</c>.
/// All Unciv engine code uses this type to ensure exact seed reproducibility across runs and ports.
/// </summary>
public class GameRandom
{
    private int x;
    private int y;
    private int z;
    private int w;
    private int v;
    private int addend;

    public GameRandom(int seed)
        : this(seed, seed >> 31)
    {
    }

    public GameRandom(long seed)
        : this((int)seed, (int)((ulong)seed >> 32))
    {
    }

    internal GameRandom(int seed1, int seed2)
        : this(seed1, seed2, 0, 0, ~seed1, (seed1 << 10) ^ (int)((uint)seed2 >> 4))
    {
    }

    internal GameRandom(int x, int y, int z, int w, int v, int addend)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
        this.v = v;
        this.addend = addend;

        if ((x | y | z | w | v) == 0)
            throw new ArgumentException("Initial state must have at least one non-zero element.");

        // Warmup rounds exactly matching Kotlin XorWowRandom
        for (int i = 0; i < 64; i++)
        {
            NextInt();
        }
    }

    public int NextInt()
    {
        unchecked
        {
            int t = x;
            t ^= (int)((uint)t >> 2);
            x = y;
            y = z;
            z = w;
            int v0 = v;
            w = v0;
            t = (t ^ (t << 1)) ^ v0 ^ (v0 << 4);
            v = t;
            addend += 362437;
            return t + addend;
        }
    }

    public int NextBits(int bitCount) =>
        (int)((uint)NextInt() >> (32 - bitCount)) & (-bitCount >> 31);

    private static int FastLog2(int value) =>
        31 - BitOperations.LeadingZeroCount((uint)value);

    public int NextInt(int until) => NextInt(0, until);

    public int NextInt(int from, int until)
    {
        if (until <= from)
            throw new ArgumentException($"Random range is empty: [{from}, {until}).");

        int n = until - from;
        if (n > 0 || n == int.MinValue)
        {
            int rnd;
            if ((n & -n) == n)
            {
                int bitCount = FastLog2(n);
                rnd = NextBits(bitCount);
            }
            else
            {
                int bits, v2;
                do
                {
                    bits = (int)((uint)NextInt() >> 1);
                    v2 = bits % n;
                } while (unchecked(bits - v2 + (n - 1)) < 0);
                rnd = v2;
            }
            return from + rnd;
        }
        else
        {
            // Range spans more than Int.MAX_VALUE
            int val;
            do
            {
                val = NextInt();
            } while (val < from || val >= until);
            return val;
        }
    }

    public long NextLong()
    {
        unchecked
        {
            return ((long)NextInt() << 32) + NextInt();
        }
    }

    public long NextLong(long until) => NextLong(0L, until);

    public long NextLong(long from, long until)
    {
        if (until <= from)
            throw new ArgumentException($"Random range is empty: [{from}, {until}).");

        long n = until - from;
        if (n > 0)
        {
            long rnd;
            if ((n & -n) == n)
            {
                int nLow = (int)n;
                int nHigh = (int)((ulong)n >> 32);
                if (nLow != 0)
                {
                    int bitCount = FastLog2(nLow);
                    rnd = (long)((ulong)NextBits(bitCount) & 0xFFFFFFFFL);
                }
                else if (nHigh == 1)
                {
                    rnd = (long)((ulong)NextInt() & 0xFFFFFFFFL);
                }
                else
                {
                    int bitCount = FastLog2(nHigh);
                    rnd = ((long)NextBits(bitCount) << 32) + ((long)NextInt() & 0xFFFFFFFFL);
                }
            }
            else
            {
                long bits, val;
                do
                {
                    bits = (long)((ulong)NextLong() >> 1);
                    val = bits % n;
                } while (bits - val + (n - 1) < 0);
                rnd = val;
            }
            return from + rnd;
        }
        else
        {
            // Full 64-bit range
            long val;
            do
            {
                val = NextLong();
            } while (val < from || val >= until);
            return val;
        }
    }

    public bool NextBoolean() => NextBits(1) != 0;

    public float NextFloat() => NextBits(24) / 16777216f;

    public double NextDouble()
    {
        long hi = (uint)NextBits(26);
        long lo = (uint)NextBits(27);
        return ((hi << 27) + lo) / 9007199254740992.0;
    }

    public double NextDouble(double until) => NextDouble(0.0, until);

    public double NextDouble(double from, double until)
    {
        if (until <= from)
            throw new ArgumentException($"Random range is empty: [{from}, {until}).");

        double size = until - from;
        double result;
        if (double.IsInfinity(size) && !double.IsInfinity(from) && !double.IsInfinity(until))
        {
            double r = NextDouble() * (until / 2.0 - from / 2.0);
            result = from + r + r;
        }
        else
        {
            result = from + NextDouble() * size;
        }

        if (result >= until)
            result = Math.BitDecrement(until);

        return result;
    }

    public byte[] NextBytes(byte[] array, int fromIndex, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (fromIndex < 0 || toIndex > array.Length || fromIndex > toIndex)
            throw new ArgumentOutOfRangeException();

        int steps = (toIndex - fromIndex) / 4;
        int position = fromIndex;
        for (int i = 0; i < steps; i++)
        {
            int v = NextInt();
            array[position] = (byte)v;
            array[position + 1] = (byte)(v >> 8);
            array[position + 2] = (byte)(v >> 16);
            array[position + 3] = (byte)(v >> 24);
            position += 4;
        }

        int remainder = toIndex - position;
        int remBits = NextBits(remainder * 8);
        for (int i = 0; i < remainder; i++)
        {
            array[position + i] = (byte)(remBits >> (i * 8));
        }

        return array;
    }

    public byte[] NextBytes(byte[] array) => NextBytes(array, 0, array.Length);

    public byte[] NextBytes(int size) => NextBytes(new byte[size]);
}
