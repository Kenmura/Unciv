using Unciv.Utils;

namespace Unciv.Core.Tests.Utils;

public class GameRandomTests
{
    [Fact]
    public void Seed42_NextInt_MatchesKotlinGoldenValues()
    {
        var r = new GameRandom(42);
        Assert.Equal(972016666, r.NextInt());
        Assert.Equal(1740578880, r.NextInt());
        Assert.Equal(-408207414, r.NextInt());
        Assert.Equal(-112774692, r.NextInt());
        Assert.Equal(1162768683, r.NextInt());
    }

    [Fact]
    public void Seed42_NextIntBound_MatchesKotlinGoldenValues()
    {
        var r = new GameRandom(42);
        Assert.Equal(3, r.NextInt(10));
        Assert.Equal(0, r.NextInt(10));
        Assert.Equal(1, r.NextInt(10));
        Assert.Equal(2, r.NextInt(10));
        Assert.Equal(1, r.NextInt(10));
    }

    [Fact]
    public void Seed42_NextIntRange_MatchesKotlinGoldenValues()
    {
        var r = new GameRandom(42);
        Assert.Equal(-2, r.NextInt(-5, 5));
        Assert.Equal(-5, r.NextInt(-5, 5));
        Assert.Equal(-4, r.NextInt(-5, 5));
        Assert.Equal(-3, r.NextInt(-5, 5));
        Assert.Equal(-4, r.NextInt(-5, 5));
    }

    [Fact]
    public void Seed42_NextFloat_MatchesKotlinGoldenValues()
    {
        var r = new GameRandom(42);
        Assert.Equal(0.22631526f, r.NextFloat());
        Assert.Equal(0.4052601f, r.NextFloat());
        Assert.Equal(0.90495676f, r.NextFloat());
        Assert.Equal(0.97374254f, r.NextFloat());
        Assert.Equal(0.27072817f, r.NextFloat());
    }

    [Fact]
    public void Seed42_NextDouble_MatchesKotlinGoldenValues()
    {
        var r = new GameRandom(42);
        Assert.Equal(0.22631526597231777, r.NextDouble());
        Assert.Equal(0.9049568172356872, r.NextDouble());
        Assert.Equal(0.27072817312675046, r.NextDouble());
        Assert.Equal(0.9704900678643866, r.NextDouble());
        Assert.Equal(0.18360383903895205, r.NextDouble());
    }

    [Fact]
    public void Seed42_NextBoolean_MatchesKotlinGoldenValues()
    {
        var r = new GameRandom(42);
        Assert.False(r.NextBoolean());
        Assert.False(r.NextBoolean());
        Assert.True(r.NextBoolean());
        Assert.True(r.NextBoolean());
        Assert.False(r.NextBoolean());
    }

    [Fact]
    public void Seed42_NextLong_MatchesKotlinGoldenValues()
    {
        var r = new GameRandom(42);
        Assert.Equal(4174779793377534016L, r.NextLong());
        Assert.Equal(-1753237493227507236L, r.NextLong());
        Assert.Equal(4994053466947955232L, r.NextLong());
        Assert.Equal(-544362425981858600L, r.NextLong());
        Assert.Equal(3386893265803317318L, r.NextLong());
    }

    [Fact]
    public void Seed64Bit_MatchesKotlinGoldenValues()
    {
        var r = new GameRandom(133713371337L);
        Assert.Equal(-347780430, r.NextInt());
        Assert.Equal(-705617561, r.NextInt());
        Assert.Equal(1321891821, r.NextInt());
        Assert.Equal(-1428172439, r.NextInt());
        Assert.Equal(-1753788442, r.NextInt());

        r = new GameRandom(133713371337L);
        Assert.Equal(0.919026f, r.NextFloat());
        Assert.Equal(0.8357106f, r.NextFloat());
        Assert.Equal(0.30777687f, r.NextFloat());
        Assert.Equal(0.66747767f, r.NextFloat());
        Assert.Equal(0.59166425f, r.NextFloat());

        r = new GameRandom(133713371337L);
        Assert.Equal(0.9190260594444067, r.NextDouble());
        Assert.Equal(0.3077769229930295, r.NextDouble());
        Assert.Equal(0.5916643092100246, r.NextDouble());
        Assert.Equal(0.40131285610055223, r.NextDouble());
        Assert.Equal(0.3262775302957267, r.NextDouble());
    }

    [Fact]
    public void NextBytesFillsArray()
    {
        var r = new GameRandom(42);
        byte[] bytes = r.NextBytes(10);
        Assert.Equal(10, bytes.Length);
        Assert.Contains(bytes, b => b != 0);
    }

    [Fact]
    public void InvalidStateThrows()
    {
        Assert.Throws<ArgumentException>(() => new GameRandom(0, 0, 0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => new GameRandom(42).NextInt(5, 5));
        Assert.Throws<ArgumentException>(() => new GameRandom(42).NextInt(10, 5));
    }
}
