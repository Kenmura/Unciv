using System.Collections;

namespace Unciv.Utils;

// java.util.LinkedHashSet (also what Kotlin setOf/mutableSetOf/toSet/distinct produce): a set that iterates in insertion
// order. Re-adding an existing element does not move it; removing and re-adding puts it last. Equality is that of a Set
// (same elements, any order). Removal is O(n); use JavaHashSet only where the Kotlin code really used a HashSet.
public sealed class LinkedHashSet<T> : ICollection<T>, IReadOnlySet<T> where T : notnull
{
    private readonly List<T> items = [];
    private readonly HashSet<T> lookup = [];

    public LinkedHashSet()
    {
    }

    public LinkedHashSet(IEnumerable<T> source)
    {
        foreach (var item in source) Add(item);
    }

    public int Count => items.Count;

    public bool IsEmpty => items.Count == 0;

    bool ICollection<T>.IsReadOnly => false;

    /// <summary>Returns true if the element was not present (it is then appended).</summary>
    public bool Add(T item)
    {
        if (!lookup.Add(item)) return false;
        items.Add(item);
        return true;
    }

    void ICollection<T>.Add(T item) => Add(item);

    /// <summary>Adds all elements in order; returns true if the set changed.</summary>
    public bool AddAll(IEnumerable<T> source)
    {
        bool changed = false;
        foreach (var item in source) changed |= Add(item);
        return changed;
    }

    public bool Remove(T item)
    {
        if (!lookup.Remove(item)) return false;
        items.RemoveAt(items.FindIndex(existing => EqualityComparer<T>.Default.Equals(existing, item)));
        return true;
    }

    public void Clear()
    {
        items.Clear();
        lookup.Clear();
    }

    public bool Contains(T item) => lookup.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool IsProperSubsetOf(IEnumerable<T> other) => lookup.IsProperSubsetOf(other);

    public bool IsProperSupersetOf(IEnumerable<T> other) => lookup.IsProperSupersetOf(other);

    public bool IsSubsetOf(IEnumerable<T> other) => lookup.IsSubsetOf(other);

    public bool IsSupersetOf(IEnumerable<T> other) => lookup.IsSupersetOf(other);

    public bool Overlaps(IEnumerable<T> other) => lookup.Overlaps(other);

    public bool SetEquals(IEnumerable<T> other) => lookup.SetEquals(other);

    public override bool Equals(object? obj) => obj is IReadOnlySet<T> other && other.Count == Count && lookup.SetEquals(other);

    // AbstractSet.hashCode: sum of the element hash codes, so it ignores order like Equals does
    public override int GetHashCode()
    {
        int sum = 0;
        foreach (var item in items) sum += item.GetHashCode();
        return sum;
    }

    public override string ToString() => "[" + string.Join(", ", items) + "]";
}
