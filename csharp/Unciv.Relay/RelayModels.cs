using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Unciv.Relay;

/// <summary>What a host's data connection hands to the joiner that is waiting for it.</summary>
internal sealed record AcceptResult(RelayConn Host, TaskCompletionSource Done);

/// <summary>A joiner announced to the host, waiting for its <c>accept</c>. Exactly one of "the host claims it" and "the join gives up" wins.</summary>
internal sealed class PendingJoin(string id, string network)
{
    private int closed;

    public string Id { get; } = id;

    public string Network { get; } = network;

    public TaskCompletionSource<AcceptResult?> Accepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>True for the single caller that may complete this join.</summary>
    public bool TryClaim() => Interlocked.Exchange(ref closed, 1) == 0;
}

/// <summary>The authenticated control channel of a room: its connection, liveness and the listing sequence.</summary>
internal sealed class ControlChannel(RelayConn conn, long now)
{
    public RelayConn Conn { get; } = conn;

    public CancellationTokenSource Cancel { get; } = new();

    public long LastSeen { get; set; } = now;

    public long LastSeq { get; set; } = -1;

    public int Closing;

    /// <summary>Tells the host why, then drops the connection. Safe to call repeatedly.</summary>
    public async Task CloseAsync(string code)
    {
        if (Interlocked.Exchange(ref Closing, 1) != 0) return;
        await Conn.TrySendAsync(new JsonObject { ["ev"] = "closing", ["code"] = code }).ConfigureAwait(false);
        await Cancel.CancelAsync().ConfigureAwait(false);
        Conn.Dispose();
    }
}

/// <summary>A room: bound to one host key and game, alive while its control channel is, remembered for the room ttl afterwards.</summary>
internal sealed class Room(string code, byte[] certificate, byte[] spki, string gameTag, ECDsa key, ByteBudget daily, long now) : IDisposable
{
    public string Code { get; } = code;

    public string Display { get; } = RelayProtocol.FormatRoom(code);

    public byte[] Certificate { get; } = certificate;

    public byte[] Spki { get; } = spki;

    public string FingerprintHex { get; } = RelayProtocol.FingerprintHex(spki);

    public string GameTag { get; } = gameTag;

    public ECDsa Key { get; } = key;

    public ByteBudget Daily { get; } = daily;

    public long FirstSeen { get; } = now;

    public long LastSeen { get; set; } = now;

    public ControlChannel? Control { get; set; }

    public string HostNetwork { get; set; } = "";

    public Dictionary<string, PendingJoin> Pending { get; } = new(StringComparer.Ordinal);

    public List<Splice> Splices { get; } = [];

    public Listing? Listing { get; set; }

    public bool Live => Control != null;

    public void Dispose() => Key.Dispose();
}

/// <summary>A validated, signed listing as the host sent it; <see cref="Body"/> is forwarded verbatim.</summary>
internal sealed record Listing(string Body, string Signature, long Seq, long FirstSeen, long LastUpdate, ListingFields Fields);

/// <summary>The fields of a listing body the directory filters on.</summary>
internal sealed record ListingFields(string Name, string HostName, int Players, int Slots, string Ruleset, IReadOnlyList<string> Mods, bool PasswordRequired, bool Started, string Version);
