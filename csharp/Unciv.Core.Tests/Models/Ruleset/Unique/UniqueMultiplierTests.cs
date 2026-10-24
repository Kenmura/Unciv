using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// <c>Unique.getUniqueMultiplier</c> (ForEveryCountable / ForEveryAmountCountable) over the real Countables.
/// Values are hand-derived from Unique.kt; the JVM facts (Int.MIN_VALUE / -1 wraps, / 0 throws) are Java semantics.
/// </summary>
public class UniqueMultiplierTests
{
    private static GameContext State(int turns)
    {
        var game = new FakeGameInfo { Turns = turns };
        return new GameContext(civInfo: new FakeCiv("civ", game));
    }

    [Fact]
    public void ForEveryCountableMultipliesByTheCountable()
    {
        var unique = new UniqueObject("[+1 Gold] <for every [turns]>");
        Assert.Equal(7, unique.GetUniqueMultiplier(State(7)));
        Assert.Equal(0, unique.GetUniqueMultiplier(State(0)));
        Assert.Equal(3, unique.GetMultiplied(State(3)).Count());
        var calls = 0;
        unique.ForEachMultiplied(State(4), _ => calls++);
        Assert.Equal(4, calls);
    }

    [Fact]
    public void UnknownCountablesCountAsZeroAndKnownOnesStillWork()
    {
        Assert.Equal(0, new UniqueObject("[+1 Gold] <for every [[42] Monkeys]>").GetUniqueMultiplier(State(5)));
        Assert.Equal(0, new UniqueObject("[+1 Gold] <for every [turns]>").GetUniqueMultiplier(GameContext.EmptyState)); // null -> ?: 0
        Assert.Equal(5, new UniqueObject("[+1 Gold] <for every [5]>").GetUniqueMultiplier(GameContext.EmptyState)); // Integer needs no game
    }

    [Fact]
    public void MultipleForEveryModifiersMultiplyAndNegativesClampToZero()
    {
        Assert.Equal(6 * 5, new UniqueObject("[+1 Gold] <for every [turns]> <for every [5]>").GetUniqueMultiplier(State(6)));
        Assert.Equal(0, new UniqueObject("[+1 Gold] <for every [-3]>").GetUniqueMultiplier(State(1))); // Math.max(amount, 0)
        Assert.Equal(6, new UniqueObject("[+1 Gold] <for every [-2]> <for every [-3]>").GetUniqueMultiplier(State(1)));
    }

    [Fact]
    public void ForEveryAmountCountableDividesWithIntTruncation()
    {
        Assert.Equal(3, new UniqueObject("[+1 Gold] <for every [2] [turns]>").GetUniqueMultiplier(State(7))); // 7 / 2
        Assert.Equal(0, new UniqueObject("[+1 Gold] <for every [10] [turns]>").GetUniqueMultiplier(State(7)));
        Assert.Equal(0, new UniqueObject("[+1 Gold] <for every [2] [turns]>").GetUniqueMultiplier(State(-7))); // -7 / 2 = -3, clamped to 0
        // truncation toward zero for negatives: -7 / 2 == -3 on the JVM (not -4), observable through two negatives multiplying
        Assert.Equal(9, new UniqueObject("[+1 Gold] <for every [2] [turns]> <for every [2] [turns]>").GetUniqueMultiplier(State(-7)));
        Assert.Equal(4, new UniqueObject("[+1 Gold] <for every [-2] [turns]>").GetUniqueMultiplier(State(-8)));
        Assert.Equal(0, new UniqueObject("[+1 Gold] <for every [2] [Monkeys]>").GetUniqueMultiplier(State(7))); // ?: 0, 0 / 2
    }

    [Fact]
    public void MixedForEveryModifiersStackMultiplicatively()
    {
        // (turns=9) * (9 / 3) * 2
        Assert.Equal(9 * 3 * 2, new UniqueObject("[+1 Gold] <for every [turns]> <for every [3] [turns]> <for every [2]>").GetUniqueMultiplier(State(9)));
    }

    [Fact]
    public void PerEveryMinusOneNegatesAndWrapsLikeTheJvmForIntMinValue()
    {
        Assert.Equal(0, new UniqueObject("[+1 Gold] <for every [-1] [turns]>").GetUniqueMultiplier(State(5))); // -5 clamps to 0
        Assert.Equal(5, new UniqueObject("[+1 Gold] <for every [-1] [turns]>").GetUniqueMultiplier(State(-5)));
        // Int.MIN_VALUE / -1 == Int.MIN_VALUE on the JVM (no exception); the countable "-2147483648" is the Integer constant.
        // amount = 1 * MIN_VALUE, then Math.max(MIN_VALUE, 0) == 0
        Assert.Equal(0, new UniqueObject("[+1 Gold] <for every [-1] [-2147483648]>").GetUniqueMultiplier(GameContext.EmptyState));
        // two wrapping modifiers multiply to MIN_VALUE * MIN_VALUE == 0 (wraps), still clamped at 0
        Assert.Equal(0, new UniqueObject("[+1 Gold] <for every [-1] [-2147483648]> <for every [-1] [-2147483648]>").GetUniqueMultiplier(GameContext.EmptyState));
        // MIN_VALUE * -1 wraps to MIN_VALUE again, so the result stays at MIN_VALUE and clamps to 0
        Assert.Equal(0, new UniqueObject("[+1 Gold] <for every [-1] [-2147483648]> <for every [-1]>").GetUniqueMultiplier(GameContext.EmptyState));
    }

    [Fact]
    public void PerEveryZeroThrowsDivideByZeroWhereTheJvmThrowsArithmeticException()
    {
        var unique = new UniqueObject("[+1 Gold] <for every [0] [turns]>");
        Assert.Throws<DivideByZeroException>(() => unique.GetUniqueMultiplier(State(5)));
        // evaluated even when the countable is unknown (0 / 0)
        Assert.Throws<DivideByZeroException>(() => new UniqueObject("[+1 Gold] <for every [0] [Monkeys]>").GetUniqueMultiplier(State(5)));
    }

    [Fact]
    public void IgnoreMultiplicationForCachingAlwaysYieldsOne() =>
        Assert.Equal(1, new UniqueObject("[+1 Gold] <for every [turns]>").GetUniqueMultiplier(GameContext.IgnoreMultiplicationForCaching));
}
