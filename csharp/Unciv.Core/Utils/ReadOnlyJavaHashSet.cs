namespace Unciv.Utils;

/// <summary>A read-only <see cref="IReadOnlySet{T}"/> view of a <see cref="JavaHashSet{T}"/> (which iterates in java.util.HashSet order).</summary>
public sealed class ReadOnlyJavaHashSet<T>(JavaHashSet<T> set) : IReadOnlySet<T> where T : notnull
{
    public int Count => set.Count;
    public bool Contains(T item) => set.Contains(item);
    public IEnumerator<T> GetEnumerator() => set.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public bool IsProperSubsetOf(IEnumerable<T> other) => new HashSet<T>(set).IsProperSubsetOf(other);
    public bool IsProperSupersetOf(IEnumerable<T> other) => new HashSet<T>(set).IsProperSupersetOf(other);
    public bool IsSubsetOf(IEnumerable<T> other) => new HashSet<T>(set).IsSubsetOf(other);
    public bool IsSupersetOf(IEnumerable<T> other) => new HashSet<T>(set).IsSupersetOf(other);
    public bool Overlaps(IEnumerable<T> other) => new HashSet<T>(set).Overlaps(other);
    public bool SetEquals(IEnumerable<T> other) => new HashSet<T>(set).SetEquals(other);
}
