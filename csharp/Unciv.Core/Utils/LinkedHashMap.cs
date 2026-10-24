using System.Collections;

namespace Unciv.Utils;

// java.util.LinkedHashMap (what Kotlin mutableMapOf/LinkedHashMap produce): a map that iterates in insertion order.
// Putting an existing key replaces the value but keeps its position; removing and re-adding puts it last. Unlike
// Dictionary<,>, removals never reorder the remaining entries. Removal is O(1).
public sealed class LinkedHashMap<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> lookup;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> order = new();

    public LinkedHashMap() => lookup = [];

    public LinkedHashMap(IEqualityComparer<TKey> comparer) => lookup = new(comparer);

    public int Count => lookup.Count;

    public IEnumerable<TKey> Keys => order.Select(it => it.Key);

    public IEnumerable<TValue> Values => order.Select(it => it.Value);

    public TValue this[TKey key]
    {
        get => lookup.TryGetValue(key, out var node) ? node.Value.Value : throw new KeyNotFoundException(key.ToString());
        set => Put(key, value);
    }

    public bool ContainsKey(TKey key) => lookup.ContainsKey(key);

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (lookup.TryGetValue(key, out var node))
        {
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>Kotlin <c>map[key] = value</c>: replaces the value of an existing key in place, else appends.</summary>
    public void Put(TKey key, TValue value)
    {
        if (lookup.TryGetValue(key, out var node))
        {
            node.Value = new KeyValuePair<TKey, TValue>(key, value);
            return;
        }
        lookup[key] = order.AddLast(new KeyValuePair<TKey, TValue>(key, value));
    }

    /// <summary>Kotlin <c>putAll</c>: puts the entries of <paramref name="source"/> in its iteration order.</summary>
    public void PutAll(IEnumerable<KeyValuePair<TKey, TValue>> source)
    {
        foreach (var (key, value) in source.ToList()) Put(key, value);
    }

    public bool Remove(TKey key)
    {
        if (!lookup.Remove(key, out var node)) return false;
        order.Remove(node);
        return true;
    }

    public void Clear()
    {
        lookup.Clear();
        order.Clear();
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => order.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
