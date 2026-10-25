using System.Globalization;
using System.Text.Json;
using Unciv.Json;

namespace Unciv.Commands.Log;

/// <summary>One line after the header of a command log.</summary>
public abstract record CommandLogLine
{
    private CommandLogLine()
    {
    }

    public sealed record Command(CommandEnvelope Envelope) : CommandLogLine;

    /// <summary>The full state hash expected once command <paramref name="Seq"/> (0: the start) has been applied.</summary>
    public sealed record Checkpoint(long Seq, int Turn, ulong FullHash) : CommandLogLine;
}

public sealed record CommandLog(CommandLogHeader Header, IReadOnlyList<CommandLogLine> Lines);

/// <summary>Reads what <see cref="CommandLogWriter"/> wrote. A line that cannot be understood is a <see cref="CommandLogException"/>.</summary>
public static class CommandLogReader
{
    public static CommandLog Read(TextReader input)
    {
        var first = input.ReadLine() ?? throw new CommandLogException("The command log is empty");
        CommandLogHeader header;
        try
        {
            header = UncivJson.Deserialize<CommandLogHeader>(first) ?? throw new CommandLogException("The command log header is empty");
        }
        catch (JsonException e)
        {
            throw new CommandLogException("The command log header is invalid: " + e.Message);
        }

        var lines = new List<CommandLogLine>();
        for (var number = 2; input.ReadLine() is { } text; number++)
        {
            if (text.Length == 0) continue;
            try
            {
                lines.Add(ParseLine(text));
            }
            catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
            {
                throw new CommandLogException($"Line {number} of the command log is invalid: {e.Message}");
            }
        }
        return new CommandLog(header, lines);
    }

    public static CommandLog Read(string text)
    {
        using var reader = new StringReader(text);
        return Read(reader);
    }

    private static CommandLogLine ParseLine(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("not a json object");
        if (root.TryGetProperty("checkpoint", out var seq))
        {
            return new CommandLogLine.Checkpoint(seq.GetInt64(), root.GetProperty("turn").GetInt32(),
                ulong.Parse(root.GetProperty("fullHash").GetString() ?? "", NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
        }
        return new CommandLogLine.Command(new CommandEnvelope(
            root.GetProperty("seq").GetInt64(),
            root.GetProperty("type").GetString() ?? throw new FormatException("type is null"),
            root.GetProperty("actor").GetString() ?? throw new FormatException("actor is null"),
            root.GetProperty("payload").Clone()));
    }
}
