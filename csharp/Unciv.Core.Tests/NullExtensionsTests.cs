using Unciv.Utils;

namespace Unciv.Core.Tests;

public class NullExtensionsTests
{
    [Fact]
    public void NotNullReturnsReferenceValue()
    {
        string? text = "a";
        Assert.Equal("a", text.NotNull());
    }

    [Fact]
    public void NotNullReturnsNullableStructValue()
    {
        int? number = 7;
        Assert.Equal(7, number.NotNull());
    }

    [Fact]
    public void NotNullThrowsNullReferenceExceptionNamingTheExpression()
    {
        string? text = null;
        var ex = Assert.Throws<NullReferenceException>(() => text.NotNull());
        Assert.Contains("text", ex.Message, StringComparison.Ordinal);

        int? number = null;
        Assert.Throws<NullReferenceException>(() => number.NotNull());
    }
}
