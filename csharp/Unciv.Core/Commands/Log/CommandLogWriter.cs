using System.Globalization;
using System.Text.Json.Nodes;
using Unciv.Json;

namespace Unciv.Commands.Log;

/// <summary>
/// Writes a command log as JSON Lines (design/COMMANDS-IMPLEMENTATION.md section 6): the header, then <c>{seq,type,actor,payload}</c> per accepted
/// command and <c>{checkpoint,turn,fullHash}</c> after every turn change. The clock the authority stamped on a command is part of its payload
/// (<see cref="Unciv.Commands.Turn.ITimedCommand.Now"/>). Every line is compact JSON with invariant number formatting and a fixed member order, so the
/// same game always yields the same bytes. Storage (fsync, rotation, snapshots) is the server's job (T176).
/// </summary>
public sealed class CommandLogWriter
{
    private readonly TextWriter output;
    private long written;

    public CommandLogWriter(TextWriter output, CommandLogHeader header)
    {
        this.output = output;
        WriteLine(UncivJson.Serialize(header));
    }

    /// <summary>The seq of the last command written.</summary>
    public long LastSeq => written;

    /// <summary>Writes the commands <paramref name="session"/> accepted since the last call.</summary>
    public void Sync(GameSession session)
    {
        for (var i = (int)written; i < session.Log.Count; i++) Write(session.Log[i]);
    }

    public void Write(CommandEnvelope envelope)
    {
        if (envelope.Seq != written + 1)
            throw new InvalidOperationException($"Log out of order: expected seq {written + 1}, got {envelope.Seq}");
        var line = new JsonObject
        {
            ["seq"] = envelope.Seq,
            ["type"] = envelope.Type,
            ["actor"] = envelope.ActorId,
            ["payload"] = JsonNode.Parse(envelope.Payload.GetRawText()),
        };
        WriteLine(line.ToJsonString());
        written = envelope.Seq;
    }

    /// <summary>The full state hash after command <paramref name="seq"/>.</summary>
    public void WriteCheckpoint(long seq, int turn, ulong fullHash)
    {
        var line = new JsonObject
        {
            ["checkpoint"] = seq,
            ["turn"] = turn,
            ["fullHash"] = fullHash.ToString("x16", CultureInfo.InvariantCulture),
        };
        WriteLine(line.ToJsonString());
    }

    private void WriteLine(string line)
    {
        output.Write(line);
        output.Write('\n');
    }
}
