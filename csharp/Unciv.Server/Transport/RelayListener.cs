using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Relay;
using Unciv.Net.Turns;

namespace Unciv.Server.Transport;

/// <summary>What a public listing says about a game (design/RELAY.md section 7). All fields are host claims, signed by the host key; there is no password, token or address in it.</summary>
public sealed record RelayListing
{
    public required string Name { get; init; }

    public required string HostName { get; init; }

    public int Players { get; init; }

    public int Slots { get; init; }

    public int Ai { get; init; }

    public string Ruleset { get; init; } = "Civ V - Vanilla";

    public string RulesetHash { get; init; } = "-";

    public IReadOnlyList<string> Mods { get; init; } = [];

    public string Speed { get; init; } = "Quick";

    public string Map { get; init; } = "-";

    public bool PasswordRequired { get; init; }

    public string TurnMode { get; init; } = "simultaneous";

    public bool Started { get; init; }

    public string Version { get; init; } = "1.0";

    public int Proto { get; init; } = ProtocolVersion.Current;
}

/// <summary>Timing and limits of a <see cref="RelayListener"/>; tests shorten them.</summary>
public sealed record RelayListenerOptions
{
    public RelayDialOptions Dial { get; init; } = new();

    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>The control channel is dead after this long without a line from the relay (it answers every ping).</summary>
    public TimeSpan SilenceTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan MinBackoff { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Listing changes are sent at most this often; several changes in between coalesce into one message.</summary>
    public TimeSpan ListingMinInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>An unchanged listing is re-sent this often so the relay does not expire it (it drops one after 30 minutes without an update).</summary>
    public TimeSpan ListingRefresh { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Joiners answered at once: more are rejected with <c>busy</c> without spending a connection and a signature on them.</summary>
    public int MaxPendingJoins { get; init; } = 16;

    public int MaxPendingPerPeer { get; init; } = 3;
}

/// <summary>
/// T338: the host side of the relay (design/RELAY.md sections 3 and 5). It dials OUT to the relay, registers a room under the host key, and for every joiner the relay announces opens a data
/// connection, runs the unchanged inner TLS server handshake on it and yields the finished <see cref="ITransport"/> from <see cref="AcceptTransportAsync"/>, exactly as
/// <see cref="TcpTlsListener"/> does for a direct connection. The control channel survives relay restarts: it reconnects with jittered backoff and reclaims the same room by host key.
/// The relay sees only the outer TLS; the game's own TLS, the password and the pinned fingerprint are end to end.
/// </summary>
public sealed class RelayListener : IAsyncDisposable
{
    private readonly RelayEndpoint endpoint;
    private readonly X509Certificate2 certificate;
    private readonly string gameTag;
    private readonly RelayListenerOptions options;
    private readonly Action<string> log;
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<ITransport> accepted = Channel.CreateUnbounded<ITransport>();
    private readonly TaskCompletionSource firstRoom = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim desiredChanged = new(0);
    private readonly Dictionary<string, int> pendingByPeer = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private int pendingJoins;
    private RelayListing? desiredListing;
    private bool listingDirty;
    private long listingSeq;
    private Task? runTask;
    private string? room;
    private volatile string? closingCode;
    private volatile bool connected;
    private int disposed;

    /// <param name="endpoint">The relay the operator or player typed.</param>
    /// <param name="certificate">The host key (with its private key): it signs the login and every accept.</param>
    /// <param name="gameId">The game's id; only its hash reaches the relay, so the same key and game get the same room back.</param>
    /// <param name="options">Timing and limits; the defaults are the design's.</param>
    /// <param name="log">Where one-line progress and failure messages go.</param>
    public RelayListener(RelayEndpoint endpoint, X509Certificate2 certificate, string gameId, RelayListenerOptions? options = null, Action<string>? log = null)
    {
        this.endpoint = endpoint;
        this.certificate = certificate;
        gameTag = RelayProtocol.GameTag(gameId);
        this.options = options ?? new RelayListenerOptions();
        this.log = log ?? (_ => { });
    }

    /// <summary>The room code in display form (<c>K7QF-3MXP4A</c>) once the relay registered it; null before the first registration. It can change if the relay lost its secret.</summary>
    public string? Room => Volatile.Read(ref room);

    /// <summary>True while the control channel is up.</summary>
    public bool Connected => connected;

    /// <summary>Raised on the listener's task each time the relay assigns a room (the first connection and any change after a reconnect).</summary>
    public event Action<string>? RoomChanged;

    /// <summary>Raised when the control channel goes up (true) or down (false).</summary>
    public event Action<bool>? ConnectionChanged;

    public RelayEndpoint Endpoint => endpoint;

    /// <summary>The relay invite for this room and host key (needs <see cref="Room"/>), optionally with an invite token.</summary>
    public string? Invite(string? token = null) => Room is { } r ? Unciv.Net.Identity.InviteCode.FormatRelay(endpoint.Host, endpoint.Port, r, Unciv.Net.Identity.HostKey.Fingerprint(certificate), token) : null;

    /// <summary>Starts dialling. Idempotent.</summary>
    public void Start()
    {
        lock (gate) runTask ??= RunAsync();
    }

    /// <summary>Completes with the room code once the relay registered it, or throws <see cref="RelayException"/> when the first attempt is refused for good (a bad login or a banned key).</summary>
    public Task WaitForRoomAsync(CancellationToken cancellationToken) => firstRoom.Task.WaitAsync(cancellationToken);

    /// <summary>The next joiner whose end-to-end TLS handshake with the host key completed. Throws when the listener is disposed.</summary>
    public async Task<ITransport> AcceptTransportAsync(CancellationToken cancellationToken = default)
    {
        try { return await accepted.Reader.ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (ChannelClosedException) { throw new ObjectDisposedException(nameof(RelayListener)); }
    }

    /// <summary>
    /// Publishes (or, with null, removes) the public listing. Changes are sent at most every <see cref="RelayListenerOptions.ListingMinInterval"/> and coalesce; a reconnect publishes the latest again.
    /// The relay may refuse it (<c>listing-disabled</c>, a name it denies): that is logged, the game is still reachable by invite and room code.
    /// </summary>
    public void SetListing(RelayListing? listing)
    {
        lock (gate)
        {
            desiredListing = listing;
            listingDirty = true;
        }
        if (desiredChanged.CurrentCount == 0) desiredChanged.Release();
    }

    // ---------------------------------------------------------------- control channel

    private async Task RunAsync()
    {
        var token = stop.Token;
        var backoff = options.MinBackoff;
        while (!token.IsCancellationRequested)
        {
            var refusedForGood = false;
            var wasUp = false;
            try
            {
                await using var control = await RelayChannel.ConnectAsync(endpoint, options.Dial, token).ConfigureAwait(false);
                await control.SendAsync(HostPreamble(control.Challenge), token).ConfigureAwait(false);
                var reply = await control.ReadAsync(token).ConfigureAwait(false);
                RelayChannel.ThrowIfRefused(reply, "registering the room");
                var code = reply!["room"]?.GetValue<string>() ?? throw new RelayException(RelayCodes.Failed, "The relay did not name the room.");
                closingCode = null;
                var changed = !string.Equals(Room, code, StringComparison.Ordinal);
                Volatile.Write(ref room, code);
                connected = true;
                wasUp = true;
                backoff = options.MinBackoff;
                firstRoom.TrySetResult();
                if (changed) RoomChanged?.Invoke(code);
                ConnectionChanged?.Invoke(true);
                log($"relay: room {code} registered on {endpoint.Key}");
                await RunSessionAsync(control, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (RelayException e)
            {
                log("relay: " + e.Message);
                refusedForGood = e.RelayCode is "bad-signature" or "unsupported-version" or "banned";
                if (!firstRoom.Task.IsCompleted && refusedForGood) firstRoom.TrySetException(e);
            }
            catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or ObjectDisposedException or OperationCanceledException or JsonException or InvalidOperationException)
            {
                log("relay: connection lost: " + e.Message);
            }
            finally
            {
                if (connected)
                {
                    connected = false;
                    ConnectionChanged?.Invoke(false);
                }
            }
            if (token.IsCancellationRequested) break;
            // A host that was up and lost the relay retries at once; one that cannot register waits, longer when the relay refused it for good.
            var wait = wasUp ? options.MinBackoff : backoff;
            if (refusedForGood || closingCode == "superseded") wait = options.MaxBackoff; // another process with the same key and game took the room: do not fight it
            else if (!wasUp) backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, options.MaxBackoff.Ticks));
            var jitter = TimeSpan.FromMilliseconds(Jitter((int)(wait.TotalMilliseconds / 4)));
            try { await Task.Delay(wait + jitter, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        accepted.Writer.TryComplete();
    }

    /// <summary>A fresh random game id for a lobby that has none yet: its hash is the room's identity next to the host key, so a new lobby gets a new room.</summary>
    public static string NewGameId()
    {
        Span<byte> bytes = stackalloc byte[16];
        SystemSecureRandom.Instance.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>0 up to <paramref name="maxMillis"/> milliseconds (not game state: it spreads the reconnects of many hosts after a relay restart).</summary>
    private static int Jitter(int maxMillis)
    {
        Span<byte> bytes = stackalloc byte[4];
        SystemSecureRandom.Instance.Fill(bytes);
        return maxMillis <= 0 ? 0 : (int)(BitConverter.ToUInt32(bytes) % (uint)maxMillis);
    }

    private JsonObject HostPreamble(string challenge) => new()
    {
        ["op"] = "host",
        ["v"] = RelayProtocol.Version,
        ["cert"] = Convert.ToBase64String(certificate.RawData),
        ["gameTag"] = gameTag,
        ["sig"] = Sign(RelayProtocol.HostSignedText(endpoint.SignedHost, challenge, gameTag)),
    };

    private string Sign(string text)
    {
        using var key = certificate.GetECDsaPrivateKey() ?? throw new InvalidOperationException("the host certificate has no ECDSA private key");
        return Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(text), HashAlgorithmName.SHA256));
    }

    private async Task RunSessionAsync(RelayChannel control, CancellationToken outer)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var token = session.Token;
        var writeGate = new SemaphoreSlim(1, 1);
        async Task WriteAsync(JsonObject message)
        {
            await writeGate.WaitAsync(token).ConfigureAwait(false);
            try { await control.SendAsync(message, token).ConfigureAwait(false); }
            finally { writeGate.Release(); }
        }

        var reader = ReadLoopAsync(control, WriteAsync, token);
        var pinger = PingLoopAsync(WriteAsync, token);
        var publisher = PublishLoopAsync(WriteAsync, token);
        var finished = await Task.WhenAny(reader, pinger, publisher).ConfigureAwait(false);
        await session.CancelAsync().ConfigureAwait(false);
        try { await Task.WhenAll(reader, pinger, publisher).ConfigureAwait(false); }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or System.Net.Sockets.SocketException) { }
        await finished.ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(RelayChannel control, Func<JsonObject, Task> write, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var silence = CancellationTokenSource.CreateLinkedTokenSource(token);
            silence.CancelAfter(options.SilenceTimeout);
            JsonObject? line;
            try { line = await control.ReadAsync(silence.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                log("relay: no word from the relay, reconnecting");
                return;
            }
            if (line == null) return;
            switch (line["ev"]?.GetValue<string>())
            {
                case "incoming":
                    HandleIncoming(line, write, token);
                    continue;
                case "closing":
                    closingCode = line["code"]?.GetValue<string>();
                    log("relay: the relay is closing this room (" + closingCode + ")");
                    return;
                case "pong":
                    continue;
            }
            if (line["ok"]?.GetValue<bool>() == false) log($"relay: refused {line["op"]?.GetValue<string>()} ({line["code"]?.GetValue<string>()})");
        }
    }

    private async Task PingLoopAsync(Func<JsonObject, Task> write, CancellationToken token)
    {
        using var timer = new PeriodicTimer(options.PingInterval);
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false)) await write(new JsonObject { ["op"] = "ping" }).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- joiners

    private void HandleIncoming(JsonObject line, Func<JsonObject, Task> write, CancellationToken token)
    {
        var id = line["id"]?.GetValue<string>();
        var peer = line["peer"]?.GetValue<string>() ?? "";
        if (id == null) return;
        var reserved = false;
        lock (gate)
        {
            if (pendingJoins < options.MaxPendingJoins && pendingByPeer.GetValueOrDefault(peer) < options.MaxPendingPerPeer)
            {
                pendingJoins++;
                pendingByPeer[peer] = pendingByPeer.GetValueOrDefault(peer) + 1;
                reserved = true;
            }
        }
        if (!reserved)
        {
            _ = write(new JsonObject { ["op"] = "reject", ["id"] = id, ["code"] = "busy" }).ContinueWith(_ => { }, TaskScheduler.Default);
            return;
        }
        _ = HandleJoinerAsync(id, peer, token);
    }

    private async Task HandleJoinerAsync(string id, string peer, CancellationToken token)
    {
        {
            try { await OpenDataChannelAsync(id, peer, token).ConfigureAwait(false); }
            catch (Exception e) when (e is RelayException or IOException or System.Net.Sockets.SocketException or OperationCanceledException or System.Security.Authentication.AuthenticationException or ObjectDisposedException)
            {
                log($"relay: joiner {id} dropped: {e.Message}");
            }
            finally
            {
                lock (gate)
                {
                    pendingJoins--;
                    var left = pendingByPeer.GetValueOrDefault(peer) - 1;
                    if (left <= 0) pendingByPeer.Remove(peer);
                    else pendingByPeer[peer] = left;
                }
            }
        }
    }

    private async Task OpenDataChannelAsync(string id, string peer, CancellationToken token)
    {
        var shownRoom = Room ?? throw new RelayException(RelayCodes.Failed, "no room yet");
        var data = await RelayChannel.ConnectAsync(endpoint, options.Dial, token).ConfigureAwait(false);
        try
        {
            await data.SendAsync(new JsonObject
            {
                ["op"] = "accept",
                ["v"] = RelayProtocol.Version,
                ["room"] = shownRoom,
                ["id"] = id,
                ["sig"] = Sign(RelayProtocol.AcceptSignedText(endpoint.SignedHost, data.Challenge, shownRoom, id)),
            }, token).ConfigureAwait(false);
            RelayChannel.ThrowIfRefused(await data.ReadAsync(token).ConfigureAwait(false), "accepting a joiner");
        }
        catch
        {
            await data.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        // From here the bytes are the joiner's: the unchanged end-to-end TLS server handshake with the host key. It owns (and on failure closes) the connection.
        var transport = await TcpTlsListener.HandshakeOverAsync(data.Tcp, data.Stream, certificate, options.HandshakeTimeout, token, peer.Length == 0 ? null : peer).ConfigureAwait(false);
        if (!accepted.Writer.TryWrite(transport)) await transport.CloseAsync().ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- listing

    private async Task PublishLoopAsync(Func<JsonObject, Task> write, CancellationToken token)
    {
        var listedOnThisChannel = false;
        var lastSent = long.MinValue;
        var clock = SystemClock.Instance;
        // A channel starts with whatever is wanted now (a reconnect publishes again).
        lock (gate) listingDirty = desiredListing != null;
        while (!token.IsCancellationRequested)
        {
            RelayListing? snapshot;
            bool dirty;
            lock (gate)
            {
                snapshot = desiredListing;
                dirty = listingDirty;
            }
            if (!dirty)
            {
                // Wait for a change, or the refresh interval (an unchanged listing is re-sent so it does not expire).
                var refresh = Task.Delay(snapshot == null ? Timeout.InfiniteTimeSpan : options.ListingRefresh, token);
                var changed = desiredChanged.WaitAsync(token);
                var winner = await Task.WhenAny(refresh, changed).ConfigureAwait(false);
                if (winner == refresh)
                {
                    lock (gate) listingDirty = snapshot != null;
                }
                continue;
            }
            if (lastSent != long.MinValue)
            {
                var wait = TimeSpan.FromMilliseconds(lastSent + (long)options.ListingMinInterval.TotalMilliseconds - clock.NowMillis);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, token).ConfigureAwait(false); // changes made meanwhile coalesce: the snapshot is re-read below
            }
            lock (gate)
            {
                snapshot = desiredListing;
                listingDirty = false;
            }
            if (snapshot == null)
            {
                if (listedOnThisChannel) await write(new JsonObject { ["op"] = "unlist" }).ConfigureAwait(false);
                listedOnThisChannel = false;
            }
            else
            {
                var body = ListingBody(snapshot, Interlocked.Increment(ref listingSeq));
                await write(new JsonObject { ["op"] = listedOnThisChannel ? "update" : "list", ["body"] = body, ["sig"] = Sign(RelayProtocol.ListingSignedText(body)) }).ConfigureAwait(false);
                listedOnThisChannel = true;
            }
            lastSent = clock.NowMillis;
        }
    }

    /// <summary>The signed text of a listing: the fields of design/RELAY.md section 7, at most 1 KiB.</summary>
    public static string ListingBody(RelayListing listing, long seq) => new JsonObject
    {
        ["v"] = RelayProtocol.Version,
        ["seq"] = seq,
        ["name"] = listing.Name,
        ["hostName"] = listing.HostName,
        ["players"] = listing.Players,
        ["slots"] = listing.Slots,
        ["ai"] = listing.Ai,
        ["ruleset"] = listing.Ruleset,
        ["rulesetHash"] = listing.RulesetHash,
        ["mods"] = new JsonArray(listing.Mods.Take(16).Select(m => (JsonNode?)JsonValue.Create(m)).ToArray()),
        ["speed"] = listing.Speed,
        ["map"] = listing.Map,
        ["passwordRequired"] = listing.PasswordRequired,
        ["turnMode"] = listing.TurnMode,
        ["started"] = listing.Started,
        ["version"] = listing.Version,
        ["proto"] = listing.Proto,
    }.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        // Closing the control channel is the unlist: the relay removes a room's listing the moment its host channel ends (design/RELAY.md section 7).
        await stop.CancelAsync().ConfigureAwait(false);
        Task? run;
        lock (gate) run = runTask;
        if (run != null)
        {
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (Exception e) when (e is TimeoutException or OperationCanceledException) { }
        }
        accepted.Writer.TryComplete();
        while (accepted.Reader.TryRead(out var orphan)) await orphan.CloseAsync().ConfigureAwait(false);
        stop.Dispose();
    }
}
