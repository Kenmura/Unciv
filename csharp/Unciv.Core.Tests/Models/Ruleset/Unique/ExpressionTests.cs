using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unique.Expressions;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// Ported from tests/src/com/unciv/uniques/ExpressionTests.kt.
/// Reference values marked "JVM" were produced by running java.lang.Math / kotlin-stdlib 2.3.0 (java 25) over the same inputs;
/// AST texts and exception positions are hand-derived from the Kotlin source.
/// </summary>
[Collection("RulesetCache")]
public class ExpressionTests
{
    private static readonly double Epsilon = System.Math.BitIncrement(100.0) - 100.0;

    [Fact]
    public void TestPrimitiveExpressions()
    {
        (string Expression, double Expected)[] input =
        [
            (".98234792374", .98234792374),
            ("4 - 2 + 4 + 30 + 6", 42.0),
            ("2 + 4 * 10", 42.0),
            ("2 * 4 + 10", 18.0),
            ("42 / 7 / 2", 3.0),
            ("42 / 2 / 7", 3.0),
            ("666.66 % 7", 666.66 % 7),
            ("42424 * -1 % 7", 3.0), // true modulo, not kotlin's -4242.0 % 7 == -4
            ("2 ^ 3 ^ 2", 512.0),
            ("pi * .5", System.Math.PI / 2),
            ("(2+1.5)*(4+10)", (2 + 1.5) * (4 + 10)),
            ("max(0,1)", 1.0),
            ("min(0,1)", 0.0),
            ("max(5,3,7,2)", 7.0),
        ];
        var fails = new List<string>();
        foreach (var (expression, expected) in input)
        {
            double actual;
            try { actual = Parser.Eval(expression); }
            catch (Parser.ParsingError ex) { fails.Add($"\"{expression}\" threw {ex.GetType().Name}: {ex.Message}"); continue; }
            if (System.Math.Abs(actual - expected) >= Epsilon)
                fails.Add($"\"{expression}\" = {actual}, expected {expected}; AST {Parser.GetASTDebugDescription(expression)}");
        }
        Assert.Empty(fails);
    }

    [Fact]
    public void TestInvalidExpressions()
    {
        (string Expression, Type Expected)[] input =
        [
            ("fake_function(2)", typeof(Parser.UnknownIdentifier)),
            ("98.234.792.374", typeof(Parser.InvalidConstant)),
            ("", typeof(Parser.MissingOperand)),
            ("() - 2", typeof(Parser.MissingOperand)),
            ("((4 + 2) * 2", typeof(Parser.UnmatchedParentheses)),
            ("(3 + 9) % 2)", typeof(Parser.UnmatchedParentheses)),
            ("1 + []", typeof(Parser.EmptyBraces)),
            ("1 + [[Your] Cities]]", typeof(Parser.UnmatchedBraces)),
            ("[[[embarked] Units] + 1", typeof(Parser.UnmatchedBraces)),
        ];
        var fails = new List<string>();
        foreach (var (expression, expected) in input)
        {
            Exception? result = null;
            try { Parser.Eval(expression); }
            catch (Exception ex) { result = ex; }
            if (result == null || !expected.IsInstanceOfType(result))
                fails.Add($"\"{expression}\" should throw {expected.Name} but {(result == null ? "didn't" : "threw " + result.GetType().Name)}");
        }
        Assert.Empty(fails);
    }

    // The 'with countables' fixture builds a TestGame (T035); this is the same input over fake game state.
    [Fact]
    public void TestExpressionsWithCountables()
    {
        var game = new FakeGameInfo();
        var civ = new FakeCiv("civ", game);
        game.FakeCivs.Add(civ);
        var capitalTile = new FakeTile(new HexCoord(0, 0));
        var city = new FakeCity("city", civ, capitalTile);
        civ.FakeCities.Add(city);
        city.CityFilters.Add("Your");
        // 7 owned tiles: the city center (worked, improved), and 6 unimproved
        var center = new FakeTile(new HexCoord(0, 0));
        center.Matching.Add("worked");
        city.OwnedTiles.Add(center);
        for (int i = 1; i <= 6; i++)
        {
            var tile = new FakeTile(new HexCoord(i, 0));
            tile.Matching.Add("unimproved");
            city.OwnedTiles.Add(tile);
        }
        var context = new GameContext(civInfo: civ, city: city);

        Assert.Equal(1.0, Parser.Eval("√[[Your] Cities]", context), Epsilon);
        Assert.Equal(100.0 / 6, Parser.Eval("[Owned [worked] Tiles] / [Owned [unimproved] Tiles] * 100", context), Epsilon);
    }

    // T154: the real-TestGame variant of the Kotlin fixture (same input as above, over the actual city and tiles).
    [Fact]
    public void TestExpressionsWithCountablesOverTestGame()
    {
        var game = new Support.TestGame();
        game.MakeHexagonalMap(2);
        var civ = game.AddCiv();
        var city = game.AddCity(civ, game.GetTile(HexCoord.Zero));
        var context = GameContext.ForCity(city);

        Assert.Equal(1.0, Parser.Eval("√[[Your] Cities]", context), Epsilon);
        // city center counts as improved
        Assert.Equal(100.0 / 6, Parser.Eval("[Owned [worked] Tiles] / [Owned [unimproved] Tiles] * 100", context), Epsilon);
    }

    [Fact]
    public void ExpressionsWithoutGameInfoEvaluateCountablesAsZero()
    {
        Assert.Equal(1.0, Parser.Eval("[turns] + 1"));
        Assert.Equal(0.0, Parser.Eval("[no such countable]"));
    }

    [Fact]
    public void AstDebugDescriptionFollowsPrecedenceAndAssociativity()
    {
        Assert.Equal("(2.0 + (4.0 * 10.0))", Parser.GetASTDebugDescription("2 + 4 * 10"));
        Assert.Equal("((42.0 / 7.0) / 2.0)", Parser.GetASTDebugDescription("42 / 7 / 2"));
        Assert.Equal("(2.0 ^ (3.0 ^ 2.0))", Parser.GetASTDebugDescription("2 ^ 3 ^ 2"));
        Assert.Equal("((- 42424.0) % 7.0)", Parser.GetASTDebugDescription("-42424 % 7"));
        Assert.Equal("[FunctionCall: max(1.0, [Countable: turns])]", Parser.GetASTDebugDescription("max(1, [turns])"));
        Assert.Equal("(1.0E7 + 0.001)", Parser.GetASTDebugDescription("10000000 + 0.001"));
    }

    [Fact]
    public void ErrorsCarryTheKotlinMessagesAndPositions()
    {
        var ex = Assert.Throws<Parser.UnknownIdentifier>(() => Parser.Eval("1 + foo"));
        Assert.Equal("Unknown identifier: \"foo\"", ex.Message);
        Assert.Equal(4, ex.Position);
        var arity = Assert.Throws<Parser.InvalidFunctionArity>(() => Parser.Eval("max(1)"));
        Assert.Equal("Function max at position 0 should get 2 arguments and up arguments, got 1", arity.Message);
        Assert.Equal("Missing operand", Assert.Throws<Parser.MissingOperand>(() => Parser.Eval("")).Message);
        Assert.Equal("Unmatched opening parenthesis", Assert.Throws<Parser.UnmatchedParentheses>(() => Parser.Eval("(1")).Message);
        Assert.Equal("Unmatched closing parenthesis", Assert.Throws<Parser.UnmatchedParentheses>(() => Parser.Eval("1)")).Message);
        Assert.Equal("Unmatched square braces", Assert.Throws<Parser.UnmatchedBraces>(() => Parser.Eval("1]")).Message);
        Assert.Equal("Invalid constant: 1.2.3", Assert.Throws<Parser.InvalidConstant>(() => Parser.Eval("1.2.3")).Message);
    }

    [Fact]
    public void TokenizerIsLazySoTheParserFailsOnEarlierTokensFirst()
    {
        // "1 + + ]": the parser meets the missing operand before the tokenizer reaches the stray ']'
        Assert.Throws<Parser.MissingOperand>(() => Parser.Eval("1 + * ]"));
    }

    [Fact]
    public void OperatorSemanticsMatchTheJvm()
    {
        Assert.Equal(0.0, Parser.Eval("5 / 0")); // division by zero is 0 by design
        // JVM: System.Math.pow(1, NaN) and pow(+-1, +-Infinity) are NaN where .NET gives 1
        Assert.True(double.IsNaN(Parser.Eval("1 ^ (0 / 0 + sqrt(-1))")));
        Assert.True(double.IsNaN(Parser.Eval("sqrt(-8)")));
        Assert.Equal(1024.0, Parser.Eval("2 ^ 10"));
        // JVM: System.Math.pow(0, -1) = Infinity
        Assert.True(double.IsPositiveInfinity(Parser.Eval("0 ^ -1")));
        // JVM: max/min of a list holding NaN is NaN; maxOf(-0.0, 0.0) is 0.0 and minOf(0.0, -0.0) is -0.0
        Assert.True(double.IsNaN(Parser.Eval("max(1, sqrt(-1), 3)")));
        Assert.False(double.IsNegative(Parser.Eval("max(0, -0)")));
        Assert.False(double.IsNegative(Parser.Eval("max(-0, 0)")));
        Assert.True(double.IsNegative(Parser.Eval("min(0, -0)")));
        Assert.True(double.IsNegative(Parser.Eval("min(-0, 0)")));
        Assert.Equal(3.0, Parser.Eval("ceil(2.1)"));
        Assert.Equal(2.0, Parser.Eval("floor(2.9)"));
        Assert.Equal(5.0, Parser.Eval("abs(-5)"));
        Assert.Equal(3.0, Parser.Eval("sqrt(9)"));
        Assert.Equal(System.Math.E, Parser.Eval("e"));
        Assert.Equal(System.Math.PI, Parser.Eval("π"));
    }

    [Fact]
    public void ExpressionCountableRoundsHalfUpLikeKotlinRoundToInt()
    {
        var game = new FakeGameInfo { Turns = 5 };
        var context = new GameContext(gameInfo: game);
        // JVM roundToInt: 2.5 -> 3, -2.5 -> -2, 0.49999999999999994 -> 0, 1e10 saturates
        Assert.Equal(3, Countables.GetCountableAmount("[turns] * 0.5", context));
        Assert.Equal(-2, Countables.GetCountableAmount("0 - 2.5", context));
        Assert.Equal(0, Countables.GetCountableAmount("0.49999999999999994 + 0", context));
        Assert.Equal(int.MaxValue, Countables.GetCountableAmount("10 ^ 10", context));
        Assert.Equal(int.MinValue, Countables.GetCountableAmount("0 - 10 ^ 10", context));
        // NaN throws like Kotlin's roundToInt
        Assert.Throws<ArgumentException>(() => Countables.GetCountableAmount("sqrt(-1)", context));
    }

    [Fact]
    public void MatchesAndErrorSeverityTellParseErrorsFromUnknownCountables()
    {
        var ruleset = EmptyRulesetParameterView.Instance;
        var engine = new Expressions();
        Assert.True(engine.Matches("[turns] + 1", ruleset));
        Assert.False(engine.Matches("1 +", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetInvariant, engine.GetErrorSeverity("1 +", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.PossibleFilteringUnique, engine.GetErrorSeverity("[bogus] + 1", ruleset));
        Assert.Null(engine.GetErrorSeverity("[turns] + 1", ruleset));
        Assert.Equal(new[] { "Unknown countable: bogus" }, Expressions.GetCountableErrors("[bogus] + [turns]", ruleset));
        Assert.IsType<Parser.MissingOperand>(Expressions.GetParsingError("1 +"));
        Assert.Null(engine.Eval("1 +", GameContext.EmptyState));
    }
}
