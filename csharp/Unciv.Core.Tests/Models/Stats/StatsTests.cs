using Unciv.Models.StatTypes;

namespace Unciv.Core.Tests.Models.Stats;

using Stats = Unciv.Models.StatTypes.Stats;

[Collection("TranslationState")]
public class StatsTests
{
    // Port of BasicTests.statParserWorks
    [Theory]
    [InlineData("+1 Production", true)]
    [InlineData("+1 Gold, +2 Production", true)]
    [InlineData("+1 Gold from tree", false)]
    [InlineData("1 Gold", false)]
    [InlineData("", false)]
    [InlineData("+1 Gold,+2 Production", false)]
    [InlineData("+1 gold", false)]
    [InlineData("+1 Gold\n", false)]
    [InlineData("+١ Gold", false)]  // Arabic-Indic digit: Java \d is ASCII only
    [InlineData("-3 Faith, +0 Food, -10 Happiness", true)]
    public void IsStats(string input, bool expected) => Assert.Equal(expected, Stats.IsStats(input));

    [Fact]
    public void ParseWorks()
    {
        Assert.True(Stats.Parse("+1 Gold, +2 Production").Equals(new Stats(gold: 1f, production: 2f)));
        Assert.True(Stats.Parse("-3 Faith, +2 Faith, -1 Food").Equals(new Stats(faith: -1f, food: -1f)));
        Assert.Throws<NullReferenceException>(() => Stats.Parse("1 Gold"));
    }

    // Port of BasicTests.statMathRandomResultTest. Kotlin's seeded random is reproduced by KotlinRandom below.
    [Fact]
    public void StatMathRandomResultTest()
    {
        var expected = new Stats(
            production: 212765.08f, food: 776.8394f, gold: -4987.297f, science: 14880.18f,
            culture: -49435.21f, happiness: -13046.4375f, faith: 7291.375f);
        var stats = StatMathRunner(42);
        Assert.True(stats.Equals(expected), $"got {stats.Production}, {stats.Food}, {stats.Gold}, {stats.Science}, {stats.Culture}, {stats.Happiness}, {stats.Faith}");
    }

    private static Stats StatMathRunner(int iterations)
    {
        var random = new KotlinRandom(42);
        int statCount = StatExtensions.Entries.Count;
        var stats = new Stats();
        for (int i = 0; i < iterations; i++)
        {
            float value = (float)random.NextDouble(-10.0, 10.0);
            stats.Add(new Stats(gold: value));
            foreach (var it in stats)   // lazy: later fields are read after earlier ones were mutated, as in Kotlin
            {
                var stat = StatExtensions.Entries[((int)it.Key + random.NextInt(1, statCount)) % statCount];
                stats.Add(stat, -it.Value);
            }
            var stat2 = StatExtensions.Entries[random.NextInt(0, statCount)];
            stats.Add(stat2, (stats * 4)[stat2]);
            stats.TimesInPlace(0.8f);
            if (stats.Max() > 1000000f) stats.TimesInPlace(0.1f);
        }
        return stats;
    }

    [Fact]
    public void IterationSkipsZeroAndIsLazy()
    {
        var s = new Stats(food: 1f, faith: -2f);
        Assert.Equal(new[] { Stat.Food, Stat.Faith }, s.Select(p => p.Key).ToArray());
        Assert.Equal(new Stats.StatValuePair(Stat.Food, 1f), s.First());
        var seq = s.AsSequence();
        s.Gold = 5f;   // modifications before enumeration are visible
        Assert.Equal(3, seq.Count());
    }

    [Fact]
    public void IndexerAndAdd()
    {
        var s = new Stats();
        foreach (var stat in StatExtensions.Entries) s[stat] = (int)stat + 1;
        Assert.Equal(new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f }, new[] { s.Production, s.Food, s.Gold, s.Science, s.Culture, s.Happiness, s.Faith });
        Assert.Same(s, s.Add(Stat.Gold, 10f));
        Assert.Equal(13f, s[Stat.Gold]);
        var t = new Stats(gold: 1f);
        Assert.Same(s, s.Add(t));
        Assert.Equal(14f, s.Gold);
        Assert.Equal(1f, t.Gold);
    }

    [Fact]
    public void EqualsIsValueOverloadButObjectEqualsIsIdentity()
    {
        var a = new Stats(gold: 1f);
        var b = new Stats(gold: 1f);
        Assert.True(a.Equals(b));
        Assert.False(a.Equals((object)b));
        Assert.False(new Stats(food: float.NaN).Equals(new Stats(food: float.NaN)));
        Assert.True(new Stats(food: 0f).Equals(new Stats(food: -0f)));
    }

    [Fact]
    public void OperatorsAndFloatSemantics()
    {
        var a = new Stats(production: 1f, gold: 2.5f);
        var b = new Stats(production: 3f, food: 1f);
        var sum = a + b;
        Assert.True(sum.Equals(new Stats(production: 4f, food: 1f, gold: 2.5f)));
        Assert.True((a - b).Equals(new Stats(production: -2f, food: -1f, gold: 2.5f)));
        Assert.True((a * 2).Equals(new Stats(production: 2f, gold: 5f)));
        Assert.True((a * 0.5f).Equals(new Stats(production: 0.5f, gold: 1.25f)));
        Assert.True((a / 4f).Equals(new Stats(production: 0.25f, gold: 0.625f)));
        Assert.True(a.Equals(new Stats(production: 1f, gold: 2.5f)));   // operands unchanged
        // float, not double: 0.1f*3 rounds to 0.3f in single precision
        Assert.Equal(0.3f, (new Stats(food: 0.1f) * 3).Food);
        // division is times(1/n) with a float reciprocal: 1/3f * 3f == 1f, whereas 3f/3f is also 1f; pick a case that differs
        Assert.Equal(10f * (1f / 3f), (new Stats(food: 10f) / 3f).Food);
    }

    [Fact]
    public void CloneClearIsEmptyTimesInPlace()
    {
        var a = new Stats(production: 1f, faith: 2f);
        var c = a.Clone();
        Assert.NotSame(a, c);
        Assert.True(a.Equals(c));
        a.TimesInPlace(2f);
        Assert.Equal(2f, a.Production);
        Assert.Equal(1f, c.Production);
        Assert.False(a.IsEmpty());
        a.Clear();
        Assert.True(a.IsEmpty());
        Assert.True(new Stats().IsEmpty());
    }

    [Fact]
    public void RankingWeights()
    {
        var s = new Stats(1f, 1f, 1f, 1f, 1f, 1f, 1f);
        s.ApplyRankingWeights();
        Assert.Equal(12.01f, s.Production);
        Assert.Equal(14f, s.Food);
        Assert.Equal(6f, s.Gold);
        Assert.Equal(9.01f, s.Science);
        Assert.Equal(8f, s.Culture);
        Assert.Equal(10f, s.Happiness);
        Assert.Equal(7f, s.Faith);
    }

    [Fact]
    public void Aggregates()
    {
        var s = new Stats(production: 1f, food: -4f, gold: 3f, science: 9f, culture: -1f, happiness: 0.5f, faith: 2f);
        Assert.Equal(1f + -4f + 3f + 9f + -1f + 0.5f + 2f, s.Sum());
        Assert.Equal(-4f, s.Min());
        Assert.Equal(9f, s.Max());
        Assert.Equal(0f, new Stats().Min());
        Assert.Equal(0f, new Stats(gold: -1f).Max());   // zeros of the other stats win
    }

    [Fact]
    public void ToStringVariants()
    {
        var s = new Stats(production: 1.9f, food: -1.9f);
        Assert.Equal("+1 \u2699Production, -1 \u2042Food", s.ToString());   // toInt truncates toward zero; stat names carry their font character
        Assert.Equal("+1 Production, -1 Food", s.ToStringWithoutIcons());
        Assert.Equal("1 Production, -1 Food", s.ToStringForNotifications().Replace("+", ""));
        Assert.Equal("+1 Production, -1 Food", s.ToStringForNotifications());
        Assert.Equal("+1 \u2699, -1 \u2042", s.ToStringOnlyIcons());
        Assert.Equal("1 \u2699, -1 \u2042", s.ToStringOnlyIcons(addPlusSign: false));
        Assert.Equal("", new Stats().ToString());
        Assert.Equal("+0 \u00A4Gold", new Stats(gold: 0.3f).ToString());          // 0.3f != 0 so it is listed; > 0 gives a sign
    }

    [Fact]
    public void NamedStats()
    {
        var n = new NamedStats();
        Assert.Throws<InvalidOperationException>(() => n.Name);
        n.Name = "Granary";
        n.Food = 2f;
        Assert.Equal("Granary", n.ToString());
        var plain = n.CloneStats();
        Assert.Equal(typeof(Stats), plain.GetType());
        Assert.Equal(2f, plain.Food);
        INamed named = n;
        named.Name = "Barn";
        Assert.Equal("Barn", n.Name);
    }

    [Fact]
    public void StatMapAddClonesAndMerges()
    {
        var map = new StatMap();
        var first = new Stats(gold: 1f);
        map.Add("a", first);
        map.Add("b", new Stats(food: 1f));
        map.Add("a", new Stats(gold: 2f));
        Assert.NotSame(first, map["a"]);
        Assert.Equal(1f, first.Gold);          // original untouched
        Assert.Equal(3f, map["a"].Gold);
        Assert.Equal(new[] { "a", "b" }, map.Keys.ToArray());   // insertion order
    }

    [Fact]
    public void StatEnumHelpers()
    {
        Assert.Equal(new[] { "Production", "Food", "Gold", "Science", "Culture", "Happiness", "Faith" }, StatExtensions.Names().ToArray());
        Assert.Equal(Stat.Gold, StatExtensions.SafeValueOf("Gold"));
        Assert.Null(StatExtensions.SafeValueOf("gold"));
        Assert.True(StatExtensions.IsStat("Faith"));
        Assert.False(StatExtensions.IsStat("Faith "));
        Assert.Equal(Stat.Faith, StatExtensions.ValueOf("Faith"));
        Assert.Throws<ArgumentException>(() => StatExtensions.ValueOf("Nope"));
        Assert.Equal(new[] { Stat.Production, Stat.Food }, StatExtensions.Entries.Where(s => s.IsCityWide()).ToArray());
        Assert.False(Stat.Gold.IsCityWide());
        Assert.True(Stat.Production.IsCityWide());
        Assert.Equal(5, StatExtensions.StatsUsableToBuy.Count);
        Assert.DoesNotContain(Stat.Production, StatExtensions.StatsUsableToBuy);
        Assert.DoesNotContain(Stat.Happiness, StatExtensions.StatsUsableToBuy);
        Assert.Equal(new Stats(food: 2f, production: 1f).Food, Stats.DefaultCityCenterMinimum.Food);
        Assert.True(Stats.ZERO.IsEmpty());
    }

    [Fact]
    public void SubStatMatchesOnText()
    {
        Assert.Equal(SubStat.GoldenAgePoints, SubStatExtensions.SafeValueOf("Golden Age points"));
        Assert.Equal(SubStat.StoredFood, SubStatExtensions.SafeValueOf("Stored Food"));
        Assert.Null(SubStatExtensions.SafeValueOf("GoldenAgePoints"));
        Assert.Equal("Stored Food", SubStat.StoredFood.Text());
    }
}

/// <summary>Port of kotlin.random.Random(seed: Int) = XorWowRandom, enough for the stat-math fixture.</summary>
internal class KotlinRandom
{
    private int x, y, z, w, v, addend;

    public KotlinRandom(int seed)
    {
        x = seed; y = seed >> 31; z = 0; w = 0; v = ~seed;
        addend = unchecked((x << 10) ^ (int)((uint)y >> 4));
        for (int i = 0; i < 64; i++) NextInt();
    }

    public int NextInt()
    {
        unchecked
        {
            int t = x;
            t ^= (int)((uint)t >> 2);
            x = y; y = z; z = w;
            int v0 = v;
            w = v0;
            t = (t ^ (t << 1)) ^ v0 ^ (v0 << 4);
            v = t;
            addend += 362437;
            return t + addend;
        }
    }

    private int NextBits(int bitCount) => (int)((uint)NextInt() >> (32 - bitCount)) & (-bitCount >> 31);

    public double NextDouble()
    {
        long hi = (uint)NextBits(26);
        long lo = (uint)NextBits(27);
        return ((hi << 27) + lo) / (double)(1L << 53);
    }

    public double NextDouble(double from, double until) => from + NextDouble() * (until - from);

    public int NextInt(int from, int until)
    {
        int n = until - from;
        if (n > 0)
        {
            int rnd;
            if ((n & -n) == n)
            {
                int bitCount = 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)n);
                rnd = NextBits(bitCount);
            }
            else
            {
                int bits, v2;
                do
                {
                    bits = (int)((uint)NextInt() >> 1);
                    v2 = bits % n;
                } while (unchecked(bits - v2 + (n - 1)) < 0);
                rnd = v2;
            }
            return from + rnd;
        }
        throw new NotSupportedException("range case not needed");
    }
}
