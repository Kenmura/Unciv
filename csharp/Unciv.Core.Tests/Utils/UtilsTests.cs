using Unciv.Json;
using Unciv.Logic;
using Unciv.Utils;
using Version = Unciv.Logic.Version;

namespace Unciv.Core.Tests.Utils;

// Goldens: com.unciv.utils.CollectionExtensionsKt.randomWeighted and com.unciv.logic.MissingModsException run on the JVM
// (installed Unciv.jar, kotlin-stdlib 2.3.0 Random); UUID.fromString results from OpenJDK 25.
public class UtilsTests
{
    private static readonly string[] Items = ["a", "b", "c", "d", "e"];
    private static readonly float[] Weights1 = [1f, 2f, 3f, 0f, 4f];
    private static readonly float[] Weights2 = [0.1f, 0.2f, 0.7f, 0.05f, 0.33f];

    // --- CollectionExtensions ---

    [Theory]
    [InlineData(0, "c e e a c a c e", "e a e c c c b c", 427785292)]
    [InlineData(1, "b e e e a c e e", "b c e b b c c a", -335604893)]
    [InlineData(42, "b e b e b c b b", "c b c e c c c c", -193062786)]
    public void RandomWeightedMatchesKotlin(int seed, string expected1, string expected2, int next)
    {
        var random = new GameRandom(seed);
        string first = string.Join(' ', Enumerable.Range(0, 8).Select(_ => Items.RandomWeighted(Weights1, random)));
        string second = string.Join(' ', Enumerable.Range(0, 8).Select(_ => Items.RandomWeighted(Weights2, random)));

        Assert.Equal(expected1, first);
        Assert.Equal(expected2, second);
        Assert.Equal(next, random.NextInt());
    }

    [Fact]
    public void RandomWeightedWithWeightFunctionMapsOnce()
    {
        int calls = 0;
        var random = new GameRandom(0);
        string picked = Items.RandomWeighted(random, item => { calls++; return Weights1[Array.IndexOf(Items, item)]; });
        Assert.Equal("c", picked);
        Assert.Equal(5, calls);
    }

    [Fact]
    public void RandomWeightedEdgeCases()
    {
        var random = new GameRandom(1);
        Assert.Throws<InvalidOperationException>(() => new List<string>().RandomWeighted(new List<float>(), random));
        Assert.Throws<NotSupportedException>(() => Items.RandomWeighted([1f], random));
        // all-zero weights: totalWeight 0 makes every share NaN, so the loop never matches and the last item is returned
        Assert.Equal("e", Items.RandomWeighted([0f, 0f, 0f, 0f, 0f], new GameRandom(1)));
    }

    [Fact]
    public void WithItemAndWithoutItemLeaveTheOriginalUntouched()
    {
        var list = new List<int> { 1, 2, 3, 2 };
        Assert.Equal([1, 2, 3, 2, 9], list.WithItem(9));
        Assert.Equal([1, 3, 2], list.WithoutItem(2)); // removes the first occurrence only
        Assert.Equal([1, 2, 3, 2], list.WithoutItem(7));
        Assert.Equal([1, 2, 3, 2], list);

        var set = new HashSet<string> { "a", "b" };
        Assert.Equal(new HashSet<string> { "a", "b", "c" }, set.WithItem("c"));
        Assert.Equal(new HashSet<string> { "b" }, set.WithoutItem("a"));
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void JavaMapOfSetsHelpers()
    {
        var map = new JavaHashMap<string, JavaHashSet<int>>();
        Assert.False(map.Contains("x", 1));
        Assert.True(map.AddToMapOfSets("x", 1));
        Assert.False(map.AddToMapOfSets("x", 1));
        Assert.True(map.Contains("x", 1));
        Assert.False(map.Contains("x", 2));
        Assert.False(map.Contains("y", 1));
    }

    [Fact]
    public void MapOfSetsHelpers()
    {
        var map = new Dictionary<string, HashSet<int>>();
        Assert.True(map.AddToMapOfSets("x", 1));
        Assert.False(map.AddToMapOfSets("x", 1));
        Assert.True(map.AddToMapOfSets("x", 2));
        Assert.True(map.Contains("x", 2));
        Assert.False(map.Contains("x", 3));
        Assert.False(map.Contains("y", 1));
        Assert.Single(map);
    }

    [Fact]
    public void GetOrPutOnSparseList()
    {
        var list = new List<string?> { "zero" };
        Assert.Equal("zero", list.GetOrPut(0, () => throw new InvalidOperationException("must not be called")));
        Assert.Equal("five", list.GetOrPut(5, () => "five"));
        Assert.Equal(6, list.Count);
        Assert.Equal(["zero", null, null, null, null, "five"], list);
        Assert.Equal("five", list.GetOrPut(5, () => "again"));
        Assert.Equal("two", list.GetOrPut(2, () => "two"));
        Assert.Equal(6, list.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => list.GetOrPut(-1, () => "neg"));
    }

    // --- StringExtensions: UUID.fromString is lenient, mirror OpenJDK 25 ---

    [Theory]
    [InlineData("ef1fb40e-3ae9-450d-b995-d0d81cded490", -1216055401155836659L, -5073919778547116912L)]
    [InlineData("EF1FB40E-3AE9-450D-B995-D0D81CDED490", -1216055401155836659L, -5073919778547116912L)]
    [InlineData("1-1-1-1-1", 4295032833L, 281474976710657L)]
    [InlineData("0-0-0-0-0", 0L, 0L)]
    [InlineData("1-2-3-4-5", 4295098371L, 1125899906842629L)]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff", -1L, -1L)]
    [InlineData("+1-2-3-4-5", 4295098371L, 1125899906842629L)]
    [InlineData("1-+2-3-4-5", 4295098371L, 1125899906842629L)]
    [InlineData("ef1fb40e-3ae9-450d-b995-d0d81cded49", -1216055401155836659L, -5074135053499503287L)] // 35 chars still parse
    [InlineData("12345678-1234-1234-1234-123456789abc", 1311768465173123636L, 1311693407470000828L)]
    [InlineData("7fffffffffffffff-1-1-1-1", -4294901759L, 281474976710657L)]
    [InlineData("1-2-3-ffff-1", 4295098371L, -281474976710655L)]
    [InlineData("1-2-3-10000-1", 4295098371L, 1L)]
    [InlineData("a-b-c-d-e", 42950393868L, 3659174697238542L)]
    [InlineData("١-١-١-١-١", 4295032833L, 281474976710657L)] // Character.digit accepts Unicode decimal digits
    [InlineData("00000000-0000-0000-0000-000000000000", 0L, 0L)]
    public void ToUUIDOrNullAcceptsWhatJavaAccepts(string text, long most, long least)
    {
        var uuid = text.ToUUIDOrNull();
        Assert.NotNull(uuid);
        Assert.Equal(most, uuid.Value.MostSignificantBits);
        Assert.Equal(least, uuid.Value.LeastSignificantBits);
        Assert.True(text.IsUUID());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ef1fb40e3ae9450db995d0d81cded490")]
    [InlineData("fffffffff-ffff-ffff-ffff-ffffffffffff")]
    [InlineData("-1-2-3-4-5")]
    [InlineData("1--2-3-4-5")]
    [InlineData("1-2-3-4-")]
    [InlineData("-1-2-3-4")]
    [InlineData("1-2-3-4-5-6")]
    [InlineData("1-2-3-4")]
    [InlineData("ef1fb40e-3ae9-450d-b995-d0d81cded4900")]
    [InlineData("ef1fb40e-3ae9-450d-b995-d0d81cded49g")]
    [InlineData(" ef1fb40e-3ae9-450d-b995-d0d81cded490")]
    [InlineData("ef1fb40e-3ae9-450d-b995-d0d81cded490 ")]
    [InlineData("ef1fb40e_3ae9_450d_b995_d0d81cded490")]
    [InlineData("0x1-0x2-0x3-0x4-0x5")]
    [InlineData("ffffffffffffffff-1-1-1-1")] // component overflows Long
    [InlineData("8000000000000000-1-1-1-1")]
    [InlineData("1-2-3-4-ffffffffffffffff")]
    [InlineData("g-1-1-1-1")]
    [InlineData("ef1fb40e-3ae9-450d-b995-d0d81cded490\n")]
    [InlineData("1-1-1-1-1-")]
    [InlineData("-----")]
    [InlineData("1-1-1--")]
    public void ToUUIDOrNullRejectsWhatJavaRejects(string text)
    {
        Assert.Null(text.ToUUIDOrNull());
        Assert.False(text.IsUUID());
    }

    // --- UncivExceptions ---

    [Theory]
    [InlineData(0, "Missing mods: []")]
    [InlineData(1, "Missing mods: [Mod1]")]
    [InlineData(4, "Missing mods: [Mod1, Mod2, Mod3, Mod4]")]
    [InlineData(5, "Missing mods: [Mod1, Mod2, Mod3, Mod4, Mod5]")]
    [InlineData(6, "Missing mods: [Mod1, Mod2, Mod3, Mod4, Mod5, ...]")]
    [InlineData(7, "Missing mods: [Mod1, Mod2, Mod3, Mod4, Mod5, ...]")]
    public void MissingModsExceptionMessageMatchesKotlin(int count, string expected)
    {
        var mods = Enumerable.Range(1, count).Select(i => "Mod" + i).ToList();
        var exception = new MissingModsException(mods);
        Assert.Equal(expected, exception.Message);
        Assert.Equal(mods, exception.MissingMods); // stored entirely
        Assert.IsAssignableFrom<UncivShowableException>(exception);
    }

    [Fact]
    public void ShowableExceptionKeepsTextAndCause()
    {
        var cause = new InvalidOperationException("inner");
        var exception = new UncivShowableException("Something [broke]", cause);
        Assert.Equal("Something [broke]", exception.Message);
        Assert.Same(cause, exception.InnerException);
        var nation = new MissingNationException("No nation", ["ModA", "ModB"]);
        Assert.Equal(["ModA", "ModB"], nation.ModNames);
        Assert.Equal("No nation", nation.Message);
    }

    // --- Versioning ---

    [Fact]
    public void VersionStringsAndCompatibility()
    {
        var version = new Version("4.22.13", 1275);
        Assert.Equal("4.22.13 (Build 1275)", version.ToSerializeString());
        Assert.Equal(version, Version.Current);

        Assert.Equal(5, CompatibilityVersion.CurrentCompatibilityNumber);
        Assert.Equal(new CompatibilityVersion(5, Version.Current), CompatibilityVersion.CurrentCompatibilityVersion);
        Assert.Equal(new CompatibilityVersion(1, new Version("4.1.14", 731)), CompatibilityVersion.FirstWithout);
        // ordering looks at the number only, equality at everything
        var other = new CompatibilityVersion(1, new Version("other", 0));
        Assert.Equal(0, CompatibilityVersion.FirstWithout.CompareTo(other));
        Assert.NotEqual(CompatibilityVersion.FirstWithout, other);
        Assert.True(CompatibilityVersion.CurrentCompatibilityVersion.CompareTo(CompatibilityVersion.FirstWithout) > 0);
        Assert.Equal(CompatibilityVersion.FirstWithout, new GameInfoSerializationVersion().Version);
    }

    [Fact]
    public void VersionJsonUsesKotlinFieldNames()
    {
        string json = UncivJson.Serialize(CompatibilityVersion.FirstWithout);
        Assert.Equal("""{"number":1,"createdWith":{"text":"4.1.14","number":731}}""", json);
        var back = UncivJson.Deserialize<CompatibilityVersion>(json);
        Assert.Equal(CompatibilityVersion.FirstWithout, back);
        var onlyVersion = UncivJson.Deserialize<GameInfoSerializationVersion>("""{"version":{"number":4,"createdWith":{"text":"x","number":2}},"other":1}""");
        Assert.Equal(new CompatibilityVersion(4, new Version("x", 2)), onlyVersion!.Version);
        Assert.IsAssignableFrom<IIsPartOfGameInfoSerialization>(Version.Current);
    }
}
