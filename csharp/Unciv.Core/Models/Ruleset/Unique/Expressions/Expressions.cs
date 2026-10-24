using System.Collections.Concurrent;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unique.Expressions;

// Ported from core/src/com/unciv/models/ruleset/unique/expressions/Expressions.kt.
// (Kotlin explains here why Unciv has its own expression parser instead of Keval or exp4j.)

public sealed class Expressions
{
    public bool Matches(string parameterText, IRulesetParameterView ruleset)
    {
        var parseResult = Parse(parameterText);
        return parseResult.Node != null && parseResult.Node.GetErrors(ruleset).Count == 0;
    }

    public int? Eval(string parameterText, GameContext gameContext)
    {
        var node = Parse(parameterText).Node;
        if (node == null) return null;
        return KotlinMath.RoundToInt(node.Eval(gameContext));
    }

    public UniqueParameterErrorSeverity? GetErrorSeverity(string parameterText, IRulesetParameterView ruleset)
    {
        var parseResult = Parse(parameterText);
        if (parseResult.Node == null) return UniqueParameterErrorSeverity.RulesetInvariant;
        if (parseResult.Node.GetErrors(ruleset).Count != 0) return UniqueParameterErrorSeverity.PossibleFilteringUnique;
        return null;
    }

    /// <param name="Node">null if there was a parse error</param>
    /// <param name="Exception">the parse error, if any</param>
    private sealed record ParseResult(INode? Node, Parser.ParsingError? Exception);

    private static readonly ConcurrentDictionary<string, ParseResult> Cache = new(StringComparer.Ordinal);

    private static ParseResult Parse(string parameterText) => Cache.GetOrAdd(parameterText, static text =>
    {
        try
        {
            return new ParseResult(Parser.Parse(text), null);
        }
        catch (Parser.ParsingError ex)
        {
            return new ParseResult(null, ex);
        }
    });

    public static Parser.ParsingError? GetParsingError(string parameterText) => Parse(parameterText).Exception;

    public static IReadOnlyList<string> GetCountableErrors(string parameterText, IRulesetParameterView ruleset)
    {
        var parseResult = Parse(parameterText);
        return parseResult.Node == null ? [] : parseResult.Node.GetErrors(ruleset);
    }
}
