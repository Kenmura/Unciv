using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Unciv.Utils;

// core/src/com/unciv/utils/Log.kt
//
// The engine side of the Kotlin Log: same call shape (message with printf-style parameters, optional tag, optional exception),
// same tag filtering, but the output goes to an injectable ILogBackend whose default does nothing. Deviations, all for determinism
// and headlessness:
// - no Gdx backend, no timestamps and no thread objects (System.Threading.Thread is banned in the engine): a backend that wants a time
//   adds its own, the thread is reported as "thread-N";
// - the noLog / onlyLog Java system properties become the DisableLogsFrom / EnableLogsFrom properties;
// - the tag is the calling class found by walking the stack (like upstream), only done when something is actually going to be logged;
// - a message whose format specifiers do not match its parameters is logged as raw text instead of throwing (upstream's String.format throws
//   out of the logging call); Kotlin's top-level debug(...) shortcuts are Log.Debug(...) (add `using static Unciv.Utils.Log;` to keep the short name);
// - softRequire returns the condition: `softRequire(c, msg, p) ?: return null` is `if (!Log.SoftRequire(c, msg, p)) return null;`.

/// <summary>A log tag, normally the name of the calling class.</summary>
public sealed record Tag(string Name);

public interface ILogBackend
{
    void Debug(Tag tag, string curThreadName, string msg);

    void Error(Tag tag, string curThreadName, string msg);

    /// <summary>Do not log on release builds for performance reasons.</summary>
    bool IsRelease { get; }

    /// <summary>Get string information about operation system.</summary>
    string GetSystemInfo();
}

/// <summary>The default backend: drops everything.</summary>
public sealed class NullLogBackend : ILogBackend
{
    public static NullLogBackend Instance { get; } = new();

    public bool IsRelease => true;

    public void Debug(Tag tag, string curThreadName, string msg)
    {
    }

    public void Error(Tag tag, string curThreadName, string msg)
    {
    }

    public string GetSystemInfo() => "";
}

/// <summary>Only for tests, or temporary main() functions: prints to the console (without a timestamp).</summary>
public class ConsoleLogBackend : ILogBackend
{
    public virtual bool IsRelease => false;

    public virtual void Debug(Tag tag, string curThreadName, string msg) =>
        Console.WriteLine($"[{curThreadName}] [{tag.Name}] {msg}");

    public virtual void Error(Tag tag, string curThreadName, string msg) =>
        Console.WriteLine($"[{curThreadName}] [{tag.Name}] [ERROR] {msg}");

    public virtual string GetSystemInfo() => "";
}

public static class Log
{
    private static readonly ImmutableArray<string> DefaultDisabledLogs =
        ["Battle", "Music", "Sounds", "Translations", "WorkerAutomation", "assignRegions", "RoadBetweenCitiesAutomation", "StageMouseOverDebug", "DiplomacyAutomation"];

    /// <summary>
    /// Log tags (= class names) <b>containing</b> one of these strings are not logged (the Kotlin <c>-DnoLog</c>; replace the content to change the list,
    /// an empty set disables the default exclusions).
    /// </summary>
    public static HashSet<string> DisableLogsFrom { get; } = new(DefaultDisabledLogs, StringComparer.Ordinal);

    /// <summary>
    /// If not empty, only tags <b>containing</b> one of these strings are logged (the Kotlin <c>-DonlyLog</c>). <see cref="DisableLogsFrom"/> is still
    /// respected. Use a string that matches nothing, such as <c>~~~</c>, to disable all logging.
    /// </summary>
    public static HashSet<string> EnableLogsFrom { get; } = new(StringComparer.Ordinal);

    public static ILogBackend Backend { get; set; } = NullLogBackend.Instance;

    public static bool ShouldLog(Tag? tag = null) =>
        !Backend.IsRelease && !IsTagDisabled(tag ?? GetTag());

    /// <summary>
    /// Logs the given message by formatting it with an optional list of <paramref name="args"/> (printf-style: <c>%s %d %f %x %n %%</c>, see <see cref="JavaFormat"/>).
    /// Only does something when logging is enabled. A tag equal to the name of the calling class is added.
    /// The args can contain <see cref="Func{TResult}"/> of object: they are invoked and their value is used instead.
    /// </summary>
    public static void Debug(string msg, params object?[] args)
    {
        if (Backend.IsRelease)
        {
            return;
        }

        Debug(GetTag(), msg, args);
    }

    public static void Debug(Tag tag, string msg, params object?[] args)
    {
        if (!ShouldLog(tag))
        {
            return;
        }

        DoLog(Backend.Debug, tag, msg, ReplaceLambdasWithValues(args));
    }

    /// <summary>Logs the given exception by appending it to <paramref name="msg"/>.</summary>
    public static void Debug(string msg, Exception throwable)
    {
        if (Backend.IsRelease)
        {
            return;
        }

        Debug(GetTag(), msg, throwable);
    }

    public static void Debug(Tag tag, string msg, Exception throwable)
    {
        if (!ShouldLog(tag))
        {
            return;
        }

        DoLog(Backend.Debug, tag, BuildThrowableMessage(msg, throwable), []);
    }

    /// <summary>Like <see cref="Debug(string, object?[])"/>, but always logs, even in release builds.</summary>
    public static void Error(string msg, params object?[] args) =>
        Error(GetTag(), msg, args);

    public static void Error(Tag tag, string msg, params object?[] args) =>
        DoLog(Backend.Error, tag, msg, ReplaceLambdasWithValues(args));

    public static void Error(string msg, Exception throwable) =>
        Error(GetTag(), msg, throwable);

    public static void Error(Tag tag, string msg, Exception throwable) =>
        DoLog(Backend.Error, tag, BuildThrowableMessage(msg, throwable), []);

    /// <summary>Get string information about operation system.</summary>
    public static string GetSystemInfo() => Backend.GetSystemInfo();

    /// <summary>
    /// If <paramref name="condition"/> is met, returns true. Otherwise, logs an error and returns false.
    /// Usage: <c>if (!Log.SoftRequire(input > 0, "input %s must be positive", input)) return;</c>
    /// </summary>
    public static bool SoftRequire(bool condition, string msg, params object?[] args)
    {
        if (condition)
        {
            return true;
        }

        Error(GetTag(), msg, args);
        return false;
    }

    private static void DoLog(Action<Tag, string, string> logger, Tag tag, string msg, object?[] args)
    {
        string formatted = args.Length == 0 ? msg : JavaFormat.FormatOrRaw(msg, args);
        logger(tag, $"thread-{Environment.CurrentManagedThreadId}", formatted);
    }

    private static bool IsTagDisabled(Tag tag) =>
        DisableLogsFrom.Any(part => tag.Name.Contains(part, StringComparison.Ordinal))
        || (EnableLogsFrom.Count > 0 && !EnableLogsFrom.Any(part => tag.Name.Contains(part, StringComparison.Ordinal)));

    private static string BuildThrowableMessage(string msg, Exception throwable) => $"{msg} | {throwable}";

    private static object?[] ReplaceLambdasWithValues(object?[] args)
    {
        object?[]? result = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is Func<object?> producer)
            {
                result ??= (object?[])args.Clone();
                result[i] = producer();
            }
        }

        return result ?? args;
    }

    private static Tag GetTag()
    {
        var frames = new StackTrace(skipFrames: 1, fNeedFileInfo: false).GetFrames();
        foreach (var frame in frames)
        {
            var type = frame.GetMethod()?.DeclaringType;
            if (type == null || type == typeof(Log))
            {
                continue;
            }

            return new Tag(ClassNameOf(type));
        }

        return new Tag("Unknown");
    }

    // The simple class name like Kotlin's `className.substringAfterLast('.')` with the anonymous-class suffix removed:
    // nested types keep their `Outer$Inner` form, compiler generated closure and state machine classes collapse to their enclosing class
    private static string ClassNameOf(Type type)
    {
        var parts = new List<string>();
        for (Type? current = type; current != null; current = current.DeclaringType)
        {
            string name = current.Name;
            int generic = name.IndexOf('`', StringComparison.Ordinal);
            if (generic >= 0)
            {
                name = name[..generic];
            }

            if (name.StartsWith('<') || name.Contains("DisplayClass", StringComparison.Ordinal))
            {
                parts.Clear();
                continue;
            }

            parts.Insert(0, name);
        }

        return parts.Count == 0 ? type.Name : string.Join('$', parts);
    }
}

/// <summary>
/// The <c>java.util.Formatter</c> subset that log messages use: <c>%s</c> (170 call sites at the time of porting), <c>%d</c>, <c>%f</c>, plus
/// <c>%x</c>, <c>%c</c>, <c>%b</c>, <c>%n</c>, <c>%%</c>, with flags <c>-</c> <c>0</c> <c>+</c> <c>,</c>, a width and a precision.
/// </summary>
public static class JavaFormat
{
    // (a source generated regex would add types outside the Unciv namespace, which ArchitectureTests rejects)
    private static readonly Regex SpecifierRegex = new(@"%(\d+\$)?([-#+ 0,(]*)(\d+)?(\.\d+)?([a-zA-Z%])", RegexOptions.CultureInvariant);

    /// <summary>Formats like <c>String.format</c>; falls back to the raw message and the arguments when they do not match.</summary>
    public static string FormatOrRaw(string format, object?[] args)
    {
        try
        {
            return Format(format, args);
        }
        catch (FormatException)
        {
            return $"{format} {ToJavaString(args)}";
        }
    }

    public static string Format(string format, object?[] args)
    {
        int nextArg = 0;
        var sb = new StringBuilder();
        int last = 0;
        foreach (Match m in SpecifierRegex.Matches(format))
        {
            sb.Append(format, last, m.Index - last);
            last = m.Index + m.Length;
            char conversion = m.Groups[5].Value[0];
            if (conversion == '%')
            {
                sb.Append('%');
                continue;
            }

            if (conversion == 'n')
            {
                sb.Append('\n');
                continue;
            }

            int argIndex = m.Groups[1].Success ? int.Parse(m.Groups[1].Value.TrimEnd('$'), CultureInfo.InvariantCulture) - 1 : nextArg++;
            if (argIndex < 0 || argIndex >= args.Length)
            {
                throw new FormatException("Format specifier '" + m.Value + "' has no argument");
            }

            string flags = m.Groups[2].Value;
            int width = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            int? precision = m.Groups[4].Success ? int.Parse(m.Groups[4].Value[1..], CultureInfo.InvariantCulture) : null;
            string text = Convert(conversion, args[argIndex], flags, precision);
            if (text.Length < width)
            {
                text = flags.Contains('-', StringComparison.Ordinal)
                    ? text.PadRight(width)
                    : flags.Contains('0', StringComparison.Ordinal) && conversion is 'd' or 'f' or 'x' or 'X'
                        ? ZeroPad(text, width)
                        : text.PadLeft(width);
            }

            sb.Append(text);
        }

        sb.Append(format, last, format.Length - last);
        return sb.ToString();
    }

    private static string ZeroPad(string text, int width)
    {
        bool signed = text.Length > 0 && (text[0] == '-' || text[0] == '+');
        string digits = signed ? text[1..] : text;
        return (signed ? text[..1] : "") + digits.PadLeft(width - (signed ? 1 : 0), '0');
    }

    private static string Convert(char conversion, object? arg, string flags, int? precision)
    {
        switch (conversion)
        {
            case 's':
            case 'S':
                {
                    string s = ToJavaString(arg);
                    if (precision.HasValue && precision.Value < s.Length)
                    {
                        s = s[..precision.Value];
                    }

                    return conversion == 'S' ? s.ToUpperInvariant() : s;
                }

            case 'b':
            case 'B':
                return arg == null ? "false" : arg is bool b ? (b ? "true" : "false") : "true";
            case 'c':
                return arg switch
                {
                    null => "null",
                    char c => c.ToString(),
                    int i => char.ConvertFromUtf32(i),
                    _ => throw new FormatException("%c needs a character"),
                };
            case 'd':
                {
                    if (arg == null)
                    {
                        return "null";
                    }

                    if (arg is not (sbyte or byte or short or ushort or int or uint or long or ulong or System.Numerics.BigInteger))
                    {
                        throw new FormatException("%d needs an integer");
                    }

                    var value = System.Numerics.BigInteger.Parse(((IFormattable)arg).ToString(null, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    return Signed(value.Sign < 0, System.Numerics.BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture), flags);
                }

            case 'x':
            case 'X':
                {
                    if (arg is not (int or long or short or sbyte or byte))
                    {
                        throw new FormatException("%x needs an integer");
                    }

                    // Java prints the two's complement for negative values, by the width of the type
                    string hex = arg switch
                    {
                        int i => i.ToString("x", CultureInfo.InvariantCulture),
                        long l => l.ToString("x", CultureInfo.InvariantCulture),
                        short sh => sh.ToString("x", CultureInfo.InvariantCulture),
                        sbyte sb => sb.ToString("x", CultureInfo.InvariantCulture),
                        _ => ((byte)arg).ToString("x", CultureInfo.InvariantCulture),
                    };
                    return conversion == 'X' ? hex.ToUpperInvariant() : hex;
                }

            case 'f':
                {
                    if (arg == null)
                    {
                        return "null";
                    }

                    double d = arg switch
                    {
                        float f => f,
                        double dd => dd,
                        decimal m => (double)m,
                        _ => throw new FormatException("%f needs a floating point number"),
                    };
                    if (double.IsNaN(d) || double.IsInfinity(d))
                    {
                        return double.IsNaN(d) ? "NaN" : d > 0 ? "Infinity" : "-Infinity";
                    }

                    // Java rounds HALF_UP on the shortest decimal digits of the value
                    int digits = precision ?? 6;
                    if (System.Math.Abs(d) >= 1e28)
                    {
                        throw new FormatException("%f of a huge number is not supported");
                    }

                    decimal exactish = decimal.Parse(d.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
                    string body = System.Math.Round(System.Math.Abs(exactish), digits, MidpointRounding.AwayFromZero).ToString("F" + digits, CultureInfo.InvariantCulture);
                    bool negative = d < 0 || (d == 0 && double.IsNegative(d));
                    return Signed(negative, body, flags);
                }

            default:
                throw new FormatException("Unknown conversion %" + conversion);
        }
    }

    private static string Signed(bool negative, string magnitude, string flags)
    {
        if (flags.Contains(',', StringComparison.Ordinal))
        {
            int dot = magnitude.IndexOf('.', StringComparison.Ordinal);
            string integer = dot >= 0 ? magnitude[..dot] : magnitude;
            string rest = dot >= 0 ? magnitude[dot..] : "";
            var grouped = new StringBuilder();
            for (int i = 0; i < integer.Length; i++)
            {
                if (i > 0 && (integer.Length - i) % 3 == 0)
                {
                    grouped.Append(',');
                }

                grouped.Append(integer[i]);
            }

            magnitude = grouped + rest;
        }

        if (negative)
        {
            return "-" + magnitude;
        }

        return flags.Contains('+', StringComparison.Ordinal) ? "+" + magnitude : flags.Contains(' ', StringComparison.Ordinal) ? " " + magnitude : magnitude;
    }

    /// <summary>Kotlin/Java <c>toString()</c> for the values that show up in log messages.</summary>
    public static string ToJavaString(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => s,
        float f => DoubleToJavaString(f, true),
        double d => DoubleToJavaString(d, false),
        char c => c.ToString(),
        System.Collections.IDictionary dict => "{" + string.Join(", ", dict.Keys.Cast<object?>().Select(k => ToJavaString(k) + "=" + ToJavaString(dict[k.NotNull()]))) + "}",
        System.Collections.IEnumerable items => "[" + string.Join(", ", items.Cast<object?>().Select(ToJavaString)) + "]",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };

    // Double.toString / Float.toString: plain notation for 1e-3 <= |x| < 1e7 with at least one fraction digit, else computerized scientific notation
    private static string DoubleToJavaString(double value, bool single)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Infinity" : "-Infinity";
        }

        if (value == 0)
        {
            return double.IsNegative(value) ? "-0.0" : "0.0";
        }

        string shortest = (single ? ((float)System.Math.Abs(value)).ToString("R", CultureInfo.InvariantCulture) : System.Math.Abs(value).ToString("R", CultureInfo.InvariantCulture));
        int exponent = 0;
        int ePos = shortest.IndexOfAny(['E', 'e']);
        if (ePos >= 0)
        {
            exponent = int.Parse(shortest[(ePos + 1)..], CultureInfo.InvariantCulture);
            shortest = shortest[..ePos];
        }

        int pointPos = shortest.IndexOf('.', StringComparison.Ordinal);
        string all = shortest.Replace(".", "", StringComparison.Ordinal);
        if (pointPos < 0)
        {
            pointPos = all.Length;
        }

        int leadingZeros = all.TakeWhile(c => c == '0').Count();
        string digits = all[leadingZeros..].TrimEnd('0');
        int sciExponent = exponent + pointPos - 1 - leadingZeros;
        string sign = value < 0 ? "-" : "";
        double abs = System.Math.Abs(value);
        if (abs >= 1e-3 && abs < 1e7)
        {
            if (sciExponent >= 0)
            {
                string integer = digits.Length > sciExponent + 1 ? digits[..(sciExponent + 1)] : digits.PadRight(sciExponent + 1, '0');
                string fraction = digits.Length > sciExponent + 1 ? digits[(sciExponent + 1)..] : "0";
                return sign + integer + "." + fraction;
            }

            return sign + "0." + new string('0', -sciExponent - 1) + digits;
        }

        return sign + digits[..1] + "." + (digits.Length > 1 ? digits[1..] : "0") + "E" + sciExponent.ToString(CultureInfo.InvariantCulture);
    }
}
