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
}
