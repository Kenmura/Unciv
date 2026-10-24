namespace Unciv.Utils;

// The kotlin.random extension functions Unciv uses with a seeded generator. Not ported on purpose: the unseeded
// overloads (`list.shuffled()`, `list.random()`), which use Random.Default; call sites must pass a GameRandom.
// Collection.Random indexes with ElementAt, so the caller's collection must have the order the Kotlin one had.
public static class GameRandomExtensions
{
    // kotlin.random.Random.nextInt(range: IntRange)
    public static int NextInt(this GameRandom random, IntRange range)
    {
        if (range.IsEmpty) throw new ArgumentException($"Cannot get random in empty range: {range}");
        if (range.Last < int.MaxValue) return random.NextInt(range.First, range.Last + 1);
        if (range.First > int.MinValue) return random.NextInt(range.First - 1, range.Last) + 1;
        return random.NextInt();
    }

    // IntRange.random(random): empty range throws NoSuchElementException in Kotlin
    public static int Random(this IntRange range, GameRandom random)
    {
        if (range.IsEmpty) throw new InvalidOperationException($"Cannot get random in empty range: {range}");
        return random.NextInt(range);
    }

    public static int? RandomOrNull(this IntRange range, GameRandom random) =>
        range.IsEmpty ? null : random.NextInt(range);

    public static T Random<T>(this IReadOnlyCollection<T> collection, GameRandom random)
    {
        if (collection.Count == 0) throw new InvalidOperationException("Collection is empty.");
        return collection.ElementAt(random.NextInt(collection.Count));
    }

    // Reference types only: for a struct `T?` cannot tell "empty" from a default element, and Unciv only needs objects.
    public static T? RandomOrNull<T>(this IReadOnlyCollection<T> collection, GameRandom random) where T : class =>
        collection.Count == 0 ? null : collection.ElementAt(random.NextInt(collection.Count));

    // MutableList.shuffle(random): Fisher-Yates from the end, `j = nextInt(i + 1)`; sizes 0 and 1 draw nothing
    public static void Shuffle<T>(this IList<T> list, GameRandom random)
    {
        for (int i = list.Count - 1; i >= 1; i--)
        {
            int j = random.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // Iterable.shuffled(random): shuffles a copy
    public static List<T> Shuffled<T>(this IEnumerable<T> source, GameRandom random)
    {
        var copy = source.ToList();
        copy.Shuffle(random);
        return copy;
    }
}
