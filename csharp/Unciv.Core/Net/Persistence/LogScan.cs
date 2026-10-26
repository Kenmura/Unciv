using System.Text;
using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Log;
using Unciv.Json;

namespace Unciv.Net.Persistence;

/// <summary>One accepted command as the log holds it, with the client command id that asked for it.</summary>
internal sealed record LoggedCommand(CommandEnvelope Envelope, string? ClientCmdId);

/// <summary>
/// What a read of <c>commands.log</c> found. Everything from the first line that is torn (no newline: the host died mid-write), is not a valid log line, or breaks the
/// seq order is the corrupt tail: it is not part of the game and <see cref="ValidBytes"/> is where the good part ends.
/// </summary>
internal sealed class LogScan
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public required CommandLogHeader Header { get; init; }

    public required string HeaderLine { get; init; }

    public List<CommandLogLine> Lines { get; } = [];

    public List<LoggedCommand> Commands { get; } = [];

    /// <summary>The seq the log starts after (0, or the snapshot seq of a rotated log).</summary>
    public long BaseSeq { get; set; }

    public long LastSeq { get; set; }

    public long ValidBytes { get; set; }

    public int DroppedLines { get; set; }

    public byte[] DroppedBytes { get; set; } = [];

    public CommandLog ToLog() => new(Header, Lines);

    public static LogScan Read(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new GameStoreException("The command log cannot be read: " + e.Message);
        }
        var headerEnd = Array.IndexOf(bytes, (byte)'\n');
        if (headerEnd < 0) throw new GameStoreException("The command log has no complete header line");
        var headerLine = Decode(bytes, 0, headerEnd) ?? throw new GameStoreException("The command log header is not text");
        CommandLogHeader header;
        try
        {
            header = UncivJson.Deserialize<CommandLogHeader>(headerLine) ?? throw new GameStoreException("The command log header is empty");
        }
        catch (JsonException e)
        {
            throw new GameStoreException("The command log header is invalid: " + e.Message);
        }

        var scan = new LogScan { Header = header, HeaderLine = headerLine, ValidBytes = headerEnd + 1 };
        var position = headerEnd + 1;
        while (position < bytes.Length)
        {
            var end = Array.IndexOf(bytes, (byte)'\n', position);
            if (end < 0) break; // a torn last line
            var text = Decode(bytes, position, end - position);
            if (text == null || !scan.TryAdd(text)) break;
            position = end + 1;
            scan.ValidBytes = position;
        }

        scan.DroppedBytes = bytes[(int)scan.ValidBytes..];
        scan.DroppedLines = scan.DroppedBytes.Length == 0 ? 0 : Math.Max(1, scan.DroppedBytes.Count(it => it == (byte)'\n'));
        return scan;
    }

    private static string? Decode(byte[] bytes, int start, int length)
    {
        try
        {
            return StrictUtf8.GetString(bytes, start, length);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private bool TryAdd(string text)
    {
        CommandLogLine line;
        string? clientCmdId = null;
        try
        {
            line = CommandLogReader.Read("{}\n" + text).Lines.SingleOrDefault() ?? throw new FormatException("blank line");
            if (line is CommandLogLine.Command)
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.TryGetProperty("clientCmdId", out var id)) clientCmdId = id.GetString();
            }
        }
        catch (Exception e) when (e is CommandLogException or FormatException or JsonException or InvalidOperationException or OverflowException)
        {
            return false;
        }

        switch (line)
        {
            case CommandLogLine.Command command:
                var seq = command.Envelope.Seq;
                if (Commands.Count == 0 && LastSeq == 0)
                {
                    if (seq < 1) return false;
                    BaseSeq = seq - 1;
                }
                else if (seq != LastSeq + 1)
                {
                    return false;
                }
                LastSeq = seq;
                Commands.Add(new LoggedCommand(command.Envelope, clientCmdId));
                break;
            case CommandLogLine.Checkpoint checkpoint:
                if (Commands.Count == 0 && LastSeq == 0) BaseSeq = LastSeq = checkpoint.Seq;
                else if (checkpoint.Seq != LastSeq) return false;
                break;
        }
        Lines.Add(line);
        return true;
    }
}
