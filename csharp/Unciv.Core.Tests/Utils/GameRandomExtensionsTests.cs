using Unciv.Utils;

namespace Unciv.Core.Tests.Utils;

// Golden values: the same call sequence run on kotlin.random.Random(seed) from kotlin-stdlib 2.3.0 on the JVM
// (IntRange.random, IntRange.randomOrNull, Collection.random/randomOrNull, Iterable.shuffled, MutableList.shuffle).
// Each sequence shares one generator, so every step also proves how many values the previous step consumed.
public class GameRandomExtensionsTests
{
    private static readonly string[] Letters = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"];

    private sealed record Golden(
        int[] Ranges, int RangeOrNull, string Col, string ColOrNull, int NextAfterSingle, string[] Shuffled,
        int[] Shuffled2, int NextAfterShuffled1, int NextAfterShuffled0, int[] ShuffleInPlace, string FromSet, int Tail);

    private static readonly Golden Seed0 = new(
        [5, 9, -18, 7, 1415412887, -587880378, 43427776], 5, "i,c,d", "i", 207793967,
        ["d", "j", "a", "b", "g", "c", "i", "h", "e", "f"], [2, 1], 783421854, 1362024251,
        [0, 3, 5, 6, 4, 7, 1, 2], "f", 721367191);

    private static readonly Golden Seed42 = new(
        [4, 1, 11, 7, 581384346, -126744254, -95941416], 10, "h,c,h", "j", 593343317,
        ["d", "j", "h", "f", "e", "b", "c", "a", "g", "i"], [2, 1], 2079073119, 1363957090,
        [3, 0, 2, 7, 6, 4, 1, 5], "h", -396283600);

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public void SequenceMatchesKotlinStdlib(int seed)
    {
        var expected = seed == 0 ? Seed0 : Seed42;
        var r = new GameRandom(seed);

        // ranges: 1..10 (twice), -20..20, 7..7, 5..MAX, MIN..5, MIN..MAX
        int[] ranges =
        [
            new IntRange(1, 10).Random(r),
            new IntRange(1, 10).Random(r),
            new IntRange(-20, 20).Random(r),
            new IntRange(7, 7).Random(r),
            new IntRange(5, int.MaxValue).Random(r),
            new IntRange(int.MinValue, 5).Random(r),
            new IntRange(int.MinValue, int.MaxValue).Random(r)
        ];
        Assert.Equal(expected.Ranges, ranges);

        // empty range: random throws, randomOrNull returns null, neither consumes a value
        Assert.Throws<InvalidOperationException>(() => new IntRange(5, 4).Random(r));
        Assert.Null(new IntRange(5, 4).RandomOrNull(r));
        Assert.Equal(expected.RangeOrNull, new IntRange(1, 10).RandomOrNull(r));

        // collections
        string col = string.Join(',', Letters.Random(r), Letters.Random(r), Letters.Random(r));
        Assert.Equal(expected.Col, col);
        Assert.Equal(expected.ColOrNull, Letters.RandomOrNull(r));
        Assert.Throws<InvalidOperationException>(() => new List<string>().Random(r));
        Assert.Null(new List<string>().RandomOrNull(r));
        Assert.Equal("x", new List<string> { "x" }.Random(r));
        Assert.Equal(expected.NextAfterSingle, r.NextInt()); // a one-element collection still consumes a value

        // shuffled: input untouched, size 0/1 consume nothing
        var input = new List<string>(Letters);
        Assert.Equal(expected.Shuffled, input.Shuffled(r));
        Assert.Equal(Letters, input);
        Assert.Equal(expected.Shuffled2, new[] { 1, 2 }.Shuffled(r));
        Assert.Equal([1], new[] { 1 }.Shuffled(r));
        Assert.Equal(expected.NextAfterShuffled1, r.NextInt());
        Assert.Empty(new List<int>().Shuffled(r));
        Assert.Equal(expected.NextAfterShuffled0, r.NextInt());

        // in-place shuffle and a set
        var list = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
        list.Shuffle(r);
        Assert.Equal(expected.ShuffleInPlace, list);
        Assert.Equal(expected.FromSet, new LinkedHashSetLike<string>(Letters).Random(r));
        Assert.Equal(expected.Tail, r.NextInt());
    }

    [Fact]
    public void IntRangeBasics()
    {
        Assert.True(new IntRange(5, 4).IsEmpty);
        Assert.False(new IntRange(5, 5).IsEmpty);
        Assert.Equal("5..4", new IntRange(5, 4).ToString());
        Assert.Equal(1, new IntRange(1, 10).First);
        Assert.Equal(10, new IntRange(1, 10).Last);
        var ex = Assert.Throws<InvalidOperationException>(() => new IntRange(5, 4).Random(new GameRandom(1)));
        Assert.Equal("Cannot get random in empty range: 5..4", ex.Message);
        var next = Assert.Throws<ArgumentException>(() => new GameRandom(1).NextInt(new IntRange(5, 4)));
        Assert.Equal("Cannot get random in empty range: 5..4", next.Message);
    }

    // insertion-ordered set so ElementAt follows Kotlin's LinkedHashSet order
    private sealed class LinkedHashSetLike<T>(IEnumerable<T> items) : IReadOnlyCollection<T>
    {
        private readonly List<T> items = [.. items];
        public int Count => items.Count;
        public IEnumerator<T> GetEnumerator() => items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
