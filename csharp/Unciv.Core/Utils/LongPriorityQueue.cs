namespace Unciv.Utils;

// A PriorityQueue<long>, except that it minimizes memory allocations
//
// This does NOT implement any collection interfaces to avoid boxing.
//
// Ported behaviour-for-behaviour from the Kotlin original, including its quirks:
//  - Remove(value) ignores its argument and removes the head.
//  - Clone() copies the tail of the backing array (see Clone).
//  - The Kotlin `plus`/`minus` operators are not ported (use Add/Remove); C# operators cannot return void.
public class LongPriorityQueue
{
    public interface Comparator
    {
        int Invoke(long a, long b);
    }

    private sealed class DefaultComparatorImpl : Comparator
    {
        public int Invoke(long a, long b) => a.CompareTo(b);
    }

    public static readonly Comparator DefaultComparator = new DefaultComparatorImpl();

    public Comparator comparator { get; }

    private long[] queue;
    private int size;
    private int mutCounter;

    public LongPriorityQueue(int initialCapacity = 100, Comparator? comparator = null)
    {
        this.comparator = comparator ?? DefaultComparator;
        queue = new long[initialCapacity];
    }

    public LongPriorityQueue(ICollection<long> c, Comparator? comparator = null)
        : this(c.Count, comparator)
    {
        AddAll(c);
    }

    public LongPriorityQueue(long[] c, Comparator? comparator = null)
        : this(c.Length, comparator)
    {
        AddAll(c);
    }

    public void Clear()
    {
        size = 0;
        ++mutCounter;
    }

    public int Size() => size;

    public bool IsEmpty() => size == 0;

    public bool IsNotEmpty() => size != 0;

    public long Element()
    {
        if (size == 0) throw new InvalidOperationException("Priority queue is empty.");
        return queue[0];
    }

    public long this[int index]
    {
        get
        {
            if (index < 0 || index >= size)
                throw new IndexOutOfRangeException($"Index {index} out of bounds for priority queue of size {size}.");
            return queue[index];
        }
    }

    public bool Contains(long value)
    {
        for (var i = 0; i < size; i++)
        {
            if (queue[i] == value) return true;
        }
        return false;
    }

    public bool ContainsAll(ICollection<long> c)
    {
        foreach (var item in c)
        {
            if (!Contains(item)) return false;
        }
        return true;
    }

    public bool ContainsAll(long[] c)
    {
        foreach (var item in c)
        {
            if (!Contains(item)) return false;
        }
        return true;
    }

    public void Add(long value)
    {
        if (size == queue.Length)
        {
            ResizeUp();
        }
        queue[size] = value;
        ++size;
        BubbleUp(size - 1);
        ++mutCounter;
    }

    public bool Offer(long value)
    {
        Add(value);
        return true;
    }

    public bool AddAll(ICollection<long> c)
    {
        ++mutCounter;
        var oldSize = size;
        if (size + c.Count > queue.Length)
        {
            Resize(size + c.Count);
        }
        foreach (var it in c) queue[size++] = it;
        // looks inefficient, but 50% do not bubble at all, 25% bubble once, etc.
        for (var i = size - 1; i >= oldSize - 1; i--)
        {
            BubbleUp(i);
        }
        ++mutCounter;
        return false;
    }

    public bool AddAll(long[] c)
    {
        ++mutCounter;
        var oldSize = size;
        if (size + c.Length > queue.Length)
        {
            Resize(size + c.Length);
        }
        Array.Copy(c, 0, queue, size, c.Length);
        size = size + c.Length;
        // looks inefficient, but 50% do not bubble at all, 25% bubble once, etc.
        for (var i = oldSize; i < size; i++)
        {
            BubbleUp(i);
        }
        ++mutCounter;
        return false;
    }

    public long? Peek() => size > 0 ? queue[0] : null;

    public long Poll()
    {
        if (size == 0) throw new InvalidOperationException("Priority queue is empty.");
        var top = queue[0];
        FillHole(0);
        ++mutCounter;
        return top;
    }

    // Quirk preserved from Kotlin: the argument is ignored, the head is removed.
    public bool Remove(long value)
    {
        if (size == 0) return false;
        FillHole(0);
        ++mutCounter;
        return true;
    }

    public bool RemoveAll(ICollection<long> c)
    {
        ++mutCounter;
        if (c.Count > size)
        {
            return RemoveIf(c.Contains);
        }
        foreach (var item in c)
        {
            if (!Remove(item)) return false;
        }
        ++mutCounter;
        return true;
    }

    public bool RemoveAll(long[] c)
    {
        ++mutCounter;
        if (c.Length > size)
        {
            return RemoveIf(x => Array.IndexOf(c, x) >= 0);
        }
        foreach (var item in c)
        {
            if (!Remove(item)) return false;
        }
        ++mutCounter;
        return true;
    }

    public bool RemoveIf(Func<long, bool> predicate)
    {
        ++mutCounter;
        var i = 0;
        while (i < size)
        {
            if (predicate(queue[i]))
            {
                FillHole(i);
                // do not increment i, as we have a new item at index i
            }
            else
            {
                ++i;
            }
        }
        ++mutCounter;
        return true;
    }

    public bool RetainAll(ICollection<long> c) => RemoveIf(x => !c.Contains(x));

    public bool RetainAll(long[] c) => RemoveIf(x => Array.IndexOf(c, x) < 0);

    private void ResizeUp() => Resize(queue.Length * 3 / 2 + 1);

    private void Resize(int newSize)
    {
        var newQueue = new long[newSize];
        Array.Copy(queue, newQueue, queue.Length);
        queue = newQueue;
    }

    private void BubbleUp(int initialIndex)
    {
        var index = initialIndex;
        while (index > 0)
        {
            var parentIndex = (index - 1) / 2;
            if (comparator.Invoke(queue[parentIndex], queue[index]) < 0)
            {
                break;
            }
            Swap(parentIndex, index);
            index = parentIndex;
        }
    }

    private void FillHole(int startIndex)
    {
        var index = startIndex;
        // bubble least child recursively to fill the hole
        while (true)
        {
            var child1 = index * 2 + 1;
            var child2 = index * 2 + 2;
            if (child1 >= size)
            {
                break;
            }
            if (child2 >= size)
            {
                queue[index] = queue[child1];
                break;
            }
            if (comparator.Invoke(queue[child1], queue[child2]) <= 0)
            {
                queue[index] = queue[child1];
                index = child1;
            }
            else
            {
                queue[index] = queue[child2];
                index = child2;
            }
        }
        // if index is the last element, just reduce size
        if (index == size - 1)
        {
            --size;
            return;
        }
        // otherwise, we still have a hole in the last layer, not at the end
        // move the last item to the hole, and bubble it up if needed.
        // This sounds inefficient, but in practice, this rarely bubbles even once.
        queue[index] = queue[size - 1];
        queue[size - 1] = long.MinValue; // unnecessary, but it makes "unused" elements more visible
        --size;
        BubbleUp(index);
    }

    private void Swap(int i, int j)
    {
        (queue[i], queue[j]) = (queue[j], queue[i]);
    }

    public long[] ToArray(long[] array)
    {
        if (array.Length >= size)
        {
            Array.Copy(queue, 0, array, 0, size);
            return array;
        }
        var newArray = new long[size];
        Array.Copy(queue, 0, newArray, 0, size);
        return newArray;
    }

    // Quirk preserved from Kotlin: the original calls `queue.copyInto(c.queue, 0, size)`, i.e.
    // destinationOffset = 0, startIndex = size, endIndex = queue.size - it copies the *tail* of the
    // backing array rather than the live elements. It throws if that tail does not fit into the clone.
    public LongPriorityQueue Clone()
    {
        var c = new LongPriorityQueue(size, comparator);
        var count = queue.Length - size;
        if (count > c.queue.Length)
            throw new IndexOutOfRangeException($"The destination array is too small: {count} > {c.queue.Length}.");
        Array.Copy(queue, size, c.queue, 0, count);
        c.size = size;
        return c;
    }

    public override bool Equals(object? other)
    {
        if (other is not LongPriorityQueue o) return false;
        if (!ReferenceEquals(comparator, o.comparator)) return false;
        if (size != o.size) return false;
        for (var i = 0; i < size; i++)
        {
            if (queue[i] != o.queue[i]) return false;
        }
        return true;
    }

    // Mirrors java.util.Objects.hash(...) chaining (31 * h + element hash).
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 31 + comparator.GetHashCode();
            for (var i = 0; i < size; i++)
            {
                var v = queue[i];
                hash = 31 * (31 + hash) + (int)(v ^ (long)((ulong)v >> 32));
            }
            return hash;
        }
    }

    public override string ToString()
    {
        return $"LongPriorityQueue[size={size} top={(size > 0 ? queue[0].ToString() : "null")}]";
    }

    public void ForEach(Action<long> action)
    {
        for (var i = 0; i < size; i++)
        {
            action(queue[i]);
        }
    }

    public QueueIterator Iterator() => new QueueIterator(this, mutCounter);

    public class QueueIterator
    {
        private readonly LongPriorityQueue owner;
        private int mutSnapshot;
        private int index = -1;
        private bool canRemove = true;

        internal QueueIterator(LongPriorityQueue owner, int mutSnapshot)
        {
            this.owner = owner;
            this.mutSnapshot = mutSnapshot;
        }

        public bool HasNext()
        {
            if (mutSnapshot != owner.mutCounter)
                throw new InvalidOperationException("Priority queue modified during iteration.");
            return index < owner.size - 1;
        }

        public long Next()
        {
            if (mutSnapshot != owner.mutCounter)
                throw new InvalidOperationException("Priority queue modified during iteration.");
            if (index >= owner.size)
                throw new InvalidOperationException("No more elements in priority queue.");
            ++index;
            canRemove = true;
            return owner.queue[index];
        }

        public void Remove()
        {
            if (mutSnapshot != owner.mutCounter)
                throw new InvalidOperationException("Priority queue modified during iteration.");
            if (!canRemove)
                throw new InvalidOperationException("Cannot remove element before calling next().");
            owner.FillHole(index);
            ++owner.mutCounter;
            ++mutSnapshot;
            canRemove = false;
        }

        // Equivalent of Kotlin's Iterator.asSequence()
        public IEnumerable<long> AsSequence()
        {
            while (HasNext()) yield return Next();
        }
    }

    // Replaces the Kotlin Spliterator/LongStream: a lazy sequence that fails fast if the queue is
    // modified after it was created.
    public IEnumerable<long> Stream()
    {
        var snapshot = mutCounter;
        return StreamImpl(snapshot, size - 1);
    }

    private IEnumerable<long> StreamImpl(int mutSnapshot, int endIndex)
    {
        var index = -1;
        while (true)
        {
            if (mutSnapshot != mutCounter)
                throw new InvalidOperationException("Priority queue modified during iteration.");
            if (index >= endIndex) yield break;
            ++index;
            yield return queue[index];
        }
    }
}
