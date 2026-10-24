using Unciv.Utils;

namespace Unciv.Core.Tests.Utils;

public class LongPriorityQueueTests
{
    [Fact]
    public void AddAllThenPopAllIsOrdered()
    {
        // enough items to hit a depth of 4
        var expectedOutputOrder = new long[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        var currentInputOrder = new long[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        var queue = new LongPriorityQueue(currentInputOrder.Length);
        // Test all 40,320 permutations.
        ForEachPermutation(currentInputOrder, () =>
        {
            queue.Clear();
            queue.AddAll(currentInputOrder);
            var initialString = string.Join(",", currentInputOrder);
            Assert.True(expectedOutputOrder.SequenceEqual(PopAll(queue)), $"After queuing and popping {initialString}");
        });
    }

    [Fact]
    public void AddOneAtATimeThenPopAllIsOrdered()
    {
        var expectedOutputOrder = new long[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        var currentInputOrder = new long[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        var queue = new LongPriorityQueue(currentInputOrder.Length);
        ForEachPermutation(currentInputOrder, () =>
        {
            queue.Clear();
            foreach (var it in currentInputOrder) queue.Add(it);
            var initialString = string.Join(",", currentInputOrder);
            Assert.True(expectedOutputOrder.SequenceEqual(PopAll(queue)), $"After queuing and popping {initialString}");
        });
    }

    [Fact]
    public void IteratorIteratesAllElements()
    {
        var queue = new LongPriorityQueue(16);
        queue.AddAll(new long[] { 0, 1, 2, 3, 4, 5, 6, 7 });

        var r = queue.Iterator().AsSequence().ToList();

        Assert.Equal(new List<long> { 0, 1, 2, 3, 4, 5, 6, 7 }, r);
    }

    [Fact]
    public void StreamIteratesAllElements()
    {
        var queue = new LongPriorityQueue(16);
        queue.AddAll(new long[] { 0, 1, 2, 3, 4, 5, 6, 7 });

        var r = queue.Stream().ToList();

        Assert.Equal(new List<long> { 0, 1, 2, 3, 4, 5, 6, 7 }, r);
    }

    // --- Additional cases for branches the Kotlin tests do not cover ---

    [Fact]
    public void PollOnEmptyThrows()
    {
        Assert.Throws<InvalidOperationException>(() => new LongPriorityQueue().Poll());
        Assert.Throws<InvalidOperationException>(() => new LongPriorityQueue().Element());
    }

    [Fact]
    public void PeekReturnsNullWhenEmptyAndTopOtherwise()
    {
        var queue = new LongPriorityQueue();
        Assert.Null(queue.Peek());
        queue.AddAll(new long[] { 5, 3, 9 });
        Assert.Equal(3L, queue.Peek());
        Assert.Equal(3L, queue.Element());
    }

    [Fact]
    public void IndexerChecksBounds()
    {
        var queue = new LongPriorityQueue();
        queue.Add(1);
        Assert.Equal(1L, queue[0]);
        Assert.Throws<IndexOutOfRangeException>(() => queue[1]);
        Assert.Throws<IndexOutOfRangeException>(() => queue[-1]);
    }

    [Fact]
    public void ContainsAndContainsAll()
    {
        var queue = new LongPriorityQueue(new long[] { 4, 2, 8 });
        Assert.True(queue.Contains(8));
        Assert.False(queue.Contains(7));
        Assert.True(queue.ContainsAll(new long[] { 2, 4 }));
        Assert.False(queue.ContainsAll(new long[] { 2, 7 }));
        Assert.True(queue.ContainsAll(new List<long> { 8, 4 }));
        Assert.False(queue.ContainsAll(new List<long> { 8, 1 }));
    }

    [Fact]
    public void AddAllReturnsFalseAndGrows()
    {
        var queue = new LongPriorityQueue(1);
        Assert.False(queue.AddAll(new List<long> { 9, 7, 8 }));
        Assert.False(queue.AddAll(new long[] { 1, 2 }));
        Assert.Equal(5, queue.Size());
        Assert.Equal(new long[] { 1, 2, 7, 8, 9 }, PopAll(queue));
    }

    [Fact]
    public void AddAllCollectionOntoExistingContentKeepsHeapOrder()
    {
        var queue = new LongPriorityQueue(new long[] { 10, 20, 30 });
        queue.AddAll(new List<long> { 5, 25, 1 });
        Assert.Equal(new long[] { 1, 5, 10, 20, 25, 30 }, PopAll(queue));
    }

    [Fact]
    public void OfferAlwaysReturnsTrue()
    {
        var queue = new LongPriorityQueue(0);
        Assert.True(queue.Offer(3));
        Assert.True(queue.Offer(2));
        Assert.Equal(2L, queue.Peek());
    }

    [Fact]
    public void RemoveQuirkAlwaysRemovesTopIgnoringArgument()
    {
        // Kotlin behaviour: remove(value) ignores value and polls the head.
        var queue = new LongPriorityQueue(new long[] { 1, 2, 3 });
        Assert.True(queue.Remove(3));
        Assert.Equal(new long[] { 2, 3 }, PopAll(queue));
        Assert.False(new LongPriorityQueue().Remove(1));
    }

    [Fact]
    public void RemoveAllWithLargerCollectionUsesRemoveIf()
    {
        var queue = new LongPriorityQueue(new long[] { 1, 2, 3 });
        Assert.True(queue.RemoveAll(new long[] { 2, 3, 4, 5 }));
        Assert.Equal(new long[] { 1 }, PopAll(queue));

        queue = new LongPriorityQueue(new long[] { 1, 2, 3 });
        Assert.True(queue.RemoveAll(new List<long> { 1, 9, 9, 9 }));
        Assert.Equal(new long[] { 2, 3 }, PopAll(queue));
    }

    [Fact]
    public void RemoveAllWithSmallerCollectionRemovesHeadPerItem()
    {
        // Follows from the Remove quirk: one head is removed per item.
        var queue = new LongPriorityQueue(new long[] { 1, 2, 3, 4 });
        Assert.True(queue.RemoveAll(new long[] { 4, 4 }));
        Assert.Equal(new long[] { 3, 4 }, PopAll(queue));
    }

    [Fact]
    public void RemoveAllWithSameSizeCollectionUsesLoopPath()
    {
        var queue = new LongPriorityQueue(new long[] { 1, 2 });
        // c.size (2) is not > size (2) -> loop path; third remove not reached.
        Assert.True(queue.RemoveAll(new long[] { 1, 2 }));
        Assert.Equal(0, queue.Size());
        Assert.True(queue.IsEmpty());
        Assert.False(queue.IsNotEmpty());
    }

    [Fact]
    public void RemoveIfAndRetainAll()
    {
        var queue = new LongPriorityQueue(new long[] { 1, 2, 3, 4, 5, 6 });
        Assert.True(queue.RemoveIf(x => x % 2 == 0));
        Assert.Equal(new long[] { 1, 3, 5 }, PopAll(queue));

        queue = new LongPriorityQueue(new long[] { 1, 2, 3, 4, 5, 6 });
        queue.RetainAll(new long[] { 2, 4, 99 });
        Assert.Equal(new long[] { 2, 4 }, PopAll(queue));

        queue = new LongPriorityQueue(new long[] { 1, 2, 3, 4, 5, 6 });
        queue.RetainAll(new List<long> { 1, 6 });
        Assert.Equal(new long[] { 1, 6 }, PopAll(queue));
    }

    [Fact]
    public void CustomComparatorGivesMaxQueue()
    {
        var queue = new LongPriorityQueue(8, new ReverseComparator());
        queue.AddAll(new long[] { 3, 9, 1, 7 });
        Assert.Equal(new long[] { 9, 7, 3, 1 }, PopAll(queue));
    }

    [Fact]
    public void ToArrayUsesProvidedArrayWhenLargeEnough()
    {
        var queue = new LongPriorityQueue(new long[] { 3, 1, 2 });
        var big = new long[5];
        Assert.Same(big, queue.ToArray(big));
        Assert.Equal(new long[] { 1, 3, 2, 0, 0 }, big);
        var small = new long[1];
        var result = queue.ToArray(small);
        Assert.NotSame(small, result);
        Assert.Equal(new long[] { 1, 3, 2 }, result);
    }

    [Fact]
    public void EqualsAndHashCodeDependOnContentAndComparator()
    {
        var a = new LongPriorityQueue(new long[] { 1, 2, 3 });
        var b = new LongPriorityQueue(new long[] { 1, 2, 3 });
        var c = new LongPriorityQueue(new long[] { 1, 2 });
        var d = new LongPriorityQueue(3, new ReverseComparator());
        d.AddAll(new long[] { 1, 2, 3 });
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(c));
        Assert.False(a.Equals(d));
        Assert.False(a.Equals("x"));
    }

    [Fact]
    public void ToStringFormat()
    {
        Assert.Equal("LongPriorityQueue[size=0 top=null]", new LongPriorityQueue().ToString());
        Assert.Equal("LongPriorityQueue[size=2 top=4]", new LongPriorityQueue(new long[] { 9, 4 }).ToString());
    }

    [Fact]
    public void ForEachVisitsBackingOrder()
    {
        var queue = new LongPriorityQueue(new long[] { 3, 1, 2 });
        var seen = new List<long>();
        queue.ForEach(seen.Add);
        Assert.Equal(new List<long> { 1, 3, 2 }, seen);
    }

    [Fact]
    public void IteratorDetectsConcurrentModification()
    {
        var queue = new LongPriorityQueue(new long[] { 1, 2, 3 });
        var it = queue.Iterator();
        queue.Add(4);
        Assert.Throws<InvalidOperationException>(() => it.HasNext());
        Assert.Throws<InvalidOperationException>(() => it.Next());
        Assert.Throws<InvalidOperationException>(() => it.Remove());
    }

    [Fact]
    public void IteratorRemoveRequiresNextAndWorksAfterIt()
    {
        var queue = new LongPriorityQueue(new long[] { 1, 2, 3 });
        var it = queue.Iterator();
        Assert.Equal(1L, it.Next());
        it.Remove();
        Assert.Throws<InvalidOperationException>(() => it.Remove());
        Assert.Equal(2, queue.Size());
    }

    [Fact]
    public void StreamDetectsModificationAfterCreation()
    {
        var queue = new LongPriorityQueue(new long[] { 1, 2, 3 });
        var stream = queue.Stream();
        queue.Clear();
        Assert.Throws<InvalidOperationException>(() => stream.ToList());
    }

    [Fact]
    public void ConstructorFromCollectionAndArray()
    {
        Assert.Equal(new long[] { 1, 2, 3 }, PopAll(new LongPriorityQueue(new List<long> { 3, 1, 2 })));
        Assert.Equal(new long[] { 1, 2, 3 }, PopAll(new LongPriorityQueue(new long[] { 3, 1, 2 })));
    }

    [Fact]
    public void ClearEmptiesQueue()
    {
        var queue = new LongPriorityQueue(new long[] { 3, 1, 2 });
        queue.Clear();
        Assert.Equal(0, queue.Size());
    }

    [Fact]
    public void CloneKotlinQuirk()
    {
        // Kotlin's clone() calls queue.copyInto(c.queue, 0, size), i.e. destinationOffset=0, startIndex=size,
        // copying the *tail* of the backing array. With capacity == size this copies nothing, so the clone
        // has the right size but an unspecified (zeroed) backing array.
        var queue = new LongPriorityQueue(new long[] { 1, 2, 3 });
        var clone = queue.Clone();
        Assert.Equal(3, clone.Size());
        Assert.Equal(0L, clone[0]);
    }

    private sealed class ReverseComparator : LongPriorityQueue.Comparator
    {
        public int Invoke(long a, long b) => b.CompareTo(a);
    }

    private static long[] PopAll(LongPriorityQueue queue)
    {
        var c = queue.Size();
        var result = new long[c];
        for (var i = 0; i < c; i++)
            result[i] = queue.Poll();
        return result;
    }

    private static void Swap(long[] a, int i, int j) => (a[i], a[j]) = (a[j], a[i]);

    // Non-recursive Heap's Algorithm for iterating permutations very quickly
    // https://en.wikipedia.org/wiki/Heap%27s_algorithm
    private static void ForEachPermutation(long[] array, Action op)
    {
        var c = new int[array.Length];
        op();
        var i = 1;
        while (i < array.Length)
        {
            if (c[i] < i)
            {
                if ((i & 1) == 0) Swap(array, 0, i);
                else Swap(array, c[i], i);
                op();
                c[i] += 1;
                i = 1;
            }
            else
            {
                c[i] = 0;
                i++;
            }
        }
    }
}
