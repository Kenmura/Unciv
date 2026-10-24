using System.Numerics;

namespace Unciv.Logic.Map.PathingMap;

// T093: the part of java.util.BitSet the pathfinder uses. Grows on Set like the Java class; bits are visited in ascending order.

/// <summary>A growable bit set (<c>java.util.BitSet</c>) over non-negative indices.</summary>
internal sealed class PathingBitSet(int initialBits = 64)
{
    private ulong[] words = new ulong[Math.Max(1, (initialBits + 63) >> 6)];

    public bool IsEmpty
    {
        get
        {
            foreach (var word in words)
                if (word != 0) return false;
            return true;
        }
    }

    public int Cardinality()
    {
        var count = 0;
        foreach (var word in words) count += BitOperations.PopCount(word);
        return count;
    }

    public bool Get(int index) => (index >> 6) < words.Length && (words[index >> 6] & (1UL << (index & 63))) != 0;

    public void Set(int index)
    {
        EnsureWords((index >> 6) + 1);
        words[index >> 6] |= 1UL << (index & 63);
    }

    public void Clear(int index)
    {
        if ((index >> 6) < words.Length) words[index >> 6] &= ~(1UL << (index & 63));
    }

    /// <summary><c>BitSet.or</c></summary>
    public void Or(PathingBitSet other)
    {
        EnsureWords(other.words.Length);
        for (var i = 0; i < other.words.Length; i++) words[i] |= other.words[i];
    }

    /// <summary><c>BitSet.andNot</c></summary>
    public void AndNot(PathingBitSet other)
    {
        var shared = Math.Min(words.Length, other.words.Length);
        for (var i = 0; i < shared; i++) words[i] &= ~other.words[i];
    }

    public PathingBitSet Clone()
    {
        var clone = new PathingBitSet(words.Length << 6);
        Array.Copy(words, clone.words, words.Length);
        return clone;
    }

    /// <summary>Kotlin <c>BitSet.forEachSetBit</c>: calls <paramref name="action"/> for each set bit index, ascending.</summary>
    public void ForEachSetBit(Action<int> action)
    {
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            while (word != 0)
            {
                var bit = BitOperations.TrailingZeroCount(word);
                action((i << 6) + bit);
                word &= word - 1;
            }
        }
    }

    private void EnsureWords(int count)
    {
        if (count > words.Length) Array.Resize(ref words, Math.Max(count, words.Length * 2));
    }
}
