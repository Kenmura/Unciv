using Unciv.Utils;

namespace Unciv.Core.Tests.Utils;

// T219: Clone is an independent copy that iterates exactly like its source, even after removals and resizes.
public class JavaHashCloneTests
{
    [Fact]
    public void MapCloneIteratesLikeTheSourceAndIsIndependent()
    {
        var map = new JavaHashMap<int, List<int>>();
        for (var i = 0; i < 100; i++) map.Put(i * 37 % 101, [i]);
        for (var i = 0; i < 100; i += 3) map.Remove(i * 37 % 101);

        var shallow = map.Clone();
        var deep = map.Clone(it => [.. it]);

        Assert.Equal(map.Select(it => it.Key), shallow.Select(it => it.Key));
        Assert.Equal(map.Select(it => it.Key), deep.Select(it => it.Key));
        Assert.Same(map.Values.First(), shallow.Values.First());
        Assert.NotSame(map.Values.First(), deep.Values.First());
        deep.Put(1000, [1]);
        deep.Remove(map.Keys.First());
        Assert.False(map.ContainsKey(1000));
        Assert.Equal(66, map.Count);
        Assert.Equal(66, shallow.Count);
    }

    [Fact]
    public void CloneOfAnEmptyMapAndOfASetIsUsable()
    {
        var empty = new JavaHashMap<string, int>().Clone();
        empty.Put("a", 1);
        Assert.Equal(1, empty.Count);

        var set = JavaHashSet<string>.HashSetOf("Rome", "Athens", "Sparta");
        var clone = set.Clone();
        clone.Add("Thebes");
        Assert.Equal(set.ToList(), set.Clone().ToList());
        Assert.Equal(3, set.Count);
        Assert.Equal(4, clone.Count);
    }
}
