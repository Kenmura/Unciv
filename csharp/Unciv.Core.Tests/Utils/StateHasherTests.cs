using Unciv.Logic.Map;
using Unciv.Mathematics;
using Unciv.Models;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Core.Tests.Utils;

public class StateHasherTests
{
    [Fact]
    public void Primitives_ProduceDeterministicAndDistinctHashes()
    {
        ulong hTrue = StateHasher.ComputeHash(h => h.Add(true));
        ulong hFalse = StateHasher.ComputeHash(h => h.Add(false));
        Assert.NotEqual(hTrue, hFalse);

        ulong h1 = StateHasher.ComputeHash(h => h.Add(42));
        ulong h2 = StateHasher.ComputeHash(h => h.Add(43));
        ulong h1Again = StateHasher.ComputeHash(h => h.Add(42));
        Assert.Equal(h1, h1Again);
        Assert.NotEqual(h1, h2);

        ulong hNull = StateHasher.ComputeHash(h => h.Add((string?)null));
        ulong hEmpty = StateHasher.ComputeHash(h => h.Add(string.Empty));
        ulong hStr = StateHasher.ComputeHash(h => h.Add("Unciv"));
        Assert.NotEqual(hNull, hEmpty);
        Assert.NotEqual(hEmpty, hStr);
    }

    [Fact]
    public void FloatAndDouble_CanonicalizeNaN()
    {
        float nan1 = float.NaN;
        float nan2 = BitConverter.Int32BitsToSingle(0x7fc00001); // Alternate NaN payload
        Assert.True(float.IsNaN(nan1) && float.IsNaN(nan2));

        ulong hNan1 = StateHasher.ComputeHash(h => h.Add(nan1));
        ulong hNan2 = StateHasher.ComputeHash(h => h.Add(nan2));
        Assert.Equal(hNan1, hNan2);

        double dNan1 = double.NaN;
        double dNan2 = BitConverter.Int64BitsToDouble(0x7ff8000000000001L); // Alternate NaN payload
        Assert.True(double.IsNaN(dNan1) && double.IsNaN(dNan2));

        ulong hdNan1 = StateHasher.ComputeHash(h => h.Add(dNan1));
        ulong hdNan2 = StateHasher.ComputeHash(h => h.Add(dNan2));
        Assert.Equal(hdNan1, hdNan2);
    }

    [Fact]
    public void HexCoordAndVectors_ProduceDeterministicHashes()
    {
        ulong hCoord1 = StateHasher.ComputeHash(h => h.Add(HexCoord.Of(2, 3)));
        ulong hCoord2 = StateHasher.ComputeHash(h => h.Add(HexCoord.Of(3, 2)));
        ulong hCoord1Again = StateHasher.ComputeHash(h => h.Add(HexCoord.Of(2, 3)));

        Assert.Equal(hCoord1, hCoord1Again);
        Assert.NotEqual(hCoord1, hCoord2);

        ulong hVec2 = StateHasher.ComputeHash(h => h.Add(new Vector2(1.5f, 2.5f)));
        ulong hVec2Again = StateHasher.ComputeHash(h => h.Add(new Vector2(1.5f, 2.5f)));
        Assert.Equal(hVec2, hVec2Again);

        ulong hVec3 = StateHasher.ComputeHash(h => h.Add(new Vector3(1f, 2f, 3f)));
        ulong hVec3Again = StateHasher.ComputeHash(h => h.Add(new Vector3(1f, 2f, 3f)));
        Assert.Equal(hVec3, hVec3Again);
    }

    [Fact]
    public void StatsAndStatMap_ProduceDeterministicHashes()
    {
        var s1 = new Stats { Production = 10f, Gold = 5f, Food = 2f };
        var s2 = new Stats { Production = 10f, Gold = 5f, Food = 2f };
        var s3 = new Stats { Production = 10f, Gold = 6f, Food = 2f };

        ulong h1 = StateHasher.ComputeHash(h => h.Add(s1));
        ulong h2 = StateHasher.ComputeHash(h => h.Add(s2));
        ulong h3 = StateHasher.ComputeHash(h => h.Add(s3));

        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, h3);

        var map1 = new StatMap { ["Terrain"] = s1, ["Buildings"] = s2 };
        var map2 = new StatMap { ["Terrain"] = s1, ["Buildings"] = s2 };
        ulong hMap1 = StateHasher.ComputeHash(h => h.Add(map1));
        ulong hMap2 = StateHasher.ComputeHash(h => h.Add(map2));
        Assert.Equal(hMap1, hMap2);
    }

    [Fact]
    public void Counter_PreservesInsertionOrder()
    {
        var c1 = new Counter<string> { ["Rome"] = 1, ["Greece"] = 2 };
        var c2 = new Counter<string> { ["Rome"] = 1, ["Greece"] = 2 };
        var c3 = new Counter<string> { ["Greece"] = 2, ["Rome"] = 1 };

        ulong h1 = StateHasher.ComputeHash(h => h.Add(c1));
        ulong h2 = StateHasher.ComputeHash(h => h.Add(c2));
        ulong h3 = StateHasher.ComputeHash(h => h.Add(c3));

        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, h3); // Counter preserves insertion order
    }

    [Fact]
    public void GameRandom_HashesInternalStateAcrossSteps()
    {
        var rng1 = new GameRandom(42);
        var rng2 = new GameRandom(42);

        ulong hInitial1 = StateHasher.ComputeHash(h => h.Add(rng1));
        ulong hInitial2 = StateHasher.ComputeHash(h => h.Add(rng2));
        Assert.Equal(hInitial1, hInitial2);

        int val1 = rng1.NextInt();
        ulong hStep1 = StateHasher.ComputeHash(h => h.Add(rng1));
        Assert.NotEqual(hInitial1, hStep1);

        int val2 = rng2.NextInt();
        ulong hStep2 = StateHasher.ComputeHash(h => h.Add(rng2));
        Assert.Equal(val1, val2);
        Assert.Equal(hStep1, hStep2);
    }

    [Fact]
    public void UnorderedMap_SortingGuaranteesOrderInvariance()
    {
        var mapA = new Dictionary<string, int>
        {
            ["Zulu"] = 10,
            ["Alpha"] = 20,
            ["Bravo"] = 30
        };

        var mapB = new Dictionary<string, int>
        {
            ["Alpha"] = 20,
            ["Zulu"] = 10,
            ["Bravo"] = 30
        };

        ulong hA = StateHasher.ComputeHash(h => h.AddUnorderedMap(mapA, k => k, (hasher, v) => hasher.Add(v)));
        ulong hB = StateHasher.ComputeHash(h => h.AddUnorderedMap(mapB, k => k, (hasher, v) => hasher.Add(v)));

        Assert.Equal(hA, hB);
    }

    [Fact]
    public void Sequence_OrderMatters()
    {
        int[] seq1 = [1, 2, 3];
        int[] seq2 = [3, 2, 1];

        ulong h1 = StateHasher.ComputeHash(h => h.AddSequence(seq1, (hasher, val) => hasher.Add(val)));
        ulong h2 = StateHasher.ComputeHash(h => h.AddSequence(seq2, (hasher, val) => hasher.Add(val)));

        Assert.NotEqual(h1, h2);
    }

    private sealed class SampleEntity(int id, HexCoord position) : IStateHashable
    {
        public int Id { get; } = id;
        public HexCoord Position { get; set; } = position;

        public void HashState(StateHasher hasher)
        {
            hasher.Add(Id).Add(Position);
        }
    }

    [Fact]
    public void IStateHashable_WorksForCustomGameObjects()
    {
        var entity1 = new SampleEntity(101, HexCoord.Of(5, -2));
        var entity2 = new SampleEntity(101, HexCoord.Of(5, -2));
        var entity3 = new SampleEntity(102, HexCoord.Of(5, -2));

        ulong h1 = StateHasher.ComputeHash(h => h.Add(entity1));
        ulong h2 = StateHasher.ComputeHash(h => h.Add(entity2));
        ulong h3 = StateHasher.ComputeHash(h => h.Add(entity3));

        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, h3);

        string hex1 = StateHasher.ComputeHexString(h => h.Add(entity1));
        Assert.Equal(16, hex1.Length);
        Assert.Equal(h1.ToString("X16", System.Globalization.CultureInfo.InvariantCulture), hex1);
    }

    // Golden values from an independent implementation (Python: FNV-1a 64 over the byte layout in DETERMINISM.md,
    // XorWow ported separately and checked against the JVM sequence for seed 42). They pin the algorithm and the
    // encoding, which the equal/not-equal tests above cannot: a changed byte layout would silently break desync detection.
    [Fact]
    public void GoldenHashes_PinAlgorithmAndByteLayout()
    {
        Assert.Equal("CBF29CE484222325", StateHasher.ComputeHexString(_ => { }));
        Assert.Equal("AF63BC4C8601B62C", StateHasher.ComputeHexString(h => h.Add(true)));
        Assert.Equal("8D9AADC8352FDF7F", StateHasher.ComputeHexString(h => h.Add(42))); // little endian
        Assert.Equal("FC1A35225397861C", StateHasher.ComputeHexString(h => h.Add(-2L)));
        Assert.Equal("4A98C77F9BA36558", StateHasher.ComputeHexString(h => h.Add(1.5f)));
        Assert.Equal("D969796611AEC9B7", StateHasher.ComputeHexString(h => h.Add("Unciv"))); // length, then UTF-16LE units
        Assert.Equal("994F76653E2A3951", StateHasher.ComputeHexString(h => h.Add((string?)null))); // int -1
        Assert.Equal("06CD822B3B579854", StateHasher.ComputeHexString(h => h.Add(HexCoord.Of(2, 3))));
        Assert.Equal("98E9A7BD4727F542", StateHasher.ComputeHexString(h => h.Add(new Stats { Production = 10f, Gold = 5f, Food = 2f })));
        Assert.Equal("A0EB07B7D19A60A0", StateHasher.ComputeHexString(h => h.Add(new Counter<string> { ["Rome"] = 1, ["Greece"] = 2 })));

        var unordered = new Dictionary<string, int> { ["Zulu"] = 10, ["Alpha"] = 20, ["Bravo"] = 30 };
        Assert.Equal("2313DBCFFE886890", StateHasher.ComputeHexString(h => h.AddUnorderedMap(unordered, k => k, (hasher, v) => hasher.Add(v))));

        var rng = new GameRandom(42);
        Assert.Equal("D0EA959456A64405", StateHasher.ComputeHexString(h => h.Add(rng))); // marker 1, then x y z w v addend
        Assert.Equal(972016666, rng.NextInt());
        Assert.Equal("2D219475C54F82E4", StateHasher.ComputeHexString(h => h.Add(rng)));
    }

    [Fact]
    public void UnorderedMapAndSet_RejectDuplicateKeys()
    {
        // Without unique keys the sort keeps the source order, so the hash would depend on it
        var entries = new List<KeyValuePair<int, int>> { new(1, 10), new(2, 20) };
        Assert.Throws<ArgumentException>(() => StateHasher.ComputeHash(h => h.AddUnorderedMap(entries, _ => "same", (hasher, v) => hasher.Add(v))));
        Assert.Throws<ArgumentException>(() => StateHasher.ComputeHash(h => h.AddUnorderedSet(new[] { 1, 2 }, _ => "same")));
    }

    [Fact]
    public void UnorderedSet_IsOrderIndependent()
    {
        ulong forward = StateHasher.ComputeHash(h => h.AddUnorderedSet(new[] { "a", "b" }, k => k));
        ulong backward = StateHasher.ComputeHash(h => h.AddUnorderedSet(new[] { "b", "a" }, k => k));
        Assert.Equal(forward, backward);
        Assert.Equal("D671FB1B30AC7CCC", forward.ToString("X16", System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotEqual(forward, StateHasher.ComputeHash(h => h.AddUnorderedSet(new[] { "a" }, k => k)));
    }

    [Fact]
    public void Enum_IsHashedByName()
    {
        Assert.Equal("386D968BEA3072E9", StateHasher.ComputeHexString(h => h.Add(Stat.Gold))); // same as the string "Gold"
        Assert.Equal(StateHasher.ComputeHash(h => h.Add("Gold")), StateHasher.ComputeHash(h => h.Add(Stat.Gold)));
        Assert.NotEqual(StateHasher.ComputeHash(h => h.Add(Stat.Gold)), StateHasher.ComputeHash(h => h.Add(Stat.Food)));
    }
}
