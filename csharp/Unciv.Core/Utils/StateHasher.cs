using System.Globalization;
using Unciv.Logic.Map;
using Unciv.Mathematics;
using Unciv.Models;
using Unciv.Models.StatTypes;

namespace Unciv.Utils;

/// <summary>
/// Defines an engine component that can contribute its state to a canonical determinism hash.
/// </summary>
public interface IStateHashable
{
    void HashState(StateHasher hasher);
}

/// <summary>
/// Canonical 64-bit state hasher using FNV-1a.
/// Provides order-explicit, platform-independent, culture-independent hashing over game state.
/// </summary>
public sealed class StateHasher
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private ulong hash = FnvOffsetBasis;

    public StateHasher()
    {
    }

    public StateHasher(ulong initialHash)
    {
        hash = initialHash;
    }

    public StateHasher Add(byte value)
    {
        unchecked
        {
            hash ^= value;
            hash *= FnvPrime;
        }
        return this;
    }

    public StateHasher Add(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            Add(b);
        }
        return this;
    }

    public StateHasher Add(bool value) => Add((byte)(value ? 1 : 0));

    public StateHasher Add(int value)
    {
        unchecked
        {
            Add((byte)value);
            Add((byte)(value >> 8));
            Add((byte)(value >> 16));
            Add((byte)(value >> 24));
        }
        return this;
    }

    public StateHasher Add(long value)
    {
        unchecked
        {
            Add((byte)value);
            Add((byte)(value >> 8));
            Add((byte)(value >> 16));
            Add((byte)(value >> 24));
            Add((byte)(value >> 32));
            Add((byte)(value >> 40));
            Add((byte)(value >> 48));
            Add((byte)(value >> 56));
        }
        return this;
    }

    public StateHasher Add(float value)
    {
        // Canonicalize NaN bit patterns to avoid platform/runtime variations
        int bits = float.IsNaN(value) ? 0x7fc00000 : BitConverter.SingleToInt32Bits(value);
        return Add(bits);
    }

    public StateHasher Add(double value)
    {
        // Canonicalize NaN bit patterns to avoid platform/runtime variations
        long bits = double.IsNaN(value) ? 0x7ff8000000000000L : BitConverter.DoubleToInt64Bits(value);
        return Add(bits);
    }

    public StateHasher Add(string? value)
    {
        if (value == null)
        {
            Add(-1); // Distinct sentinel for null
            return this;
        }

        Add(value.Length);
        foreach (char c in value)
        {
            unchecked
            {
                Add((byte)c);
                Add((byte)(c >> 8));
            }
        }
        return this;
    }

    public StateHasher Add(HexCoord coord)
    {
        Add(coord.x);
        Add(coord.y);
        return this;
    }

    public StateHasher Add(Vector2 vector)
    {
        Add(vector.x);
        Add(vector.y);
        return this;
    }

    public StateHasher Add(Vector3 vector)
    {
        Add(vector.x);
        Add(vector.y);
        Add(vector.z);
        return this;
    }

    public StateHasher Add(Stats stats)
    {
        Add(stats.Production);
        Add(stats.Food);
        Add(stats.Gold);
        Add(stats.Science);
        Add(stats.Culture);
        Add(stats.Happiness);
        Add(stats.Faith);
        return this;
    }

    public StateHasher Add(StatMap statMap)
    {
        Add(statMap.Count);
        foreach (var (k, v) in statMap)
        {
            Add(k);
            Add(v);
        }
        return this;
    }

    public StateHasher Add<K>(Counter<K> counter) where K : notnull
    {
        Add(counter.Count);
        foreach (var (k, v) in counter)
        {
            Add(k.ToString());
            Add(v);
        }
        return this;
    }

    public StateHasher Add(IStateHashable? hashable)
    {
        if (hashable == null)
        {
            Add(0); // Null marker
        }
        else
        {
            Add(1); // Present marker
            hashable.HashState(this);
        }
        return this;
    }

    public StateHasher AddSequence<T>(IEnumerable<T> items, Action<StateHasher, T> hashItem)
    {
        var list = items as IReadOnlyCollection<T> ?? items.ToList();
        Add(list.Count);
        foreach (var item in list)
        {
            hashItem(this, item);
        }
        return this;
    }

    public StateHasher AddUnorderedMap<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>> map,
        Func<TKey, string> keyToString,
        Action<StateHasher, TValue> hashValue)
    {
        var entries = map.Select(kv => (KeyStr: keyToString(kv.Key), Value: kv.Value))
                         .OrderBy(e => e.KeyStr, StringComparer.Ordinal)
                         .ToList();
        Add(entries.Count);
        foreach (var (keyStr, value) in entries)
        {
            Add(keyStr);
            hashValue(this, value);
        }
        return this;
    }

    public ulong ToHash64() => hash;

    public string ToHexString() => hash.ToString("X16", CultureInfo.InvariantCulture);

    public override string ToString() => ToHexString();

    public static ulong ComputeHash(Action<StateHasher> build)
    {
        var hasher = new StateHasher();
        build(hasher);
        return hasher.ToHash64();
    }

    public static string ComputeHexString(Action<StateHasher> build)
    {
        var hasher = new StateHasher();
        build(hasher);
        return hasher.ToHexString();
    }
}
