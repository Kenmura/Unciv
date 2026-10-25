namespace Unciv.DevConsole;

/// <summary>Kotlin: <c>ConsoleCommand</c>. <see cref="Handle"/> plans without mutating (see <see cref="ConsolePlan"/>).</summary>
public abstract class ConsoleCommand
{
    public abstract ConsolePlan Handle(ConsoleContext console, IReadOnlyList<CliInput> parameters);

    /// <summary>The string replacing the last parameter of the existing command; a null <see cref="AutocompleteResult.Text"/> means no change.
    /// The text ends in a space if and only if the match is an unambiguous choice.</summary>
    public virtual AutocompleteResult? Autocomplete(ConsoleContext console, IReadOnlyList<CliInput> parameters) => null;
}

/// <summary>A leaf command with a format string like <c>city setpop &lt;amount&gt;</c> (<c>&lt;&gt;</c> required, <c>[]</c> optional, <c>[x]...</c> any number).</summary>
public class ConsoleAction(string format, Func<ConsoleContext, IReadOnlyList<CliInput>, ConsolePlan> action) : ConsoleCommand
{
    public string Format { get; } = format;

    public override ConsolePlan Handle(ConsoleContext console, IReadOnlyList<CliInput> parameters)
    {
        try
        {
            ValidateFormat(parameters);
            return action(console, parameters);
        }
        catch (ConsoleHintException hint)
        {
            return ConsolePlan.Hint(hint.Hint);
        }
        catch (ConsoleErrorException error)
        {
            return ConsolePlan.Error(error.Error);
        }
    }

    public override AutocompleteResult? Autocomplete(ConsoleContext console, IReadOnlyList<CliInput> parameters)
    {
        var formatParams = Format.Split(' ').Skip(2).Select(it => Unwrap(Unwrap(Unwrap(it, '<', '>'), '[', ']'), '"', '"')).ToList();
        if (formatParams.Count == 0) return null; // nothing to autocomplete - for example "history " + tab
        if (formatParams.Count < parameters.Count) return null; // format has no definition, so there are no options to choose from
        // We may be here *with* another format parameter but an *empty* parameters (e.g. `tile addriver` and hit tab)
        bool inRange = parameters.Count > 0 && parameters.Count - 1 < formatParams.Count;
        string formatParam = inRange ? formatParams[parameters.Count - 1] : formatParams[0];
        var lastParam = inRange ? parameters[^1] : null;

        var options = ConsoleParameterType.MultiOptions(formatParam, console);
        var result = CliInput.GetAutocompleteString(lastParam ?? CliInput.Empty, options);
        if (lastParam != null || result.Text == null) return result;
        // The caller will ultimately replace the second subcommand, so add it back (only right after the second token)
        return result with { Text = Format.Split(' ')[1] + " " + result.Text };
    }

    private static string Unwrap(string text, char prefix, char suffix) =>
        text.Length >= 2 && text[0] == prefix && text[^1] == suffix ? text[1..^1] : text;

    private void ValidateFormat(IReadOnlyList<CliInput> parameters)
    {
        var allParams = Format.Split(' ');
        int required = allParams.Count(it => it.StartsWith('<'));
        int optional = Format.EndsWith("]...", StringComparison.Ordinal) ? 999999 : allParams.Count(it => it.StartsWith('['));
        // Ignore an empty token caused by a trailing blank
        int size = parameters.Count == 0 ? 0 : parameters[^1].IsEmpty() ? parameters.Count - 1 : parameters.Count;
        if (size < required || size > required + optional)
            throw new ConsoleHintException($"Format: {Format}");
    }
}

/// <summary>Kotlin: <c>ConsoleCommandNode</c>. Sub-commands are kept in ordinal order so nothing depends on hash order.</summary>
public class ConsoleCommandNode : ConsoleCommand
{
    public SortedDictionary<string, ConsoleCommand> Subcommands { get; } = new(StringComparer.Ordinal);

    public override ConsolePlan Handle(ConsoleContext console, IReadOnlyList<CliInput> parameters)
    {
        if (parameters.Count == 0) return ConsolePlan.Hint(AvailableCommands(false));
        return Subcommands.TryGetValue(parameters[0].ToString(), out var handler)
            ? handler.Handle(console, [.. parameters.Skip(1)])
            : ConsolePlan.Error(AvailableCommands(true));
    }

    public override AutocompleteResult? Autocomplete(ConsoleContext console, IReadOnlyList<CliInput> parameters)
    {
        var first = parameters.Count > 0 ? parameters[0] : CliInput.Empty;
        return Subcommands.TryGetValue(first.ToString(), out var handler)
            ? handler.Autocomplete(console, [.. parameters.Skip(1)])
            : CliInput.GetAutocompleteString(first, Subcommands.Keys);
    }

    private string AvailableCommands(bool invalid)
    {
        string separator = invalid ? "\n- " : ", ";
        const string available = "Available commands:";
        string prefix = invalid ? $"Invalid command.\n{available}\n- " : $"{available} ";
        return prefix + string.Join(separator, Subcommands.Keys);
    }
}
