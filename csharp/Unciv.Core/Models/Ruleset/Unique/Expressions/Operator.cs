using System.Globalization;

namespace Unciv.Models.Ruleset.Unique.Expressions;

// Ported from core/src/com/unciv/models/ruleset/unique/expressions/Operator.kt.
// Kotlin nests enums implementing the sealed interface; C# enums cannot implement interfaces, so each Kotlin enum is a sealed class with
// static instances and an ordered Entries list (the order matters: it feeds the documentation text and Operator.Of's overwrite order).
// All ToString() overrides are for exception messages and debugging only.

/// <summary>Kotlin <c>Tokenizer.Token</c>: what the tokenizer yields and the parser consumes.</summary>
internal interface IToken
{
    bool CanBeUnary() => this is IUnaryOperator or IUnaryOrBinaryOperator;
    bool CanBeBinary() => this is IBinaryOperator or IUnaryOrBinaryOperator;

    // Note: not named GetUnaryOperator to keep the Kotlin names (fetchUnaryOperator)
    IUnaryOperator FetchUnaryOperator() => this switch
    {
        IUnaryOperator unary => unary,
        IUnaryOrBinaryOperator both => both.Unary,
        _ => throw new InvalidOperationException("InternalError"), // Kotlin: InternalError()
    };

    IBinaryOperator FetchBinaryOperator() => this switch
    {
        IBinaryOperator binary => binary,
        IUnaryOrBinaryOperator both => both.Binary,
        _ => throw new InvalidOperationException("InternalError"),
    };
}

internal interface IOperator : IToken
{
    string Symbol { get; }
}

internal interface IUnaryOperator : IOperator
{
    Func<double, double> Implementation { get; }
}

internal interface IBinaryOperator : IOperator
{
    /// <summary>Higher numbers are evaluated *first*</summary>
    int Precedence { get; }
    bool IsLeftAssociative { get; }
    Func<double, double, double> Implementation { get; }
}

internal interface IUnaryOrBinaryOperator : IOperator
{
    IUnaryOperator Unary { get; }
    IBinaryOperator Binary { get; }
}

internal interface IFunctionOperator : IOperator
{
    /// <summary>Number of arguments the function accepts - can be a range for variadic functions</summary>
    Utils.IntRange ArityRange { get; }
    Func<IReadOnlyList<double>, double> Implementation { get; }
}

internal sealed class UnaryOperators : IUnaryOperator
{
    private UnaryOperators(string symbol, Func<double, double> implementation, string description)
    {
        Symbol = symbol;
        Implementation = implementation;
        Description = description;
    }

    public string Symbol { get; }
    public Func<double, double> Implementation { get; }
    public string Description { get; }
    public override string ToString() => Symbol;

    public static readonly UnaryOperators Negation = new("-", operand => -operand, "negation");
    public static readonly UnaryOperators Ciel = new("√", Math.Sqrt, "square root");
    public static readonly UnaryOperators Abs = new("abs", Math.Abs, "absolute value - turns negative into positive");
    public static readonly UnaryOperators Sqrt2 = new("sqrt", Math.Sqrt, "square root");
    public static readonly UnaryOperators Floor = new("floor", Math.Floor, "round down");
    public static readonly UnaryOperators Ceil = new("ceil", Math.Ceiling, "round up");

    public static readonly IReadOnlyList<UnaryOperators> Entries = [Negation, Ciel, Abs, Sqrt2, Floor, Ceil];
}

internal sealed class BinaryOperators : IBinaryOperator
{
    private BinaryOperators(string symbol, int precedence, bool isLeftAssociative, Func<double, double, double> implementation)
    {
        Symbol = symbol;
        Precedence = precedence;
        IsLeftAssociative = isLeftAssociative;
        Implementation = implementation;
    }

    public string Symbol { get; }
    public int Precedence { get; }
    public bool IsLeftAssociative { get; }
    public Func<double, double, double> Implementation { get; }
    public override string ToString() => Symbol;

    public static readonly BinaryOperators Addition = new("+", 2, true, (left, right) => left + right);
    public static readonly BinaryOperators Subtraction = new("-", 2, true, (left, right) => left - right);
    public static readonly BinaryOperators Multiplication = new("*", 3, true, (left, right) => left * right);
    public static readonly BinaryOperators Division = new("/", 3, true, (left, right) => right == 0.0 ? 0.0 : left / right);
    // true modulo, always non-negative. C# `%` on doubles is IEEE fmod with the sign of the dividend, exactly like Kotlin/Java.
    public static readonly BinaryOperators Remainder = new("%", 3, true, (left, right) => ((left % right) + right) % right);
    public static readonly BinaryOperators Exponent = new("^", 4, false, JavaPow);

    public static readonly IReadOnlyList<BinaryOperators> Entries = [Addition, Subtraction, Multiplication, Division, Remainder, Exponent];

    // java.lang.Math.pow differs from Math.Pow (IEEE 754-2008 pow) in two cases: pow(1, NaN) and pow(+-1, +-Infinity) are NaN on the JVM, 1 in .NET.
    private static double JavaPow(double left, double right)
    {
        if (double.IsNaN(right)) return double.NaN;
        if (double.IsInfinity(right) && Math.Abs(left) == 1.0) return double.NaN;
        return Math.Pow(left, right);
    }
}

internal sealed class UnaryOrBinaryOperators : IUnaryOrBinaryOperator
{
    private UnaryOrBinaryOperators(string symbol, IUnaryOperator unary, IBinaryOperator binary)
    {
        Symbol = symbol;
        Unary = unary;
        Binary = binary;
    }

    public string Symbol { get; }
    public IUnaryOperator Unary { get; }
    public IBinaryOperator Binary { get; }
    public override string ToString() => Symbol;

    public static readonly UnaryOrBinaryOperators Minus = new("-", UnaryOperators.Negation, BinaryOperators.Subtraction);

    public static readonly IReadOnlyList<UnaryOrBinaryOperators> Entries = [Minus];
}

internal sealed class Functions : IFunctionOperator
{
    private Functions(string symbol, Utils.IntRange arityRange, Func<IReadOnlyList<double>, double> implementation)
    {
        Symbol = symbol;
        ArityRange = arityRange;
        Implementation = implementation;
    }

    public string Symbol { get; }
    public Utils.IntRange ArityRange { get; }
    public Func<IReadOnlyList<double>, double> Implementation { get; }
    public override string ToString() => Symbol;

    public static readonly Functions Max = new("max", new Utils.IntRange(2, int.MaxValue), args => MaxOrNull(args) ?? 0.0);
    public static readonly Functions Min = new("min", new Utils.IntRange(2, int.MaxValue), args => MinOrNull(args) ?? 0.0);

    public static readonly IReadOnlyList<Functions> Entries = [Max, Min];

    // kotlin.collections.Iterable<Double>.maxOrNull: a NaN anywhere wins (the first NaN is returned); otherwise the running `maxOf` (Math.max semantics: 0.0 beats -0.0; checked against kotlin-stdlib 2.3.0).
    private static double? MaxOrNull(IReadOnlyList<double> args)
    {
        if (args.Count == 0) return null;
        double max = args[0];
        if (double.IsNaN(max)) return max;
        for (int i = 1; i < args.Count; i++)
        {
            double e = args[i];
            if (double.IsNaN(e)) return e;
            max = Math.Max(max, e);
        }
        return max;
    }

    private static double? MinOrNull(IReadOnlyList<double> args)
    {
        if (args.Count == 0) return null;
        double min = args[0];
        if (double.IsNaN(min)) return min;
        for (int i = 1; i < args.Count; i++)
        {
            double e = args[i];
            if (double.IsNaN(e)) return e;
            min = Math.Min(min, e);
        }
        return min;
    }
}

internal sealed class NamedConstants : IConstant, IOperator
{
    private NamedConstants(string symbol, double value)
    {
        Symbol = symbol;
        Value = value;
    }

    public string Symbol { get; }
    public double Value { get; }
    public override string ToString() => Symbol;

    public static readonly NamedConstants Pi = new("pi", Math.PI);
    public static readonly NamedConstants Pi2 = new("π", Math.PI);
    public static readonly NamedConstants Euler = new("e", Math.E);

    public static readonly IReadOnlyList<NamedConstants> Entries = [Pi, Pi2, Euler];
}

internal sealed class Parentheses : IOperator
{
    private Parentheses(string symbol, string name)
    {
        Symbol = symbol;
        Name = name;
    }

    public string Symbol { get; }
    /// <summary>Kotlin enum <c>name</c> (used in exception messages after <c>lowercase()</c>).</summary>
    public string Name { get; }
    public override string ToString() => Symbol;

    public static readonly Parentheses Opening = new("(", "Opening");
    public static readonly Parentheses Closing = new(")", "Closing");

    public static readonly IReadOnlyList<Parentheses> Entries = [Opening, Closing];
}

internal static class Operator
{
    private static readonly Dictionary<string, IOperator> Cache = BuildCache();

    // Entries later in the sequence overwrite earlier ones with the same symbol (Kotlin associateBy), e.g. UnaryOrBinary Minus replaces Negation and Subtraction.
    private static Dictionary<string, IOperator> BuildCache()
    {
        var cache = new Dictionary<string, IOperator>(StringComparer.Ordinal);
        foreach (var entry in UnaryOperators.Entries) cache[entry.Symbol] = entry;
        foreach (var entry in BinaryOperators.Entries) cache[entry.Symbol] = entry;
        foreach (var entry in UnaryOrBinaryOperators.Entries) cache[entry.Symbol] = entry;
        foreach (var entry in Functions.Entries) cache[entry.Symbol] = entry;
        foreach (var entry in NamedConstants.Entries) cache[entry.Symbol] = entry;
        foreach (var entry in Parentheses.Entries) cache[entry.Symbol] = entry;
        return cache;
    }

    public static IOperator? Of(string symbol) => Cache.GetValueOrDefault(symbol);
}

/// <summary>A constant term: <see cref="NumericConstant"/> or <see cref="NamedConstants"/>.</summary>
internal interface IConstant : INode, IToken
{
    double Value { get; }
    double INode.Eval(GameContext context) => Value;
    IReadOnlyList<string> INode.GetErrors(IRulesetParameterView ruleset) => [];
}

/// <summary><c>Double.toString()</c> as the JVM prints it (JDK 19+ shortest repr), used only in debug and exception texts.</summary>
internal static class JavaDouble
{
    public static string ToString(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (value == 0.0) return double.IsNegative(value) ? "-0.0" : "0.0";

        // Shortest round-trip digits via "R", re-laid out as Java does.
        string shortest = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        (string digits, int exponent) = Decompose(shortest);
        string sign = value < 0 ? "-" : "";
        double abs = Math.Abs(value);
        if (abs >= 1e-3 && abs < 1e7)
        {
            // plain decimal with at least one digit after the point
            if (exponent >= 0)
            {
                string intPart = digits.Length > exponent + 1 ? digits[..(exponent + 1)] : digits.PadRight(exponent + 1, '0');
                string frac = digits.Length > exponent + 1 ? digits[(exponent + 1)..] : "0";
                return sign + intPart + "." + frac;
            }
            return sign + "0." + new string('0', -exponent - 1) + digits;
        }
        string mantissa = digits.Length == 1 ? digits + ".0" : digits[..1] + "." + digits[1..];
        return sign + mantissa + "E" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    // Splits a positive shortest round-trip string into significant digits (no trailing zeros) and the decimal exponent of the first digit.
    private static (string Digits, int Exponent) Decompose(string text)
    {
        int exp = 0;
        int ePos = text.IndexOf('E', StringComparison.Ordinal);
        if (ePos >= 0)
        {
            exp = int.Parse(text[(ePos + 1)..], CultureInfo.InvariantCulture);
            text = text[..ePos];
        }
        int dot = text.IndexOf('.', StringComparison.Ordinal);
        string intDigits = dot >= 0 ? text[..dot] : text;
        string fracDigits = dot >= 0 ? text[(dot + 1)..] : "";
        string all = intDigits + fracDigits;
        int firstNonZero = 0;
        while (firstNonZero < all.Length - 1 && all[firstNonZero] == '0') firstNonZero++;
        int exponent = intDigits.Length - 1 - firstNonZero + exp;
        string digits = all[firstNonZero..].TrimEnd('0');
        if (digits.Length == 0) digits = "0";
        return (digits, exponent);
    }
}
