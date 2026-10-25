using System.Collections;
using Unciv.Logic.Map;

namespace Unciv.Utils;

// java.util.HashMap, reproduced so that iteration order matches the JVM bit-for-bit (OpenJDK 25).
//
// Kotlin `HashMap`/`HashSet`/`hashMapOf`/`hashSetOf`/`toHashSet` iterate in bucket order, which depends on the keys'
// hashCode() and on the table size history. Port such a collection with this type (keys: String, Int, Long, Boolean,
// HexCoord, whose JVM hashCode is deterministic). Keys with identity hashCodes (classes without a hashCode override,
// enums) iterate in a different order on every JVM run, so there is nothing to reproduce: use an insertion-ordered
// collection instead and say so in the ticket notes.
//
// Reproduced: initial capacity 16 or tableSizeFor(n), load factor 0.75 and resize (bins keep their relative order),
// hash spreading h ^ (h >>> 16), new nodes appended at the bin tail, resize instead of treeify below 64 buckets,
// `clear()` keeping the table, HashMap(Map)/putAll pre-sizing. Not reproduced: treeified bins (nine or more nodes in one
// bin of a 64+ table) stay linked lists (T222). The JVM's treeify (on the ninth node) moves the tree root to the front of the bin, so iteration order of such a bin can differ from the JVM from then on; only pathological hashes (nine keys in one bin of a 64+ table) get there, and for non-Comparable keys the JVM's own tie-break uses identityHashCode.
// The version dependent part is HashSet(Collection) sizing (changed in JDK 19); this follows JDK 25.
public sealed class JavaHashMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>> where TKey : notnull
{
    private const int DefaultInitialCapacity = 16;
    private const int MaximumCapacity = 1 << 30;
    private const float LoadFactor = 0.75f;
    private const int TreeifyThreshold = 8;
    private const int MinTreeifyCapacity = 64;

    private sealed class Node(int hash, TKey key, TValue value)
    {
        public readonly int Hash = hash;
        public readonly TKey Key = key;
        public TValue Value = value;
        public Node? Next;
    }

    private readonly Func<TKey, int> hashCode;
    private Node?[]? table;
    private int threshold;
    private int modCount;

    public JavaHashMap() : this(JavaHashCodes.Default<TKey>())
    {
    }

    /// <summary>HashMap(initialCapacity).</summary>
    public JavaHashMap(int initialCapacity) : this(initialCapacity, JavaHashCodes.Default<TKey>())
    {
    }

    /// <summary>HashMap(Map): pre-sizes from the source's size.</summary>
    public JavaHashMap(IEnumerable<KeyValuePair<TKey, TValue>> source) : this(JavaHashCodes.Default<TKey>())
    {
        PutMapEntries(source);
    }

    /// <param name="hashCode">The key type's JVM hashCode(); required for key types without a default.</param>
    public JavaHashMap(Func<TKey, int> hashCode)
    {
        this.hashCode = hashCode;
    }

    public JavaHashMap(int initialCapacity, Func<TKey, int> hashCode)
    {
        if (initialCapacity < 0) throw new ArgumentException($"Illegal initial capacity: {initialCapacity}");
        this.hashCode = hashCode;
        threshold = TableSizeFor(initialCapacity);
    }

    public int Count { get; private set; }

    public bool IsEmpty => Count == 0;

    public TValue this[TKey key]
    {
        get => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"Key not found: {key}");
        set => Put(key, value);
    }

    public bool ContainsKey(TKey key) => FindNode(key) != null;

    public bool TryGetValue(TKey key, out TValue value)
    {
        var node = FindNode(key);
        value = node != null ? node.Value : default!; // JUSTIFY: T119 default of an unconstrained V on a miss, as Java null
        return node != null;
    }

    /// <summary>Put; returns true if the key was new. An existing key keeps its position.</summary>
    public bool Put(TKey key, TValue value) => PutVal(Hash(key), key, value);

    public void PutAll(IEnumerable<KeyValuePair<TKey, TValue>> source) => PutMapEntries(source);

    public bool Remove(TKey key)
    {
        if (table == null) return false;
        int hash = Hash(key);
        int index = (table.Length - 1) & hash;
        Node? previous = null;
        for (var node = table[index]; node != null; previous = node, node = node.Next)
        {
            if (node.Hash != hash || !EqualityComparer<TKey>.Default.Equals(node.Key, key)) continue;
            if (previous == null) table[index] = node.Next;
            else previous.Next = node.Next;
            modCount++;
            Count--;
            return true;
        }
        return false;
    }

    /// <summary>Removes all entries but, like the JVM, keeps the table size.</summary>
    public void Clear()
    {
        modCount++;
        if (table != null && Count > 0)
        {
            Count = 0;
            Array.Clear(table);
        }
    }

    /// <summary>
    /// T208: rebuilds the table the way a load does (default capacity, entries re-added in the current iteration order), so a set or map that
    /// saw removals or resizes iterates exactly like its own reload.
    /// </summary>
    public void Canonicalize()
    {
        var entries = this.ToList();
        modCount++;
        table = null;
        threshold = 0;
        Count = 0;
        foreach (var entry in entries) PutVal(Hash(entry.Key), entry.Key, entry.Value);
    }

    public IEnumerable<TKey> Keys => this.Select(entry => entry.Key);

    public IEnumerable<TValue> Values => this.Select(entry => entry.Value);

    /// <summary>Entries in bucket order; modifying the map during enumeration throws, as ConcurrentModificationException.</summary>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        int expectedModCount = modCount;
        var buckets = table;
        if (buckets == null || Count == 0) yield break;
        foreach (var bucket in buckets)
        {
            for (var node = bucket; node != null; node = node.Next)
            {
                if (modCount != expectedModCount) throw new InvalidOperationException("Collection was modified during enumeration");
                yield return new KeyValuePair<TKey, TValue>(node.Key, node.Value);
            }
        }
        if (modCount != expectedModCount) throw new InvalidOperationException("Collection was modified during enumeration");
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Number of buckets (0 before the first insertion); exposed for tests and diagnostics.</summary>
    internal int Capacity => table?.Length ?? 0;

    private int Hash(TKey key)
    {
        int h = hashCode(key);
        return h ^ (int)((uint)h >> 16);
    }

    private Node? FindNode(TKey key)
    {
        if (table == null) return null;
        int hash = Hash(key);
        for (var node = table[(table.Length - 1) & hash]; node != null; node = node.Next)
        {
            if (node.Hash == hash && EqualityComparer<TKey>.Default.Equals(node.Key, key)) return node;
        }
        return null;
    }

    private bool PutVal(int hash, TKey key, TValue value)
    {
        var buckets = table;
        if (buckets == null || buckets.Length == 0) buckets = Resize();
        int index = (buckets.Length - 1) & hash;
        var first = buckets[index];
        if (first == null)
        {
            buckets[index] = new Node(hash, key, value);
        }
        else
        {
            var node = first;
            for (int binCount = 0; ; ++binCount)
            {
                if (node.Hash == hash && EqualityComparer<TKey>.Default.Equals(node.Key, key))
                {
                    node.Value = value; // existing mapping: value replaced, no structural change
                    return false;
                }
                if (node.Next == null)
                {
                    node.Next = new Node(hash, key, value);
                    if (binCount >= TreeifyThreshold - 1) TreeifyBin();
                    break;
                }
                node = node.Next;
            }
        }
        ++modCount;
        if (++Count > threshold) Resize();
        return true;
    }

    // treeifyBin: below 64 buckets the JVM resizes instead; with 64+ it would build a tree bin. We keep the bin a linked list:
    // lookups stay correct (only slower); the JVM would move the tree root to the front of the bin (moveRootToFront) and place later
    // inserts after their tree parent, neither of which is reproduced. A mid-game
    // crash is worse than that pathological divergence (T222).
    private void TreeifyBin()
    {
        if (table is { Length: < MinTreeifyCapacity }) Resize();
    }

    private Node?[] Resize()
    {
        var oldTable = table;
        int oldCap = oldTable?.Length ?? 0;
        int oldThr = threshold;
        int newCap;
        int newThr = 0;
        if (oldCap > 0)
        {
            if (oldCap >= MaximumCapacity)
            {
                threshold = int.MaxValue;
                return oldTable.NotNull();
            }
            newCap = oldCap << 1;
            if (newCap < MaximumCapacity && oldCap >= DefaultInitialCapacity) newThr = oldThr << 1;
        }
        else if (oldThr > 0)
        {
            newCap = oldThr; // initial capacity was placed in threshold
        }
        else
        {
            newCap = DefaultInitialCapacity;
            newThr = (int)(LoadFactor * DefaultInitialCapacity);
        }
        if (newThr == 0)
        {
            float ft = newCap * LoadFactor;
            newThr = newCap < MaximumCapacity && ft < MaximumCapacity ? (int)ft : int.MaxValue;
        }
        threshold = newThr;
        var newTable = new Node?[newCap];
        table = newTable;
        if (oldTable == null) return newTable;

        for (int j = 0; j < oldTable.Length; ++j)
        {
            var node = oldTable[j];
            if (node == null) continue;
            oldTable[j] = null;
            // split the bin into the part staying at j and the part moving to j + oldCap, each keeping its order
            Node? loHead = null, loTail = null, hiHead = null, hiTail = null;
            for (; node != null; node = node.Next)
            {
                if ((node.Hash & oldCap) == 0)
                {
                    if (loTail == null) loHead = node; else loTail.Next = node;
                    loTail = node;
                }
                else
                {
                    if (hiTail == null) hiHead = node; else hiTail.Next = node;
                    hiTail = node;
                }
            }
            if (loTail != null)
            {
                loTail.Next = null;
                newTable[j] = loHead;
            }
            if (hiTail != null)
            {
                hiTail.Next = null;
                newTable[j + oldCap] = hiHead;
            }
        }
        return newTable;
    }

    // HashMap.putMapEntries (JDK 21+ form): pre-size an empty map, grow a non-empty one, then put in source order
    private void PutMapEntries(IEnumerable<KeyValuePair<TKey, TValue>> source)
    {
        var entries = source as ICollection<KeyValuePair<TKey, TValue>> ?? source.ToList();
        int s = entries.Count;
        if (s <= 0) return;
        if (table == null)
        {
            float ft = s / LoadFactor + 1.0f;
            int t = ft < MaximumCapacity ? (int)ft : MaximumCapacity;
            if (t > threshold) threshold = TableSizeFor(t);
        }
        else
        {
            while (s > threshold && table.Length < MaximumCapacity) Resize();
        }
        foreach (var entry in entries) PutVal(Hash(entry.Key), entry.Key, entry.Value);
    }

    // Kotlin hashMapOf(vararg pairs): HashMap(mapCapacity(size)) then put each
    public static JavaHashMap<TKey, TValue> HashMapOf(params KeyValuePair<TKey, TValue>[] pairs)
    {
        var map = new JavaHashMap<TKey, TValue>(MapCapacity(pairs.Length));
        foreach (var pair in pairs) map.Put(pair.Key, pair.Value);
        return map;
    }

    // kotlin.collections.mapCapacity
    internal static int MapCapacity(int expectedSize) => expectedSize switch
    {
        < 0 => expectedSize,
        < 3 => expectedSize + 1,
        < 1 << 30 => (int)(expectedSize / 0.75f + 1.0f),
        _ => int.MaxValue
    };

    internal static int TableSizeFor(int capacity)
    {
        int n = -1 >>> System.Numerics.BitOperations.LeadingZeroCount((uint)(capacity - 1));
        return n < 0 ? 1 : n >= MaximumCapacity ? MaximumCapacity : n + 1;
    }
}

/// <summary>java.util.HashSet: a <see cref="JavaHashMap{TKey,TValue}"/> with dummy values.</summary>
public sealed class JavaHashSet<T> : IEnumerable<T> where T : notnull
{
    private readonly JavaHashMap<T, bool> map;

    public JavaHashSet() => map = new JavaHashMap<T, bool>();

    /// <summary>HashSet(initialCapacity).</summary>
    public JavaHashSet(int initialCapacity) => map = new JavaHashMap<T, bool>(initialCapacity);

    /// <summary>HashSet(Collection), sized as OpenJDK 25 does: HashMap.newHashMap(max(size, 12)).</summary>
    public JavaHashSet(IEnumerable<T> collection)
    {
        var items = collection as ICollection<T> ?? collection.ToList();
        map = new JavaHashMap<T, bool>(CalculateHashMapCapacity(Math.Max(items.Count, 12)));
        foreach (var item in items) map.Put(item, true);
    }

    public JavaHashSet(Func<T, int> hashCode) => map = new JavaHashMap<T, bool>(hashCode);

    public int Count => map.Count;

    /// <summary>Returns true if the item was not present.</summary>
    public bool Add(T item) => map.Put(item, true);

    public bool Contains(T item) => map.ContainsKey(item);

    public bool Remove(T item) => map.Remove(item);

    public void Clear() => map.Clear();

    /// <summary>T208: rebuild like a load does, see <see cref="JavaHashMap{TKey,TValue}.Canonicalize"/>.</summary>
    public void Canonicalize() => map.Canonicalize();

    public IEnumerator<T> GetEnumerator() => map.Keys.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal int Capacity => map.Capacity;

    // Kotlin hashSetOf(vararg): HashSet(mapCapacity(size)) then addAll
    public static JavaHashSet<T> HashSetOf(params T[] elements)
    {
        var set = new JavaHashSet<T>(MapCapacity(elements.Length));
        foreach (var element in elements) set.Add(element);
        return set;
    }

    // Kotlin Iterable.toHashSet(): HashSet(mapCapacity(collectionSizeOrDefault(12))) then add each
    public static JavaHashSet<T> ToHashSet(IEnumerable<T> items)
    {
        int size = items is ICollection<T> collection ? collection.Count : 12;
        var set = new JavaHashSet<T>(MapCapacity(size));
        foreach (var item in items) set.Add(item);
        return set;
    }

    // HashMap.calculateHashMapCapacity (JDK 19+)
    private static int CalculateHashMapCapacity(int numMappings) => (int)Math.Ceiling(numMappings / (double)0.75f);

    private static int MapCapacity(int expectedSize) => JavaHashMap<T, bool>.MapCapacity(expectedSize);
}

// JVM hashCode() for the key types whose value is deterministic
internal static class JavaHashCodes
{
    public static Func<T, int> Default<T>()
    {
        object? function = null;
        if (typeof(T) == typeof(string)) function = (Func<string, int>)JavaHash.HashCode;
        else if (typeof(T) == typeof(int)) function = (Func<int, int>)JavaHash.HashCode;
        else if (typeof(T) == typeof(long)) function = (Func<long, int>)JavaHash.HashCode;
        else if (typeof(T) == typeof(bool)) function = (Func<bool, int>)JavaHash.HashCode;
        else if (typeof(T) == typeof(HexCoord)) function = (Func<HexCoord, int>)(coord => coord.GetHashCode());
        if (function == null)
            // JUSTIFY: T118 caller error, JVM hash code unknown (permanent)
            throw new NotSupportedException($"{typeof(T).Name} has no deterministic JVM hashCode(); pass a hash function, or use an insertion-ordered collection");
        return (Func<T, int>)function;
    }
}
