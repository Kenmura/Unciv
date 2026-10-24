using System.Collections;
using System.Text;

namespace Unciv.Models;

// Specialized map storing non-zero integers (port of Kotlin `Counter<K> : LinkedHashMap<K, Int>`).
//  - Put (and the indexer setter, and Add/Remove/Times) remove the key when the value would be zero
//  - Get/indexer on a nonexistent key returns 0
//  - Insertion order is preserved (LinkedHashMap)
//
// Kotlin inherits the whole java.util.Map surface; C# does not have a virtual dictionary base, so this wraps an
// OrderedDictionary and exposes the Map members the Kotlin code can use (Count, ContainsKey, ContainsValue, Keys, Values,
// Put, Remove, PutAll, Clear, enumeration of KeyValuePair, AbstractMap equality/hash/ToString).
//
// Kotlin quirks preserved deliberately:
//  - The constructor copying a map, Clone() and PutAll() bypass the zero-removal (they use LinkedHashMap internals /
//    `super.put`), so they can store zero values.
//  - Counter.ZERO only throws on Put; Remove/Clear/PutAll are not guarded.
//
// Not ported: Json.Serializable write/read and the IsPartOfGameInfoSerialization marker (see JSON-layer ticket).
public class Counter<K> : IEnumerable<KeyValuePair<K, int>> where K : notnull
{
    private readonly OrderedDictionary<K, int> map;

    public Counter(IEnumerable<KeyValuePair<K, int>>? fromMap = null)
    {
        map = new OrderedDictionary<K, int>();
        if (fromMap != null)
            foreach (var (key, value) in fromMap)
                map[key] = value;   // Kotlin: super.put - bypasses the zero-removal
    }

    public int Count => map.Count;
    public bool IsEmpty() => map.Count == 0;
    public bool ContainsKey(K key) => map.ContainsKey(key);
    public bool ContainsValue(int value) => map.Values.Contains(value);
    public IEnumerable<K> Keys => map.Keys;
    public IEnumerable<int> Values => map.Values;

    /// <summary>Like Kotlin `get`: returns 0 for a missing key instead of null.</summary>
    public int Get(K key) => map.TryGetValue(key, out int value) ? value : 0;

    /// <summary>Kotlin `put`: a zero value removes the key. Returns the previous value or null.</summary>
    public virtual int? Put(K key, int value)
    {
        if (value == 0) return Remove(key);   // No objects of this sort left, no need to count
        if (map.TryGetValue(key, out int previous))
        {
            map[key] = value;
            return previous;
        }
        map.Add(key, value);
        return null;
    }

    public int this[K key]
    {
        get => Get(key);
        set => Put(key, value);
    }

    public int? Remove(K key) => map.Remove(key, out int value) ? value : null;

    /// <summary>Kotlin `putAll` (java.util.HashMap): does not go through <see cref="Put"/>, so zero values are stored.</summary>
    public void PutAll(IEnumerable<KeyValuePair<K, int>> from)
    {
        foreach (var (key, value) in from.ToList())
            map[key] = value;
    }

    public void Clear() => map.Clear();

    public void Add(K key, int value) => Put(key, Get(key) + value);

    public void Add(Counter<K> other)
    {
        foreach (var (key, value) in other) Add(key, value);
    }

    /// <summary>Kotlin `plusAssign` (`+=`). C# `+=` would create a new instance, so this is a named method.</summary>
    public void PlusAssign(Counter<K> other) => Add(other);

    public void Remove(Counter<K> other)
    {
        foreach (var (key, value) in other) Add(key, -value);
    }

    /// <summary>Kotlin `minusAssign` (`-=`).</summary>
    public void MinusAssign(Counter<K> other) => Remove(other);

    /// <summary>Creates a new instance (does not modify)</summary>
    public static Counter<K> operator *(Counter<K> counter, int amount)
    {
        var newCounter = new Counter<K>();
        foreach (var key in counter.Keys) newCounter[key] = counter[key] * amount;
        return newCounter;
    }

    public static Counter<K> operator +(Counter<K> counter, Counter<K> other)
    {
        var clone = counter.Clone();
        clone.Add(other);
        return clone;
    }

    public int SumValues() => map.Values.Sum();

    /// <summary>Always returns a plain <see cref="Counter{K}"/> (so cloning <see cref="Counter.ZERO"/> yields a mutable one).</summary>
    public Counter<K> Clone() => new(this);

    public IEnumerator<KeyValuePair<K, int>> GetEnumerator() => map.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // java.util.AbstractMap semantics: order-insensitive equality, hash = sum of (keyHash ^ valueHash), "{k=v, k=v}"
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        if (obj is not Counter<K> other || other.Count != Count) return false;
        foreach (var (key, value) in map)
            if (!other.map.TryGetValue(key, out int otherValue) || otherValue != value) return false;
        return true;
    }

    public override int GetHashCode()
    {
        int hash = 0;
        foreach (var (key, value) in map) hash = unchecked(hash + (key.GetHashCode() ^ value));
        return hash;
    }

    public override string ToString()
    {
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var (key, value) in map)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(key).Append('=').Append(value);
        }
        return sb.Append('}').ToString();
    }
}

/// <summary>Kotlin `Counter.Companion`; a non-generic holder because C# generic classes cannot have a typed shared static of a closed type.</summary>
public static class Counter
{
    private sealed class ZeroCounter : Counter<string>
    {
        public override int? Put(string key, int value) =>
            // JUSTIFY: T118 Kotlin ZERO counter rejects writes (permanent)
            throw new NotSupportedException("Do not modify Counter.ZERO");
    }

    public static readonly Counter<string> ZERO = new ZeroCounter();
}
