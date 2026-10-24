namespace Unciv.Utils;

// core/src/com/unciv/utils/CollectionExtensions.kt
//
// Not ported: toGdxArray (libGDX Array), yieldIfNotNull/yieldAllNotNull (Kotlin sequence builders; use `yield return`),
// forEach/fold on libGDX IntArray/LongArray (only used by automation/Timers), forEachSetBit on java.util.BitSet (T040
// decides the BitSet type for pathfinding), ArrayList.removeRange(start, endExclusive) (call List.RemoveRange(start, count)).
//
// Kotlin HashSet/HashMap iterate in JVM hash order; the C# HashSet/Dictionary do not. WithItem/WithoutItem on sets and
// AddToMapOfSets therefore must not feed anything order-sensitive until the iteration-order policy ticket (T044) is done.
public static class CollectionExtensions
{
    /// <summary>
    /// One random element of the list; the probability of each element is proportional to the corresponding weight.
    /// </summary>
    public static T RandomWeighted<T>(this IReadOnlyList<T> list, IReadOnlyList<float> weights, GameRandom random)
    {
        if (list.Count == 0) throw new InvalidOperationException("Empty list.");
        // JUSTIFY: T118 mirrors Kotlin require() failure (permanent)
        if (list.Count != weights.Count) throw new NotSupportedException("Weights size does not match this list size.");

        float totalWeight = 0f; // Iterable<Float>.sum() adds in Float, LINQ Sum would add in Double
        foreach (float weight in weights) totalWeight += weight;
        double randDouble = random.NextDouble();
        float sum = 0f;

        for (int i = 0; i < weights.Count; i++)
        {
            sum += weights[i] / totalWeight;
            if (randDouble <= sum)
                return list[i];
        }
        return list[^1];
    }

    /// <summary>Same, with the weight of each element computed once by <paramref name="getWeight"/>.</summary>
    public static T RandomWeighted<T>(this IReadOnlyList<T> list, GameRandom random, Func<T, float> getWeight) =>
        list.RandomWeighted(list.Select(getWeight).ToList(), random);

    /// <summary>A copy of the list with an additional item; the original is not changed.</summary>
    public static List<T> WithItem<T>(this IReadOnlyList<T> list, T item)
    {
        var newList = new List<T>(list) { item };
        return newList;
    }

    /// <summary>A copy of the set with an additional item; the original is not changed.</summary>
    public static HashSet<T> WithItem<T>(this IReadOnlySet<T> set, T item)
    {
        var newSet = new HashSet<T>(set) { item };
        return newSet;
    }

    /// <summary>A copy of the list without the first occurrence of an item; the original is not changed.</summary>
    public static List<T> WithoutItem<T>(this IReadOnlyList<T> list, T item)
    {
        var newList = new List<T>(list);
        newList.Remove(item);
        return newList;
    }

    /// <summary>A copy of the set without an item; the original is not changed.</summary>
    public static HashSet<T> WithoutItem<T>(this IReadOnlySet<T> set, T item)
    {
        var newSet = new HashSet<T>(set);
        newSet.Remove(item);
        return newSet;
    }

    /// <summary>
    /// Adds to a sparse map of sets, creating the set for <paramref name="key"/> when needed.
    /// Returns false if the element was already present, true if it was new (like Set.add).
    /// </summary>
    public static bool AddToMapOfSets<TKey, TElement>(this Dictionary<TKey, HashSet<TElement>> map, TKey key, TElement element)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var set))
        {
            set = [];
            map[key] = set;
        }
        return set.Add(element);
    }

    /// <summary>Whether in a sparse map of sets the <paramref name="element"/> exists for <paramref name="key"/>.</summary>
    public static bool Contains<TKey, TElement>(this Dictionary<TKey, HashSet<TElement>> map, TKey key, TElement element)
        where TKey : notnull =>
        map.TryGetValue(key, out var set) && set.Contains(element);

    /// <summary>
    /// Adds to a sparse Java-order map of sets, creating the set for <paramref name="key"/> when needed.
    /// Returns false if the element was already present, true if it was new (like Set.add).
    /// </summary>
    public static bool AddToMapOfSets<TKey, TElement>(this JavaHashMap<TKey, JavaHashSet<TElement>> map, TKey key, TElement element)
        where TKey : notnull
        where TElement : notnull
    {
        if (!map.TryGetValue(key, out var set))
        {
            set = new JavaHashSet<TElement>();
            map[key] = set;
        }
        return set.Add(element);
    }

    /// <summary>Whether in a sparse Java-order map of sets the <paramref name="element"/> exists for <paramref name="key"/>.</summary>
    public static bool Contains<TKey, TElement>(this JavaHashMap<TKey, JavaHashSet<TElement>> map, TKey key, TElement element)
        where TKey : notnull
        where TElement : notnull =>
        map.TryGetValue(key, out var set) && set.Contains(element);

    /// <summary>For lists that replace hashmaps: they contain nulls where we don't know the answer yet.</summary>
    public static T GetOrPut<T>(this List<T?> list, int index, Func<T> getValue) where T : class
    {
        var currentValue = index >= 0 && index < list.Count ? list[index] : null;
        if (currentValue != null) return currentValue;

        var value = getValue();

        // grow the list if required - if not, these are no-ops
        list.EnsureCapacity(index + 1); // So we don't need to copy the array multiple times if adding a lot
        while (list.Count <= index) list.Add(null); // Fill with nulls until we reach the index

        list[index] = value; // Now we can safely set the value
        return value;
    }
}
