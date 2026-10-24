using Unciv.Utils;

namespace Unciv.Core.Tests.Utils;

// T130: the read-only view delegates membership and iteration to the JavaHashSet and set relations to a HashSet copy.
public class ReadOnlyJavaHashSetTests
{
    private static ReadOnlyJavaHashSet<string> View(params string[] items) => new(JavaHashSet<string>.HashSetOf(items));

    [Fact]
    public void CountContainsAndEnumeration()
    {
        var view = View("a", "b", "c");
        Assert.Equal(3, view.Count);
        Assert.Contains("b", view);
        Assert.DoesNotContain("z", view);
        Assert.Equal(new[] { "a", "b", "c" }.Order(), view.Order());
        var nonGeneric = ((System.Collections.IEnumerable)view).GetEnumerator();
        var n = 0;
        while (nonGeneric.MoveNext()) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    public void SetRelations()
    {
        var view = View("a", "b");
        Assert.True(view.IsSubsetOf(["a", "b", "c"]));
        Assert.True(view.IsProperSubsetOf(["a", "b", "c"]));
        Assert.False(view.IsProperSubsetOf(["a", "b"]));
        Assert.True(view.IsSupersetOf(["a"]));
        Assert.True(view.IsProperSupersetOf(["a"]));
        Assert.False(view.IsProperSupersetOf(["a", "b"]));
        Assert.True(view.Overlaps(["b", "x"]));
        Assert.False(view.Overlaps(["x"]));
        Assert.True(view.SetEquals(["b", "a"]));
        Assert.False(view.SetEquals(["a"]));
    }
}
