using System.Text.Json.Nodes;
using Unciv.Net.Relay;
using Unciv.Net.Turns;
using Unciv.Server.Transport;

namespace Unciv.Client.Net.Hosting;

/// <summary>What <c>GET /v1/info</c> says about the relay, after the listing text rules (a relay must not smuggle control characters into the browser header).</summary>
/// <param name="Name">The relay's name, or its address when the name fails the rules.</param>
/// <param name="Contact">Who to ask about this relay; empty when none or not acceptable.</param>
/// <param name="Listing">The relay's listing policy word (<c>open</c>, <c>allowlist</c> or <c>off</c>).</param>
/// <param name="Rooms">Rooms with a live host.</param>
/// <param name="Listed">Games listed in the directory.</param>
public sealed record RelayInfo(string Name, string Contact, string Listing, int Rooms, int Listed);

/// <summary>The filters sent to the relay (it applies them) and applied again to every verified listing (a hostile relay may ignore them).</summary>
public sealed record DirectoryQuery
{
    public string Search { get; init; } = "";

    public string? Ruleset { get; init; }

    public bool OnlyWithSlots { get; init; }

    /// <summary>True: only password games; false: only open ones; null: both.</summary>
    public bool? PasswordRequired { get; init; }

    public bool HideStarted { get; init; }

    /// <summary>The request path and query for this filter.</summary>
    public string ToPath()
    {
        var parts = new List<string>();
        void Add(string name, string value) => parts.Add(name + "=" + Uri.EscapeDataString(value));
        if (Search.Trim().Length > 0) Add("q", Search.Trim());
        if (Ruleset != null) Add("ruleset", Ruleset);
        if (OnlyWithSlots) Add("hasSlots", "1");
        if (PasswordRequired is { } password) Add("passwordRequired", password ? "1" : "0");
        if (HideStarted) Add("started", "0");
        Add("limit", "100");
        return "/v1/games?" + string.Join('&', parts);
    }

    /// <summary>Whether a verified listing satisfies the filter (the same tests the relay does: name or host name contains the text, case-insensitively).</summary>
    public bool Accepts(ListedGame game)
    {
        var info = game.Info;
        var search = Search.Trim();
        return (search.Length == 0 || info.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || info.HostName.Contains(search, StringComparison.OrdinalIgnoreCase))
            && (Ruleset == null || info.Ruleset == Ruleset)
            && (!OnlyWithSlots || info.Players < info.Slots)
            && (PasswordRequired == null || info.PasswordRequired == PasswordRequired)
            && (!HideStarted || !info.Started);
    }
}

/// <summary>The directory as the browser shows it.</summary>
/// <param name="Games">Verified listings that satisfy the query, in the relay's order (newest first).</param>
/// <param name="Dropped">Entries that failed verification or the query (counted, never shown).</param>
/// <param name="More">The relay has more than one page: narrow the search.</param>
/// <param name="PingMillis">Round trip of the directory request to the relay.</param>
/// <param name="Info">What the relay says about itself.</param>
public sealed record DirectoryResult(RelayInfo Info, IReadOnlyList<ListedGame> Games, int Dropped, bool More, long PingMillis);

/// <summary>
/// T339 (design/RELAY.md sections 3 and 7, R16): reads the lobby directory of the CONFIGURED relay over the tunnel op <c>get</c> (the optional HTTP listener is never needed), 10 s and 512 KiB at most per reply, and
/// verifies every listing (<see cref="ListingVerifier"/>) before anything reaches the screen. Two short connections per refresh (the relay closes after each reply). Nothing here dials an address a peer supplied.
/// </summary>
public static class RelayDirectoryClient
{
    public static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);

    /// <exception cref="RelayException">the relay is unreachable, too old, has no directory (<see cref="RelayCodes.ListingOff"/>), is rate limiting, or sent something that is not the protocol.</exception>
    public static async Task<DirectoryResult> FetchAsync(RelayEndpoint relay, DirectoryQuery query, RelayDialOptions? dial = null, CancellationToken cancellationToken = default, IClock? clock = null, TimeSpan? replyTimeout = null)
    {
        dial ??= new RelayDialOptions();
        clock ??= SystemClock.Instance;
        var started = clock.NowMillis;
        var infoReply = await GetAsync(relay, dial, "/v1/info", replyTimeout ?? ReplyTimeout, cancellationToken).ConfigureAwait(false);
        var ping = clock.NowMillis - started;
        var info = ReadInfo(infoReply, relay);
        if (string.Equals(info.Listing, "off", StringComparison.Ordinal)) throw new RelayException(RelayCodes.ListingOff, "This relay does not offer a game directory.");
        var data = await GetAsync(relay, dial, query.ToPath(), replyTimeout ?? ReplyTimeout, cancellationToken).ConfigureAwait(false);
        var games = new List<ListedGame>();
        var dropped = 0;
        if (data["games"] is not JsonArray entries) throw new RelayException(RelayCodes.Failed, "The relay's game list is not a list.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var (game, _) = ListingVerifier.Verify(entry);
            if (game == null || !query.Accepts(game) || !seen.Add(game.Room)) dropped++; // a room listed twice is a relay bug or a replay: the first one counts
            else games.Add(game);
        }
        return new DirectoryResult(info, games, dropped, data["next"] is JsonValue, ping);
    }

    private static RelayInfo ReadInfo(JsonObject data, RelayEndpoint relay)
    {
        var name = data["name"] is JsonValue n && n.TryGetValue<string>(out var text) && ListingRules.TextOk(text, 1, 64) ? text : relay.Key;
        var contact = data["contact"] is JsonValue c && c.TryGetValue<string>(out var contactText) && ListingRules.TextOk(contactText, 1, 200) ? contactText : "";
        var listing = data["listing"] is JsonValue l && l.TryGetValue<string>(out var policy) ? policy : "open";
        return new RelayInfo(name, contact, listing, Count(data, "rooms"), Count(data, "listed"));
    }

    private static int Count(JsonObject data, string name) => data[name] is JsonValue v && v.TryGetValue<int>(out var number) && number >= 0 ? number : 0;

    private static async Task<JsonObject> GetAsync(RelayEndpoint relay, RelayDialOptions dial, string path, TimeSpan replyTimeout, CancellationToken cancellationToken)
    {
        await using var channel = await RelayChannel.ConnectAsync(relay, dial, cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(replyTimeout);
        JsonObject? reply;
        try
        {
            await channel.SendAsync(new JsonObject { ["op"] = "get", ["v"] = RelayProtocol.Version, ["path"] = path }, timeout.Token).ConfigureAwait(false);
            reply = await channel.ReadLargeAsync(RelayProtocol.GetReplyMaxBytes, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RelayException(RelayCodes.NoAnswer, "The relay did not answer in time.");
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException)
        {
            throw new RelayException(RelayCodes.Failed, "The connection to the relay broke: " + e.Message, inner: e);
        }
        if (reply?["ok"] is JsonValue ok && ok.TryGetValue<bool>(out var accepted) && !accepted && reply["code"] is JsonValue code && code.TryGetValue<string>(out var wire) && wire == "not-found" && path.StartsWith("/v1/games", StringComparison.Ordinal))
            throw new RelayException(RelayCodes.ListingOff, "This relay does not offer a game directory.", "not-found");
        RelayChannel.ThrowIfRefused(reply, "asking for the game list");
        return reply!["data"] as JsonObject ?? throw new RelayException(RelayCodes.Failed, "The relay answered without data.");
    }
}
