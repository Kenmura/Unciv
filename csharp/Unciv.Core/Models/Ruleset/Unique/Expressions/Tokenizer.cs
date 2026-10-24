using System.Globalization;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unique.Expressions;

// Ported from core/src/com/unciv/models/ruleset/unique/expressions/Tokenizer.kt.
// Both Tokenize and ToToken are lazy iterators/mappers like the Kotlin sequences: the parser pulls one token at a time, so a tokenizer
// error late in the text surfaces only after the parser has had its chance to fail on earlier tokens.

/// <summary>Argument separator in function calls. Kotlin <c>data object Comma</c>.</summary>
internal sealed class Comma : IToken
{
    public static readonly Comma Instance = new();
    private Comma() { }
    public override string ToString() => ",";
}

internal static class Tokenizer
{
    // Define our own "Char is part of literal constant" and "Char is part of identifier" functions - decouple from Java CharacterData
    private static bool IsNumberLiteral(char c) => c == '.' || c is >= '0' and <= '9'; // NOT using library isDigit() here - potentially non-latin
    private static bool IsIdentifierStart(char c) => char.IsLetter(c); // Allow potentially non-latin script // (questionable, says Kotlin)
    private static bool IsIdentifierContinuation(char c) => c == '_' || char.IsLetterOrDigit(c);

    /// <summary>Kotlin <c>String.toDoubleOrNull()</c> for text made only of digits and '.'.</summary>
    internal static double? ParseNumber(string text) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) ? value : null;

    /// <summary>Position in text, to token found</summary>
    public static IEnumerable<(int Position, IToken Token)> ToTokens(this IEnumerable<(int Position, string Text)> source)
    {
        foreach (var (position, text) in source)
            yield return (position, ToToken(position, text));
    }

    private static IToken ToToken(int position, string text)
    {
        if (text.Length == 0) throw new Parser.EmptyExpression();
        if (IsNumberLiteral(text[0]))
            // Kotlin text.toDouble(): the tokenizer has validated the text with toDoubleOrNull
            return new NumericConstant(ParseNumber(text) ?? throw new FormatException($"Invalid double: \"{text}\""));
        if (text == ",") return Comma.Instance;

        // Check if this is a function (identifier followed by parentheses will be handled in parsing)
        var op = Operator.Of(text);
        if (op != null) return op;

        // Countable tokens must come here still wrapped in braces to avoid infinite recursion
        if (!text.StartsWith('[') || !text.EndsWith(']'))
            throw new Parser.UnknownIdentifier(position, text);

        string countableText = text.Substring(1, text.Length - 2);

        var rulesetInvariantCountable = Countables.GetMatching(countableText, null);

        return new CountableNode(countableText, rulesetInvariantCountable);
    }

    public static IEnumerable<(int Position, string Text)> Tokenize(this string text)
    {
        // If set, indicates we're in the middle of an identifier
        int firstIdentifierPosition = -1;
        // If set, indicates we're in the middle of a number
        int firstNumberPosition = -1;
        // If set, indicates we're in the middle of a countable
        int openingBracePosition = -1;
        int braceNestingLevel = 0;

        for (int pos = 0; pos < text.Length; pos++)
        {
            char c = text[pos];
            if (firstIdentifierPosition >= 0)
            {
                if (IsIdentifierContinuation(c)) continue;
                yield return (firstIdentifierPosition, text.Substring(firstIdentifierPosition, pos - firstIdentifierPosition));
                firstIdentifierPosition = -1;
            }
            else if (firstNumberPosition >= 0)
            {
                if (IsNumberLiteral(c)) continue;
                string token = text.Substring(firstNumberPosition, pos - firstNumberPosition);
                if (ParseNumber(token) == null) throw new Parser.InvalidConstant(firstNumberPosition, token);
                yield return (firstNumberPosition, token);
                firstNumberPosition = -1;
            }
            if (c.IsKotlinWhitespace()) continue;

            if (openingBracePosition >= 0)
            {
                if (c == '[')
                    braceNestingLevel++;
                else if (c == ']')
                    braceNestingLevel--;
                if (braceNestingLevel == 0)
                {
                    if (pos - openingBracePosition <= 1) throw new Parser.EmptyBraces(pos);
                    yield return (pos, text.Substring(openingBracePosition, pos + 1 - openingBracePosition)); // Leave the braces
                    openingBracePosition = -1;
                }
            }
            else if (IsIdentifierStart(c))
            {
                firstIdentifierPosition = pos;
            }
            else if (IsNumberLiteral(c))
            {
                firstNumberPosition = pos;
            }
            else if (c == '[')
            {
                openingBracePosition = pos;
                braceNestingLevel++;
            }
            else if (c == ']')
            {
                throw new Parser.UnmatchedBraces(pos);
            }
            else
            {
                yield return (pos, c.ToString());
            }
        }
        // End of expression, let's see if there's still anything open
        if (firstIdentifierPosition >= 0)
            yield return (firstIdentifierPosition, text.Substring(firstIdentifierPosition, text.Length - firstIdentifierPosition));
        if (firstNumberPosition >= 0)
        {
            string token = text.Substring(firstNumberPosition, text.Length - firstNumberPosition);
            if (ParseNumber(token) == null) throw new Parser.InvalidConstant(firstNumberPosition, token);
            yield return (firstNumberPosition, token);
        }
        if (braceNestingLevel > 0) throw new Parser.UnmatchedBraces(text.Length);
    }
}
