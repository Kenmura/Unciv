using System.Globalization;

namespace Unciv.Cli;

/// <summary>Thrown for a command line the user got wrong; the runner prints the message and exits with code 2.</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>A parsed command line: <c>command [--option value]... [--flag]...</c>. No positional arguments after the command.</summary>
public sealed class CliArgs
{
    private readonly Dictionary<string, string?> options = new(StringComparer.Ordinal);
    private readonly HashSet<string> consumed = new(StringComparer.Ordinal);

    public string Command { get; }

    /// <param name="args">The command line: the command, then the options.</param>
    /// <param name="flags">The option names that take no value.</param>
    public CliArgs(IReadOnlyList<string> args, IReadOnlySet<string> flags)
    {
        if (args.Count == 0) throw new UsageException("missing command");
        Command = args[0];
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length == 2)
                throw new UsageException($"unexpected argument '{arg}'");
            var name = arg[2..];
            if (options.ContainsKey(name)) throw new UsageException($"option --{name} given twice");
            if (flags.Contains(name))
            {
                options[name] = null;
                continue;
            }
            if (i + 1 >= args.Count) throw new UsageException($"option --{name} needs a value");
            if (args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new UsageException($"option --{name} needs a value, got '{args[i + 1]}'");
            options[name] = args[++i];
        }
    }

    public bool Flag(string name)
    {
        consumed.Add(name);
        return options.ContainsKey(name);
    }

    public string? Get(string name)
    {
        consumed.Add(name);
        return options.GetValueOrDefault(name);
    }

    public string Require(string name) => Get(name) ?? throw new UsageException($"missing required option --{name}");

    public int GetInt(string name, int defaultValue, int min = int.MinValue, int max = int.MaxValue)
    {
        var text = Get(name);
        if (text == null) return defaultValue;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new UsageException($"--{name} needs an integer, got '{text}'");
        if (value < min || value > max) throw new UsageException($"--{name} must be between {min} and {max}, got {value}");
        return value;
    }

    public long GetLong(string name, long defaultValue)
    {
        var text = Get(name);
        if (text == null) return defaultValue;
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new UsageException($"--{name} needs an integer, got '{text}'");
        return value;
    }

    /// <summary>Call after reading every option the command knows: a typo'd option must fail instead of being ignored.</summary>
    public void RejectUnknown()
    {
        var unknown = options.Keys.Where(it => !consumed.Contains(it)).Order(StringComparer.Ordinal).FirstOrDefault();
        if (unknown != null) throw new UsageException($"unknown option --{unknown} for command '{Command}'");
    }
}
