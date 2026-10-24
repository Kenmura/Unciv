using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unique.Expressions;

// Ported from core/src/com/unciv/models/ruleset/unique/expressions/Parser.kt.

/// <summary>
///  Parse and evaluate simple expressions
///  - <see cref="Eval"/> Does a one-off AST conversion and evaluation in one go
///  - <see cref="GetASTDebugDescription"/> Builds the AST without evaluating
///  - Supports <see cref="Countables"/> as terms, enclosed in square brackets (they're optional when the countable is a single identifier!).
///
///  Very freely inspired by Keval.
///
///  Current limitations: non-alphanumeric tokens are always one character, numeric constants do not support scientific notation,
///  identifiers can only contain letters, digits and '_', functions take comma-separated arguments, only prefix unary operators.
/// </summary>
public static class Parser
{
    /// <summary>Parse and evaluate an expression. If it needs to support countables, <paramref name="context"/> should be supplied.</summary>
    public static double Eval(string text, GameContext? context = null) =>
        Parse(text).Eval(context ?? GameContext.EmptyState);

    internal static INode Parse(string text)
    {
        var tokens = text.Tokenize().ToTokens();
        var engine = new StateEngine(tokens.GetEnumerator());
        return engine.BuildAST();
    }

    public static string GetASTDebugDescription(string text) => Parse(text).ToString().NotNull();

    //region Exceptions
    /// <summary>
    /// Parent of all exceptions <see cref="Parse"/> can throw.
    /// If the exception caught is not <see cref="SyntaxError"/>, then an Expression Countable should say NO "that can't possibly an expression".
    /// </summary>
    public class ParsingError(string message, int position) : Exception(message)
    {
        public int Position { get; } = position;
    }

    /// <summary>Less severe than <see cref="ParsingError"/>. It allows an Expression Countable to say "Maybe", meaning the string might be of type Expression, but malformed.</summary>
    public class SyntaxError(string message, int position) : ParsingError(message, position);

    public class UnmatchedBraces(int position) : ParsingError("Unmatched square braces", position);
    public class EmptyBraces(int position) : ParsingError("Empty square braces", position);
    public class UnmatchedParentheses(int position, string name) : SyntaxError($"Unmatched {name} parenthesis", position);
    internal class UnexpectedToken(int position, IToken expected, IToken found) : ParsingError($"Unexpected token: {found} instead of {expected}", position);
    public class MissingOperand(int position) : SyntaxError("Missing operand", position);
    public class InvalidConstant(int position, string text) : SyntaxError($"Invalid constant: {text}", position);
    public class UnknownIdentifier(int position, string text) : ParsingError($"Unknown identifier: \"{text}\"", position);
    public class InvalidFunctionArity(int position, string functionName, string expected, int actual)
        : ParsingError($"Function {functionName} at position {position} should get {expected} arguments, got {actual}", position);
    public class EmptyExpression() : ParsingError("Empty expression", 0);
    //endregion

    /// <summary>Marker for beginning of the expression</summary>
    private sealed class StartToken : IToken
    {
        public static readonly StartToken Instance = new();
        public override string ToString() => "start of expression";
    }

    /// <summary>Marker for end of the expression</summary>
    private sealed class EndToken : IToken
    {
        public static readonly EndToken Instance = new();
        public override string ToString() => "end of expression";
    }

    private sealed class StateEngine(IEnumerator<(int Position, IToken Token)> iterator)
    {
        private IToken currentToken = StartToken.Instance;
        private int currentPosition;
        private int openParenthesesCount;

        private void Expect(IToken expected)
        {
            if (ReferenceEquals(currentToken, expected)) return;
            if (ReferenceEquals(expected, Parentheses.Closing) && ReferenceEquals(currentToken, EndToken.Instance))
                throw new UnmatchedParentheses(currentPosition, Parentheses.Opening.Name.ToLowerInvariant());
            if (ReferenceEquals(expected, EndToken.Instance) && ReferenceEquals(currentToken, Parentheses.Closing))
                throw new UnmatchedParentheses(currentPosition, Parentheses.Closing.Name.ToLowerInvariant());
            throw new UnexpectedToken(currentPosition, expected, currentToken);
        }

        private void Next()
        {
            if (ReferenceEquals(currentToken, Parentheses.Opening))
            {
                openParenthesesCount++;
            }
            else if (ReferenceEquals(currentToken, Parentheses.Closing))
            {
                if (openParenthesesCount == 0)
                    throw new UnmatchedParentheses(currentPosition, Parentheses.Closing.Name.ToLowerInvariant());
                openParenthesesCount--;
            }
            if (iterator.MoveNext())
            {
                (currentPosition, currentToken) = iterator.Current;
            }
            else
            {
                currentToken = EndToken.Instance;
                // Open question in Kotlin: which position to report here. Kotlin leaves it unchanged, so do we.
            }
        }

        private INode HandleUnary()
        {
            var op = currentToken.FetchUnaryOperator();
            Next();
            return new UnaryOperation(op, FetchOperand());
        }

        private INode HandleFunction(IFunctionOperator function)
        {
            int functionPosition = currentPosition;
            Next(); // consume function name
            Expect(Parentheses.Opening);
            Next(); // consume '('

            var arguments = new List<INode>
            {
                // Parse first argument
                Expression(),
            };

            // Parse additional arguments separated by commas
            while (ReferenceEquals(currentToken, Comma.Instance))
            {
                Next(); // consume ','
                arguments.Add(Expression());
            }

            Expect(Parentheses.Closing);
            Next(); // consume ')'

            // Check arity range
            var expectedRange = function.ArityRange;
            if (arguments.Count < expectedRange.First || arguments.Count > expectedRange.Last)
            {
                string expectedDesc;
                if (expectedRange.Last == int.MaxValue) expectedDesc = $"{expectedRange.First} arguments and up";
                else if (expectedRange.First == expectedRange.Last)
                    expectedDesc = $"{expectedRange.First} argument{(expectedRange.First == 1 ? "" : "s")}";
                else expectedDesc = $"{expectedRange.First}-{expectedRange.Last} arguments";
                throw new InvalidFunctionArity(functionPosition, function.Symbol, expectedDesc, arguments.Count);
            }

            return new FunctionCall(function, arguments);
        }

        // Precedence climbing; see the long worked examples in Parser.kt (1+2*3 and 1*2+3).
        // minPrecedence: precedence of the operator that called this function. Each node under an operator will always be of equal or higher precedence.
        private INode Expression(int minPrecedence = 0)
        {
            var result = FetchOperand();
            while (currentToken.CanBeBinary())
            {
                var op = currentToken.FetchBinaryOperator();
                if (op.Precedence < minPrecedence) break;
                Next();
                int newPrecedence = op.IsLeftAssociative ? op.Precedence + 1 : op.Precedence;
                result = new BinaryOperation(op, result, Expression(newPrecedence));
            }
            return result;
        }

        private INode FetchOperand()
        {
            if (ReferenceEquals(currentToken, StartToken.Instance)) Next();
            if (currentToken.CanBeUnary())
            {
                return HandleUnary();
            }
            else if (currentToken is IFunctionOperator function)
            {
                return HandleFunction(function);
            }
            else if (ReferenceEquals(currentToken, Parentheses.Opening))
            {
                Next();
                var node = Expression();
                Expect(Parentheses.Closing);
                Next();
                return node;
            }
            else if (currentToken is IConstant or CountableNode)
            {
                var node = (INode)currentToken;
                Next();
                return node;
            }
            else
            {
                throw new MissingOperand(currentPosition);
            }
        }

        public INode BuildAST()
        {
            var node = Expression();
            Expect(EndToken.Instance);
            return node;
        }
    }
}
