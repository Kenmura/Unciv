using Unciv.Models;

namespace Unciv.Core.Tests.Models;

public class CounterTests
{
    private static Counter<string> Make(params (string, int)[] items)
    {
        var c = new Counter<string>();
        foreach (var (k, v) in items) c[k] = v;
        return c;
    }

    [Fact]
    public void MissingKeyReturnsZero()
    {
        var c = new Counter<string>();
        Assert.Equal(0, c["x"]);
        Assert.False(c.ContainsKey("x"));
        Assert.True(c.IsEmpty());
    }

    [Fact]
    public void PutZeroRemovesAndReturnsPrevious()
    {
        var c = Make(("a", 3));
        Assert.Null(c.Put("b", 2));
        Assert.Equal(3, c.Put("a", 5));
        Assert.Equal(5, c.Put("a", 0));
        Assert.False(c.ContainsKey("a"));
        Assert.Null(c.Put("zzz", 0));
        Assert.Equal(1, c.Count);
        c["b"] = 0;
        Assert.True(c.IsEmpty());
    }

    [Fact]
    public void AddKeyZeroingRemovesKey()
    {
        var c = Make(("a", 2));
        c.Add("a", 3);
        Assert.Equal(5, c["a"]);
        c.Add("a", -5);
        Assert.False(c.ContainsKey("a"));
        c.Add("n", -2);
        Assert.Equal(-2, c["n"]);
        c.Add("none", 0);
        Assert.False(c.ContainsKey("none"));
    }

    [Fact]
    public void AddAndRemoveCounters()
    {
        var a = Make(("x", 1), ("y", 2));
        var b = Make(("y", 2), ("z", 5));
        a.Add(b);
        Assert.Equal(new[] { "x", "y", "z" }, a.Keys.ToArray());
        Assert.Equal(4, a["y"]);
        Assert.Equal(5, b["z"]);
        a.Remove(b);
        Assert.Equal(2, a["y"]);
        Assert.False(a.ContainsKey("z"));
        a.PlusAssign(b);
        Assert.Equal(5, a["z"]);
        a.MinusAssign(b);
        Assert.False(a.ContainsKey("z"));
    }

    [Fact]
    public void AddSelfDoubles()
    {
        var a = Make(("x", 1), ("y", -2));
        a.Add(a);
        Assert.Equal(2, a["x"]);
        Assert.Equal(-4, a["y"]);
    }

    [Fact]
    public void RemoveSelfThrowsConcurrentModification()
    {
        var a = Make(("x", 1), ("y", -2));
        // Self-removal zeroes (= structurally removes) entries during iteration: ConcurrentModificationException on the JVM
        Assert.Throws<InvalidOperationException>(() => a.Remove(a));
    }

    [Fact]
    public void TimesAndPlusCreateNewInstances()
    {
        var a = Make(("x", 2), ("y", -3));
        var t = a * 3;
        Assert.Equal(6, t["x"]);
        Assert.Equal(-9, t["y"]);
        Assert.Equal(2, a["x"]);
        Assert.True((a * 0).IsEmpty());
        var sum = a + Make(("x", -2), ("w", 1));
        Assert.Equal(new[] { "y", "w" }, sum.Keys.ToArray());
        Assert.Equal(2, a["x"]);
        Assert.Equal(unchecked(int.MaxValue * 2), (Make(("k", int.MaxValue)) * 2)["k"]);   // Int overflow wraps like on the JVM
    }

    [Fact]
    public void SumValues()
    {
        Assert.Equal(0, new Counter<string>().SumValues());
        Assert.Equal(4, Make(("a", 7), ("b", -3)).SumValues());
    }

    [Fact]
    public void InsertionOrderIsPreserved()
    {
        var c = Make(("z", 1), ("a", 2), ("m", 3));
        c["a"] = 9;   // update keeps position
        Assert.Equal(new[] { "z", "a", "m" }, c.Keys.ToArray());
        Assert.Equal(new[] { 1, 9, 3 }, c.Values.ToArray());
        c.Remove("z");
        c["z"] = 1;   // re-insert goes last
        Assert.Equal(new[] { "a", "m", "z" }, c.Select(kv => kv.Key).ToArray());
    }

    [Fact]
    public void CopyConstructorAndCloneBypassZeroRemoval()
    {
        var source = new Dictionary<string, int> { ["a"] = 0, ["b"] = 2 };
        var c = new Counter<string>(source);
        Assert.True(c.ContainsKey("a"));   // Kotlin: super.put keeps the zero
        Assert.Equal(2, c.Count);
        var clone = c.Clone();
        Assert.NotSame(c, clone);
        Assert.True(c.Equals(clone));
        clone["b"] = 5;
        Assert.Equal(2, c["b"]);
    }

    [Fact]
    public void PutAllBypassesZeroRemoval()
    {
        var c = new Counter<string>();
        c.PutAll(new Dictionary<string, int> { ["a"] = 0, ["b"] = 1 });
        Assert.Equal(2, c.Count);
        Assert.True(c.ContainsKey("a"));
    }

    [Fact]
    public void RemoveKeyReturnsPreviousOrNull()
    {
        var c = Make(("a", 4));
        Assert.Equal(4, c.Remove("a"));
        Assert.Null(c.Remove("a"));
        c.Put("q", 1);
        c.Clear();
        Assert.Equal(0, c.Count);
    }

    [Fact]
    public void ContainsValue()
    {
        var c = Make(("a", 4));
        Assert.True(c.ContainsValue(4));
        Assert.False(c.ContainsValue(0));
    }

    [Fact]
    public void ZeroRejectsPutButCloneIsMutable()
    {
        Assert.Throws<NotSupportedException>(() => Counter.ZERO["a"] = 1);
        Assert.Throws<NotSupportedException>(() => Counter.ZERO.Add("a", 1));
        Assert.True(Counter.ZERO.IsEmpty());
        Assert.Equal(0, Counter.ZERO["a"]);
        var clone = Counter.ZERO.Clone();
        clone["a"] = 1;
        Assert.Equal(1, clone["a"]);
    }

    [Fact]
    public void EqualityIsOrderInsensitiveValueEquality()
    {
        var a = Make(("x", 1), ("y", 2));
        var b = Make(("y", 2), ("x", 1));
        Assert.True(a.Equals(b));   // not Assert.Equal: that would compare as an ordered sequence
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(Make(("x", 1), ("y", 3))));
        Assert.False(a.Equals(Make(("x", 1))));
    }

    [Fact]
    public void ToStringMatchesJavaMapFormat()
    {
        Assert.Equal("{}", new Counter<string>().ToString());
        Assert.Equal("{a=1, b=-2}", Make(("a", 1), ("b", -2)).ToString());
    }

    [Fact]
    public void NonStringKeys()
    {
        var c = new Counter<int>();
        c.Add(5, 2);
        c.Add(5, 1);
        Assert.Equal(3, c[5]);
        foreach (var (key, value) in c) Assert.Equal((5, 3), (key, value));
    }
}
