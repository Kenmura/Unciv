using Unciv.Utils;

namespace Unciv.Core.Tests.Utils;

public class KotlinLazyTests
{
    [Fact]
    public void SuccessIsCachedAndInitializerRunsOnce()
    {
        int calls = 0;
        var lazy = new KotlinLazy<int>(() => ++calls);
        Assert.False(lazy.IsValueCreated);
        Assert.Equal(1, lazy.Value);
        Assert.Equal(1, lazy.Value);
        Assert.Equal(1, calls);
        Assert.True(lazy.IsValueCreated);
    }

    [Fact]
    public void ExceptionIsNotCachedAndInitializerIsRetried()
    {
        int calls = 0;
        var lazy = new KotlinLazy<string>(() => ++calls < 3 ? throw new InvalidOperationException("boom " + calls) : "ok");
        Assert.Equal("boom 1", Assert.Throws<InvalidOperationException>(() => lazy.Value).Message);
        Assert.False(lazy.IsValueCreated);
        Assert.Equal("boom 2", Assert.Throws<InvalidOperationException>(() => lazy.Value).Message);
        Assert.Equal("ok", lazy.Value);
        Assert.Equal("ok", lazy.Value);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void NullValueIsCached()
    {
        int calls = 0;
        var lazy = new KotlinLazy<string?>(() => { calls++; return null; });
        Assert.Null(lazy.Value);
        Assert.Null(lazy.Value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ConcurrentAccessRunsInitializerOnce()
    {
        int calls = 0;
        var lazy = new KotlinLazy<object>(() => { Interlocked.Increment(ref calls); Thread.Sleep(20); return new object(); });
        var results = new object[16];
        var threads = Enumerable.Range(0, results.Length).Select(i => new Thread(() => results[i] = lazy.Value)).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Same(results[0], r));
    }
}
